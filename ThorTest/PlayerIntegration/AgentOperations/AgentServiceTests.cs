using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using GTA.Native;
using GTAVMods.Agent;
using GTAVMods.Tests.Support;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace GTAVMods.Tests.PlayerIntegration.AgentOperations
{
    [TestClass, TestCategory("PlayerIntegration")]
    public class AgentServiceTests
    {
        internal static string Project()
        {
            var directory = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
            while (directory != null && !File.Exists(Path.Combine(directory.FullName, "pyproject.toml"))) directory = directory.Parent;
            return directory?.FullName ?? throw new InvalidOperationException("Run tests from a source checkout after uv sync --frozen.");
        }
        private static string ServiceProject(AgentScene scene)
        {
            string built = Path.Combine(Project(), "artifacts", "rust", "release", "gta-use-server.exe");
            string executable = Environment.GetEnvironmentVariable("GTA_USE_RUST_TEST_EXECUTABLE") ?? built;
            CollectionAssert.AreEqual(File.ReadAllBytes(built), File.ReadAllBytes(executable), "The staged service must match this build.");
            string project = Path.Combine(Path.GetDirectoryName(executable), "service-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(project);
            File.Copy(Path.Combine(Project(), ".python-path"), Path.Combine(project, ".python-path"));
            File.Copy(executable, Path.Combine(project, "gta-use-server.exe"));
            // A stale backend selector from an older install must not redirect startup.
            File.WriteAllText(Path.Combine(project, ".rust-server-path"), Path.Combine(scene.Root, "obsolete-server.exe"));
            return project;
        }
        [TestMethod]
        public void StartingTheBridgeAutomaticallyEnablesRustControlFromPythonAndRestartsAFailedService()
        {
            using (var s = new AgentScene())
            {
                // An agent requests one nearby pedestrian and one navigation marker,
                // skipping a closer vehicle and retaining an actual foreground capture.
                s.Game.Entity(20).Position = new GTA.Math.Vector3(2, 2, 3);
                s.Game.AddEntity(40).Position = new GTA.Math.Vector3(6, 1, 0);
                s.Game.World.Nearby = (position, radius) => new[] { 10, 20, 30, 40 };
                s.Game.Native.Queries[Hash.GET_ENTITY_TYPE] = a => (int)a[0] == 20 ? 2 : 1;
                s.Game.Native.Returns(Hash.GET_RELATIONSHIP_BETWEEN_PEDS, 3);
                s.Blips = new[] { new GTA.Blip(90), new GTA.Blip(91) };
                s.Game.Native.Returns(Hash.DOES_BLIP_HAVE_GPS_ROUTE, false);
                s.Game.Native.Returns(Hash.GET_BLIP_INFO_ID_ENTITY_INDEX, 0);
                s.Game.Native.Returns(Hash.GET_NAME_OF_ZONE, "STRAW");
                s.Game.Native.Returns(Hash.GET_STREET_NAME_FROM_HASH_KEY, "Forum Drive");
                s.Game.Native.Returns(Hash.GET_GPS_BLIP_ROUTE_FOUND, false);
                s.Game.Native.Returns(Hash.GET_GPS_BLIP_ROUTE_LENGTH, 0);
                string project = ServiceProject(s);
                using (var bridge = BridgeComposition.Create(s.World, s.Root, Process.GetCurrentProcess().Id, s.Log, new WindowPixels(s.Pixels), project))
                {
                    string discovery = Path.Combine(s.Root, "service-" + Process.GetCurrentProcess().Id + ".json");
                    int server = 0, replacement = 0;
                    Action tick = () => { s.Game.Host.GameTime += 16; bridge.Tick(); };
                    try
                    {
                        Wait(() => File.Exists(discovery), tick, 15000);
                        var entry = Protocol.Decode(File.ReadAllText(discovery)); server = (int)entry["serviceProcess"];
                        var info = new ProcessStartInfo(File.ReadAllText(Path.Combine(Project(), ".python-path")).Trim(),
                            "\"" + Path.Combine(Project(), "tools", "gta_use", "tests", "bridge_scenario.py") + "\" \"" + s.Root + "\" " + Process.GetCurrentProcess().Id) {
                            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true
                        };
                        using (var client = Process.Start(info))
                        {
                            var output = client.StandardOutput.ReadToEndAsync(); var error = client.StandardError.ReadToEndAsync();
                            // This includes six fresh CLI launches as well as the supervised policy.
                            try { Wait(() => client.HasExited, tick, 45000); }
                            finally { if (!client.HasExited) { client.Kill(); client.WaitForExit(); } }
                            Assert.AreEqual(0, client.ExitCode, error.GetAwaiter().GetResult());
                            StringAssert.Contains(output.GetAwaiter().GetResult(), "screenshot");
                        }
                        Assert.IsTrue(s.Game.Native.Calls.Any(c => c.Hash == Hash.SET_CONTROL_VALUE_NEXT_FRAME && (float)c.Arguments[2] == -.5f));
                        Assert.AreEqual(true, s.Game.Native.Calls.Last(c => c.Hash == Hash.SET_GAME_PAUSED).Arguments[0]);
                        StringAssert.Contains(s.Events, "agent.service.starting");
                        // Discovery temporarily disappears; the live child must not be duplicated.
                        string saved = File.ReadAllText(discovery); File.Delete(discovery);
                        var timer = Stopwatch.StartNew(); Wait(() => timer.ElapsedMilliseconds > 10500, tick, 12000);
                        Assert.IsFalse(File.Exists(discovery));
                        File.WriteAllText(discovery, saved);
                        using (var process = Process.GetProcessById(server)) { process.Kill(); process.WaitForExit(); }
                        Wait(() => { replacement = (int)Protocol.Decode(File.ReadAllText(discovery))["serviceProcess"]; return replacement != server; }, tick, 15000);
                        Assert.AreNotEqual(server, replacement);
                        // A managed reload publishes a new session but keeps the companion and URL.
                        bridge.Dispose();
                        using (var reloaded = BridgeComposition.Create(s.World, s.Root, Process.GetCurrentProcess().Id, s.Log, new WindowPixels(s.Pixels), project))
                        using (var client = Process.Start(info))
                        {
                            var output = client.StandardOutput.ReadToEndAsync(); var error = client.StandardError.ReadToEndAsync();
                            try { Wait(() => client.HasExited, () => { s.Game.Host.GameTime += 16; reloaded.Tick(); }, 45000); }
                            finally { if (!client.HasExited) { client.Kill(); client.WaitForExit(); } }
                            Assert.AreEqual(0, client.ExitCode, error.GetAwaiter().GetResult());
                            StringAssert.Contains(output.GetAwaiter().GetResult(), "screenshot");
                            Assert.AreEqual(replacement, (int)Protocol.Decode(File.ReadAllText(discovery))["serviceProcess"]);
                        }
                    }
                    finally
                    {
                        foreach (int id in new[] { server, replacement }.Where(id => id != 0).Distinct())
                            try { using (var process = Process.GetProcessById(id)) { process.Kill(); process.WaitForExit(); } } catch (ArgumentException) { }
                        foreach (string file in Directory.GetFiles(s.Root, "*-stdio.log")) Console.WriteLine(File.ReadAllText(file));
                        foreach (string file in Directory.GetFiles(s.Root, "service-*.jsonl")) Console.WriteLine(File.ReadAllText(file));
                        Console.WriteLine(s.Events);
                        Directory.Delete(project, true);
                    }
                }
            }
        }
        [TestMethod, DataRow(false), DataRow(true)]
        public void MissingPreparedServiceIsLoggedWhileThePlayerCanStillUseTheBridge(bool pythonPrepared)
        {
            using (var s = new AgentScene())
            using (var bridge = BridgeComposition.Create(s.World, s.Root, Process.GetCurrentProcess().Id, s.Log, new WindowPixels(s.Pixels), s.Root))
            {
                if (pythonPrepared)
                {
                    File.Copy(Path.Combine(Project(), ".python-path"), Path.Combine(s.Root, ".python-path"));
                }
                File.WriteAllText(Path.Combine(s.Root, "service-" + Process.GetCurrentProcess().Id + ".json"), Protocol.Encode(new { serviceProcess = Process.GetCurrentProcess().Id }));
                bridge.Tick();
                Wait(() => s.Events.Contains("agent.service.start.failed"), bridge.Tick, 5000);
                StringAssert.Contains(s.Events, "agent.service.start.failed"); StringAssert.Contains(s.Events, pythonPrepared ? "Rust service is missing" : "uv sync --frozen");
                var request = new { version = 1, id = Guid.NewGuid().ToString("N"), session = Protocol.Decode(Protocol.Encode(bridge.Status()))["session"], op = "status" };
                Assert.AreEqual(true, Protocol.Decode(bridge.Dispatch(Protocol.Encode(request)))["ok"]);
            }
        }
        private static void Wait(Func<bool> completed, Action tick, int timeout)
        {
            var watch = Stopwatch.StartNew();
            while (!completed() && watch.ElapsedMilliseconds < timeout) { tick(); Thread.Sleep(4); }
            Assert.IsTrue(completed(), "The external service scenario timed out.");
        }
    }
}
