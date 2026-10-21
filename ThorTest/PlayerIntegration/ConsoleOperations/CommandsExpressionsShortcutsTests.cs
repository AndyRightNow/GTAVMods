using DeveloperConsole;
using GTAVMods.Tests.Support;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Globalization;
using System.Linq;
using System.Reflection.Emit;
using System.Reflection;
using System;

namespace GTAVMods.Tests.PlayerIntegration.ConsoleOperations
{
    [TestClass, TestCategory("PlayerIntegration")]
    public class CommandsExpressionsShortcutsTests
    {
        [TestMethod]
        [DataRow("12", "12")] [DataRow("-2", "-2")]
        [DataRow("1.5", "1.5")] [DataRow("1e3", "1000")] [DataRow("2147483648", "2147483648")]
        [DataRow("TRUE", "True")] [DataRow("on", "True")] [DataRow("enable", "True")]
        [DataRow("false", "False")] [DataRow("off", "False")] [DataRow("disable", "False")]
        [DataRow("ordinary", "ordinary")] [DataRow("\"two words\"", "two words")]
        public void ConsoleDisplaysLiteralValuesIndependentlyOfWindowsNumericCulture(string input, string expected) {
            var culture = CultureInfo.CurrentCulture;
            try {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
                using (var scene = new ConsoleFixture()) {
                    scene.Submit("eval " + input);
                    Assert.IsTrue(scene.Platform.Text.Any(t => t.Text == "12:34:56: " + expected));
                }
            } finally { CultureInfo.CurrentCulture = culture; }
        }

        [TestMethod]
        [DataRow("self")] [DataRow("self.character")] [DataRow("self.ped")] [DataRow("self.vehicle")]
        public void CaptionShortcutRejectsPlayerAndEntityObjectsWithItsExpectedTextSignature(string alias) {
            using (var scene = new ConsoleFixture()) {
                scene.Environment.InVehicle = true;
                scene.Evaluate("var c = new CommandDispatcher.Command(\"caption\", \"Add a text caption\", a => CONSOLE.PrintLine(\"Caption: \" + a.Tokens[0].Eval)); c.AddArgumentSet(new CommandDispatcher.CommandArgument(\"text\", \"Text to show\", typeof(string))); CONSOLE.CommandDispatcher.RegisterCommand(c)");
                scene.Submit("clear"); scene.Submit("caption " + alias);
                Assert.IsTrue(scene.Shows("Provided arguments are not valid"));
                Assert.IsTrue(scene.Shows("caption <System.String text>"));
                Assert.IsFalse(scene.Platform.Text.Any(t => t.Text.StartsWith("12:34:56: Caption:")));
            }
        }

        [TestMethod]
        public void CSharpRunsOnceAndItsPlayerContextAndRuntimeErrorsAppearInConsole() {
            using (var scene = new ConsoleFixture()) {
                scene.Evaluate("CONSOLE.PrintLine(\"evaluated once\"); return Tuple.Create(MP_ID, POS, PLAYER.Handle, PED.Handle)");
                Assert.AreEqual(1, scene.Platform.Text.Count(t => t.Text == "12:34:56: evaluated once"));
                Assert.IsTrue(scene.ShowsOutput("(37, X:1 Y:2 Z:3, 7, 11)"));
                scene.Submit("clear");
                scene.Evaluate("return invalid syntax");
                Assert.IsTrue(scene.Shows("C# ERROR:"));
                scene.Submit("clear");
                scene.Evaluate("throw new InvalidOperationException(\"expression failed\")");
                Assert.IsTrue(scene.ShowsOutput("[Error] C# evaluation failed: expression failed"));
                scene.Submit("eval 9");
                Assert.IsTrue(scene.Platform.Text.Any(t => t.Text.EndsWith(": 9")), "A rejected expression must leave the console usable.");
            }
        }

        [TestMethod]
        public void UserCanDefineReplaceAndUseATypedScreenshotLabelCommand() {
            using (var scene = new ConsoleFixture()) {
                scene.Evaluate("var c = new CommandDispatcher.Command(\"label\", \"Annotate a screenshot\", a => CONSOLE.PrintLine(\"Label: \" + a.Tokens[0].Eval)); c.AddArgumentSet(new CommandDispatcher.CommandArgument(\"text\", \"Screenshot caption\", typeof(string))); return CONSOLE.CommandDispatcher.RegisterCommand(c)");
                Assert.IsTrue(scene.ShowsOutput("True"));
                scene.Submit("clear");
                scene.Submit("label \"Mjolnir in daylight\"");
                Assert.IsTrue(scene.ShowsOutput("Label: Mjolnir in daylight"));
                scene.Submit("clear");
                scene.Submit("label 12");
                Assert.IsTrue(scene.Shows("Provided arguments are not valid"));
                Assert.IsTrue(scene.Shows("Screenshot caption"));
                scene.Submit("clear"); scene.Submit("help label");
                Assert.IsTrue(scene.ShowsOutput("Description: Annotate a screenshot"));
                scene.Submit("clear");
                scene.Evaluate("return CONSOLE.CommandDispatcher.RegisterCommand(new CommandDispatcher.Command(\"label\", \"Clear caption\", a => CONSOLE.PrintLine(\"Caption removed\")))");
                Assert.IsTrue(scene.ShowsOutput("False"));
                scene.Submit("clear");
                scene.Submit("label \"Original remains\"");
                Assert.IsTrue(scene.ShowsOutput("Label: Original remains"));
                scene.Submit("clear");
                scene.Evaluate("return CONSOLE.CommandDispatcher.RegisterCommand(new CommandDispatcher.Command(\"label\", \"Clear caption\", a => CONSOLE.PrintLine(\"Caption removed\")), true)");
                Assert.IsTrue(scene.ShowsOutput("True"));
                scene.Submit("clear");
                scene.Submit("label");
                Assert.IsTrue(scene.ShowsOutput("Caption removed"));
            }
        }

        [TestMethod]
        public void ScreenshotNoteShortcutAcceptsNumberedOrFreeformCaptionsAndExplainsRejectedInput() {
            using (var scene = new ConsoleFixture()) {
                scene.Evaluate("var signatures = new System.Collections.Generic.List<System.Collections.Generic.List<CommandDispatcher.CommandArgument>> { new System.Collections.Generic.List<CommandDispatcher.CommandArgument> { new CommandDispatcher.CommandArgument(\"number\", \"Screenshot number\", typeof(int)), new CommandDispatcher.CommandArgument(\"caption\", \" \", typeof(string)) }, new System.Collections.Generic.List<CommandDispatcher.CommandArgument> { new CommandDispatcher.CommandArgument(\"caption\", \"Caption without a number\", typeof(string)) } }; return CONSOLE.CommandDispatcher.RegisterCommand(new CommandDispatcher.Command(\"note\", \"Add a screenshot note\", signatures, a => CONSOLE.PrintLine(a.CommandName + \"[\" + a.ArgIndex + \"]: \" + string.Join(\" / \", a.Tokens.ConvertAll(t => t.Eval)))))");
                Assert.IsTrue(scene.ShowsOutput("True"));
                scene.Submit("clear");
                scene.Submit("note 3 \"Thunder over the city\"");
                Assert.IsTrue(scene.ShowsOutput("note[0]: 3 / Thunder over the city"));
                scene.Submit("clear");
                scene.Submit("note \"Hammer recalled\"");
                Assert.IsTrue(scene.ShowsOutput("note[1]: Hammer recalled"));
                scene.Submit("clear");
                scene.Submit("note true { CONSOLE.PrintLine(\"should not execute\"); return \"caption\"; }");
                Assert.IsTrue(scene.ShowsOutput("[Error] Provided arguments are not valid for command 'note'"));
                Assert.IsFalse(scene.Platform.Text.Any(t => t.Text == "12:34:56: should not execute"));
                Assert.IsTrue(scene.Shows("Screenshot number"));
                Assert.IsFalse(scene.Platform.Text.Any(t => t.Text.EndsWith("caption:  ")));
                scene.Submit("clear"); scene.Submit("note self.vehicle \"Missing screenshot number\"");
                Assert.IsTrue(scene.ShowsOutput("[Error] Provided arguments are not valid for command 'note'"), "An absent vehicle cannot be treated as a required numeric screenshot number.");
                scene.Submit("clear"); scene.Submit("note self.vehicle");
                Assert.IsTrue(scene.ShowsOutput("note[1]: "), "A missing optional caption value is printable rather than converted into a number.");
                scene.Submit("clear"); scene.Submit("note");
                Assert.IsTrue(scene.ShowsOutput("[Error] Provided arguments are not valid for command 'note'"));
            }
        }

        [TestMethod]
        public void ExpressionsStillCompileWithDynamicModsLoadedAndCompilerWarningsDoNotHideErrors() {
            AppDomain.CurrentDomain.DefineDynamicAssembly(new AssemblyName("ConsoleExpressionPlugin" + Guid.NewGuid().ToString("N")), AssemblyBuilderAccess.Run);
            using (var scene = new ConsoleFixture()) {
                scene.Evaluate("return 37");
                Assert.IsTrue(scene.ShowsOutput("37"));
                scene.Submit("clear");
                scene.Evaluate("\n#warning preview snippet\nreturn unavailableValue");
                Assert.IsTrue(scene.Shows("C# ERROR:"));
                Assert.IsTrue(scene.Platform.Text.Any(t => t.Color == System.Drawing.Color.FromArgb(ConsoleSettings.TextAlpha, System.Drawing.Color.Red)
                    && t.Text.Contains("unavailableValue")));
                Assert.IsFalse(scene.Platform.Text.Any(t => t.Text.StartsWith("12:34:56: [Error] C# ERROR: preview snippet")));
                scene.Evaluate("\n#warning preview snippet\nreturn 41");
                Assert.IsTrue(scene.ShowsOutput("41"), "A warning-only snippet is still executable.");
            }
        }
    }
}
