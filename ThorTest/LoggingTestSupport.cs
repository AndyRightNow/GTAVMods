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

}
