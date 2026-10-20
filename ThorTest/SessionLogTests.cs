using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using ADModUtils;
using GTA;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ModUtilsTest
{
    [TestClass, TestCategory("PureFunction")]
    public class JsonLogFormatTests
    {
        private sealed class UnavailableDiagnostic { public override string ToString() => null; }

        [TestMethod]
        public void JsonPreservesEscapedTextNumericTypesAndNonFiniteObservations()
        {
            const string text = "path=\"C:\\game\"\r\n\tline\u0000";
            var data = new Dictionary<string, object> {
                { "text", text }, { "yes", true }, { "no", false }, { "missing", null },
                { "byte", (byte)2 }, { "sbyte", (sbyte)-2 }, { "short", (short)-3 }, { "ushort", ushort.MaxValue },
                { "int", 4 }, { "long", 5000000000L }, { "uint", uint.MaxValue }, { "ulong", ulong.MaxValue },
                { "decimal", 1.25m }, { "float", 0.5f }, { "double", -1.75d }, { "precise", 1.2345678901234567d },
                { "nan", float.NaN }, { "positiveInfinity", double.PositiveInfinity }, { "negativeInfinity", double.NegativeInfinity }
            };
            var original = CultureInfo.CurrentCulture;
            System.Xml.XmlElement record;
            try
            {
                CultureInfo.CurrentCulture = new CultureInfo("fr-FR");
                record = LogJson.Parse(JsonLogFormat.Record(new Dictionary<string, object> { { "event", "snapshot" } }, data));
            }
            finally { CultureInfo.CurrentCulture = original; }
            Assert.AreEqual(text, LogJson.Text(record, "data/text"));
            Assert.AreEqual("true", LogJson.Text(record, "data/yes"));
            Assert.AreEqual("false", LogJson.Text(record, "data/no"));
            Assert.AreEqual("null", record["data"]["missing"].GetAttribute("type"));
            foreach (var key in new[] { "byte", "sbyte", "short", "ushort", "int", "long", "uint", "ulong", "decimal", "float", "double" })
            {
                Assert.AreEqual("number", record["data"][key].GetAttribute("type"));
                Assert.AreEqual(Convert.ToString(data[key], CultureInfo.InvariantCulture), record["data"][key].InnerText);
            }
            Assert.AreEqual(1.2345678901234567d, double.Parse(record["data"]["precise"].InnerText, CultureInfo.InvariantCulture));
            Assert.AreEqual("NaN", record["data"]["nan"].InnerText);
            Assert.AreEqual("Infinity", record["data"]["positiveInfinity"].InnerText);
            Assert.AreEqual("-Infinity", record["data"]["negativeInfinity"].InnerText);
        }

        [TestMethod]
        public void MissingDataAndUnavailableTextRemainValidJsonAndLongTextIsBounded()
        {
            var record = LogJson.Parse(JsonLogFormat.Record(new Dictionary<string, object> { { "level", null } },
                new Dictionary<string, object> { { "text", new string('x', 40000) }, { "unavailable", new UnavailableDiagnostic() } }));
            Assert.AreEqual(new string('x', 32768), record["data"]["text"].InnerText);
            Assert.AreEqual("null", record["level"].GetAttribute("type"));
            Assert.AreEqual("null", record["data"]["unavailable"].GetAttribute("type"));
            var absent = LogJson.Parse(JsonLogFormat.Record(new Dictionary<string, object>(), null));
            Assert.AreEqual("object", absent["data"].GetAttribute("type"));
            Assert.AreEqual(0, absent["data"].ChildNodes.Count);
        }
    }

    [TestClass, TestCategory("PlayerIntegration")]
    public class PlayerDiagnosticStorageTests
    {
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
            observed.Storage.BeforeAppend = null;
            observed.Storage.BeforeCreate = null;
            observed.Clock.ElapsedMilliseconds = 5000;
            s.Controller.KeyDown(new KeyEventArgs(Keys.F9));
            Assert.AreEqual("manual-F9", observed.Last("gameplay.snapshot").Data["reason"]);
            Assert.AreEqual(observed.Writer.DroppedRecords.ToString(CultureInfo.InvariantCulture),
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
            observed.Storage.BeforeDelete = path => { if (path == "locked") throw new IOException("old file locked"); };
            var s = new PlayerSessionFixture(log: observed);
            s.Frame(0);
            string first = observed.Session.FilePath;
            s.Controller.KeyDown(new KeyEventArgs(Keys.F9));
            s.Controller.KeyDown(new KeyEventArgs(Keys.F9));
            Assert.AreNotEqual(first, observed.Session.FilePath);
            Assert.IsFalse(observed.Storage.Deleted.Contains("newest"));
            Assert.IsFalse(observed.Storage.Deleted.Contains("locked"));
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
    }
}
