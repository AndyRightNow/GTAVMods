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

        public DiagnosticLog(SessionLog session, Func<long> milliseconds)
        {
            this.session = Require.NotNull(session, nameof(session));
            this.milliseconds = Require.NotNull(milliseconds, nameof(milliseconds));
        }

        public string FilePath => session.FilePath;
        public long DroppedRecords => session.DroppedRecords;

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

    }

    public static class Logger
    {
        private static readonly LogClock clock = new LogClock();
        public static DiagnosticLog Default { get; } = CreateDefault(
            AppDomain.CurrentDomain.BaseDirectory,
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), new FileLogStorage(), clock);

        public static DiagnosticLog CreateDefault(string baseDirectory, string localApplicationData, ILogStorage storage, ILogClock clock) =>
            new DiagnosticLog(new SessionLog(Path.Combine(baseDirectory, "Logs"),
                Path.Combine(localApplicationData, "GTAVMods", "Logs"), storage, clock),
                () => clock.ElapsedMilliseconds);
        public static string LogFilePath => Default.FilePath;
        public static long DroppedRecords => Default.DroppedRecords;

        public static void Init(DeveloperConsole.DeveloperConsole console) =>
            console.PrintLine("Thor diagnostics: " + Default.FilePath);
    }
}
