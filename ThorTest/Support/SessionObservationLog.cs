using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml;
using ADModUtils;

namespace GTAVMods.Tests.Support
{
    // An observation of bytes emitted by the real logger, never an IEventLog substitute.
    internal sealed class SessionObservationLog
    {
        public readonly MemoryLogStorage Storage;
        public readonly ManualLogClock Clock;
        public readonly DiagnosticLog Writer;
        public readonly SessionLog Session;
        private readonly string realDirectory;
        private readonly bool useDefaultLogger;
        private readonly string initialFile;
        private readonly int initialLines;

        public SessionObservationLog(MemoryLogStorage storage = null, ManualLogClock clock = null,
            long maxBytes = 5 * 1024 * 1024, int retainedFiles = 10, string realDirectory = null,
            bool useDefaultLogger = false)
        {
            this.realDirectory = realDirectory;
            this.useDefaultLogger = useDefaultLogger;
            Storage = storage ?? new MemoryLogStorage();
            Clock = clock ?? new ManualLogClock();
            if (useDefaultLogger)
            {
                Writer = Logger.Default;
                initialFile = Writer.FilePath;
                initialLines = File.Exists(initialFile) ? File.ReadAllLines(initialFile).Length : 0;
                return;
            }
            Session = new SessionLog(realDirectory ?? Path.Combine(Path.GetTempPath(), "Thor-player-scripts", "Logs"),
                realDirectory ?? Path.Combine(Path.GetTempPath(), "Thor-player-local", "Logs"),
                realDirectory == null ? (ILogStorage)Storage : new FileLogStorage(), Clock, maxBytes, retainedFiles);
            Writer = new DiagnosticLog(Session, () => Clock.ElapsedMilliseconds);
        }

        public EventRecord[] Records => (useDefaultLogger ?
            File.ReadAllLines(Writer.FilePath).Skip(Writer.FilePath == initialFile ? initialLines : 0).Select(LogJson.Parse) :
            realDirectory == null ? Storage.Records :
            Directory.GetFiles(realDirectory, "Thor-*.jsonl").SelectMany(File.ReadAllLines).Select(LogJson.Parse))
            .Select(Read).ToArray();
        public EventRecord Last(string name) => Records.Last(record => record.Name == name);

        private static EventRecord Read(XmlElement record)
        {
            var data = new Dictionary<string, object>();
            if (record["data"] != null)
                foreach (XmlElement field in record["data"].ChildNodes)
                    data[field.Name] = Value(field);
            return new EventRecord { Name = record["event"].InnerText, Level = record["level"].InnerText, Data = data };
        }

        private static object Value(XmlElement value)
        {
            switch (value.GetAttribute("type"))
            {
                case "null": return null;
                case "boolean": return bool.Parse(value.InnerText);
                case "number":
                    if (int.TryParse(value.InnerText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int integer)) return integer;
                    if (long.TryParse(value.InnerText, NumberStyles.Integer, CultureInfo.InvariantCulture, out long wide)) return wide;
                    return double.Parse(value.InnerText, CultureInfo.InvariantCulture);
                default: return value.InnerText;
            }
        }
    }
}
