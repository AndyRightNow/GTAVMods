using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using GTAVMods.Game;

namespace GTAVMods.Agent
{
    public sealed class BridgeSession : IDisposable
    {
        private readonly AgentJournal journal;
        private readonly IAgentWorld world;
        private readonly WorldControl control;
        private readonly BatchRunner runner;
        private readonly PipeServer pipe;
        private readonly AgentLease lease;
        private readonly ShvdnRuntime runtime;
        private readonly Action<object> publish;
        private readonly Action serviceTick;
        private string reloadRequestId;
        private bool reloadReady, stopped;
        private int outputSequence;
        public BridgeSession(IAgentWorld world, AgentJournal journal, Func<long> clock, Func<IAgentConnection> open,
            Func<IEnumerable<Assembly>> assemblies, Func<string> capture, Action<object> publish, int compilationLimit = 128, Action serviceTick = null)
        {
            this.journal = journal; this.publish = publish; this.world = world;
            this.serviceTick = serviceTick ?? (() => { });
            control = new WorldControl(world); runtime = new ShvdnRuntime(assemblies);
            Commands = new CommandRegistry();
            var context = new CommandContext(world, WriteOutput);
            BuiltinCommands.Register(Commands, world, control, clock, new CSharpCommands(context, assemblies, compilationLimit), capture);
            runner = new BatchRunner(Commands, journal, control.Release, () => control.Pause(true));
            lease = new AgentLease(clock, StopForInspection);
            pipe = new PipeServer(open, clock, Dispatch, error => journal.Record("transport.error", new { error }), ResponseSent);
            journal.Record("session.started", new { protocol = Protocol.Version });
            Publish("ready");
        }
        public CommandRegistry Commands { get; }
        public object Status() => new { protocol = Protocol.Version, session = journal.Session, paused = control.Paused,
            stopped, lease = lease.Status(), batch = runner.Status(), directory = journal.Directory, log = journal.LogPath };
        public void Tick()
        {
            if (stopped) return;
            try
            {
                lease.Tick();
                serviceTick();
                pipe.Tick();
                runner.Tick();
                if (reloadReady)
                {
                    reloadReady = false;
                    runner.Cancel("script reload");
                    journal.Record("reload.requested", Status());
                    Publish("reloading");
                    runtime.Reload();
                }
            }
            catch (Exception)
            {
                // Fail closed on storage/runtime errors. Controls cannot continue silently.
                StopForInspection();
                Publish("error");
                throw;
            }
        }

        public string Dispatch(string json)
        {
            string id = null;
            bool recorded = false;
            try
            {
                var request = Protocol.Decode(json) ?? throw new ArgumentException("Expected a JSON object.");
                id = Protocol.Text(request, "id");
                if (!Guid.TryParseExact(id, "N", out var _)) throw new ArgumentException("id must be a 32-character GUID.");
                if (Protocol.Integer(request, "version", 1, 1, 0) != Protocol.Version) throw new ArgumentException("Unsupported protocol version.");
                if (Protocol.Text(request, "session") != journal.Session) throw new InvalidOperationException("Stale session. Rediscover the bridge.");
                if (stopped) throw new InvalidOperationException("Bridge stopped.");
                if (journal.Exists("response-" + id)) return journal.Read("response-" + id);
                if (journal.Exists("request-" + id)) throw new InvalidOperationException("Request outcome is uncertain; inspect files and do not automatically replay.");
                journal.Save("request-" + id, request);
                recorded = true;
                journal.Record("request", new { id, op = Protocol.Text(request, "op") });
                string op = Protocol.Text(request, "op");
                object result;
                if (op == "batch" || op == "reload")
                    lease.Authorize(request.TryGetValue("token", out var token) ? token as string : null);
                switch (op)
                {
                    case "describe": result = new { commands = Commands.Describe(), stateSections = world.StateSections, controls = Enum.GetNames(typeof(GTA.Control)),
                        operations = new[] { "describe", "status", "observe", "state", "capture", "batch", "cancel", "reload", "lease" } }; break;
                    case "status": result = Status(); break;
                    case "lease":
                        var leaseArgs = new Dictionary<string, object>(request);
                        foreach (string key in new[] { "version", "session", "id" }) leaseArgs.Remove(key);
                        result = lease.Update(leaseArgs); break;
                    case "observe":
                    case "state":
                    case "capture":
                        var arguments = new Dictionary<string, object>(request);
                        foreach (string key in new[] { "version", "session", "id", "token" }) arguments.Remove(key);
                        var inspection = Commands.Prepare(arguments); inspection.Run(); result = inspection.Result; break;
                    case "cancel": runner.Cancel("external cancellation"); result = Status(); break;
                    case "reload":
                        runner.Cancel("external reload"); reloadRequestId = id;
                        result = new { state = "accepted", previousSession = journal.Session }; break;
                    case "batch": result = runner.Start(Protocol.Objects(request, "steps", Protocol.MaximumSteps)); break;
                    default: throw new ArgumentException("Unknown request operation: " + op);
                }
                var response = new { id, session = journal.Session, ok = true, result };
                string encoded = Protocol.Encode(response);
                journal.SaveJson("response-" + id, encoded);
                journal.Record("response", new { id, ok = true });
                return encoded;
            }
            catch (Exception error)
            {
                var response = new { id, session = journal.Session, ok = false, error = error.GetBaseException().Message };
                string encoded = Protocol.Encode(response);
                if (recorded) journal.SaveJson("response-" + id, encoded);
                journal.Record("request.failed", response);
                return encoded;
            }
        }
        private void StopForInspection()
        {
            try { runner.Cancel("agent stopped; inspect before resuming"); }
            finally { control.Pause(true); }
        }
        private void ResponseSent(string response)
        {
            if (reloadRequestId != null && Protocol.Text(Protocol.Decode(response), "id") == reloadRequestId)
            { reloadRequestId = null; reloadReady = true; }
        }
        private void WriteOutput(string text)
        {
            string path = journal.Save("output-" + (++outputSequence), new { batch = runner.Id, text });
            journal.Record("output", new { path, batch = runner.Id });
        }
        private void Publish(string state) => publish(new { version = Protocol.Version, session = journal.Session, state,
            directory = journal.Directory, log = journal.LogPath });
        public void Dispose()
        {
            if (stopped) return;
            stopped = true;
            try { runner.Cancel("script aborted"); }
            finally { pipe.Dispose(); }
            journal.Record("session.stopped", Status());
            Publish("stopped");
        }
    }
}
