using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.CodeDom.Compiler;
using Microsoft.CSharp;
using GTA.Math;
using GTA.Native;
using GTAVMods.Agent;
using GTAVMods.Tests.Support;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static GTAVMods.Tests.Support.AgentScene;

namespace GTAVMods.Tests.PlayerIntegration.AgentOperations
{
    [TestClass, TestCategory("PlayerIntegration")]
    public class CommandExecutionTests
    {
        [TestMethod]
        public void ConsoleReturnsNestedGameVectorsRotationsAndHandlesWithoutTraversingLiveNativeProperties()
        {
            using (var s = new AgentScene())
            {
                s.Batch(Step("command", ("code", "return new { position = new Vector3(1, 2, 3), rotation = new Quaternion(4, 5, 6, 7), camera = new Camera(42) };")));
                s.Frame();
                Assert.AreEqual("{\"position\":{\"x\":1,\"y\":2,\"z\":3},\"rotation\":{\"x\":4,\"y\":5,\"z\":6,\"w\":7},\"camera\":{\"handle\":42}}", Protocol.Encode(s.Results.Single()["result"]));
                StringAssert.Contains(s.Status, "completed");
            }
        }
        [TestMethod]
        public void ExternalConsoleReturnsObservedStateAndPersistsStdoutStderrAndStructuredOutput()
        {
            var dynamicAssembly = AppDomain.CurrentDomain.DefineDynamicAssembly(new AssemblyName("AgentExtension"), AssemblyBuilderAccess.Run);
            var memoryAssembly = Assembly.Load(File.ReadAllBytes(typeof(CommandContext).Assembly.Location));
            using (var s = new AgentScene(assemblies: () => AppDomain.CurrentDomain.GetAssemblies().Concat(new[] { dynamicAssembly, memoryAssembly })))
            {
                // The live host loads API 2 and API 3 together. Their duplicate GTA
                // types must not make every external command fail with CS0433.
                using (var compiler = new CSharpCodeProvider())
                {
                    // Loaded assemblies stay locked until the test process exits; use
                    // its generated output directory, outside the disposable session.
                    var options = new CompilerParameters { OutputAssembly = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ScriptHookVDotNet2.dll") };
                    var legacy = compiler.CompileAssemblyFromSource(options, "namespace GTA { public class Ped {} }");
                    Assert.IsFalse(legacy.Errors.HasErrors);
                    Assert.IsNotNull(legacy.CompiledAssembly);
                }
                var stdout = Console.Out; var stderr = Console.Error;
                s.Game.Entity(10).Quaternion = new Quaternion(.1f, .2f, .3f, .4f);
                s.Game.Outputs.Projection = pos => pos.X == 1 ? Tuple.Create(true, .125f, .875f) : Tuple.Create(false, 3f, 4f);
                string code = @"CONTEXT.Print(new { message = ""world observed"", gameTime = CONTEXT.World.GameTime });
                    Console.Write('A'); Console.Write((string)null); Console.Write(""""); Console.WriteLine(""hello 世界"");
                    Console.Error.WriteLine(""warning""); Console.Write(Console.Out.Encoding.WebName);
                    System.Diagnostics.Trace.WriteLine(""trace-event"");
                    return CONTEXT.World.Observe(12, 2);";
                var accepted = s.Batch(Step("command", ("code", code)));
                Assert.AreEqual(true, accepted["ok"], Protocol.Encode(accepted));
                s.Frame();
                StringAssert.Contains(s.Status, "\"state\":\"completed\"");
                string result = Protocol.Encode(s.Results.Single());
                StringAssert.Contains(result, "\"quaternion\":{\"x\":0.1,\"y\":0.2,\"z\":0.3,\"w\":0.4}");
                StringAssert.Contains(result, "\"screen\":{\"x\":0.125,\"y\":0.875}");
                StringAssert.Contains(result, "\"screen\":null");
                string output = string.Join("\n", Directory.GetFiles(s.Root, "output-*.json").Select(File.ReadAllText));
                foreach (string text in new[] { "world observed", "hello 世界", "warning", "utf-8", "trace-event" }) StringAssert.Contains(output, text);
                Assert.AreSame(stdout, Console.Out); Assert.AreSame(stderr, Console.Error);
                StringAssert.Contains(s.Events, "agent.output");
            }
        }

        [TestMethod]
        public void InvalidCSharpRejectsWholeBatchAndCompilationBudgetLeavesNormalControlsAvailable()
        {
            using (var s = new AgentScene(compilationLimit: 2))
            {
                var invalid = s.Batch(Step("pause"), Step("command", ("code", "this is not C#")));
                Assert.AreEqual(false, invalid["ok"]);
                StringAssert.Contains((string)invalid["error"], "error CS");
                Assert.IsFalse(s.Game.Native.Calls.Any());
                Assert.AreEqual(true, s.Batch(Step("command", ("code", "return 7;")))["ok"]);
                s.Frame(); Assert.AreEqual(7, s.Results.Single()["result"]);
                StringAssert.Contains((string)s.Batch(Step("command", ("code", "return 8;")))["error"], "compilation budget");
                Assert.AreEqual(true, s.Batch(Step("resume"), Step("wait", ("ms", 1)))["ok"]);
                s.Frame(); s.Frame(); s.Frame(1);
                StringAssert.Contains(s.Status, "\"state\":\"completed\"");
            }
        }

        [TestMethod]
        [DataRow("throw new InvalidOperationException(\"command failed\");", "command failed")]
        [DataRow("Console.Write(new string('x', 1024 * 1024 + 1));", "output exceeds 1 MiB")]
        public void CommandFailureRestoresConsoleStreamsPausesForInspectionAndStopsLaterActions(string code, string expected)
        {
            using (var s = new AgentScene())
            {
                var stdout = Console.Out; var stderr = Console.Error;
                Assert.AreEqual(true, s.Batch(Step("pause"), Step("command", ("code", code)), Step("capture"))["ok"]);
                s.Frame(); s.Frame();
                StringAssert.Contains(s.Status, "\"state\":\"failed\""); StringAssert.Contains(s.Status, expected);
                Assert.AreEqual(true, s.Game.Native.Calls.Last(c => c.Hash == Hash.SET_GAME_PAUSED).Arguments[0]);
                Assert.IsEmpty(Directory.GetFiles(s.Root, "frame-*.png"));
                Assert.AreSame(stdout, Console.Out); Assert.AreSame(stderr, Console.Error);
            }
        }

        [TestMethod]
        [DataRow("not-a-control")] [DataRow("9999")]
        public void UnknownNamedControlsCannotPartiallyStartAnEarlierAction(string control)
        {
            using (var s = new AgentScene())
            {
                var reply = s.Batch(Step("pause"), Step("controls", ("inputs", new[] { Step("", ("control", control)) })));
                Assert.AreEqual(false, reply["ok"]); StringAssert.Contains((string)reply["error"], "Unknown GTA.Control");
                s.Frame(); Assert.IsFalse(s.Game.Native.Calls.Any());
            }
        }

        [TestMethod]
        public void MissingControlAndInvalidTimingClockAreActionableRejections()
        {
            using (var s = new AgentScene())
            {
                StringAssert.Contains((string)s.Batch(Step("controls", ("inputs", new[] { Step("") })))["error"], "Missing control");
                StringAssert.Contains((string)s.Batch(Step("wait", ("clock", "cpu")))["error"], "clock must be game or wall");
                s.Frame(); Assert.IsFalse(s.Game.Native.Calls.Any());
            }
        }

        [TestMethod]
        public void CancellingAnUnstartedBatchDoesNotPauseTheWorld()
        {
            using (var s = new AgentScene())
            {
                s.Batch(Step("pause")); s.Request("cancel"); s.Frame();
                Assert.IsFalse(s.Game.Native.Calls.Any()); StringAssert.Contains(s.Status, "\"state\":\"cancelled\"");
            }
        }
    }
}
