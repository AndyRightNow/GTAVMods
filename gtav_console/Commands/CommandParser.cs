using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using GTAVMods.Game;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;
using GTA;
using GTA.Math;
using Microsoft.CSharp;

namespace DeveloperConsole {
    /// <summary>Primitive values used by the console's expressions.</summary>
    public interface ICommandEnvironment {
        object Player { get; }
        object Character { get; }
        bool IsInVehicle { get; }
        object Vehicle { get; }
        Vector3 Position { get; }
        string PlayerName { get; }
        int PlayerHandle { get; }
        CompilerResults Compile(CompilerParameters parameters, string source);
    }

    internal sealed class LiveCommandEnvironment : ICommandEnvironment {
        public object Player { [GamePrimitive] get { return Game.Player; } }
        public object Character { [GamePrimitive] get { return Game.Player.Character; } }
        public bool IsInVehicle { [GamePrimitive] get { return Game.Player.Character.IsInVehicle(); } }
        public object Vehicle { [GamePrimitive] get { return Game.Player.Character.CurrentVehicle; } }
        public Vector3 Position { [GamePrimitive] get { return Game.Player.Character.Position; } }
        public string PlayerName { [GamePrimitive] get { return Game.Player.Name; } }
        public int PlayerHandle { [GamePrimitive] get { return Game.Player.Handle; } }

        public CompilerResults Compile(CompilerParameters parameters, string source) {
            using (var provider = new CSharpCodeProvider())
                return provider.CompileAssemblyFromSource(parameters, source);
        }
    }

    internal sealed class CommandParser {
        public List<CommandToken> Tokens { get; } = new List<CommandToken>();

        public CommandParser(string data, IDeveloperConsole console)
            : this(data, console, new LiveCommandEnvironment()) { }

        public CommandParser(string data, IDeveloperConsole console, ICommandEnvironment environment) {
            var position = 0;
            while (position < data.Length) {
                if (char.IsWhiteSpace(data[position])) {
                    position++;
                    continue;
                }
                var kind = CommandTokenKind.Word;
                var end = '\0';
                if (data[position] == '"') {
                    kind = CommandTokenKind.String;
                    end = '"';
                    position++;
                } else if (data[position] == '{') {
                    kind = CommandTokenKind.CodeBlock;
                    end = '}';
                    position++;
                }
                var value = new StringBuilder();
                var depth = 1;
                var codeQuote = '\0';
                var verbatimString = false;
                while (position < data.Length) {
                    var character = data[position++];
                    if (end == '\0') {
                        if (char.IsWhiteSpace(character)) break;
                    } else {
                        if (character == '\\' && codeQuote != '\0' && !verbatimString && position < data.Length) {
                            value.Append(character).Append(data[position++]);
                            continue;
                        }
                        if (codeQuote == '\0' && character == '\\' && position < data.Length && data[position] == end) {
                            value.Append(data[position++]);
                            continue;
                        }
                        if (kind == CommandTokenKind.CodeBlock) {
                            if (codeQuote == '\0') {
                                if (character == '"' || character == '\'') {
                                    codeQuote = character;
                                    verbatimString = character == '"' && value.Length > 0 && value[value.Length - 1] == '@';
                                }
                                if (character == '{') depth++;
                                if (character == '}') depth--;
                            } else if (character == codeQuote) {
                                if (verbatimString && position < data.Length && data[position] == '"') {
                                    value.Append(character).Append(data[position++]);
                                    continue;
                                }
                                codeQuote = '\0';
                                verbatimString = false;
                            }
                            if (depth == 0) break;
                        } else if (character == end) break;
                    }
                    value.Append(character);
                }
                var text = value.ToString();
                double number;
                if (kind == CommandTokenKind.Word &&
                    double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out number))
                    kind = CommandTokenKind.Number;
                // Parsing and diagnostics must never execute expressions.
                console.PrintDebug("Found token -> " + kind + " : " + text);
                Tokens.Add(new CommandToken(kind, text, console, environment));
            }
        }
    }

    public enum CommandTokenKind { Word, Number, String, CodeBlock }

    public class CommandToken {
        private readonly IDeveloperConsole _console;
        private readonly ICommandEnvironment _environment;
        private bool _evaluated;
        private object _value;

        public CommandToken(CommandTokenKind kind, string data, IDeveloperConsole console)
            : this(kind, data, console, new LiveCommandEnvironment()) { }

        public CommandToken(CommandTokenKind kind, string data, IDeveloperConsole console,
            ICommandEnvironment environment) {
            _console = console;
            _environment = environment;
            Kind = kind;
            String = data;
        }

        public CommandTokenKind Kind { get; }
        public string String { get; }

        /// <summary>Evaluate once, so argument validation cannot repeat side effects.</summary>
        public object Eval {
            get {
                if (!_evaluated) {
                    _value = Evaluate();
                    _evaluated = true;
                }
                return _value;
            }
        }

        public Type Type { get { return Eval?.GetType(); } }

        private object Evaluate() {
            switch (Kind) {
                case CommandTokenKind.Word:
                    switch (String.ToLowerInvariant()) {
                        case "true": case "on": case "enable": return true;
                        case "false": case "off": case "disable": return false;
                        case "self": return _environment.Player;
                        case "self.character": case "self.ped": return _environment.Character;
                        case "self.vehicle": return _environment.IsInVehicle ? _environment.Vehicle : null;
                        case "self.pos": case "self.position": return _environment.Position;
                        case "self.name": return _environment.PlayerName;
                        case "self.id": case "self.handle": return _environment.PlayerHandle;
                        default: return String;
                    }
                case CommandTokenKind.CodeBlock:
                    try {
                        return CSharpEval(String);
                    } catch (Exception error) {
                        _console.PrintError("C# evaluation failed: " + error.Message);
                        return null;
                    }
                case CommandTokenKind.Number:
                    int integer;
                    if (int.TryParse(String, NumberStyles.Integer, CultureInfo.InvariantCulture, out integer))
                        return integer;
                    return double.Parse(String, NumberStyles.Float, CultureInfo.InvariantCulture);
                default: return String;
            }
        }

        internal object CSharpEval(string source) {
            var parameters = new CompilerParameters { CompilerOptions = "/t:library", GenerateInMemory = true };
            AddReferences(parameters, AppDomain.CurrentDomain.GetAssemblies());
            foreach (var script in DeveloperConsole.RegisteredScripts)
                parameters.ReferencedAssemblies.Add(script.Key.GetType().Assembly.Location);
            var code = "using System;\nusing System.Drawing;\nusing System.Windows.Forms;\n" +
                "using GTA;\nusing GTA.Math;\nusing GTA.Native;\nusing DeveloperConsole;\n" +
                "namespace DeveloperConsoleCodeEval { public class DeveloperConsoleCodeEval { " +
                "public object EvalCode() { Player PLAYER = Game.Player; Ped PED = PLAYER.Character; " +
                "int MP_ID = Function.Call<int>(Hash.NETWORK_GET_PLAYER_INDEX, PED.Handle);\n" +
                source + ";\nreturn null;\n} } }";
            var result = _environment.Compile(parameters, code);
            if (result.Errors.HasErrors) {
                var error = result.Errors.Cast<CompilerError>().First(e => !e.IsWarning);
                _console.PrintError("C# ERROR: " + error.ErrorText + " at " + error.Line + ":" + error.Column);
                return null;
            }
            var instance = result.CompiledAssembly.CreateInstance("DeveloperConsoleCodeEval.DeveloperConsoleCodeEval");
            if (instance == null) return null;
            return instance.GetType().GetMethod("EvalCode").Invoke(instance, null);
        }

        internal static void AddReferences(CompilerParameters parameters, IEnumerable<Assembly> assemblies) {
            foreach (var assembly in assemblies) {
                try {
                    var location = assembly.Location;
                    if (!string.IsNullOrEmpty(location)) parameters.ReferencedAssemblies.Add(location);
                } catch (NotSupportedException) {
                    // Dynamic assemblies cannot be compiler references.
                }
            }
        }
    }
}
