using System;
using System.Collections.Generic;
using System.IO;

namespace ADModUtils
{
    public interface IEventLog
    {
        void Event(string level, string name, params object[] fields);
        void Throttled(string level, string name, int intervalMs, params object[] fields);
    }

    public sealed class DiagnosticLog : IEventLog
    {
        private readonly SessionLog session;
        private readonly Func<long> milliseconds;
        private readonly Dictionary<string, long> lastEvents = new Dictionary<string, long>();
        private Action<string> print;

        public DiagnosticLog(SessionLog session, Func<long> milliseconds)
        {
            this.session = session ?? throw new ArgumentNullException(nameof(session));
            this.milliseconds = milliseconds ?? throw new ArgumentNullException(nameof(milliseconds));
        }

        public string FilePath => session.FilePath;
        public long DroppedRecords => session.DroppedRecords;

        public void AttachConsole(Action<string> writeLine)
        {
            print = writeLine;
            print?.Invoke("Thor diagnostics: " + FilePath);
        }

        public void Event(string level, string name, params object[] fields)
        {
            var data = new Dictionary<string, object>();
            for (int i = 0; i + 1 < fields.Length; i += 2)
                data[Convert.ToString(fields[i])] = fields[i + 1];
            session.Write(level, name, data);
        }

        public void Throttled(string level, string name, int intervalMs, params object[] fields)
        {
            lock (lastEvents)
            {
                long now = milliseconds();
                if (lastEvents.TryGetValue(name, out var previous) && now - previous < intervalMs) return;
                lastEvents[name] = now;
            }
            Event(level, name, fields);
        }

        public void Log(string level, params string[] message) =>
            Event(level, "message", "message", string.Join(" ", message));

        public void LogConsole(string level, params string[] message)
        {
            Log(level, message);
            print?.Invoke(level + " " + string.Join(" ", message));
        }
    }

    public static class Logger
    {
        private static readonly LogClock clock = new LogClock();
        public static DiagnosticLog Default { get; } = new DiagnosticLog(new SessionLog(
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Logs"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GTAVMods", "Logs")),
            () => clock.ElapsedMilliseconds);
        public static string LogFilePath => Default.FilePath;
        public static long DroppedRecords => Default.DroppedRecords;

        public static void Init(DeveloperConsole.DeveloperConsole console) =>
            Default.AttachConsole(console == null ? null : new Action<string>(console.PrintLine));
        public static void Event(string level, string name, params object[] fields) => Default.Event(level, name, fields);
        public static void Throttled(string level, string name, int intervalMs, params object[] fields) =>
            Default.Throttled(level, name, intervalMs, fields);
        public static void Log(string level, params string[] message) => Default.Log(level, message);
        public static void LogConsole(string level, params string[] message) => Default.LogConsole(level, message);
    }
}
