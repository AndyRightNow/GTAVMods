using ADModUtils;
using Control = GTA.Control;

using GTA.Native;
using GTA;
using GTA.Math;
using Thor;
using GTAVMods.Tests.Support;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using System;

namespace GTAVMods.Tests.PlayerIntegration.Diagnostics
{
    [TestClass, TestCategory("PlayerIntegration")]
    public class LogStorageTests
    {
        [TestMethod]
        public void AssetAndAnimationDiagnosticsReachSessionLogWithoutPreventingThrow()
        {
            var s = new PlayerSessionFixture();
            s.Frame();
            var startup = s.Log.Last("session.start");
            Assert.AreEqual(s.Log.Writer.FilePath, startup.Data["logPath"]);
            Assert.AreEqual(File.GetLastWriteTimeUtc(typeof(ThorController).Assembly.Location).ToString("O"), startup.Data["buildUtc"]);
            Assert.AreEqual(typeof(Script).Assembly.GetName().Version.ToString(), startup.Data["apiVersion"]);
            Assert.AreEqual(0, s.Log.Last("performance").Data["droppedLogRecords"]);
            var created = s.Log.Records.Where(record => record.Name == "asset.weapon.created").ToArray();
            CollectionAssert.AreEqual(s.CreatedWeapons.ToArray(),
                created.Select(record => (int)record.Data["handle"]).ToArray());
            Assert.HasCount(2, created);
            Assert.IsTrue(created.All(record => record.Level == "INFO" && (string)record.Data["weapon"] == "Hammer"));
            Assert.HasCount(2, s.Log.Records.Where(record => record.Name == "asset.weapon.request").ToArray());

            s.Game.Native.Returns(Hash.HAS_ANIM_DICT_LOADED, false);
            s.Game.Host.OnYield = () => s.Game.Host.GameTime = unchecked(s.Game.Host.GameTime + 500);
            s.Frame(16, Keys.T, aim: true);
            var dictionary = (string)s.Game.Native.Calls.Last(call => call.Hash == Hash.REQUEST_ANIM_DICT).Arguments[0];
            var warning = s.Log.Last("animation.timeout." + dictionary);
            Assert.AreEqual("WARN", warning.Level);
            Assert.AreEqual(dictionary, warning.Data["dictionary"]);
            Assert.IsFalse(string.IsNullOrEmpty((string)warning.Data["animation"]));
            Assert.IsTrue(s.Game.Calls.Any(call => call.Name == "host.yield"),
                "Waiting for animation streaming must yield to the game before falling back.");
            Assert.IsFalse(s.Game.Native.Calls.Any(call => call.Hash == Hash.TASK_PLAY_ANIM));
            Assert.AreEqual(WeaponHash.Unarmed, s.Game.Entity(10).CurrentWeapon);
            Assert.IsTrue(Vector3.Dot(s.Game.Entity(20).Velocity, s.Player.Forward) > 0,
                "An unavailable animation must still release the hammer forwards.");
            s.Controller.Abort();
            Assert.AreEqual(0, s.Log.Last("session.end").Data["droppedLogRecords"]);
            s.AssertNoErrors();
            s.AssertReleased();
        }

        [TestMethod]
        public void ReadOnlyScriptsKeepThePlayersSnapshotInTheFallbackDirectory()
        {
            var storage = new MemoryLogStorage();
            storage.BeforeCreate = path => { if (path.Contains("Thor-player-scripts")) throw new UnauthorizedAccessException("read-only scripts"); };
            var observed = new SessionObservationLog(storage);
            var s = new PlayerSessionFixture(log: observed);
            s.Frame(0);
            s.Controller.KeyDown(new KeyEventArgs(Keys.F9));
            Assert.IsTrue(observed.Session.FilePath.Contains("Thor-player-local"));
            Assert.AreEqual(0L, observed.Writer.DroppedRecords);
            Assert.AreEqual("manual-F9", observed.Last("gameplay.snapshot").Data["reason"]);
            Assert.AreEqual(WeaponHash.Hammer, s.Player.CurrentWeapon);
            Assert.IsTrue(observed.Records.Any(record => record.Name == "log.recovered"));
            s.Controller.Abort();
        }

        [TestMethod]
        public void LockedLogRollsToANewPartAndPreservesThePlayersThrow()
        {
            var observed = new SessionObservationLog();
            var s = new PlayerSessionFixture(log: observed);
            s.Frame(0);
            string locked = observed.Session.FilePath;
            observed.Storage.BeforeAppend = path => { if (path == locked) throw new IOException("sharing violation"); };
            s.Throw();
            Assert.AreNotEqual(locked, observed.Session.FilePath);
            string recovered = observed.Session.FilePath;
            Assert.AreEqual(WeaponHash.Unarmed, s.Player.CurrentWeapon);
            Assert.HasCount(1, observed.Records.Where(record => record.Name == "log.recovered").ToArray());
            s.Controller.KeyDown(new KeyEventArgs(Keys.F9));
            Assert.AreEqual(recovered, observed.Session.FilePath);
            Assert.AreEqual("manual-F9", observed.Last("gameplay.snapshot").Data["reason"]);
            Assert.AreEqual(0L, observed.Writer.DroppedRecords);
            s.Controller.Abort();
        }

        [TestMethod]
        public void FailedStorageBacksOffWhileRecallContinuesAndNextSnapshotReportsLostRecords()
        {
            var observed = new SessionObservationLog();
            var s = new PlayerSessionFixture(log: observed);
            s.DropHammer(new GTA.Math.Vector3(0, 20, 0));
            observed.Storage.BeforeAppend = path => throw new IOException("disk unavailable");
            observed.Storage.BeforeCreate = path => throw new UnauthorizedAccessException("disk unavailable");
            s.Controller.KeyDown(new KeyEventArgs(Keys.F9));
            int attempts = observed.Storage.Creates;
            long lost = observed.Writer.DroppedRecords;
            observed.Clock.ElapsedMilliseconds = 4999;
            s.Recall();
            s.Controller.KeyDown(new KeyEventArgs(Keys.F9));
            Assert.AreEqual(attempts, observed.Storage.Creates, "Frames within backoff must not keep probing the disk.");
            Assert.IsTrue(observed.Writer.DroppedRecords > lost);
            Assert.IsTrue(s.Game.Entity(20).Velocity.Length() > 0, "A failed diagnostic write cannot stop recall.");
            long expectedLost = observed.Writer.DroppedRecords;
            for (int snapshot = 0; snapshot < 3; snapshot++)
            {
                // At the same instant the key diagnostic is throttled, but every F9
                // still requests a snapshot. Count those failed requests independently.
                s.Controller.KeyDown(new KeyEventArgs(Keys.F9));
                expectedLost++;
            }
            Assert.AreEqual(expectedLost, observed.Writer.DroppedRecords);
            observed.Storage.BeforeAppend = null;
            observed.Storage.BeforeCreate = null;
            observed.Clock.ElapsedMilliseconds = 5000;
            s.Controller.KeyDown(new KeyEventArgs(Keys.F9));
            Assert.AreEqual("manual-F9", observed.Last("gameplay.snapshot").Data["reason"]);
            Assert.AreEqual(expectedLost.ToString(CultureInfo.InvariantCulture),
                LogJson.Text(observed.Storage.Records.Last(), "droppedRecords"));
            s.Controller.Abort();
        }

        [TestMethod]
        public void LongPlayRotatesLogsAndPreservesSnapshotsWhenAnOldFileIsLocked()
        {
            var observed = new SessionObservationLog(maxBytes: 1024, retainedFiles: 2);
            observed.Storage.Existing.Add(new LogFile("newest", observed.Clock.UtcNow));
            observed.Storage.Existing.Add(new LogFile("locked", observed.Clock.UtcNow.AddDays(-1)));
            observed.Storage.Existing.Add(new LogFile("oldest", observed.Clock.UtcNow.AddDays(-2)));
            var deletionAttempts = new List<string>();
            observed.Storage.BeforeDelete = path =>
            {
                deletionAttempts.Add(path);
                if (path == "locked") throw new IOException("old file locked");
            };
            var s = new PlayerSessionFixture(log: observed);
            s.Frame(0);
            string first = observed.Session.FilePath;
            s.Controller.KeyDown(new KeyEventArgs(Keys.F9));
            s.Controller.KeyDown(new KeyEventArgs(Keys.F9));
            Assert.AreNotEqual(first, observed.Session.FilePath);
            Assert.IsFalse(deletionAttempts.Contains("newest"));
            Assert.IsTrue(deletionAttempts.Contains("locked"), "Retention must encounter the locked old file.");
            Assert.IsTrue(observed.Storage.Deleted.Contains("oldest"));
            Assert.AreEqual("manual-F9", observed.Last("gameplay.snapshot").Data["reason"]);
            Assert.AreEqual(0L, observed.Writer.DroppedRecords);
            Assert.AreEqual(WeaponHash.Hammer, s.Player.CurrentWeapon);
            s.Controller.Abort();
        }

        [TestMethod]
        public void CollectingLogsDuringPlayDoesNotBlockSnapshotsAndRotationPreservesUnrelatedFiles()
        {
            string directory = Path.Combine(Path.GetTempPath(), "Thor-player-files-" + Guid.NewGuid());
            Directory.CreateDirectory(directory);
            string unrelated = Path.Combine(directory, "another-mod.log");
            File.WriteAllText(unrelated, "preserve");
            try
            {
                var observed = new SessionObservationLog(maxBytes: 2048, retainedFiles: 3, realDirectory: directory);
                var s = new PlayerSessionFixture(log: observed);
                s.Frame(0);
                using (var reader = new FileStream(observed.Session.FilePath, FileMode.Open, FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete))
                {
                    for (int i = 0; i < 20; i++) s.Controller.KeyDown(new KeyEventArgs(Keys.F9));
                    Assert.IsTrue(reader.Length > 0);
                }
                Assert.HasCount(3, Directory.GetFiles(directory, "Thor-*.jsonl"));
                Assert.AreEqual("preserve", File.ReadAllText(unrelated));
                Assert.AreEqual(0L, observed.Writer.DroppedRecords);
                Assert.AreEqual("manual-F9",
                    LogJson.Text(File.ReadAllLines(observed.Session.FilePath).Select(LogJson.Parse).Last(), "data/reason"));
                s.Throw();
                Assert.AreEqual(WeaponHash.Unarmed, s.Player.CurrentWeapon);
                s.Controller.Abort();
            }
            finally { Directory.Delete(directory, true); }
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
    }
}
