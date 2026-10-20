using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace ADModUtils
{
    /// <summary>Crash-friendly JSON lines; no persistent handles or background threads.</summary>
    public sealed class SessionLog
    {
        private readonly object sync = new object();
        private readonly string directory, fallbackDirectory;
        private readonly long maxBytes;
        private readonly int retainedFiles;
        private readonly ILogClock clock;
        private readonly ILogStorage storage;
        private readonly string sessionId = Guid.NewGuid().ToString("N");
        private readonly int processId = Process.GetCurrentProcess().Id;
        private long sequence, length, retryAfterMs;
        private int part;
        private bool initialized;
        public string FilePath { get; private set; }
        public long DroppedRecords { get; private set; }

        public SessionLog(string directory, string fallbackDirectory, long maxBytes = 5 * 1024 * 1024, int retainedFiles = 10)
            : this(directory, fallbackDirectory, new FileLogStorage(), new LogClock(), maxBytes, retainedFiles)
        {
        }

        public SessionLog(string directory, string fallbackDirectory, ILogStorage storage, ILogClock clock,
            long maxBytes = 5 * 1024 * 1024, int retainedFiles = 10)
        {
            this.storage = storage ?? throw new ArgumentNullException(nameof(storage));
            this.clock = clock ?? throw new ArgumentNullException(nameof(clock));
            this.directory = directory;
            this.fallbackDirectory = fallbackDirectory;
            this.maxBytes = Math.Max(1024, maxBytes);
            this.retainedFiles = Math.Max(2, retainedFiles);
            FilePath = NewPath(directory);
        }

        private string NewPath(string folder) => Path.Combine(folder, "Thor-" +
            clock.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + "-" +
            processId + "-" + sessionId + "-" + (++part) + ".jsonl");

        public void Write(string level, string name, IDictionary<string, object> data = null)
        {
            lock (sync)
            {
                if (clock.ElapsedMilliseconds < retryAfterMs) { DroppedRecords++; return; }
                var row = new Dictionary<string, object> {
                    { "utc", clock.UtcNow.ToString("O", CultureInfo.InvariantCulture) },
                    { "elapsedMs", clock.ElapsedMilliseconds }, { "session", sessionId },
                    { "sequence", ++sequence }, { "process", processId },
                    { "thread", Thread.CurrentThread.ManagedThreadId }, { "level", level },
                    { "event", name }, { "droppedRecords", DroppedRecords }
                };
                string line = "{" + string.Join(",", row.Select(p => Quote(p.Key) + ":" + Value(p.Value))) +
                    ",\"data\":{" + string.Join(",", (data ?? new Dictionary<string, object>()).Select(p => Quote(p.Key) + ":" + Value(p.Value))) + "}}\n";
                var bytes = Encoding.UTF8.GetBytes(line);
                try
                {
                    if (!initialized) { Prepare(); initialized = true; }
                    if (length + bytes.Length > maxBytes) { FilePath = NewPath(Path.GetDirectoryName(FilePath)); length = 0; Prepare(); }
                    Append(bytes);
                }
                catch (Exception ex) when (IsStorageFailure(ex))
                {
                    // Retry a unique file, then an independent directory. Never crash the script for logging.
                    foreach (var folder in new[] { directory, fallbackDirectory })
                    {
                        try
                        {
                            FilePath = NewPath(folder); length = 0; Prepare();
                            Append(Encoding.UTF8.GetBytes("{\"level\":\"WARN\",\"event\":\"log.recovered\",\"reason\":" + Quote(ex.Message) + "}\n"));
                            Append(bytes); initialized = true; return;
                        }
                        catch (Exception retry) when (IsStorageFailure(retry)) { }
                    }
                    DroppedRecords++;
                    retryAfterMs = clock.ElapsedMilliseconds + 5000;
                }
            }
        }

        private static bool IsStorageFailure(Exception ex) => ex is IOException || ex is UnauthorizedAccessException;
        private void Append(byte[] bytes)
        {
            storage.Append(FilePath, bytes);
            length += bytes.Length;
        }
        private void Prepare()
        {
            var folder = Path.GetDirectoryName(FilePath);
            storage.CreateDirectory(folder);
            // Only our own logs; tolerate files held by another game/script domain.
            foreach (var old in storage.GetLogFiles(folder).Where(file => file.Path != FilePath)
                .OrderByDescending(f => f.LastWriteTimeUtc).Skip(retainedFiles - 1))
            {
                try { storage.Delete(old.Path); }
                catch (Exception ex) when (IsStorageFailure(ex)) { }
            }
        }
        private static string Value(object value)
        {
            if (value == null) return "null";
            if (value is bool b) return b ? "true" : "false";
            if (value is byte || value is sbyte || value is short || value is ushort || value is int || value is long || value is uint || value is ulong || value is decimal)
                return Convert.ToString(value, CultureInfo.InvariantCulture);
            if (value is float || value is double)
            {
                double d = Convert.ToDouble(value, CultureInfo.InvariantCulture);
                return double.IsNaN(d) || double.IsInfinity(d) ? Quote(d.ToString(CultureInfo.InvariantCulture)) : d.ToString("R", CultureInfo.InvariantCulture);
            }
            return Quote(Convert.ToString(value, CultureInfo.InvariantCulture));
        }
        private static string Quote(string value)
        {
            if (value == null) return "null";
            var result = new StringBuilder("\"");
            foreach (char c in value.Take(32768))
            {
                if (c == '"' || c == '\\') result.Append('\\').Append(c);
                else if (c < 32) result.Append("\\u").Append(((int)c).ToString("x4"));
                else result.Append(c);
            }
            return result.Append('"').ToString();
        }
    }
}
