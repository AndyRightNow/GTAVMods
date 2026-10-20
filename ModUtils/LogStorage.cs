using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace ADModUtils
{
    public interface ILogClock
    {
        DateTime UtcNow { get; }
        long ElapsedMilliseconds { get; }
    }

    public sealed class LogClock : ILogClock
    {
        private readonly Stopwatch elapsed = Stopwatch.StartNew();
        public DateTime UtcNow => DateTime.UtcNow;
        public long ElapsedMilliseconds => elapsed.ElapsedMilliseconds;
    }

    public sealed class LogFile
    {
        public LogFile(string path, DateTime lastWriteTimeUtc)
        {
            Path = path;
            LastWriteTimeUtc = lastWriteTimeUtc;
        }
        public string Path { get; }
        public DateTime LastWriteTimeUtc { get; }
    }

    public interface ILogStorage
    {
        void CreateDirectory(string path);
        void Append(string path, byte[] bytes);
        IEnumerable<LogFile> GetLogFiles(string directory);
        void Delete(string path);
    }

    public sealed class FileLogStorage : ILogStorage
    {
        public void CreateDirectory(string path) => Directory.CreateDirectory(path);
        public void Append(string path, byte[] bytes)
        {
            using (var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete))
                stream.Write(bytes, 0, bytes.Length);
        }
        public IEnumerable<LogFile> GetLogFiles(string directory) =>
            new DirectoryInfo(directory).GetFiles("Thor-*.jsonl")
                .Select(file => new LogFile(file.FullName, file.LastWriteTimeUtc));
        public void Delete(string path) => File.Delete(path);
    }
}
