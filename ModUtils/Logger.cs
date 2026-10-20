using System;
using System.Collections.Generic;
using System.IO;

namespace ADModUtils
{
    public static class Logger
    {
        private static DeveloperConsole.DeveloperConsole developerConsole;
        private static readonly SessionLog session = new SessionLog(
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Logs"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GTAVMods", "Logs"));
        private static readonly Dictionary<string, long> lastEvents = new Dictionary<string, long>();
        public static string LogFilePath => session.FilePath;
        public static long DroppedRecords => session.DroppedRecords;

        public static void Init(DeveloperConsole.DeveloperConsole console)
        {
            developerConsole = console;
            developerConsole?.PrintLine("Thor diagnostics: " + LogFilePath);
        }

        public static void Event(string level, string name, params object[] fields)
        {
            var data = new Dictionary<string, object>();
            for (int i = 0; i + 1 < fields.Length; i += 2)
                data[Convert.ToString(fields[i])] = fields[i + 1];
            session.Write(level, name, data);
        }

        public static void Throttled(string level, string name, int intervalMs, params object[] fields)
        {
            lock (lastEvents)
            {
                long now = System.Diagnostics.Stopwatch.GetTimestamp();
                if (lastEvents.TryGetValue(name, out var previous) &&
                    (now - previous) * 1000.0 / System.Diagnostics.Stopwatch.Frequency < intervalMs) return;
                lastEvents[name] = now;
            }
            Event(level, name, fields);
        }

        public static void Log(string level, params string[] message) =>
            Event(level, "message", "message", string.Join(" ", message));

        public static void LogConsole(string level, params string[] message)
        {
            Log(level, message);
            developerConsole?.PrintLine(level + " " + string.Join(" ", message));
        }
    }
}
