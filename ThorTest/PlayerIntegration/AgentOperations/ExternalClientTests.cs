using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using ADModUtils;
using GTA.Native;
using GTAVMods.Agent;
using GTAVMods.Tests.Support;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace GTAVMods.Tests.PlayerIntegration.AgentOperations
{
    [TestClass, TestCategory("PlayerIntegration")]
    public class ExternalClientTests
    {
        [TestMethod]
        public void ExternalUtilityDiscoversTheGameMovesPausesInspectsAndSavesAScreenshotOverARealPipe()
        {
            using (var s = new AgentScene())
            using (var bridge = BridgeComposition.Create(s.World, s.Root, Process.GetCurrentProcess().Id, s.Log, new WindowPixels(s.Pixels)))
            {
                // An extension registered against the published live bridge is available to external clients.
                BridgeScript.Active.Commands.Register(new CommandDefinition("mission.inspect-clock", "Inspect simulation time", new { },
                    args => new ImmediateStep(() => s.Game.Host.GameTime)));
                Action tick = () => { s.Game.Host.GameTime += 16; bridge.Tick(); };
                var described = AgentClient.Run(s.Root, "-Operation describe", tick);
                Assert.AreEqual(0, described.ExitCode, described.Error); StringAssert.Contains(described.Output, "mission.inspect-clock");
                var filtered = AgentClient.Run(s.Root, "-Operation state -StateJson \"{\\\"sections\\\":[\\\"player\\\",\\\"camera\\\"]}\"", tick);
                Assert.AreEqual(0, filtered.ExitCode, filtered.Error); StringAssert.Contains(filtered.Output, "gta.world-state/1");
                StringAssert.Contains(filtered.Output, "camera");
                var reply = AgentClient.Run(s.Root, "-Operation step -Milliseconds 32 -Capture -InputsJson \"[{\\\"control\\\":\\\"MoveUpDown\\\",\\\"value\\\":-1}]\"", tick);
                Assert.AreEqual(0, reply.ExitCode, reply.Error + reply.Output + string.Join("\n", Directory.GetFiles(s.Root, "request-*.json", SearchOption.AllDirectories).Select(File.ReadAllText)));
                StringAssert.Contains(reply.Output, "completed"); StringAssert.Contains(reply.Output, "screenshot");
                StringAssert.Contains(reply.Output, "missionActive");
                Assert.HasCount(0, Directory.GetFiles(s.Root, "request-*.json", SearchOption.AllDirectories),
                    "Requests are ephemeral; batch outputs remain externally readable.");
                Assert.IsTrue(s.Game.Native.Calls.Any(c => c.Hash == Hash.SET_CONTROL_VALUE_NEXT_FRAME));
                Assert.AreEqual(true, s.Game.Native.Calls.Last(c => c.Hash == Hash.SET_GAME_PAUSED).Arguments[0]);
                Assert.HasCount(1, Directory.GetFiles(s.Root, "frame-*.png", SearchOption.AllDirectories));
                var nextTurn = AgentClient.Run(s.Root, "-Operation step -Milliseconds 16 -Capture", tick);
                Assert.AreEqual(0, nextTurn.ExitCode, nextTurn.Error);
                Assert.HasCount(2, Directory.GetFiles(s.Root, "frame-*.png", SearchOption.AllDirectories));
                StringAssert.Contains(nextTurn.Output, "completed");
                Assert.AreEqual(2, s.Game.Native.Calls.Count(c => c.Hash == Hash.SET_GAME_PAUSED && !(bool)c.Arguments[0]),
                    "The next decision resumes the agent-owned pause before advancing another turn.");
                var cancelled = AgentClient.Run(s.Root, "-Operation cancel", tick);
                Assert.AreEqual(0, cancelled.ExitCode, cancelled.Error);
                Assert.AreEqual(false, s.Game.Native.Calls.Last(c => c.Hash == Hash.SET_GAME_PAUSED).Arguments[0]);
            }
        }

        [TestMethod, DataRow(false), DataRow(true)]
        public void TemporarilyUnavailableStatusFileDoesNotReplayThePlayersAcceptedAction(bool locked)
        {
            using (var s = new AgentScene())
            using (var bridge = BridgeComposition.Create(s.World, s.Root, Process.GetCurrentProcess().Id, s.Log, new WindowPixels(s.Pixels)))
            {
                string batch = Path.Combine(s.Root, "pause.json");
                File.WriteAllText(batch, "[{\"op\":\"pause\"}]");
                string held = null, status = null;
                FileStream denied = null;
                var unavailable = new Stopwatch();
                Action tick = () => {
                    bridge.Tick();
                    if (held == null)
                    {
                        status = Directory.GetFiles(s.Root, "batch-*-status.json", SearchOption.AllDirectories).SingleOrDefault();
                        if (status != null && ReadShared(status).Contains("completed"))
                        {
                            held = status + ".withheld";
                            if (locked) denied = new FileStream(status, FileMode.Open, FileAccess.Read, FileShare.None);
                            else File.Move(status, held);
                            unavailable.Start();
                        }
                    }
                    else if (unavailable.IsRunning && unavailable.ElapsedMilliseconds >= 400)
                    {
                        if (locked) { denied.Dispose(); denied = null; }
                        else File.Move(held, status);
                        unavailable.Stop();
                    }
                };
                (int ExitCode, string Output, string Error) reply;
                try { reply = AgentClient.Run(s.Root, "-Operation batch -BatchFile \"" + batch + "\"", tick); }
                finally { denied?.Dispose(); }
                Assert.AreEqual(0, reply.ExitCode, reply.Error);
                StringAssert.Contains(reply.Output, "completed");
                Assert.IsNotNull(held, "The storage interruption must have occurred.");
                Assert.AreEqual(1, s.Game.Native.Calls.Count(c => c.Hash == Hash.SET_GAME_PAUSED));
                Assert.IsEmpty(Directory.GetFiles(s.Root, "request-*.json", SearchOption.AllDirectories));
            }
        }

        private static string ReadShared(string path)
        {
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var reader = new StreamReader(stream)) return reader.ReadToEnd();
        }

        [TestMethod]
        public void ExternalBatchCanExecuteConsoleCodeAndReadTheSharedDiagnosticLogFromDisk()
        {
            using (var s = new AgentScene())
            using (var bridge = BridgeComposition.Create(s.World, s.Root, Process.GetCurrentProcess().Id, s.Log, new WindowPixels(s.Pixels)))
            {
                string batch = Path.Combine(s.Root, "commands.json");
                File.WriteAllText(batch, Protocol.Encode(new[] { new { op = "command", code = "ADModUtils.Logger.Default.Event(\"INFO\", \"external-agent-log-query\"); return ADModUtils.Logger.LogFilePath;" } }));
                var reply = AgentClient.Run(s.Root, "-Operation batch -BatchFile \"" + batch + "\"", bridge.Tick);
                Assert.AreEqual(0, reply.ExitCode, reply.Error + reply.Output + string.Join("\n", Directory.GetFiles(s.Root, "request-*.json", SearchOption.AllDirectories).Select(File.ReadAllText)));
                var json = Protocol.Decode(reply.Output);
                var results = ((System.Collections.IEnumerable)json["results"]).Cast<object>();
                string log = (string)((System.Collections.Generic.Dictionary<string, object>)results.Single())["result"];
                StringAssert.Contains(File.ReadAllText(log), "external-agent-log-query");
                StringAssert.Contains(reply.Output, "completed");
            }
        }
    }
}
