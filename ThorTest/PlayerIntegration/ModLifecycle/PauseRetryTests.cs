using ADModUtils;
using Control = GTA.Control;
using GTA.Math;
using GTA.Native;
using GTA;
using GTAVMods.Game;
using GTAVMods.Tests.Support;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using System;
using Thor;
using static GTAVMods.Tests.Support.SessionAssertions;

namespace GTAVMods.Tests.PlayerIntegration.ModLifecycle
{
    [TestClass, TestCategory("PlayerIntegration")]
    public class PauseRetryTests
    {
        [TestMethod]
        public void ExternalHeldObjectLossPausesUntilAConsistentInventoryObservationAllowsRecovery()
        {
            var s = new PlayerSessionFixture();
            s.Frame();
            s.Game.ObserveNextFrame(game => game.Observations[20].Exists = false);
            s.Frame(expectFailure: true);
            int allocations = s.CreatedWeapons.Count;
            s.Frame(expectFailure: true);
            Assert.AreEqual(allocations, s.CreatedWeapons.Count);
            Assert.HasCount(1, s.Log.Records.Where(record => record.Name == "tick.exception").ToArray());
            Assert.HasCount(1, s.Log.Records.Where(record => record.Name == "cleanup.exception").ToArray());
            Assert.IsFalse(s.Log.Records.Any(record => record.Name == "ability.enabled"));
            Assert.AreEqual("missing", s.Log.Last("gameplay.snapshot").Data["hammer"]);

            s.Game.ObserveNextFrame(game => game.ApplyWeaponObservation(10, WeaponHash.Unarmed, 0));
            s.Frame(expectFailure: true);
            int failuresBeforeRecovery = FailureCount(s);
            s.Chord(Keys.O);
            s.Frame(expectFailure: true);
            Assert.AreEqual(failuresBeforeRecovery, FailureCount(s));
            Assert.IsTrue(s.State().Contains("attached=True"));
            Assert.AreEqual(3, s.CreatedWeapons.Count(handle => s.Game.Observations[handle].Exists));
            Assert.IsFalse(s.Game.Calls.Any(call => call.Name == "entity.delete" && (int)call.Arguments[0] == 20),
                "The game already removed that object; cleanup must not delete the stale handle.");
            s.Controller.Abort();
            s.AssertReleased();
            Assert.HasCount(2, s.Log.Records.Where(record => record.Level == "ERROR").ToArray());
        }

        [TestMethod]
        public void MissingHammerAssetPausesWithoutCreatingAnObjectAndCanRecoverWhenStreamingSucceeds()
        {
            var tuning = new GameplayTuning();
            tuning.Hammer.AssetLoadTimeoutMs = 431;
            var s = new PlayerSessionFixture(tuning: tuning,
                configure: game => game.Weapons.AssetResults[WeaponHash.Hammer] = false);
            s.Frame(0, expectFailure: true);
            Assert.IsFalse(s.Game.Native.Calls.Any(call => call.Hash == Hash.CREATE_WEAPON_OBJECT));
            var request = s.Game.Calls.Single(call => call.Name == "weapon.request");
            Assert.AreEqual(WeaponHash.Hammer, request.Arguments[0]);
            Assert.AreEqual(tuning.Hammer.AssetLoadTimeoutMs, request.Arguments[1]);
            StringAssert.Contains((string)s.Log.Last("tick.exception").Data["exception"], "Could not load weapon asset Hammer");
            Assert.IsTrue(s.Game.Calls.Any(call => call.Name == "host.notify" &&
                ((string)call.Arguments[0]).Contains("paused")));
            int failuresBeforeRecovery = FailureCount(s);
            s.Game.Weapons.AssetResults[WeaponHash.Hammer] = true;
            s.Game.Entities.CreatedBlips.Enqueue(2001);
            s.Chord(Keys.O);
            s.Frame(0, expectFailure: true);
            var retryRequests = s.Game.Calls.Where(call => call.Name == "weapon.request").Skip(1).ToArray();
            Assert.IsNotEmpty(retryRequests, "Explicit retry must request the asset again after streaming recovers.");
            Assert.IsTrue(retryRequests.All(call => (WeaponHash)call.Arguments[0] == WeaponHash.Hammer &&
                (int)call.Arguments[1] == tuning.Hammer.AssetLoadTimeoutMs));
            Assert.IsTrue(s.CreatedWeapons.Any(handle => s.Game.Entity(handle).Exists));
            StringAssert.Contains(s.State(), "attached=True");
            Assert.AreEqual(failuresBeforeRecovery, FailureCount(s));
            s.Controller.Abort();
        }

        [TestMethod]
        [DataRow("model")]
        [DataRow("vehicle")]
        [DataRow("driver")]
        public void NavigationAllocationFailurePausesCleanlyAndEnableRetriesTheAbility(string failure)
        {
            var s = new PlayerSessionFixture(configure: game =>
            {
                if (failure == "model") game.World.ModelResults[(int)VehicleHash.Thruster] = false;
                if (failure == "vehicle") { game.World.Vehicles.Clear(); game.World.Vehicles.Enqueue(0); }
                if (failure == "driver") { game.World.Drivers.Clear(); game.World.Drivers.Enqueue(0); }
            });
            s.Frame(0, expectFailure: true);
            Assert.IsTrue(s.Log.Records.Any(record => record.Level == "ERROR"));
            if (failure != "model")
                Assert.IsTrue(s.Game.Calls.Any(call => call.Name == "world.releaseModel"));
            if (failure == "driver") Assert.IsFalse(s.Game.Observations[40].Exists);
            s.Game.World.ModelResults[(int)VehicleHash.Thruster] = true;
            s.Game.World.Vehicles.Clear();
            s.Game.World.Drivers.Clear();
            s.Game.AddEntity(42);
            s.Game.AddEntity(43);
            s.Game.World.Vehicles.Enqueue(42);
            s.Game.World.Drivers.Enqueue(43);
            s.Game.Entities.CreatedBlips.Enqueue(2001);
            int previousFailures = FailureCount(s);
            s.Chord(Keys.O);
            s.Frame(0, expectFailure: true);
            Assert.IsTrue(s.Log.Records.Any(record => record.Name == "pathfinder.created"),
                string.Join(Environment.NewLine, s.Log.Records.Select(record => record.Name + ": " + string.Join(", ", record.Data.Values))));
            Assert.AreEqual(42, s.Log.Last("pathfinder.created").Data["vehicle"]);
            StringAssert.Contains(s.State(), "attached=True", "The retried ability must be attached to the player again.");
            Assert.AreEqual(previousFailures, FailureCount(s),
                "Recovery must not hide a second gameplay or diagnostics error.");
            s.Controller.Abort();
            Assert.IsFalse(s.Game.Observations[42].Exists);
            Assert.IsFalse(s.Game.Observations[43].Exists);
        }

        [TestMethod]
        public void FailedDriverCreationAndVehicleRollbackPreserveBothErrorsAndRetryTheOwnedVehicleBeforeAllocating()
        {
            var s = new PlayerSessionFixture(configure: game =>
            {
                game.World.Drivers.Clear();
                game.World.Drivers.Enqueue(0);
                game.BeforeCall = call =>
                {
                    if (call.Name == "entity.delete" && (int)call.Arguments[0] == 40)
                        throw new InvalidOperationException("navigation vehicle release unavailable");
                };
            });
            s.Frame(0, expectFailure: true);
            string diagnostic = (string)s.Log.Last("tick.exception").Data["exception"];
            StringAssert.Contains(diagnostic, "Could not create the pathfinder driver");
            StringAssert.Contains(diagnostic, "navigation vehicle release unavailable");
            Assert.IsTrue(s.Game.Entity(40).Exists);
            Assert.IsTrue(s.Game.Calls.Any(call => call.Name == "world.releaseModel"));
            int failuresBeforeRecovery = FailureCount(s);

            s.Game.BeforeCall = null;
            s.Game.AddEntity(42);
            s.Game.AddEntity(43);
            s.Game.World.Vehicles.Enqueue(42);
            s.Game.World.Drivers.Enqueue(43);
            s.Game.Entities.CreatedBlips.Enqueue(2001);
            s.Game.Calls.Clear();
            s.Chord(Keys.O);
            s.Frame(0, expectFailure: true);
            Assert.IsFalse(s.Game.Observations[40].Exists);
            int retirement = s.Game.Calls.FindIndex(call => call.Name == "entity.delete" && (int)call.Arguments[0] == 40);
            int replacement = s.Game.Calls.FindIndex(call => call.Name == "world.createVehicle");
            Assert.IsTrue(retirement >= 0 && replacement > retirement,
                "An old owned helper must be retired before its replacement is allocated.");
            Assert.AreEqual(failuresBeforeRecovery, FailureCount(s));
            Assert.AreEqual(42, s.Log.Last("pathfinder.created").Data["vehicle"]);
            s.Controller.Abort();
            Assert.IsFalse(s.Game.Observations[42].Exists);
            Assert.IsFalse(s.Game.Observations[43].Exists);
        }

        [TestMethod]
        [DataRow("native.ACTIVATE_PHYSICS")]
        [DataRow("native.SET_OBJECT_PHYSICS_PARAMS")]
        [DataRow("blip.name")]
        public void DroppingPausesAfterConfigurationFailureAndReloadStillDeletesTheAcquiredHammer(string operation)
        {
            var s = new PlayerSessionFixture();
            s.Frame(0);
            s.Game.BeforeCall = call =>
            {
                if (call.Name == operation) throw new InvalidOperationException("hammer setup unavailable");
            };
            s.Frame(0, Keys.Y, expectFailure: true);
            Assert.IsTrue(s.Game.Entity(20).Exists);
            Assert.AreEqual(200, s.Player.MaxHealth);
            StringAssert.Contains((string)s.Log.Last("tick.exception").Data["exception"], "hammer setup unavailable");
            s.Game.BeforeCall = null;
            s.Reload();
            Assert.IsFalse(s.Game.Observations[20].Exists);
            Assert.HasCount(1, s.Game.Calls.Where(call => call.Name == "entity.delete" && (int)call.Arguments[0] == 20).ToArray());
        }

        [TestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public void FailedWhirlAllocationPausesAndExplicitRetryReleasesPartialRopeBeforePlayingAgain(bool ropeCreated)
        {
            var s = new PlayerSessionFixture();
            s.Frame(0);
            s.Game.Ropes.Creations.Enqueue(ropeCreated ? 70 : 0);
            if (ropeCreated) s.WeaponCreations.Enqueue(0);
            s.Frame(0, Keys.Z, expectFailure: true);
            Assert.HasCount(1, s.Log.Records.Where(record => record.Name == "tick.exception").ToArray());
            StringAssert.Contains((string)s.Log.Last("tick.exception").Data["exception"], ropeCreated
                ? "Could not create the hammer rope anchor." : "Could not create the hammer rope.");
            Assert.HasCount(1, s.Game.Calls.Where(call => call.Name == "rope.create").ToArray());
            CollectionAssert.AreEqual(ropeCreated ? new[] { 70 } : Array.Empty<int>(),
                s.Game.Calls.Where(call => call.Name == "rope.delete").Select(call => (int)call.Arguments[0]).ToArray());
            Assert.IsEmpty(s.Game.Ropes.Ropes);
            Assert.IsFalse(s.Game.Calls.Any(call => call.Name == "rope.connect"));
            Assert.IsTrue(s.Game.Entity(20).Exists);
            int failuresBeforeRecovery = FailureCount(s);
            s.Chord(Keys.O);
            Assert.IsFalse(s.Game.Observations[20].Exists);
            s.Game.Entities.CreatedBlips.Enqueue(2001);
            s.Frame(0, expectFailure: true);
            Assert.AreEqual(failuresBeforeRecovery, FailureCount(s),
                "Successful retry must not introduce another gameplay or diagnostics failure.");
            Assert.HasCount(1, s.Log.Records.Where(record => record.Name == "ability.enabled").ToArray());
            Assert.AreEqual(3, s.CreatedWeapons.Count(handle => s.Game.Observations[handle].Exists));
        }

        [TestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public void FailedDropPausesAndRetryWaitsForConfirmedHammerDeletion(bool silentlyRefused)
        {
            var tuning = new GameplayTuning();
            tuning.Ability.UnpoweredMaxHealth = 127;
            var s = new PlayerSessionFixture(tuning: tuning);
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
            Assert.AreEqual(tuning.Ability.UnpoweredMaxHealth, s.Player.MaxHealth);
            Assert.HasCount(1, s.Log.Records.Where(record => record.Name == "ability.enabled").ToArray());
            Assert.IsTrue(s.CreatedWeapons.Skip(allocations).Any(handle => s.Game.Entity(handle).Exists));
        }

        [TestMethod]
        public void FailedCleanupKeepsTheModPausedEvenWhileThePlayerIsBetweenCharacters()
        {
            var tuning = new GameplayTuning();
            tuning.Ability.UnpoweredMaxHealth = 127;
            var s = new PlayerSessionFixture(tuning: tuning);
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
            Assert.AreEqual(tuning.Ability.UnpoweredMaxHealth, s.Player.MaxHealth);
            Assert.HasCount(1, s.Log.Records.Where(record => record.Name == "ability.enabled").ToArray());
        }

        [TestMethod]
        public void UnavailablePositionObservationsPauseGameplayAndLeaveADiagnosticFailureRecord()
        {
            var tuning = new GameplayTuning();
            tuning.Ability.UnpoweredMaxHealth = 127;
            var s = new PlayerSessionFixture(tuning: tuning);
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
            Assert.AreEqual(tuning.Ability.UnpoweredMaxHealth, s.Player.MaxHealth);
        }
    }
}
