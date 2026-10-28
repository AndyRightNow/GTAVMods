using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Diagnostics;
using System.Text;
using Microsoft.CSharp;
using GTAVMods.Game;

namespace GTAVMods.Agent
{
    public sealed class CommandContext
    {
        public CommandContext(IAgentWorld world, Action<string> print) { World = world; this.print = print; }
        private readonly Action<string> print;
        public IAgentWorld World { get; }
        public void Print(object value) => print(Protocol.Encode(value));
        public object Execute(Func<object> action)
        {
            var stdout = Console.Out; var stderr = Console.Error;
            using (var writer = new AgentOutputWriter(print))
            using (var trace = new TextWriterTraceListener(writer))
            {
                Console.SetOut(writer); Console.SetError(writer); Trace.Listeners.Add(trace);
                try { return action(); }
                finally { Trace.Listeners.Remove(trace); Console.SetOut(stdout); Console.SetError(stderr); }
            }
        }
    }

    internal sealed class AgentOutputWriter : TextWriter
    {
        private readonly Action<string> output;
        private int characters;
        internal AgentOutputWriter(Action<string> output) { this.output = output; }
        public override Encoding Encoding => Encoding.UTF8;
        public override void Write(char value) => Write(value.ToString());
        public override void Write(string value)
        {
            if (string.IsNullOrEmpty(value)) return;
            characters += value.Length;
            if (characters > 1024 * 1024) throw new IOException("Command output exceeds 1 MiB; command stopped.");
            output(value);
        }
        public override void WriteLine(string value) => Write(value + Environment.NewLine);
    }

    /// <summary>Trusted local C# runs on the SHVDNE script thread. Compilation is bounded per domain.</summary>
    public sealed class CSharpCommands
    {
        private readonly CommandContext context;
        private readonly Func<IEnumerable<Assembly>> assemblies;
        private readonly int compilationLimit;
        private readonly Dictionary<string, Func<object>> compiled = new Dictionary<string, Func<object>>(StringComparer.Ordinal);
        private int compilations;
        public CSharpCommands(CommandContext context, Func<IEnumerable<Assembly>> assemblies, int compilationLimit = 128)
        { this.context = context; this.assemblies = assemblies; this.compilationLimit = compilationLimit; }
        public Func<object> Compile(string code)
        {
            if (compiled.TryGetValue(code, out var existing)) return existing;
            if (compilations >= compilationLimit) throw new InvalidOperationException("C# compilation budget reached. Reuse previously compiled C#, use native/state operations, or reload scripts before submitting new C#; ordinary controls remain available.");
            compilations++;
            var options = new CompilerParameters { GenerateInMemory = true, CompilerOptions = "/optimize /define:TRACE", TreatWarningsAsErrors = false };
            foreach (var assembly in assemblies())
            {
                if (!assembly.IsDynamic && !string.IsNullOrEmpty(assembly.Location)
                    && assembly.GetName().Name != "ScriptHookVDotNet2") options.ReferencedAssemblies.Add(assembly.Location);
            }
            string source = @"using System; using System.Linq; using System.Collections.Generic; using GTA; using GTA.Math; using GTA.Native; using GTAVMods.Agent;
public static class AgentExpression {
 public static Ped P { get { return GTA.Game.Player.Character; } }
 public static Vehicle V { get { return P.CurrentVehicle; } }
 public static Player PLAYER { get { return GTA.Game.Player; } }
 public static Ped PED { get { return P; } }
 public static Vector3 POS { get { return P.Position; } }
 public static object Run(CommandContext CONTEXT) { " + code + @";
 return null; } }";
            using (var provider = new CSharpCodeProvider())
            {
                var result = provider.CompileAssemblyFromSource(options, source);
                if (result.Errors.HasErrors)
                    throw new ArgumentException(string.Join(Environment.NewLine, result.Errors.Cast<CompilerError>().Where(e => !e.IsWarning).Select(e => e.ToString())));
                var method = result.CompiledAssembly.GetType("AgentExpression").GetMethod("Run");
                Func<object> invoke = () => method.Invoke(null, new object[] { context });
                Func<object> execute = () => context.Execute(invoke);
                compiled.Add(code, execute);
                return execute;
            }
        }
    }

    public sealed class ShvdnRuntime
    {
        private readonly Func<IEnumerable<Assembly>> assemblies;
        public ShvdnRuntime(Func<IEnumerable<Assembly>> assemblies) { this.assemblies = assemblies; }
        public void Reload()
        {
            var core = assemblies().Select(a => a.GetType("ScriptHookVDotNet", false)).FirstOrDefault(t => t != null);
            if (core == null) throw new NotSupportedException("SHVDNE core is unavailable.");
            var method = core.GetMethod("Reload", BindingFlags.Public | BindingFlags.Static, null, Type.EmptyTypes, null);
            if (method == null) throw new NotSupportedException("This SHVDNE runtime has no compatible Reload entry point.");
            method.Invoke(null, null);
        }
    }
}
