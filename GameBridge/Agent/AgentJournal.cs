using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ADModUtils;

namespace GTAVMods.Agent
{
    public interface IAgentFiles
    {
        void Save(string path, string text);
    }

    public sealed class AgentFiles : IAgentFiles
    {
        public void Save(string path, string text)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string temporary = path + ".tmp";
            using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.Read))
            {
                byte[] bytes = new UTF8Encoding(false).GetBytes(text);
                stream.Write(bytes, 0, bytes.Length);
            }
            if (File.Exists(path)) File.Replace(temporary, path, null);
            else File.Move(temporary, path);
        }
    }

    /// <summary>Best-effort file diagnostics, never a prerequisite for ephemeral game actions.</summary>
    public sealed class AgentJournal
    {
        private readonly IAgentFiles files;
        private readonly DiagnosticLog log;
        private readonly object sync = new object();
        private readonly Queue<(string Name, string Json, bool File)> pending = new Queue<(string, string, bool)>();
        private readonly int maximumCharacters;
        private Task writer = Task.CompletedTask;
        private int characters;
        private long dropped;
        public AgentJournal(string directory, string session, IAgentFiles files, DiagnosticLog log, int maximumCharacters = 2 * 1024 * 1024)
        { Directory = directory; Session = session; this.files = files; this.log = log; this.maximumCharacters = maximumCharacters; }
        public string Directory { get; }
        public string Session { get; }
        public string LogPath => log.FilePath;
        public long DroppedRecords => Interlocked.Read(ref dropped) + log.DroppedRecords;
        public string Save(string name, object value) => SaveJson(name, Protocol.Encode(value));
        internal string SaveJson(string name, string json)
        {
            string path = Path.Combine(Directory, name + ".json");
            Enqueue(path, json, true);
            return path;
        }
        public void Record(string name, object data) => Enqueue(name, Protocol.Encode(data), false);
        private void Enqueue(string name, string json, bool file)
        {
            lock (sync)
            {
                if (characters + json.Length > maximumCharacters) { Interlocked.Increment(ref dropped); return; }
                characters += json.Length;
                pending.Enqueue((name, json, file));
                if (writer.IsCompleted) writer = Task.Run(WritePending);
            }
        }
        private void WritePending()
        {
            while (true)
            {
                (string Name, string Json, bool File) entry;
                lock (sync)
                {
                    if (pending.Count == 0) { writer = Task.CompletedTask; return; }
                    entry = pending.Dequeue();
                }
                try
                {
                    if (entry.File) files.Save(entry.Name, entry.Json);
                    else log.Event("INFO", "agent." + entry.Name, "agentSession", Session, "record", entry.Json);
                }
                catch (Exception error) when (error is IOException || error is UnauthorizedAccessException)
                {
                    Interlocked.Increment(ref dropped);
                    log.Event("WARN", "agent.file.failed", "path", entry.Name, "error", error.Message);
                }
                finally { lock (sync) characters -= entry.Json.Length; }
            }
        }
        // Only shutdown/test synchronization; the request and frame paths never wait for storage.
        public bool Drain(int milliseconds = 2000)
        {
            Task current;
            lock (sync) current = writer;
            return current.Wait(milliseconds);
        }
    }
}
