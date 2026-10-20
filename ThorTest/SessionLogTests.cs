using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using ADModUtils;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ModUtilsTest
{
    internal sealed class ManualLogClock : ILogClock
    {
        public DateTime UtcNow { get; set; } = new DateTime(2026, 10, 20, 1, 2, 3, DateTimeKind.Utc);
        public long ElapsedMilliseconds { get; set; }
    }

    internal sealed class MemoryLogStorage : ILogStorage
    {
        public readonly Dictionary<string, string> Files = new Dictionary<string, string>();
        public readonly List<LogFile> Existing = new List<LogFile>();
        public readonly List<string> Deleted = new List<string>();
        public Action<string> BeforeCreate;
        public Action<string> BeforeAppend;
        public Action<string> BeforeDelete;
        public int Creates;
        public void CreateDirectory(string path) { Creates++; BeforeCreate?.Invoke(path); }
        public void Append(string path, byte[] bytes)
        {
            BeforeAppend?.Invoke(path);
            Files.TryGetValue(path, out string existing);
            Files[path] = existing + Encoding.UTF8.GetString(bytes);
        }
        public IEnumerable<LogFile> GetLogFiles(string directory) => Existing;
        public void Delete(string path) { BeforeDelete?.Invoke(path); Deleted.Add(path); Files.Remove(path); }
        public XmlElement[] Records => Files.Values.SelectMany(value => value.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries)).Select(LogJson.Parse).ToArray();
    }

    internal static class LogJson
    {
        public static XmlElement Parse(string line)
        {
            var document = new XmlDocument();
            using (var reader = JsonReaderWriterFactory.CreateJsonReader(Encoding.UTF8.GetBytes(line), XmlDictionaryReaderQuotas.Max))
                document.Load(reader);
            return document.DocumentElement;
        }
        public static string Text(XmlElement record, string xpath) => record.SelectSingleNode(xpath).InnerText;
    }

    [TestClass]
    public class SessionLogTests
    {
        private sealed class UnavailableDiagnostic
        {
            public override string ToString() => null;
        }

        private string folder;
        [TestInitialize] public void Setup() { folder = Path.Combine(Path.GetTempPath(), "ThorLogTests-" + Guid.NewGuid()); Directory.CreateDirectory(folder); }
        [TestCleanup] public void Cleanup() { Directory.Delete(folder, true); }

        [TestMethod]
        public void JsonPreservesEscapesTypesAndInvariantNumbers()
        {
            var storage = new MemoryLogStorage();
            var clock = new ManualLogClock { ElapsedMilliseconds = 123 };
            var log = new SessionLog(folder, folder, storage, clock);
            const string text = "path=\"C:\\game\"\r\n\tline\u0000";
            var data = new Dictionary<string, object> {
                { "text", text }, { "yes", true }, { "no", false }, { "missing", null },
                { "byte", (byte)2 }, { "sbyte", (sbyte)-2 }, { "short", (short)-3 }, { "ushort", ushort.MaxValue }, { "int", 4 }, { "long", 5000000000L },
                { "uint", uint.MaxValue }, { "ulong", ulong.MaxValue }, { "decimal", 1.25m },
                { "float", 0.5f }, { "double", -1.75d }, { "preciseDouble", 1.2345678901234567d }, { "nan", float.NaN },
                { "positiveInfinity", double.PositiveInfinity }, { "negativeInfinity", double.NegativeInfinity }
            };
            CultureInfo original = CultureInfo.CurrentCulture;
            try { CultureInfo.CurrentCulture = new CultureInfo("fr-FR"); log.Write("ERROR", "failure", data); }
            finally { CultureInfo.CurrentCulture = original; }
            var record = storage.Records.Single();
            Assert.AreEqual(text, LogJson.Text(record, "data/text"));
            Assert.AreEqual("true", LogJson.Text(record, "data/yes"));
            Assert.AreEqual("false", LogJson.Text(record, "data/no"));
            Assert.AreEqual("null", record.SelectSingleNode("data/missing").Attributes["type"].Value);
            foreach (string name in new[] { "byte", "sbyte", "short", "ushort", "int", "long", "uint", "ulong", "decimal", "float", "double" })
            {
                Assert.AreEqual("number", record.SelectSingleNode("data/" + name).Attributes["type"].Value);
                Assert.AreEqual(Convert.ToString(data[name], CultureInfo.InvariantCulture), LogJson.Text(record, "data/" + name));
            }
            Assert.AreEqual("NaN", LogJson.Text(record, "data/nan"));
            Assert.AreEqual("Infinity", LogJson.Text(record, "data/positiveInfinity"));
            Assert.AreEqual("-Infinity", LogJson.Text(record, "data/negativeInfinity"));
            foreach (string name in new[] { "nan", "positiveInfinity", "negativeInfinity" })
                Assert.AreEqual("string", record.SelectSingleNode("data/" + name).Attributes["type"].Value,
                    "JSON has no nonfinite numeric literals; these diagnostics must remain strings.");
            Assert.AreEqual((double)data["preciseDouble"],
                double.Parse(LogJson.Text(record, "data/preciseDouble"), CultureInfo.InvariantCulture),
                "Finite double values must survive serialization without losing significant digits.");
            Assert.AreEqual("2026-10-20T01:02:03.0000000Z", LogJson.Text(record, "utc"));
            Assert.AreEqual("123", LogJson.Text(record, "elapsedMs"));
            Assert.AreEqual("1", LogJson.Text(record, "sequence"));
            Assert.AreEqual("0", LogJson.Text(record, "droppedRecords"));
            Assert.AreEqual(Process.GetCurrentProcess().Id.ToString(CultureInfo.InvariantCulture), LogJson.Text(record, "process"));
            Assert.AreEqual(Thread.CurrentThread.ManagedThreadId.ToString(CultureInfo.InvariantCulture), LogJson.Text(record, "thread"));
            Assert.IsTrue(Guid.TryParseExact(LogJson.Text(record, "session"), "N", out _));
        }

        [TestMethod]
        public void StringPayloadIsBoundedAndAbsentDataIsAnEmptyObject()
        {
            var storage = new MemoryLogStorage();
            var log = new SessionLog(folder, folder, storage, new ManualLogClock());
            log.Write(null, null, new Dictionary<string, object> {
                { "text", new string('x', 40000) }, { "unavailable", new UnavailableDiagnostic() }
            });
            log.Write("INFO", "empty");
            var records = storage.Records;
            Assert.AreEqual(new string('x', 32768), LogJson.Text(records[0], "data/text"));
            Assert.AreEqual("null", records[0]["level"].GetAttribute("type"));
            Assert.AreEqual("null", records[0]["data"]["unavailable"].GetAttribute("type"));
            Assert.AreEqual("object", records[1]["data"].GetAttribute("type"));
            Assert.AreEqual(0, records[1]["data"].ChildNodes.Count);
        }

        [TestMethod]
        public void UnwritablePrimaryDirectoryUsesFallbackAndPreservesRecord()
        {
            string blocked = Path.Combine(folder, "file-not-directory"); File.WriteAllText(blocked, "blocked");
            string fallback = Path.Combine(folder, "fallback");
            var log = new SessionLog(blocked, fallback);
            log.Write("INFO", "survived");
            Assert.AreEqual(fallback, Path.GetDirectoryName(log.FilePath));
            Assert.AreEqual(0L, log.DroppedRecords);
            var records = File.ReadAllLines(log.FilePath).Select(LogJson.Parse).ToArray();
            CollectionAssert.AreEqual(new[] { "log.recovered", "survived" }, records.Select(record => LogJson.Text(record, "event")).ToArray());
            Assert.AreEqual("blocked", File.ReadAllText(blocked));
        }

        [TestMethod]
        public void LockedFileRecoveryKeepsSubsequentWritesInTheNewSessionPart()
        {
            var log = new SessionLog(folder, folder);
            log.Write("INFO", "before-lock");
            string original = log.FilePath;
            using (var locked = new FileStream(original, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                log.Write("INFO", "after-lock");
                Assert.AreNotEqual(original, log.FilePath);
                string recovered = log.FilePath;
                log.Write("INFO", "again");
                Assert.AreEqual(recovered, log.FilePath);
                CollectionAssert.AreEqual(new[] { "log.recovered", "after-lock", "again" },
                    File.ReadAllLines(recovered).Select(LogJson.Parse).Select(record => LogJson.Text(record, "event")).ToArray());
            }
            Assert.AreEqual(0L, log.DroppedRecords);
        }

        [TestMethod]
        public void LiveLogReaderDoesNotPreventAppendingTheNextRecord()
        {
            string path = Path.Combine(folder, "Thor-live.jsonl");
            File.WriteAllText(path, "first\n");
            using (var reader = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                new FileLogStorage().Append(path, Encoding.UTF8.GetBytes("second\n"));
                using (var text = new StreamReader(reader))
                    Assert.AreEqual("first\nsecond\n", text.ReadToEnd());
            }
        }

        [TestMethod]
        public void FailedStorageBacksOffUntilExactDeadlineThenReportsLoss()
        {
            var storage = new MemoryLogStorage { BeforeCreate = path => throw new UnauthorizedAccessException("denied") };
            var clock = new ManualLogClock();
            var log = new SessionLog(folder, folder, storage, clock);
            log.Write("ERROR", "lost");
            Assert.AreEqual(3, storage.Creates);
            clock.ElapsedMilliseconds = 4999; log.Write("ERROR", "backoff");
            Assert.AreEqual(3, storage.Creates);
            Assert.AreEqual(2L, log.DroppedRecords);
            storage.BeforeCreate = null;
            clock.ElapsedMilliseconds = 5000; log.Write("INFO", "recovered");
            Assert.AreEqual(4, storage.Creates);
            Assert.AreEqual("recovered", LogJson.Text(storage.Records.Single(), "event"));
            Assert.AreEqual("2", LogJson.Text(storage.Records.Single(), "droppedRecords"));
            Assert.AreEqual("2", LogJson.Text(storage.Records.Single(), "sequence"));
        }

        [TestMethod]
        public void AppendFailureRetriesUniqueFileAndDoesNotDoubleCountTheEvent()
        {
            int attempts = 0;
            var storage = new MemoryLogStorage { BeforeAppend = path => { if (++attempts == 1) throw new IOException("locked"); } };
            var log = new SessionLog(folder, folder, storage, new ManualLogClock());
            string original = log.FilePath;
            log.Write("WARN", "kept");
            Assert.AreNotEqual(original, log.FilePath);
            Assert.AreEqual(3, attempts);
            CollectionAssert.AreEqual(new[] { "log.recovered", "kept" }, storage.Records.Select(record => LogJson.Text(record, "event")).ToArray());
            Assert.AreEqual(0L, log.DroppedRecords);
            Assert.AreEqual(2, storage.Creates);
            string recovered = log.FilePath;
            log.Write("INFO", "next");
            Assert.AreEqual(recovered, log.FilePath);
            Assert.AreEqual(2, storage.Creates, "A recovered session must not rescan retention on every write.");
            Assert.AreEqual("next", LogJson.Text(storage.Records.Last(), "event"));
        }

        [TestMethod]
        public void RotationOccursOnlyWhenTheNextUtf8RecordWouldExceedTheLimit()
        {
            var storage = new MemoryLogStorage();
            var log = new SessionLog(folder, folder, storage, new ManualLogClock(), 4096);
            var payload = new Dictionary<string, object> { { "message", "" } };
            log.Write("INFO", "boundary", payload);
            string first = log.FilePath;
            int emptyRecordBytes = Encoding.UTF8.GetByteCount(storage.Files[first]);
            // Each accented character takes two UTF-8 bytes. The fixed-width session IDs
            // and one-digit sequence numbers leave the two record envelopes the same size.
            payload["message"] = new string('é', (4096 - 2 * emptyRecordBytes) / 2);
            log.Write("INFO", "boundary", payload);
            Assert.AreEqual(first, log.FilePath, "Exactly filling the limit must not rotate early.");
            Assert.AreEqual(4096, Encoding.UTF8.GetByteCount(storage.Files[first]));
            Assert.AreEqual(1, storage.Creates, "Retention preparation belongs to each file, not each record.");
            payload["message"] = "";
            log.Write("INFO", "boundary", payload);
            Assert.AreNotEqual(first, log.FilePath);
            Assert.AreEqual(2, storage.Creates);
            Assert.HasCount(2, storage.Files);
            Assert.AreEqual(4096, Encoding.UTF8.GetByteCount(storage.Files[first]));
            Assert.AreEqual("3", LogJson.Text(LogJson.Parse(storage.Files[log.FilePath]), "sequence"));
        }

        [TestMethod]
        public void RotationBoundsRetentionAndKeepsTheLatestPayload()
        {
            var log = new SessionLog(folder, folder, 1024, 3);
            string unrelated = Path.Combine(folder, "user.log"); File.WriteAllText(unrelated, "keep");
            for (int i = 0; i < 30; i++) log.Write("INFO", "rotation", new Dictionary<string, object> { { "id", i }, { "message", new string('x', 500) } });
            var files = Directory.GetFiles(folder, "Thor-*.jsonl");
            Assert.HasCount(3, files);
            Assert.AreEqual("keep", File.ReadAllText(unrelated));
            Assert.AreEqual(0L, log.DroppedRecords);
            Assert.AreEqual("29", LogJson.Text(File.ReadAllLines(log.FilePath).Select(LogJson.Parse).Last(), "data/id"));
            foreach (string file in files) Assert.IsTrue(File.ReadAllLines(file).All(line => LogJson.Text(LogJson.Parse(line), "event") == "rotation"));
        }

        [TestMethod]
        public void RetentionSkipsActiveFileAndToleratesAnOldFileLock()
        {
            var clock = new ManualLogClock();
            var storage = new MemoryLogStorage();
            var log = new SessionLog(folder, folder, storage, clock, 0, 0);
            storage.Existing.Add(new LogFile(log.FilePath, clock.UtcNow.AddDays(-3)));
            storage.Existing.Add(new LogFile("newest", clock.UtcNow));
            storage.Existing.Add(new LogFile("locked", clock.UtcNow.AddDays(-1)));
            storage.Existing.Add(new LogFile("oldest", clock.UtcNow.AddDays(-2)));
            storage.BeforeDelete = path => { if (path == "locked") throw new IOException("locked"); };
            log.Write("INFO", "retained");
            CollectionAssert.AreEqual(new[] { "oldest" }, storage.Deleted);
            Assert.AreEqual("retained", LogJson.Text(storage.Records.Single(), "event"));
            Assert.AreEqual(0L, log.DroppedRecords);
        }

        [TestMethod]
        public void NonStorageFaultsAreNotMisreportedAsDiskLoss()
        {
            var storage = new MemoryLogStorage { BeforeCreate = path => throw new InvalidOperationException("programming error") };
            var log = new SessionLog(folder, folder, storage, new ManualLogClock());
            Assert.ThrowsExactly<InvalidOperationException>(() => log.Write("INFO", "failure"));
            Assert.AreEqual(0L, log.DroppedRecords);
            Assert.ThrowsExactly<ArgumentNullException>(() => new SessionLog(folder, folder, null, new ManualLogClock()));
            Assert.ThrowsExactly<ArgumentNullException>(() => new SessionLog(folder, folder, storage, null));
        }

        [TestMethod]
        public void ConcurrentWritersPersistEveryPayloadOnceInSequenceOrder()
        {
            var log = new SessionLog(folder, folder);
            Parallel.For(0, 100, i => log.Write("INFO", "parallel", new Dictionary<string, object> { { "id", i } }));
            var records = File.ReadAllLines(log.FilePath).Select(LogJson.Parse).ToArray();
            Assert.HasCount(100, records);
            CollectionAssert.AreEqual(Enumerable.Range(0, 100).ToArray(), records.Select(record => int.Parse(LogJson.Text(record, "data/id"))).OrderBy(id => id).ToArray());
            CollectionAssert.AreEqual(Enumerable.Range(1, 100).ToArray(), records.Select(record => int.Parse(LogJson.Text(record, "sequence"))).ToArray());
            Assert.HasCount(1, records.Select(record => LogJson.Text(record, "session")).Distinct().ToArray());
            Assert.AreEqual(0L, log.DroppedRecords);
        }

        [TestMethod]
        public void DiagnosticLoggerThrottlesEachNameAndReleasesAtExactInterval()
        {
            var storage = new MemoryLogStorage();
            var clock = new ManualLogClock { ElapsedMilliseconds = 5000 };
            var session = new SessionLog(folder, folder, storage, clock);
            var log = new DiagnosticLog(session, () => clock.ElapsedMilliseconds);
            var printed = new List<string>();
            log.AttachConsole(printed.Add);
            Assert.AreEqual("Thor diagnostics: " + log.FilePath, printed.Single());
            log.Throttled("WARN", "repeat", 1000, "value", 1);
            clock.ElapsedMilliseconds = 5999; log.Throttled("WARN", "repeat", 1000, "value", 2);
            log.Throttled("WARN", "other", 1000);
            clock.ElapsedMilliseconds = 6000; log.Throttled("WARN", "repeat", 1000, "value", 3);
            log.Event("INFO", "fields", "value", 1, "value", 2, "unpaired");
            log.LogConsole("INFO", "hello", "world");
            log.AttachConsole(null); log.LogConsole("INFO", "file-only");
            var records = storage.Records;
            CollectionAssert.AreEqual(new[] { "repeat", "other", "repeat", "fields", "message", "message" }, records.Select(record => LogJson.Text(record, "event")).ToArray());
            Assert.AreEqual("3", LogJson.Text(records[2], "data/value"));
            Assert.AreEqual("2", LogJson.Text(records[3], "data/value"));
            Assert.AreEqual(1, records[3]["data"].ChildNodes.Count);
            Assert.AreEqual("hello world", LogJson.Text(records[4], "data/message"));
            Assert.AreEqual("INFO hello world", printed[1]);
            Assert.HasCount(2, printed);
            Assert.AreEqual(0L, log.DroppedRecords);
            Assert.ThrowsExactly<ArgumentNullException>(() => new DiagnosticLog(null, () => 0));
            Assert.ThrowsExactly<ArgumentNullException>(() => new DiagnosticLog(session, null));
        }

        [TestMethod]
        public void StaticLoggerFacadeWritesStructuredEventsAndOnlyMirrorsConsoleMessages()
        {
            string marker = "facade-" + Guid.NewGuid().ToString("N");
            using (var console = new ConsoleFixture())
            {
                try
                {
                    Logger.Init(console.Model);
                    Logger.Event("INFO", marker, "value", 42);
                    Logger.Throttled("WARN", marker + "-throttle", 1000, "value", 7);
                    Logger.Log("INFO", marker, "plain");
                    Logger.LogConsole("WARN", marker, "console");
                    Logger.Init(null);
                    Logger.LogConsole("INFO", marker, "file-only");
                    console.Press(System.Windows.Forms.Keys.F8);
                    console.Draw();
                    Assert.IsTrue(console.Platform.Text.Any(line => line.Text.Contains(marker + " console")));
                    Assert.IsFalse(console.Platform.Text.Any(line => line.Text.Contains(marker + " file-only")));
                    var records = File.ReadAllLines(Logger.LogFilePath).Select(LogJson.Parse).ToArray();
                    Assert.AreEqual("42", LogJson.Text(records.Single(record => LogJson.Text(record, "event") == marker), "data/value"));
                    Assert.AreEqual("7", LogJson.Text(records.Single(record => LogJson.Text(record, "event") == marker + "-throttle"), "data/value"));
                    var messages = records.Where(record => LogJson.Text(record, "event") == "message")
                        .Select(record => LogJson.Text(record, "data/message")).Where(message => message.StartsWith(marker)).ToArray();
                    CollectionAssert.AreEqual(new[] { marker + " plain", marker + " console", marker + " file-only" }, messages);
                    Assert.AreEqual(0L, Logger.DroppedRecords);
                }
                finally { Logger.Init(null); }
            }
        }

        [TestMethod]
        public void DefaultLoggerUsesScriptLogsAndRecoversInTheApplicationSpecificFallback()
        {
            string scripts = Path.Combine(folder, "scripts");
            string localData = Path.Combine(folder, "local-data");
            string primary = Path.Combine(scripts, "Logs");
            string fallback = Path.Combine(localData, "GTAVMods", "Logs");
            var storage = new MemoryLogStorage();
            var clock = new ManualLogClock { ElapsedMilliseconds = 4000 };
            var log = Logger.CreateDefault(scripts, localData, storage, clock);
            Assert.AreEqual(primary, Path.GetDirectoryName(log.FilePath));
            storage.BeforeCreate = path => { if (path == primary) throw new UnauthorizedAccessException("read-only scripts"); };
            log.Throttled("WARN", "retry", 1000);
            Assert.AreEqual(fallback, Path.GetDirectoryName(log.FilePath));
            clock.ElapsedMilliseconds = 4999;
            log.Throttled("WARN", "retry", 1000);
            clock.ElapsedMilliseconds = 5000;
            log.Throttled("WARN", "retry", 1000);
            CollectionAssert.AreEqual(new[] { "log.recovered", "retry", "retry" },
                storage.Records.Select(record => LogJson.Text(record, "event")).ToArray());
            Assert.AreEqual(0L, log.DroppedRecords);
        }
    }
}
