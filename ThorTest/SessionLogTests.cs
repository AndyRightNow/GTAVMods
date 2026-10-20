using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading.Tasks;
using System.Xml;
using ADModUtils;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ModUtilsTest
{
    [TestClass]
    public class SessionLogTests
    {
        private string folder;
        [TestInitialize] public void Setup() { folder = Path.Combine(Path.GetTempPath(), "ThorLogTests-" + Guid.NewGuid()); Directory.CreateDirectory(folder); }
        [TestCleanup] public void Cleanup() { Directory.Delete(folder, true); }
        private static void AssertJson(string line)
        {
            using (var reader = JsonReaderWriterFactory.CreateJsonReader(Encoding.UTF8.GetBytes(line), XmlDictionaryReaderQuotas.Max))
                while (reader.Read()) { }
        }
        [TestMethod] public void JsonEscapesExceptionsAndNonFiniteValues()
        {
            var log = new SessionLog(folder, folder);
            log.Write("ERROR", "failure", new Dictionary<string, object> { { "exception", "path=\"C:\\game\"\r\n\tline\u0000" }, { "speed", float.NaN }, { "flag", true } });
            var lines = File.ReadAllLines(log.FilePath);
            Assert.HasCount(1, lines);
            AssertJson(lines[0]);
            StringAssert.Contains(lines[0], "\\u000a");
            StringAssert.Contains(lines[0], "\"flag\":true");
        }
        [TestMethod] public void UnwritableDirectoryFallsBackWithoutLosingRecord()
        {
            string blocked = Path.Combine(folder, "file-not-directory"); File.WriteAllText(blocked, "blocked");
            var log = new SessionLog(blocked, Path.Combine(folder, "fallback"));
            log.Write("INFO", "survived");
            Assert.AreEqual(0L, log.DroppedRecords);
            StringAssert.Contains(File.ReadAllText(log.FilePath), "survived");
        }
        [TestMethod] public void StorageFailureDoesNotEscapeAndCountsLoss()
        {
            string blocked = Path.Combine(folder, "file-not-directory"); File.WriteAllText(blocked, "blocked");
            var log = new SessionLog(blocked, blocked);
            log.Write("ERROR", "one"); log.Write("ERROR", "two");
            Assert.AreEqual(2L, log.DroppedRecords);
        }
        [TestMethod] public void RotationBoundsRetentionAndPreservesOtherFiles()
        {
            var log = new SessionLog(folder, folder, 1024, 3);
            var unrelated = Path.Combine(folder, "user.log"); File.WriteAllText(unrelated, "keep");
            for (int i = 0; i < 30; i++) log.Write("INFO", "rotation", new Dictionary<string, object> { { "message", new string('x', 250) } });
            Assert.IsTrue(Directory.GetFiles(folder, "Thor-*.jsonl").Length <= 3);
            Assert.IsTrue(File.Exists(unrelated));
            foreach(var file in Directory.GetFiles(folder, "Thor-*.jsonl")) foreach(var line in File.ReadAllLines(file)) AssertJson(line);
        }
        [TestMethod] public void ConcurrentWritersProduceCompleteUniqueRecords()
        {
            var log = new SessionLog(folder, folder);
            Parallel.For(0, 100, i => log.Write("INFO", "parallel", new Dictionary<string, object> { { "id", i } }));
            var lines = File.ReadAllLines(log.FilePath);
            Assert.HasCount(100, lines);
            Assert.HasCount(100, lines.Distinct().ToArray());
            foreach(var line in lines) AssertJson(line);
        }
    }
}
