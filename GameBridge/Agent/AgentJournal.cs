using System;
using System.IO;
using System.Text;
using ADModUtils;

namespace GTAVMods.Agent
{
    public interface IAgentFiles
    {
        bool Exists(string path);
        void Save(string path, string text);
        string Read(string path);
    }

    public sealed class AgentFiles : IAgentFiles
    {
        public bool Exists(string path) => File.Exists(path);
        public void Save(string path, string text)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string temporary = path + ".tmp";
            using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.Read))
            {
                byte[] bytes = new UTF8Encoding(false).GetBytes(text);
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush(true);
            }
            if (File.Exists(path)) File.Replace(temporary, path, null);
            else File.Move(temporary, path);
        }
        public string Read(string path) => File.ReadAllText(path);
    }

    /// <summary>Results and snapshots are disk artifacts. Only the current batch is held in memory.</summary>
    public sealed class AgentJournal
    {
        private readonly IAgentFiles files;
        private readonly DiagnosticLog log;
        public AgentJournal(string directory, string session, IAgentFiles files, DiagnosticLog log)
        { Directory = directory; Session = session; this.files = files; this.log = log; }
        public string Directory { get; }
        public string Session { get; }
        public string LogPath => log.FilePath;
        public string Save(string name, object value) => SaveJson(name, Protocol.Encode(value));
        internal string SaveJson(string name, string json)
        {
            string path = Path.Combine(Directory, name + ".json");
            files.Save(path, json);
            return path;
        }
        public string Read(string name) => files.Read(Path.Combine(Directory, name + ".json"));
        public bool Exists(string name) => files.Exists(Path.Combine(Directory, name + ".json"));
        public void Record(string name, object data)
        {
            long dropped = log.DroppedRecords;
            log.Event("INFO", "agent." + name, "agentSession", Session, "record", Protocol.Encode(data));
            if (log.DroppedRecords != dropped) throw new IOException("Agent audit log unavailable; command execution stopped.");
        }
    }
}
