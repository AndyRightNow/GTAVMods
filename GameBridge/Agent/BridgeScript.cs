using System;
using System.Diagnostics;
using System.IO;
using ADModUtils;
using GTA;
using GTAVMods.Game;

namespace GTAVMods.Agent
{
    public static class BridgeComposition
    {
        public static BridgeSession CreateForProject(IAgentWorld world, string project, int process, DiagnosticLog log, IWindowPixels pixels)
        {
            string configuration = Path.Combine(project, ".runtime-root");
            string root = File.Exists(configuration) ? File.ReadAllText(configuration).Trim() : Path.Combine("artifacts", "gta-use");
            if (string.IsNullOrWhiteSpace(root)) throw new InvalidDataException("GTA use .runtime-root is empty. Run Install-AgentService.ps1 to configure its runtime directory.");
            root = Path.GetFullPath(Path.Combine(project, root));
            return Create(world, Path.Combine(root, "Bridge"), process, log, pixels, project);
        }
        public static BridgeSession Create(IAgentWorld world, string root, int process, DiagnosticLog log, IWindowPixels pixels, string serviceDirectory = null)
        {
            string session = Guid.NewGuid().ToString("N");
            string pipeName = "GTAVMods-" + process + "-" + session;
            var clock = Stopwatch.StartNew();
            var files = new AgentFiles();
            string directory = Path.Combine(root, "session-" + process + "-" + session);
            var journal = new AgentJournal(directory, session, files, log);
            var capture = new ScreenCapture(pixels, directory);
            Action serviceTick = null;
            if (serviceDirectory != null)
                serviceTick = new ServiceSupervisor(serviceDirectory, root, process, () => clock.ElapsedMilliseconds, journal.Record).Tick;
            var bridge = new BridgeSession(world, journal, () => clock.ElapsedMilliseconds,
                () => new AgentPipeConnection(pipeName), () => AppDomain.CurrentDomain.GetAssemblies(), capture.Capture,
                state => files.Save(Path.Combine(root, "bridge-" + process + ".json"), Protocol.Encode(new {
                    process, pipe = pipeName, updatedUtc = DateTime.UtcNow.ToString("O"), bridge = state
                })), serviceTick: serviceTick);
            BridgeScript.Active = bridge;
            return bridge;
        }
    }

    public sealed class BridgeScript : Script
    {
        private readonly BridgeSession session;
        public static BridgeSession Active { get; internal set; }
        [GamePrimitive]
        public BridgeScript()
        {
            var native = new NativeInvoker(new GtaNative(), new ScalarArgumentMarshaller(new GtaArgumentMarshaller()));
            var game = GtaRuntime.Create(new GtaHost(), new GtaInput(),
                new GtaEntities(new GtaEntityLifetimeBindings()), new GtaPeds(), new GtaWeapons(),
                new GtaWorld(new GtaWorldQueries()), new GtaCameras(new GtaCameraLifetimeBindings()),
                new GtaRopes(new GtaRopeLifetimeBindings()), new GtaNative(), new ScalarArgumentMarshaller(new GtaArgumentMarshaller()), new GtaOutputs());
            session = BridgeComposition.CreateForProject(new AgentWorld(game, native, new Func<Blip[]>(GtaAgentQueries.Blips)),
                Path.Combine(BaseDirectory, "GtaUse"), Process.GetCurrentProcess().Id, Logger.Default,
                new WindowPixels(new NativeWindowCaptureApi()));
            Tick += OnTick;
            Aborted += OnAborted;
            Interval = 0;
        }
        [GamePrimitive] private void OnTick(object sender, EventArgs args) => session.Tick();
        [GamePrimitive] private void OnAborted(object sender, EventArgs args) => session.Dispose();
    }
}
