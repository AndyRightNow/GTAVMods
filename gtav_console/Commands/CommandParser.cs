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
        int NetworkPlayerIndex { get; }
        CompilerResults Compile(CompilerParameters parameters, string source);
    }

    internal class LiveCommandEnvironment : ICommandEnvironment {
        private readonly GameServices _game;
        internal LiveCommandEnvironment(GameServices game) { _game = game; }
        public virtual object Player { [GamePrimitive] get { return Game.Player; } }
        public virtual object Character { [GamePrimitive] get { return Game.Player.Character; } }
        public virtual bool IsInVehicle { [GamePrimitive] get { return Game.Player.Character.IsInVehicle(); } }
        public virtual object Vehicle { [GamePrimitive] get { return Game.Player.Character.CurrentVehicle; } }
        public virtual Vector3 Position { [GamePrimitive] get { return Game.Player.Character.Position; } }
        public virtual string PlayerName { [GamePrimitive] get { return Game.Player.Name; } }
        public virtual int PlayerHandle { [GamePrimitive] get { return Game.Player.Handle; } }
        public int NetworkPlayerIndex { get { return _game.ConsolePlayers.Index(_game.Host.PlayerPed); } }

        public CompilerResults Compile(CompilerParameters parameters, string source) {
            using (var provider = new CSharpCodeProvider())
                return provider.CompileAssemblyFromSource(parameters, source);
        }
    }

    internal sealed class CommandParser {
        public List<CommandToken> Tokens { get; } = new List<CommandToken>();

        public CommandParser(string data, IDeveloperConsole console, ICommandEnvironment environment) {
            foreach (var token in Tokenize(data)) {
                // Parsing and diagnostics must never execute expressions.
                console.PrintDebug("Found token -> " + token.Key + " : " + token.Value);
                Tokens.Add(new CommandToken(token.Key, token.Value, console, environment));
            }
        }

        internal static List<KeyValuePair<CommandTokenKind, string>> Tokenize(string data) {
            var tokens = new List<KeyValuePair<CommandTokenKind, string>>();
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
                tokens.Add(new KeyValuePair<CommandTokenKind, string>(kind, text));
            }
            return tokens;
        }
    }

    public enum CommandTokenKind { Word, Number, String, CodeBlock }

    public class CommandToken {
        private readonly IDeveloperConsole _console;
        private readonly ICommandEnvironment _environment;
        private bool _evaluated;
        private object _value;

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
                        _console.PrintError("C# evaluation failed: " + error.GetBaseException().Message);
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
            var code = "using System;\nusing System.Drawing;\nusing System.Windows.Forms;\n" +
                "using GTA;\nusing GTA.Math;\nusing GTA.Native;\nusing DeveloperConsole;\nusing Control = GTA.Control;\n" +
                "namespace DeveloperConsoleCodeEval { public class DeveloperConsoleCodeEval { " +
                "private ICommandEnvironment context; " +
                "public global::DeveloperConsole.DeveloperConsole CONSOLE { get { return global::DeveloperConsole.DeveloperConsole.Instance; } } " +
                "public GTAFuncs FUNCTIONS { get { return CONSOLE.GameFunctions; } } " +
                "public Player PLAYER { get { return (Player)context.Player; } } " +
                "public Ped PED { get { return (Ped)context.Character; } } " +
                "public int MP_ID { get { return context.NetworkPlayerIndex; } } " +
                "public Vector3 POS { get { return context.Position; } } " +
                "public object EvalCode(ICommandEnvironment environment) { context = environment;\n" +
                source + ";\nreturn null;\n} } }";
            var result = _environment.Compile(parameters, code);
            if (result.Errors.HasErrors) {
                var error = result.Errors.Cast<CompilerError>().First(e => !e.IsWarning);
                _console.PrintError("C# ERROR: " + error.ErrorText + " at " + error.Line + ":" + error.Column);
                return null;
            }
            var instance = result.CompiledAssembly.CreateInstance("DeveloperConsoleCodeEval.DeveloperConsoleCodeEval");
            return instance.GetType().GetMethod("EvalCode").Invoke(instance, new object[] { _environment });
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
