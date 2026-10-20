using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using DeveloperConsole;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using EvaluatedCode = DeveloperConsoleCodeEval.DeveloperConsoleCodeEval;

namespace ModUtilsTest {
    [TestClass]
    public class ConsoleCommandTests {
        [TestMethod]
        public void ParserPreservesQuotedWhitespaceNestedCodeAndEscapedDelimitersWithoutEvaluating() {
            var messages = new ConsoleMessages();
            var environment = new ConsoleEnvironment();
            var parsed = new CommandParser(" \tcmd 42 -3 1.5 1e3 \"\" \"hello  world\" { if (true) { return \"}\"; } } \"a\\\"b\" {a\\}b} tail\n", messages, environment);
            CollectionAssert.AreEqual(new[] { "cmd", "42", "-3", "1.5", "1e3", "", "hello  world",
                " if (true) { return \"}\"; } ", "a\"b", "a}b", "tail" }, parsed.Tokens.Select(t => t.String).ToArray());
            Assert.AreEqual(CommandTokenKind.Word, parsed.Tokens[0].Kind);
            Assert.AreEqual(CommandTokenKind.Number, parsed.Tokens[4].Kind);
            Assert.AreEqual(CommandTokenKind.String, parsed.Tokens[5].Kind);
            Assert.AreEqual(CommandTokenKind.CodeBlock, parsed.Tokens[7].Kind);
            Assert.AreEqual(0, environment.Compilations, "Debug token messages must not compile code.");
            Assert.AreEqual(parsed.Tokens.Count, messages.Debug.Count);
        }

        [TestMethod]
        public void ParserHandlesEmptyInputTrailingEscapeAndUnterminatedSegments() {
            var messages = new ConsoleMessages();
            Assert.AreEqual(0, new CommandParser("", messages).Tokens.Count);
            Assert.AreEqual(0, new CommandParser(" \r\n ", messages).Tokens.Count);
            Assert.AreEqual("unfinished\\", new CommandParser("\"unfinished\\", messages).Tokens.Single().Eval);
            Assert.AreEqual("unfinished", new CommandParser("{unfinished", messages).Tokens.Single().String);
            Assert.AreEqual("", new CommandParser("\"", messages).Tokens.Single().Eval);
            Assert.AreEqual("a\\qb", new CommandParser("\"a\\qb\"", messages).Tokens.Single().Eval);
            Assert.AreEqual("", new CommandParser("{}", messages).Tokens.Single().String);
            Assert.AreEqual(" return \"a\\\"}b\"; ", new CommandParser("{ return \"a\\\"}b\"; }", messages).Tokens.Single().String);
            Assert.AreEqual("\"tail\\", new CommandParser("{\"tail\\", messages).Tokens.Single().String);
            Assert.AreEqual("@\"x\"", new CommandParser("{@\"x\"", messages).Tokens.Single().String);
        }

        [TestMethod]
        [DataRow(" return '}'; ")]
        [DataRow(" return '{'; ")]
        [DataRow(" return '\\''; ")]
        [DataRow(" return '\\\\'; ")]
        [DataRow(" return @\"C:\\\"; ")]
        [DataRow(" return @\"a\"\"}b\"; ")]
        [DataRow(" return @\"a\"\"b\\\"; ")]
        [DataRow(" return @\"a\\}b\"; ")]
        [DataRow("\"a\"")]
        public void CodeBlockPreservesCharacterLiteralsAndVerbatimStringDelimiters(string source) {
            var parsed = new CommandParser("{" + source + "}", new ConsoleMessages());
            Assert.AreEqual(1, parsed.Tokens.Count);
            Assert.AreEqual(CommandTokenKind.CodeBlock, parsed.Tokens[0].Kind);
            Assert.AreEqual(source, parsed.Tokens[0].String);
        }

        [TestMethod]
        [DataRow("TRUE", true)] [DataRow("on", true)] [DataRow("enable", true)]
        [DataRow("false", false)] [DataRow("off", false)] [DataRow("disable", false)]
        public void BooleanAliasesHaveBooleanType(string text, bool expected) {
            var token = new CommandToken(CommandTokenKind.Word, text, new ConsoleMessages());
            Assert.AreEqual(expected, token.Eval);
            Assert.AreEqual(typeof(bool), token.Type);
        }

        [TestMethod]
        public void PlayerTokensResolvePrimitiveObservationsAndCacheNullableResult() {
            var environment = new ConsoleEnvironment();
            var messages = new ConsoleMessages();
            var expected = new Dictionary<string, object> {
                ["self"] = environment.Player, ["self.character"] = environment.Character, ["self.ped"] = environment.Character,
                ["self.vehicle"] = null, ["self.pos"] = environment.Position, ["self.position"] = environment.Position,
                ["self.name"] = environment.PlayerName, ["self.id"] = environment.PlayerHandle,
                ["self.handle"] = environment.PlayerHandle, ["ordinary"] = "ordinary"
            };
            foreach (var pair in expected) {
                var token = new CommandToken(CommandTokenKind.Word, pair.Key, messages, environment);
                Assert.AreEqual(pair.Value, token.Eval, pair.Key);
            }
            var vehicle = new CommandToken(CommandTokenKind.Word, "self.vehicle", messages, environment);
            Assert.IsNull(vehicle.Type);
            environment.Vehicle = new object();
            Assert.IsNull(vehicle.Eval, "A token is a snapshot, including an absent vehicle.");
            Assert.IsNull(new CommandToken(CommandTokenKind.Word, "self.vehicle", messages, environment).Eval,
                "The vehicle alias is absent while the player is not in a vehicle.");
            environment.IsInVehicle = true;
            Assert.AreSame(environment.Vehicle, new CommandToken(CommandTokenKind.Word, "self.vehicle", messages, environment).Eval);
        }

        [TestMethod]
        public void NumericTokensUseInvariantCultureAndSupportExponentAndLargeInteger() {
            var prior = CultureInfo.CurrentCulture;
            try {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
                var parsed = new CommandParser("12 -2 1.5 1e3 2147483648", new ConsoleMessages());
                CollectionAssert.AreEqual(new object[] { 12, -2, 1.5d, 1000d, 2147483648d },
                    parsed.Tokens.Select(t => t.Eval).ToArray());
            } finally { CultureInfo.CurrentCulture = prior; }
        }

        [TestMethod]
        public void CompilerRunsOnceAcrossTokenTypeAndValueReads() {
            var environment = new ConsoleEnvironment();
            var token = new CommandToken(CommandTokenKind.CodeBlock, "return 37", new ConsoleMessages(), environment);
            EvaluatedCode.Evaluations = 0;
            EvaluatedCode.Result = 37;
            using (var fixture = new ConsoleFixture()) {
                var owner = new object();
                DeveloperConsole.DeveloperConsole.RegisterConsoleScript(owner, console => { });
                Assert.AreEqual(typeof(int), token.Type);
                Assert.AreEqual(37, token.Eval);
                Assert.AreEqual(1, environment.Compilations);
                Assert.AreEqual(1, EvaluatedCode.Evaluations);
                Assert.IsTrue(environment.Source.Contains("return 37;"));
                Assert.IsTrue(environment.Source.Contains("context.NetworkPlayerIndex"));
                Assert.IsFalse(environment.Source.Contains("Function.Call"));
                Assert.IsFalse(environment.Source.Contains("Game.Player"));
                Assert.IsTrue(environment.Parameters.GenerateInMemory);
                Assert.AreEqual("/t:library", environment.Parameters.CompilerOptions);
                CollectionAssert.Contains(environment.Parameters.ReferencedAssemblies, owner.GetType().Assembly.Location);
            }
        }

        [TestMethod]
        public void ActualGeneratedCodeWrapperCompilesWithCollectedProductionReferences() {
            using (var fixture = new ConsoleFixture()) {
                EvaluatedCode.Result = 37;
                fixture.Environment.Player = null;
                fixture.Environment.Character = null;
                var token = new CommandToken(CommandTokenKind.CodeBlock, "return Tuple.Create(MP_ID, POS, PLAYER, PED)", new ConsoleMessages(), fixture.Environment);
                Assert.AreEqual(typeof(int), token.Type);
                var compiled = new LiveCommandEnvironment().Compile(fixture.Environment.Parameters, fixture.Environment.Source);
                Assert.IsFalse(compiled.Errors.HasErrors,
                    string.Join(Environment.NewLine, compiled.Errors.Cast<CompilerError>().Select(error => error.ToString())));
                var evaluator = compiled.CompiledAssembly.GetType("DeveloperConsoleCodeEval.DeveloperConsoleCodeEval");
                Assert.IsNotNull(evaluator);
                Assert.AreEqual(typeof(object), evaluator.GetMethod("EvalCode").ReturnType);
                var instance = Activator.CreateInstance(evaluator);
                Assert.AreEqual(Tuple.Create(37, fixture.Environment.Position, (GTA.Player)null, (GTA.Ped)null),
                    evaluator.GetMethod("EvalCode").Invoke(instance, new object[] { fixture.Environment }));
                fixture.Native.Returns(GTA.Native.Hash.NETWORK_GET_PLAYER_INDEX, 19);
                Assert.AreEqual(19, new LiveCommandEnvironment(fixture.Game).NetworkPlayerIndex);
                CollectionAssert.AreEqual(new object[] { 11 }, fixture.Native.Calls.Last().Arguments);
            }
        }

        [TestMethod]
        public void CompilerWarningsAreAllowedErrorsAndThrownFailuresAreReportedAndMissingTypeReturnsNull() {
            var messages = new ConsoleMessages();
            var environment = new ConsoleEnvironment();
            environment.Compiler = () => {
                var result = new CompilerResults(new TempFileCollection());
                result.Errors.Add(new CompilerError("input", 1, 2, "warning", "warning") { IsWarning = true });
                result.Errors.Add(new CompilerError("input", 7, 4, "error", "bad syntax"));
                return result;
            };
            Assert.IsNull(new CommandToken(CommandTokenKind.CodeBlock, "bad", messages, environment).Eval);
            Assert.AreEqual("C# ERROR: bad syntax at 7:4", messages.Errors.Single());
            messages.Errors.Clear();
            environment.Compiler = () => {
                var result = new CompilerResults(new TempFileCollection()) { CompiledAssembly = typeof(string).Assembly };
                result.Errors.Add(new CompilerError("input", 1, 1, "warning", "warning") { IsWarning = true });
                return result;
            };
            Assert.IsNull(new CommandToken(CommandTokenKind.CodeBlock, "missing", messages, environment).Eval);
            Assert.AreEqual(0, messages.Errors.Count);
            environment.Compiler = () => throw new InvalidOperationException("compiler unavailable");
            Assert.IsNull(new CommandToken(CommandTokenKind.CodeBlock, "broken", messages, environment).Eval);
            StringAssert.Contains(messages.Errors.Single(), "compiler unavailable");
        }

        private sealed class LocationlessAssembly : Assembly {
            public override string Location => "";
        }
        [TestMethod]
        public void CompilerReferencesSkipDynamicAndLocationlessAssemblies() {
            var dynamicAssembly = AppDomain.CurrentDomain.DefineDynamicAssembly(new AssemblyName("ConsoleDynamicReference"), AssemblyBuilderAccess.Run);
            var parameters = new CompilerParameters();
            CommandToken.AddReferences(parameters, new Assembly[] { dynamicAssembly, new LocationlessAssembly(), typeof(string).Assembly });
            CollectionAssert.AreEqual(new[] { typeof(string).Assembly.Location }, parameters.ReferencedAssemblies);
        }

        [TestMethod]
        public void DuplicateRegistrationKeepsOriginalCommandUntilOverwriteChangesExecutionAndUsage() {
            using (var fixture = new ConsoleFixture()) {
                fixture.Model.ShowConsole(true);
                fixture.Model.ClearLines();
                var dispatcher = fixture.Model.CommandDispatcher;
                var executions = new List<string>();
                var first = new CommandDispatcher.Command("sample", "read status", args => executions.Add("status"));
                var argument = new CommandDispatcher.CommandArgument("name", "display name", typeof(string));
                var second = new CommandDispatcher.Command("sample", "set display name", args =>
                    executions.Add("renamed: " + args.Tokens.Single().Eval));
                Assert.AreEqual(0, second.AddArgumentSet(argument));
                Assert.IsTrue(dispatcher.RegisterCommand(first));
                Assert.IsFalse(dispatcher.RegisterCommand(second));
                fixture.Model.Input = "sample";
                fixture.Model.RunCommand();
                CollectionAssert.AreEqual(new[] { "status" }, executions,
                    "A rejected duplicate must leave the original command executable.");

                Assert.IsTrue(dispatcher.RegisterCommand(second, true));
                fixture.Model.Input = "sample";
                fixture.Model.RunCommand();
                fixture.Draw();
                CollectionAssert.AreEqual(new[] { "status" }, executions,
                    "The replacement's required argument must be validated before its callback.");
                Assert.IsTrue(fixture.Platform.Text.Any(line => line.Text.Contains("Provided arguments are not valid")));
                Assert.IsTrue(fixture.Platform.Text.Any(line => line.Text.Contains("Description: set display name")));
                Assert.IsTrue(fixture.Platform.Text.Any(line => line.Text.Contains("sample <System.String name>")));
                Assert.IsTrue(fixture.Platform.Text.Any(line => line.Text.EndsWith("name: display name")),
                    "Rejected input must explain the replacement's argument, not just show its type.");

                fixture.Model.Input = "sample \"Mjolnir\"";
                fixture.Model.RunCommand();
                CollectionAssert.AreEqual(new[] { "status", "renamed: Mjolnir" }, executions,
                    "Overwrite must dispatch the replacement with the parsed argument.");
            }
        }

        [TestMethod]
        public void CommandHelpOmitsBlankDescriptionsWhileKeepingEveryArgumentInTheSignature() {
            using (var fixture = new ConsoleFixture()) {
                fixture.Model.ShowConsole(true);
                fixture.Model.ClearLines();
                var command = new CommandDispatcher.Command("rename", "set display name", args => { });
                command.AddArgumentSet(
                    new CommandDispatcher.CommandArgument("name", "display name", typeof(string)),
                    new CommandDispatcher.CommandArgument("style", " \t", typeof(int)));
                fixture.Model.CommandDispatcher.RegisterCommand(command);

                fixture.Model.PrintCommandInfo("rename");
                fixture.Draw();

                CollectionAssert.AreEqual(new[] {
                    "12:34:56: Command: rename",
                    "12:34:56: Description: set display name",
                    "12:34:56: rename <System.String name> <System.Int32 style> ",
                    "12:34:56: name: display name"
                }, fixture.Platform.Text.Take(4).Select(line => line.Text).ToArray());
                Assert.HasCount(6, fixture.Platform.Text,
                    "A blank description must not consume an output row; only version and input follow the help.");
            }
        }

        [TestMethod]
        public void ArgumentsRejectSiblingScalarTypesAndAcceptAssignableAndNullableValues() {
            var messages = new ConsoleMessages();
            var empty = new CommandDispatcher.Command("empty", "", args => { });
            Assert.AreEqual(0, empty.AreTokensValid(new List<CommandToken>()));
            Assert.AreEqual(-1, empty.AreTokensValid(new List<CommandToken> { new CommandToken(CommandTokenKind.Number, "1", messages) }));
            var command = new CommandDispatcher.Command("choose", "", new List<List<CommandDispatcher.CommandArgument>>(), args => { });
            command.AddArgumentSet(new CommandDispatcher.CommandArgument("value", "", typeof(int)));
            command.AddArgumentSet(new CommandDispatcher.CommandArgument("value", "", typeof(string)));
            command.AddArgumentSet(new CommandDispatcher.CommandArgument("value", "", typeof(object)));
            var boolean = new CommandToken(CommandTokenKind.Word, "true", messages);
            Assert.AreEqual(2, command.AreTokensValid(new List<CommandToken> { boolean }), "int and bool share ValueType but are incompatible.");
            Assert.AreEqual(0, command.AreTokensValid(new List<CommandToken> { new CommandToken(CommandTokenKind.Number, "1", messages) }));
            Assert.AreEqual(1, command.AreTokensValid(new List<CommandToken> { new CommandToken(CommandTokenKind.String, "text", messages) }));
            var absent = new CommandToken(CommandTokenKind.Word, "self.vehicle", messages, new ConsoleEnvironment());
            Assert.AreEqual(1, command.AreTokensValid(new List<CommandToken> { absent }));
            var nullable = new CommandDispatcher.Command("nullable", "", args => { });
            nullable.AddArgumentSet(new CommandDispatcher.CommandArgument("value", "", typeof(int?)));
            Assert.AreEqual(0, nullable.AreTokensValid(new List<CommandToken> { absent }));
            Assert.AreEqual(-1, nullable.AreTokensValid(new List<CommandToken> { boolean }));
            Assert.AreEqual(-1, command.AreTokensValid(new List<CommandToken>()));
            command.AddArgumentSet();
            Assert.AreEqual(3, command.AreTokensValid(new List<CommandToken>()));
        }

        [TestMethod]
        public void RejectedArgumentSetDoesNotEvaluateLaterCodeArguments() {
            var messages = new ConsoleMessages();
            var environment = new ConsoleEnvironment();
            var command = new CommandDispatcher.Command("pair", "pair of numbers", args => { });
            command.AddArgumentSet(new CommandDispatcher.CommandArgument("first", "", typeof(int)),
                new CommandDispatcher.CommandArgument("second", "", typeof(int)));
            var tokens = new CommandParser("true { return 42; }", messages, environment).Tokens;
            Assert.AreEqual(-1, command.AreTokensValid(tokens));
            Assert.AreEqual(0, environment.Compilations,
                "Once an argument set cannot match, evaluating later user code would introduce unrelated side effects.");
        }
    }
}
