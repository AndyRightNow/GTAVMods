using System;
using System.Diagnostics;
using System.IO;

namespace GTAVMods.Agent
{
    /// <summary>Starts the packaged Rust companion without downloading or blocking on HTTP startup.</summary>
    public sealed class ServiceSupervisor
    {
        private readonly string project, root;
        private readonly int gameProcess;
        private readonly Func<long> clock;
        private readonly Action<string, object> log;
        private long nextCheck;
        private int child;
        public ServiceSupervisor(string project, string root, int gameProcess, Func<long> clock, Action<string, object> log)
        { this.project = project; this.root = root; this.gameProcess = gameProcess; this.clock = clock; this.log = log; }
        public void Tick()
        {
            if (clock() < nextCheck) return;
            nextCheck = clock() + 10000;
            try
            {
                string discovery = Path.Combine(root, "service-" + gameProcess + ".json");
                if (File.Exists(discovery))
                {
                    var status = Protocol.Decode(File.ReadAllText(discovery));
                    if (Running(Protocol.Integer(status, "serviceProcess", 1, int.MaxValue, 0))) return;
                }
                if (Running(child)) return;
                string interpreterFile = Path.Combine(project, ".python-path");
                string python = File.Exists(interpreterFile) ? File.ReadAllText(interpreterFile).Trim() : Path.Combine(project, ".venv", "Scripts", "python.exe");
                if (!File.Exists(python)) throw new FileNotFoundException("Prepare the Python 3.14 environment with Install-AgentService.ps1 (uv sync --frozen) before starting GTA.", python);
                string executable = Path.Combine(project, "gta-use-server.exe");
                if (!File.Exists(executable)) throw new FileNotFoundException("Rust service is missing. Extract the complete package and run Install-AgentService.ps1.", executable);
                string arguments = "\"" + root + "\" " + gameProcess + " \"" + python + "\"";
                var info = new ProcessStartInfo(executable, arguments) {
                    UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden, WorkingDirectory = project
                };
                using (var process = Process.Start(info)) child = process.Id;
                log("service.starting", new { process = child, project });
            }
            catch (Exception error) { log("service.start.failed", new { error = error.ToString(), project }); }
        }
        private static bool Running(int id)
        {
            if (id == 0) return false;
            try { using (var process = Process.GetProcessById(id)) return process.ProcessName.Equals("gta-use-server", StringComparison.OrdinalIgnoreCase); }
            catch (ArgumentException) { return false; }
        }
    }
}
