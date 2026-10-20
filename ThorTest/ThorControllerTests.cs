using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using GTA;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ModUtilsTest
{
    [TestClass]
    [TestCategory("PlayerIntegration")]
    public class ThorControllerTests
    {
        [TestMethod]
        [DataRow(0, false, false)]
        [DataRow(10, false, false)]
        [DataRow(10, true, true)]
        public void StartingWithoutALivingCharacterWaitsWithoutAllocatingOrGivingEquipment(int ped, bool exists, bool dead)
        {
            var s = new PlayerSessionFixture(configure: game => {
                game.Host.PlayerPed = ped;
                game.Observations[10].Exists = exists;
                game.Observations[10].Dead = dead;
            });
            s.Frame();
            s.Frame();
            s.Disable();
            Assert.IsEmpty(s.CreatedWeapons);
            Assert.IsFalse(s.Game.Calls.Any(call => call.Name == "weapon.give" || call.Name == "ped.maxHealth"));
            Assert.AreEqual("Ctrl+A+F", s.Log.Last("ability.disabled").Data["reason"]);
            Assert.AreEqual(200, s.Game.Observations[10].MaxHealth);
        }

        [TestMethod]
        public void ChangingCharactersAndDyingRestoreThePreviousCharactersHealth()
        {
            var s = new PlayerSessionFixture();
            s.Frame();
            Assert.AreEqual(30000, s.Player.MaxHealth);
            s.Game.Host.PlayerPed = 11;
            s.Frame();
            Assert.AreEqual(200, s.Game.Entity(10).MaxHealth);
            Assert.AreEqual(100, s.Game.Entity(11).MaxHealth, "The new character has not picked up the hammer.");
            Assert.IsFalse(s.Game.Observations[21].Exists);
            Assert.IsFalse(s.Game.Observations[22].Exists);
            s.Game.Entity(11).Dead = true;
            s.Frame();
            Assert.AreEqual(250, s.Game.Entity(11).MaxHealth);
            int allocated = s.CreatedWeapons.Count;
            s.Frame();
            Assert.AreEqual(allocated, s.CreatedWeapons.Count);
            s.Game.Entity(11).Dead = false;
            s.Frame();
            Assert.AreEqual(100, s.Player.MaxHealth);
        }

        [TestMethod]
        [DataRow(true, "2.0.1158.13", false)]
        [DataRow(true, "2.0.1158.17", true)]
        [DataRow(false, null, true)]
        public void CompatibilityNoticeAppearsOnceAndDoesNotPreventUsingTheHammer(bool enhanced, string version, bool warn)
        {
            var s = new PlayerSessionFixture(configure: game => {
                game.Host.IsEnhanced = enhanced;
                game.Host.FileVersion = version == null ? null : new Version(version);
            });
            s.Frame();
            s.Frame();
            Assert.AreEqual(30000, s.Player.MaxHealth);
            Assert.AreEqual(version, s.Log.Last("session.start").Data["shvdneVersion"]);
            Assert.AreEqual("3.9.0.0", s.Log.Last("session.start").Data["apiVersion"]);
            Assert.HasCount(warn ? 1 : 0, s.Log.Records.Where(record => record.Name == "compatibility.baseline").ToArray());
            Assert.HasCount(warn ? 1 : 0, s.Game.Calls.Where(call => call.Name == "host.notify").ToArray());
            s.Frame(0, Keys.Y);
            Assert.AreEqual(WeaponHash.Unarmed, s.Player.CurrentWeapon,
                "Compatibility advice must not block the player's drop control.");
            Assert.IsTrue(s.Game.Entity(20).Exists, "The dropped hammer remains available to recall.");
        }

        [TestMethod]
        public void OnlyTheCompleteDisableChordRestoresHealthAndSuppliesOneUnequippedParachute()
        {
            var s = new PlayerSessionFixture();
            s.Frame();
            s.Controller.KeyDown(new KeyEventArgs(Keys.F));
            s.Controller.KeyDown(new KeyEventArgs(Keys.F | Keys.Control));
            s.Chord(Keys.P);
            s.Chord(Keys.O);
            Assert.AreEqual(30000, s.Player.MaxHealth);
            Assert.IsFalse(s.Log.Records.Any(record => record.Name == "ability.disabled"));

            s.Disable();
            s.Disable();
            s.Frame();
            Assert.AreEqual(200, s.Player.MaxHealth);
            CollectionAssert.AreEqual(new object[] { 10, WeaponHash.Parachute, 1, false, false },
                s.Game.Calls.Single(call => call.Name == "weapon.give" && (WeaponHash)call.Arguments[1] == WeaponHash.Parachute).Arguments);
            Assert.HasCount(1, s.Log.Records.Where(record => record.Name == "ability.disabled").ToArray());
            s.Chord(Keys.O);
            s.Frame();
            Assert.AreEqual(100, s.Player.MaxHealth, "Enabling does not confirm that the loose hammer was caught.");
            Assert.HasCount(1, s.Log.Records.Where(record => record.Name == "ability.enabled").ToArray());
        }

        [TestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public void FailedDropPausesAndRetryWaitsForConfirmedHammerDeletion(bool silentlyRefused)
        {
            var s = new PlayerSessionFixture();
            s.Frame();
            s.Game.BeforeCall = call => {
                if (call.Name == "native.ACTIVATE_PHYSICS" && (int)call.Arguments[0] == 20)
                    throw new InvalidOperationException("hammer physics unavailable");
            };
            s.Frame(0, Keys.Y, expectFailure: true);
            Assert.AreEqual(200, s.Player.MaxHealth);
            StringAssert.Contains((string)s.Log.Last("tick.exception").Data["exception"], "hammer physics unavailable");
            Assert.AreEqual("exception", s.Log.Last("gameplay.snapshot").Data["reason"]);
            Assert.IsTrue(s.Game.Entity(20).Exists);
            Assert.IsTrue(s.Game.Calls.Any(call => call.Name == "host.notify" && ((string)call.Arguments[0]).Contains("paused")));

            s.Game.BeforeCall = call => {
                if (!silentlyRefused && call.Name == "entity.delete" && (int)call.Arguments[0] == 20)
                    throw new InvalidOperationException("hammer deletion unavailable");
            };
            if (silentlyRefused) s.Game.Entities.SilentDeletionRefusals.Add(20);
            int allocations = s.CreatedWeapons.Count;
            s.Chord(Keys.O);
            s.Frame(expectFailure: true);
            Assert.IsTrue(s.Game.Entity(20).Exists);
            Assert.AreEqual(allocations, s.CreatedWeapons.Count);
            Assert.IsFalse(s.Log.Records.Any(record => record.Name == "ability.enabled"));
            Assert.IsTrue(s.Game.Calls.Any(call => call.Name == "host.notify" && ((string)call.Arguments[0]).Contains("remains paused")));

            s.Game.BeforeCall = null;
            s.Game.Entities.SilentDeletionRefusals.Clear();
            int failuresBeforeRecovery = FailureCount(s);
            s.Chord(Keys.O);
            Assert.IsFalse(s.Game.Observations[20].Exists);
            s.Frame(expectFailure: true);
            Assert.AreEqual(failuresBeforeRecovery, FailureCount(s), "Retry must not introduce another gameplay or diagnostics failure.");
            Assert.AreEqual(100, s.Player.MaxHealth);
            Assert.HasCount(1, s.Log.Records.Where(record => record.Name == "ability.enabled").ToArray());
            Assert.IsTrue(s.CreatedWeapons.Skip(allocations).Any(handle => s.Game.Entity(handle).Exists));
        }

        [TestMethod]
        public void FailedCleanupKeepsTheModPausedEvenWhileThePlayerIsBetweenCharacters()
        {
            var s = new PlayerSessionFixture();
            s.Frame();
            s.Game.Entities.SilentDeletionRefusals.Add(21);
            s.Disable();
            s.Game.Host.PlayerPed = 0;
            s.Chord(Keys.O);
            s.Frame(expectFailure: true);
            Assert.IsTrue(s.Game.Entity(21).Exists);
            Assert.IsFalse(s.Log.Records.Any(record => record.Name == "ability.enabled"));
            s.Game.Entities.SilentDeletionRefusals.Clear();
            s.Game.Host.PlayerPed = 10;
            s.Game.Entities.CreatedBlips.Enqueue(2001);
            int failuresBeforeRecovery = FailureCount(s);
            s.Chord(Keys.O);
            s.Frame(expectFailure: true);
            Assert.AreEqual(failuresBeforeRecovery, FailureCount(s));
            Assert.IsFalse(s.Game.Observations[21].Exists);
            Assert.AreEqual(100, s.Player.MaxHealth);
            Assert.HasCount(1, s.Log.Records.Where(record => record.Name == "ability.enabled").ToArray());
        }

        [TestMethod]
        public void ReloadReleasesIndependentResourcesWhenOneOwnedObjectCannotBeDeleted()
        {
            var s = new PlayerSessionFixture();
            s.Game.Ropes.Creations.Enqueue(70);
            s.Frame(0, Keys.Z);
            s.Frame(0, Keys.Z);
            Assert.IsNotEmpty(s.Audio.Clips, "Whirling starts audio that reload must release.");
            s.Game.Entities.SilentDeletionRefusals.Add(21);
            s.Reload();
            Assert.IsTrue(s.Game.Entity(21).Exists);
            Assert.IsFalse(s.Game.Observations[22].Exists);
            Assert.AreEqual(200, s.Player.MaxHealth);
            Assert.IsTrue(s.Audio.Clips.Values.All(clip => clip.Disposals == 1));
            Assert.AreEqual("script-aborted-or-reloaded", s.Log.Last("session.stopping").Data["reason"]);
            Assert.AreEqual("session.end", s.Log.Records.Last().Name);
            s.Game.Entities.SilentDeletionRefusals.Clear();
            s.Reload();
            Assert.IsFalse(s.Game.Observations[21].Exists);
            Assert.IsTrue(s.Audio.Clips.Values.All(clip => clip.Disposals == 1));
        }

        [TestMethod]
        public void UnavailablePositionObservationsPauseGameplayAndLeaveADiagnosticFailureRecord()
        {
            var s = new PlayerSessionFixture();
            s.Frame();
            s.Game.BeforeObservation = (operation, entity) => {
                if (operation == "entity.position") throw new InvalidOperationException("position unavailable");
            };
            s.Log.Clock.ElapsedMilliseconds = 5000;
            s.Frame(16, Keys.T, aim: true, expectFailure: true);
            StringAssert.Contains((string)s.Log.Last("tick.exception").Data["exception"], "position unavailable");
            Assert.AreEqual("WARN", s.Log.Last("diagnostics.exception").Level);
            Assert.AreEqual(200, s.Player.MaxHealth);
            s.Controller.KeyDown(new KeyEventArgs(Keys.F9));
            Assert.AreEqual("WARN", s.Log.Last("diagnostics.exception").Level);
            s.Game.BeforeObservation = null;
            s.Game.Entities.CreatedBlips.Enqueue(2001);
            int failuresBeforeRecovery = FailureCount(s);
            s.Chord(Keys.O);
            s.Frame(expectFailure: true);
            Assert.AreEqual(failuresBeforeRecovery, FailureCount(s));
            Assert.AreEqual(100, s.Player.MaxHealth);
        }

        [TestMethod]
        public void ManualSnapshotsReportPlayerStateAndRapidRepeatedKeysDoNotFloodTheLog()
        {
            var s = new PlayerSessionFixture();
            s.Frame();
            s.Controller.KeyDown(new KeyEventArgs(Keys.F9 | Keys.Shift));
            s.Controller.KeyDown(new KeyEventArgs(Keys.F9 | Keys.Shift));
            Assert.HasCount(1, s.Log.Records.Where(record => record.Name == "input.F9").ToArray());
            Assert.AreEqual(true, s.Log.Last("input.F9").Data["shift"]);
            Assert.AreEqual(false, s.Log.Last("input.F9").Data["control"]);
            Assert.AreEqual("manual-F9", s.Log.Last("gameplay.snapshot").Data["reason"]);
            Assert.AreEqual(200, s.Log.Last("gameplay.snapshot").Data["health"],
                "F9 reports current health, not the new maximum health capacity.");
            s.Log.Clock.ElapsedMilliseconds += 250;
            s.Controller.KeyDown(new KeyEventArgs(Keys.F9));
            Assert.HasCount(2, s.Log.Records.Where(record => record.Name == "input.F9").ToArray());
        }

        [TestMethod]
        public void SessionPerformanceUsesTheHostCounterFrequencyWhenReportingAPlayerFrame()
        {
            var timestamps = new Queue<long>(new[] { 1000000L, 1000050L });
            var s = new PlayerSessionFixture(timestamp: () => timestamps.Dequeue(), frequency: 2000);
            s.Frame();
            Assert.AreEqual(30000, s.Player.MaxHealth);
            Assert.AreEqual(25d, Convert.ToDouble(s.Log.Last("performance").Data["averageTickMs"]));
            Assert.AreEqual(25d, Convert.ToDouble(s.Log.Last("performance").Data["maxTickMs"]));
        }

        [TestMethod]
        public void DefaultDiagnosticsWriteStartupAndManualPlayerSnapshotsToTheAdvertisedLogFile()
        {
            var s = new PlayerSessionFixture(useProductionDiagnostics: true);
            s.Frame();
            s.Controller.KeyDown(new KeyEventArgs(Keys.F9));
            Assert.IsTrue(File.Exists(s.Log.Writer.FilePath));
            Assert.AreEqual(s.Log.Writer.FilePath, s.Log.Last("session.start").Data["logPath"]);
            var snapshot = s.Log.Last("gameplay.snapshot");
            Assert.AreEqual("manual-F9", snapshot.Data["reason"]);
            Assert.AreEqual("Hammer", snapshot.Data["selectedWeapon"]);
            Assert.AreEqual(200, snapshot.Data["health"]);
            Assert.AreEqual(0L, Convert.ToInt64(s.Log.Last("performance").Data["droppedLogRecords"]));
            var records = File.ReadAllLines(s.Log.Writer.FilePath).Select(LogJson.Parse).ToArray();
            Assert.IsTrue(records.All(record => long.Parse(LogJson.Text(record, "elapsedMs")) >= 0));
            Assert.IsTrue(records.All(record => DateTime.TryParse(LogJson.Text(record, "utc"), out _)));
            s.Reload();
            Assert.AreEqual("session.end", s.Log.Records.Last().Name);
        }

        [TestMethod]
        public void DisablingAfterTheGameRemovesTheWhirlRopeStillReleasesItsAnchorAndRestoresThePlayer()
        {
            var s = new PlayerSessionFixture();
            s.Game.Ropes.Creations.Enqueue(70);
            s.Frame(0, Keys.Z);
            Assert.IsTrue(s.Game.Ropes.Ropes.Contains(70));
            int anchor = (int)s.Game.Calls.Last(call => call.Name == "rope.connect").Arguments[1];
            s.Game.Ropes.Ropes.Remove(70);
            s.Game.Calls.Clear();
            s.Disable();
            s.AssertNoErrors();
            Assert.AreEqual(200, s.Player.MaxHealth);
            Assert.IsFalse(s.Game.Observations[anchor].Exists);
            Assert.IsTrue(s.Game.Entity(20).Exists, "Disabling leaves the released hammer available for a later recall.");
            Assert.IsFalse(s.Game.Calls.Any(call => call.Name == "rope.delete"),
                "An already absent rope needs no repeated deletion request.");
        }

        private static int FailureCount(PlayerSessionFixture session) =>
            session.Log.Records.Count(record => record.Level == "ERROR" || record.Name == "diagnostics.exception");
    }
}
