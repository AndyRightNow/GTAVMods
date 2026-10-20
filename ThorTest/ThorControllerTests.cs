using System;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using GTA;
using GTA.Math;
using GTA.Native;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Thor;
using ADModUtils;

namespace ModUtilsTest
{
    [TestClass]
    public class ThorControllerTests
    {
        private sealed class Fixture
        {
            public readonly FakeGame Game = new FakeGame();
            public readonly AbilityProbe Ability = new AbilityProbe();
            public readonly RecordingLog Log = new RecordingLog();
            public readonly ThorController Controller;
            public bool ConsoleOpen, ThrowPathfinderCleanup;
            public int PathfinderDisposals;
            public long DroppedRecords = 13;
            public long Time { get; set; }
            public Fixture()
            {
                Game.AddEntity(10);
                Game.Host.FileVersion = new Version(2, 0, 1158, 17);
                var diagnostics = new GameplayDiagnostics(Game.Services, Log, () => Time, () => 0);
                Controller = new ThorController(Game.Services, Ability, diagnostics, Log,
                    () => { PathfinderDisposals++; if (ThrowPathfinderCleanup) throw new InvalidOperationException("pathfinder"); },
                    () => ConsoleOpen, () => Time, 1000,
                    new ThorSessionInfo("scripts/Logs/Thor-test.jsonl", "2026-10-20T00:00:00.0000000Z", "3.9.0.0"),
                    () => DroppedRecords);
            }
            public void Key(Keys key) => Controller.KeyDown(new KeyEventArgs(key));
            public void Chord(Keys key) { Game.Input.Keys.Add(Keys.A); Key(key | Keys.Control); }
        }

        [TestMethod]
        public void PlayerChangesAndDeathDetachBeforeReapplyingAndMissingPlayersAreIgnored()
        {
            var f = new Fixture();
            f.Controller.Start();
            f.Controller.Tick();
            f.Controller.Tick();
            f.Game.AddEntity(20);
            f.Game.Host.PlayerPed = 20;
            f.Controller.Tick();
            f.Game.Entity(20).Dead = true;
            f.Controller.Tick();
            f.Controller.Tick();
            f.Game.Entity(20).Dead = false;
            f.Game.Entity(20).Exists = false;
            f.Controller.Tick();
            f.Game.Host.PlayerPed = 0;
            f.Controller.Tick();
            CollectionAssert.AreEqual(new[] { "apply:10", "tick:False", "tick:False", "remove", "apply:20", "tick:False", "remove" }, f.Ability.Calls);
            Assert.AreEqual("1.0.1158.17", f.Log.Last("session.start").Data["executableVersion"]);
            var startup = f.Log.Last("session.start");
            Assert.AreEqual("scripts/Logs/Thor-test.jsonl", startup.Data["logPath"]);
            Assert.AreEqual("2026-10-20T00:00:00.0000000Z", startup.Data["buildUtc"]);
            Assert.AreEqual("3.9.0.0", startup.Data["apiVersion"]);
            Assert.AreEqual("2.0.1158.17", startup.Data["shvdneVersion"]);
            Assert.HasCount(1, f.Log.Records.Where(record => record.Name == "compatibility.baseline").ToArray());
        }

        [TestMethod]
        public void AssessedBuildRunsWithoutWarningAndNullVersionIsRecordedSafely()
        {
            var f = new Fixture();
            f.Game.Host.FileVersion = new Version(2, 0, 1158, 13);
            f.Controller.Start(); f.Controller.Tick();
            Assert.IsFalse(f.Log.Records.Any(record => record.Name == "compatibility.baseline"));
            var unknown = new Fixture();
            unknown.Game.Host.IsEnhanced = false;
            unknown.Game.Host.FileVersion = null;
            unknown.Controller.Start(); unknown.Controller.Tick();
            Assert.IsNull(unknown.Log.Last("session.start").Data["shvdneVersion"]);
            Assert.AreEqual("3.9.0.0", unknown.Log.Last("session.start").Data["apiVersion"]);
            Assert.HasCount(1, unknown.Log.Records.Where(record => record.Name == "compatibility.baseline").ToArray());
        }

        [TestMethod]
        public void ConsoleAndChordGuardsPreventTogglesAndDisableIsIdempotent()
        {
            var f = new Fixture();
            f.Controller.Tick();
            f.ConsoleOpen = true; f.Chord(Keys.F);
            f.ConsoleOpen = false; f.Key(Keys.F);
            f.Game.Input.Keys.Clear(); f.Key(Keys.F | Keys.Control);
            f.Chord(Keys.O); f.Chord(Keys.P);
            Assert.IsFalse(f.Ability.Calls.Contains("remove"));
            f.Chord(Keys.F); f.Chord(Keys.F); f.Controller.Tick();
            Assert.HasCount(1, f.Ability.Calls.Where(call => call == "remove").ToArray());
            Assert.IsTrue(f.Game.Entity(10).Weapons.Contains(WeaponHash.Parachute));
            Assert.HasCount(1, f.Ability.Calls.Where(call => call == "tick:False").ToArray());
            f.Chord(Keys.O); f.Controller.Tick();
            Assert.AreEqual("Ctrl+A+O", f.Log.Last("ability.enabled").Data["reason"]);
            Assert.HasCount(2, f.Ability.Calls.Where(call => call == "apply:10").ToArray());
            Assert.IsFalse(f.Ability.Calls.Contains("reset"), "An ordinary disable/enable preserves the dropped hammer.");
        }

        [TestMethod]
        [DataRow(0, false, false)]
        [DataRow(10, false, false)]
        [DataRow(10, true, true)]
        public void DisableDoesNotGiveParachuteToAbsentDeletedOrDeadPed(int ped, bool exists, bool dead)
        {
            var f = new Fixture();
            f.Game.Host.PlayerPed = ped;
            f.Game.Observations[10].Exists = exists;
            f.Game.Observations[10].Dead = dead;
            f.Chord(Keys.F);
            Assert.IsFalse(f.Game.Calls.Any(call => call.Name == "weapon.give"));
            Assert.AreEqual("Ctrl+A+F", f.Log.Last("ability.disabled").Data["reason"]);
        }

        [TestMethod]
        public void TickFailurePausesUntilRetryAndDiagnosticsFailuresDoNotEscape()
        {
            var f = new Fixture();
            f.Ability.Failure = "tick:False";
            f.Log.FailOn = "gameplay.snapshot";
            f.Controller.Tick(); f.Controller.Tick();
            Assert.HasCount(1, f.Ability.Calls.Where(call => call == "tick:False").ToArray());
            Assert.AreEqual("ERROR", f.Log.Last("tick.exception").Level);
            Assert.IsTrue(f.Log.Records.Any(record => record.Name == "diagnostics.exception" && record.Interval == null));
            Assert.IsTrue(f.Log.Records.Any(record => record.Name == "diagnostics.exception" && record.Interval == 10000));
            f.Log.FailOn = null; f.Ability.Failure = null; f.Ability.Calls.Clear();
            f.Chord(Keys.O); f.Controller.Tick();
            CollectionAssert.AreEqual(new[] { "remove", "reset", "apply:10", "tick:False" }, f.Ability.Calls);
        }

        [TestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public void RetryRemainsPausedUntilCleanupSucceedsThenReattachesBeforeTicking(bool playerUnavailable)
        {
            var f = new Fixture();
            f.Controller.Tick();
            f.Ability.Failure = "remove";
            f.Chord(Keys.F);
            f.Ability.Calls.Clear();
            if (playerUnavailable) f.Game.Host.PlayerPed = 0;

            f.Chord(Keys.O);
            f.Controller.Tick();
            f.Chord(Keys.O);
            f.Controller.Tick();

            CollectionAssert.AreEqual(new[] { "remove", "remove" }, f.Ability.Calls);
            Assert.IsTrue(f.Ability.IsAttachedToPed, "Failed cleanup must remain owned for the next retry.");
            Assert.IsFalse(f.Log.Records.Any(record => record.Name == "ability.enabled"));
            Assert.HasCount(3, f.Log.Records.Where(record => record.Name == "cleanup.exception").ToArray());
            Assert.HasCount(2, f.Game.Calls.Where(call => call.Name == "host.notify" &&
                ((string)call.Arguments[0]).Contains("remains paused")).ToArray());

            f.Ability.Failure = "reset";
            f.Game.Host.PlayerPed = 10;
            f.Ability.Calls.Clear();
            f.Chord(Keys.O);
            Assert.IsFalse(f.Ability.IsAttachedToPed);
            f.Controller.Tick();
            CollectionAssert.AreEqual(new[] { "remove", "reset" }, f.Ability.Calls);
            Assert.IsFalse(f.Log.Records.Any(record => record.Name == "ability.enabled"));

            f.Ability.Failure = null;
            f.Ability.Calls.Clear();
            f.Chord(Keys.O);
            f.Controller.Tick();
            CollectionAssert.AreEqual(new[] { "remove", "reset", "apply:10", "tick:False" }, f.Ability.Calls);
            Assert.HasCount(1, f.Log.Records.Where(record => record.Name == "ability.enabled").ToArray());
        }

        [TestMethod]
        public void RecoveryReplacesPartiallyConfiguredHammerOnlyAfterDeletionSucceedsAndKeepsAudioUsable()
        {
            var f = new HammerFixture();
            f.HoldHammer();
            f.PrepareHoverObjects();
            var diagnostics = new GameplayDiagnostics(f.Game.Services, f.Log, () => 0, () => 0);
            var controller = new ThorController(f.Game.Services, f.Ability, diagnostics, f.Log,
                f.Pathfinder.Dispose, () => false, () => 0, 1000,
                new ThorSessionInfo("recovery.jsonl", "test-build", "test-api"), () => 0);
            controller.Tick();
            Assert.AreEqual(30000, f.Player.MaxHealth);

            f.Game.Input.Keys.Add(Keys.Y);
            var setupFailure = new InvalidOperationException("hammer physics unavailable");
            f.Game.BeforeCall = call =>
            {
                if (call.Name == "native.ACTIVATE_PHYSICS" && (int)call.Arguments[0] == 20)
                    throw setupFailure;
            };
            controller.Tick();
            f.Game.Input.Keys.Clear();
            Assert.AreEqual(20, f.Hammer.WeaponObject);
            Assert.IsTrue(f.Game.Entity(20).Exists);
            Assert.IsFalse(f.Ability.IsAttachedToPed);
            Assert.AreEqual(200, f.Player.MaxHealth);
            StringAssert.Contains((string)f.Log.Last("tick.exception").Data["exception"], setupFailure.Message);
            Assert.IsFalse(f.Game.Calls.Any(call => call.Name == "blip.create"));

            f.Game.BeforeCall = call =>
            {
                if (call.Name == "entity.delete" && (int)call.Arguments[0] == 20)
                    throw new InvalidOperationException("hammer deletion unavailable");
            };
            f.Game.Input.Keys.Add(Keys.A);
            controller.KeyDown(new KeyEventArgs(Keys.Control | Keys.O));
            controller.Tick();
            Assert.IsTrue(f.Game.Entity(20).Exists);
            Assert.IsFalse(f.Log.Records.Any(record => record.Name == "ability.enabled"));
            Assert.HasCount(2, f.Game.Native.Calls.Where(call => call.Hash == Hash.CREATE_WEAPON_OBJECT).ToArray(),
                "A paused tick must not create replacement hammers while the failed handle remains owned.");

            f.Game.BeforeCall = null;
            controller.KeyDown(new KeyEventArgs(Keys.Control | Keys.O));
            Assert.IsFalse(f.Game.Observations[20].Exists);
            Assert.AreEqual(0, f.Hammer.WeaponObject);
            foreach (int handle in new[] { 31, 32, 60 })
            {
                f.Game.AddEntity(handle);
                f.WeaponCreations.Enqueue(handle);
            }
            f.Game.Entities.CreatedBlips.Enqueue(50);
            f.ClearTerrainRay();
            controller.Tick();
            Assert.IsTrue(f.Ability.IsAttachedToPed);
            Assert.AreEqual(60, f.Hammer.WeaponObject);
            CollectionAssert.AreEqual(new object[] { 60, 50 },
                f.Game.Calls.Single(call => call.Name == "blip.create").Arguments);
            CollectionAssert.AreEqual(new object[] { 50, "GodlyWeapon" },
                f.Game.Calls.Single(call => call.Name == "blip.name").Arguments);
            Assert.HasCount(1, f.Log.Records.Where(record => record.Name == "ability.enabled").ToArray());
            Assert.HasCount(1, f.Log.Records.Where(record => record.Name == "tick.exception").ToArray());
            Assert.HasCount(2, f.Game.Calls.Where(call => call.Name == "entity.delete" &&
                (int)call.Arguments[0] == 20).ToArray());

            f.Hammer.Whirl(new ADModUtils.Plane(Vector3.UnitZ, Vector3.Zero, new Random(1)));
            f.ClearTerrainRay();
            f.Hammer.OnTick();
            Assert.IsTrue(f.Audio.Clips["whirling-hammer.wav"].IsPlaying);
            Assert.AreEqual(0, f.Audio.Clips["whirling-hammer.wav"].Disposals);
        }

        [TestMethod]
        public void AbortAttemptsEveryResourceEvenWhenOneCleanupFails()
        {
            var f = new Fixture { ThrowPathfinderCleanup = true };
            f.Ability.Failure = "remove";
            f.Controller.Abort();
            CollectionAssert.AreEqual(new[] { "remove", "sounds", "weapon" }, f.Ability.Calls);
            Assert.AreEqual(1, f.PathfinderDisposals);
            Assert.HasCount(2, f.Log.Records.Where(record => record.Name == "cleanup.exception").ToArray());
            Assert.AreEqual("session.end", f.Log.Records.Last().Name);
            Assert.AreEqual(13L, f.Log.Last("session.end").Data["droppedLogRecords"]);
        }

        [TestMethod]
        public void GameplayKeysAreThrottledAndManualSnapshotsAreBestEffort()
        {
            var f = new Fixture();
            foreach (Keys key in new[] { Keys.H, Keys.T, Keys.U, Keys.Y, Keys.Z, Keys.R, Keys.F9 })
            {
                f.Key(key | Keys.Shift);
                var record = f.Log.Last("input." + key);
                Assert.AreEqual(250, record.Interval);
                Assert.AreEqual(true, record.Data["shift"]);
            }
            Assert.AreEqual("manual-F9", f.Log.Last("gameplay.snapshot").Data["reason"]);
            f.Log.FailOn = "gameplay.snapshot";
            f.Key(Keys.F9);
            Assert.AreEqual("WARN", f.Log.Last("diagnostics.exception").Level);
        }

        [TestMethod]
        public void ProductionCompositionReportsMetadataAndRunsIdleLifecycleWithoutEngineOperations()
        {
            var game = new FakeGame();
            game.Host.PlayerPed = 0;
            var audio = new SilentAudio();
            var log = new RecordingLog();
            var session = ThorSessionInfo.Capture("scripts/Logs/composition.jsonl");
            var controller = ThorComposition.Create(game.Services, audio, log, new Random(17),
                Thor.Thor.ReadConsoleOpen, Thor.Thor.ReadDroppedRecords, session);
            controller.Start();
            controller.Tick();
            var startup = log.Last("session.start");
            Assert.AreEqual("scripts/Logs/composition.jsonl", startup.Data["logPath"]);
            Assert.AreEqual(File.GetLastWriteTimeUtc(typeof(ThorController).Assembly.Location).ToString("O"), startup.Data["buildUtc"]);
            Assert.AreEqual(typeof(Script).Assembly.GetName().Version.ToString(), startup.Data["apiVersion"]);
            StringAssert.Contains((string)log.Last("gameplay.snapshot").Data["state"], "attached=False");
            Assert.AreEqual(Logger.DroppedRecords, log.Last("performance").Data["droppedLogRecords"]);
            controller.Abort();
            Assert.AreEqual(Logger.DroppedRecords, log.Last("session.end").Data["droppedLogRecords"]);
            Assert.IsFalse(log.Records.Any(record => record.Name == "cleanup.exception" || record.Name == "tick.exception"));
            Assert.HasCount(0, audio.Clips);
            Assert.HasCount(0, game.Calls);
            Assert.HasCount(0, game.Native.Calls);
        }

        [TestMethod]
        public void ConsoleVisibilityBridgeTracksOnlyTheCurrentConsoleAndDisposal()
        {
            Assert.IsFalse(Thor.Thor.ReadConsoleOpen());
            using (var console = new ConsoleFixture())
            {
                Assert.IsFalse(Thor.Thor.ReadConsoleOpen());
                console.Model.ShowConsole(true);
                Assert.IsTrue(Thor.Thor.ReadConsoleOpen());
                console.Model.ShowConsole(false);
                Assert.IsFalse(Thor.Thor.ReadConsoleOpen());
            }
            Assert.IsFalse(Thor.Thor.ReadConsoleOpen());
            Assert.IsNull(DeveloperConsole.DeveloperConsole.Instance);
            Assert.HasCount(0, DeveloperConsole.DeveloperConsole.RegisteredScripts);
        }
    }
}
