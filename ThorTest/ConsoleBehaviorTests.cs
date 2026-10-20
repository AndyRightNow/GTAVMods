using System;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using DeveloperConsole;
using GTA.Native;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Control = GTA.Control;

namespace ModUtilsTest {
    [TestClass]
    public class ConsoleBehaviorTests {
        [TestMethod]
        [TestCategory("PlayerIntegration")]
        public void CommandOutputUsesTheCurrentLocalClockWhenThePlayerSubmitsIt() {
            using (var scene = new ConsoleFixture()) {
                scene.Platform.UseSystemClock = true;
                var before = DateTime.Now;
                scene.Submit("eval 45");
                var after = DateTime.Now;
                var result = scene.Platform.Text.Single(t => t.Text.EndsWith(": 45")).Text;
                var expectedPrefixes = Enumerable.Range(0, (int)Math.Ceiling((after - before).TotalSeconds) + 1)
                    .Select(second => before.AddSeconds(second).ToString("HH:mm:ss") + ": ");
                Assert.IsTrue(expectedPrefixes.Any(prefix => result.StartsWith(prefix)));
            }
        }

        [TestMethod]
        [TestCategory("PlayerIntegration")]
        [DataRow(false)] [DataRow(true)]
        public void ThorStartupMakesDiagnosticLogDiscoverableAcrossConsoleReloadAndDetachesOnAbort(bool consoleStartsFirst) {
            var thorScript = new object(); // Host-owned script identity; the callback is the shipped integration.
            if (!consoleStartsFirst) DeveloperConsole.DeveloperConsole.RegisterConsoleScript(thorScript, ADModUtils.Logger.Init);
            using (var first = new ConsoleFixture()) {
                if (consoleStartsFirst) DeveloperConsole.DeveloperConsole.RegisterConsoleScript(thorScript, ADModUtils.Logger.Init);
                first.Press(Keys.F8); first.Draw();
                Assert.IsTrue(string.Concat(first.Platform.Text.Select(t => t.Text)).Contains("Thor diagnostics: " + ADModUtils.Logger.LogFilePath));
                first.Evaluate("return POS.Z");
                Assert.IsTrue(first.Platform.Text.Any(t => t.Text == "12:34:56: 3"));
                using (var replacement = new ConsoleFixture()) {
                    first.Model.Dispose();
                    replacement.Press(Keys.F8); replacement.Draw();
                    Assert.IsTrue(replacement.Shows("Thor diagnostics:"));
                    DeveloperConsole.DeveloperConsole.UnregisterConsoleScript(thorScript);
                    using (var detached = new ConsoleFixture()) {
                        detached.Press(Keys.F8); detached.Draw();
                        Assert.IsFalse(detached.Shows("Thor diagnostics:"), "An aborted Thor script must not attach to the next console.");
                    }
                }
            }
        }

        [TestMethod]
        [TestCategory("PlayerIntegration")]
        public void OpeningF8ShowsRealHelpAndClosingOrReloadRestoresThePlayersControls() {
            using (var scene = new ConsoleFixture()) {
                scene.Enabled.Add(Control.Attack); scene.Enabled.Add(Control.Jump);
                scene.Model.Tick();
                Assert.IsFalse(scene.Model.IsOpen);
                scene.Press(Keys.B);
                Assert.AreEqual("", scene.Model.Input);
                scene.Press(Keys.F8); scene.Draw();
                Assert.IsTrue(scene.Model.IsOpen);
                Assert.IsFalse(scene.Enabled.Contains(Control.Attack));
                Assert.IsTrue(scene.Enabled.Contains(Control.MoveUpDown));
                Assert.IsTrue(scene.Shows("F8") && scene.Shows("F4"));
                scene.Submit("help");
                Assert.IsTrue(scene.Shows("help:") && scene.Shows("eval:") && scene.Shows("clear:"));
                scene.Press(Keys.F8);
                CollectionAssert.AreEquivalent(new[] { Control.Attack, Control.Jump }, scene.Enabled.ToArray());
                scene.Press(Keys.F8); scene.Press(Keys.Escape); scene.Press(Keys.Escape);
                Assert.IsFalse(scene.Model.IsOpen);
                scene.Press(Keys.F8); scene.Model.Dispose();
                CollectionAssert.AreEquivalent(new[] { Control.Attack, Control.Jump }, scene.Enabled.ToArray());
            }
        }

        [TestMethod]
        [TestCategory("PlayerIntegration")]
        public void EditingAndClipboardSubmitTheCorrectExpressionWithoutLosingCursorPosition() {
            using (var scene = new ConsoleFixture()) {
                scene.Press(Keys.F8);
                foreach (var key in new[] { Keys.Up, Keys.Down, Keys.Delete, Keys.Back, Keys.Left, Keys.Right }) scene.Press(key);
                scene.Platform.Clipboard = "eval 123"; scene.Press(Keys.Control | Keys.V);
                scene.Press(Keys.Delete); scene.Press(Keys.Left); scene.Press(Keys.Delete);
                Assert.AreEqual("eval 12", scene.Model.Input);
                scene.Press(Keys.Back); Assert.AreEqual("eval 1", scene.Model.Input);
                scene.Platform.Clipboard = "5"; scene.Press(Keys.Control | Keys.V);
                scene.Press(Keys.Control | Keys.C);
                Assert.AreEqual("eval 15", scene.Platform.Clipboard);
                Assert.AreEqual(ApartmentState.STA, scene.Platform.ClipboardDevice.Apartment);
                scene.Press(Keys.Enter); scene.Draw();
                Assert.IsTrue(scene.ShowsOutput("15"));
                scene.Press(Keys.Up); Assert.AreEqual("eval 15", scene.Model.Input);
                for (int i = 0; i < 20; i++) scene.Press(Keys.Left);
                scene.Press(Keys.Back); scene.Press(Keys.Right); scene.Press(Keys.Back);
                scene.Type(Keys.E, "e");
                Assert.AreEqual("eval 15", scene.Model.Input);
                scene.Press(Keys.Right); scene.Platform.Time = 601; scene.Draw();
                Assert.IsTrue(scene.Shows("ev|al 15"));
                for (int i = 0; i < "eval 15".Length; i++) scene.Press(Keys.Right);
                for (int i = 0; i < "eval 15".Length; i++) scene.Press(Keys.Back);
                scene.Press(Keys.Right); Assert.AreEqual("", scene.Model.Input);
                scene.Type(Keys.C, "c"); scene.Type(Keys.V, "v");
                Assert.AreEqual("cv", scene.Model.Input);
            }
        }

        [TestMethod]
        [TestCategory("PlayerIntegration")]
        public void KeyboardDeadKeysModifiersForegroundAndRepeatProduceOnlyIntendedText() {
            using (var scene = new ConsoleFixture()) {
                scene.Press(Keys.F8);
                scene.Platform.Translation = null; scene.Press(Keys.A);
                scene.Platform.Translation = ""; scene.Press(Keys.A);
                scene.Platform.Translation = "ab"; scene.Press(Keys.A);
                scene.Platform.Translation = "a"; scene.Press(Keys.A | Keys.Shift | Keys.Alt);
                Assert.AreEqual(255, scene.Platform.Keyboard.State[(int)Keys.ShiftKey]);
                Assert.AreEqual(255, scene.Platform.Keyboard.State[(int)Keys.ControlKey]);
                Assert.AreEqual(255, scene.Platform.Keyboard.State[(int)Keys.Menu]);
                scene.Press(Keys.A); Assert.AreEqual("a", scene.Model.Input);
                scene.Model.KeyUp(new KeyEventArgs(Keys.A)); scene.Type(Keys.A, "a");
                scene.Platform.Keyboard.Window = IntPtr.Zero; scene.Type(Keys.B, "b");
                scene.Platform.Keyboard.Window = new IntPtr(23); scene.Platform.Keyboard.ForegroundProcess = 99; scene.Type(Keys.B, "b");
                scene.Platform.Keyboard.ForegroundProcess = 42; scene.Type(Keys.Tab, "\t");
                Assert.AreEqual("aa", scene.Model.Input);
                scene.Platform.Keyboard.Caps = 1; scene.Type(Keys.B, "b"); scene.Type(Keys.C, "C"); scene.Type(Keys.D1, "1");
                scene.Platform.Keyboard.Caps = unchecked((short)0x8000); scene.Type(Keys.D, "d");
                Assert.AreEqual("aaBc1D", scene.Model.Input);
                scene.Platform.Keyboard.Caps = 0; scene.Platform.Translation = "z"; scene.Press(Keys.Z);
                scene.Press(Keys.F8); scene.Press(Keys.F8); scene.Press(Keys.Z);
                Assert.AreEqual("aaBc1Dzz", scene.Model.Input);
            }
        }

        [TestMethod]
        [TestCategory("PlayerIntegration")]
        public void LockedClipboardLeavesTheTypedCommandIntactAndRetryCanSubmitIt() {
            using (var scene = new ConsoleFixture()) {
                scene.Press(Keys.F8); scene.Type(Keys.E, "e");
                scene.Platform.ClipboardDevice.Failure = new InvalidOperationException("clipboard locked");
                var error = Assert.ThrowsExactly<InvalidOperationException>(() => scene.Press(Keys.Control | Keys.V));
                Assert.AreEqual("clipboard locked", error.InnerException.Message);
                Assert.AreEqual("e", scene.Model.Input);
                scene.Platform.ClipboardDevice.Failure = null; scene.Platform.Clipboard = "val 27";
                scene.Press(Keys.Control | Keys.V); scene.Press(Keys.Enter); scene.Draw();
                Assert.IsTrue(scene.ShowsOutput("27"));
            }
        }

        [TestMethod]
        [TestCategory("PlayerIntegration")]
        public void HistoryCanBeRecalledEditedAndScrolledWhileInvalidCommandsStayVisible() {
            using (var scene = new ConsoleFixture()) {
                scene.Submit("eval 12"); scene.Submit("eval true");
                scene.Press(Keys.Up); Assert.AreEqual("eval true", scene.Model.Input);
                scene.Press(Keys.Up); scene.Press(Keys.Up); Assert.AreEqual("eval 12", scene.Model.Input);
                scene.Press(Keys.Down); Assert.AreEqual("eval true", scene.Model.Input);
                scene.Press(Keys.Down); Assert.AreEqual("", scene.Model.Input);
                scene.Press(Keys.Up); scene.Type(Keys.X, "x"); scene.Press(Keys.Up);
                Assert.AreEqual("eval true", scene.Model.Input);
                foreach (var invalid in new[] { "", "123 eval", "\"eval\"", "missing { throw null; }", "eval", "clear extra", "help missing" }) {
                    scene.Submit("clear"); scene.Submit(invalid);
                    Assert.IsTrue(scene.Platform.Text.Any(t => t.Color == Color.FromArgb(ConsoleSettings.TextAlpha, Color.Red)));
                }
                scene.Submit("clear");
                scene.Evaluate("for (int i = 0; i < 20; i++) CONSOLE.PrintLine(\"line\" + i)");
                Assert.IsFalse(scene.Shows(": line0")); Assert.IsTrue(scene.Shows(": line19"));
                for (int i = 0; i < 30; i++) scene.Press(Keys.PageUp);
                scene.Draw(); Assert.IsTrue(scene.Shows(": line0")); Assert.IsFalse(scene.Shows(": line19"));
                for (int i = 0; i < 30; i++) scene.Press(Keys.PageDown);
                scene.Draw(); Assert.IsTrue(scene.Shows(": line19"));
                Assert.AreEqual(new Size(1200, 300), scene.Platform.Rectangles[0].Size);
                scene.Platform.Time = 600; scene.Draw(); Assert.IsFalse(scene.Platform.Text.Last().Text.Contains("|"));
                scene.Platform.Time = 601; scene.Draw(); Assert.IsTrue(scene.Platform.Text.Last().Text.Contains("|"));
                scene.Platform.Time = 1202; scene.Draw(); Assert.IsFalse(scene.Platform.Text.Last().Text.Contains("|"));
                scene.Submit("clear"); scene.Draw(); Assert.AreEqual(2, scene.Platform.Text.Count);
            }
        }

        [TestMethod]
        [TestCategory("PlayerIntegration")]
        public void ConsoleMessagesRetainSeverityAndLongLinesContinueAtMeasuredPixelPositions() {
            using (var scene = new ConsoleFixture()) {
                scene.Evaluate("CONSOLE.PrintWarning(\"practice save\"); CONSOLE.PrintError(\"example failure\"); CONSOLE.PrintLine(\"note\", Color.Purple); CONSOLE.PrintDebug(\"hidden debug\"); CONSOLE.Debug = true; CONSOLE.PrintDebug(\"debug visible\")");
                Assert.IsTrue(scene.Platform.Text.Any(t => t.Text.EndsWith("[Warning] practice save") && t.Color == Color.FromArgb(ConsoleSettings.TextAlpha, Color.Yellow)));
                Assert.IsTrue(scene.Platform.Text.Any(t => t.Text.EndsWith("[Error] example failure") && t.Color == Color.FromArgb(ConsoleSettings.TextAlpha, Color.Red)));
                Assert.IsTrue(scene.Platform.Text.Any(t => t.Text.EndsWith(": note") && t.Color == Color.Purple));
                Assert.IsFalse(scene.Platform.Text.Any(t => t.Text.EndsWith("[Debug] hidden debug")));
                Assert.IsTrue(scene.Platform.Text.Any(t => t.Text == "12:34:56: [Debug] debug visible"
                    && t.Color == Color.FromArgb(ConsoleSettings.TextAlpha, Color.LightGray)));
                scene.Evaluate("CONSOLE.Debug = false; CONSOLE.ClearLines(); CONSOLE.PrintLine(new string('x', 200))");
                var chunks = scene.Platform.Text.Take(3).ToArray();
                CollectionAssert.AreEqual(new[] { 99, 99, 12 }, chunks.Select(t => t.Text.Length).ToArray());
                CollectionAssert.AreEqual(new[] { 5, 17, 29 }, chunks.Select(t => t.Position.X).ToArray());
                scene.Evaluate("CONSOLE.ClearLines(); CONSOLE.RemoveLastLine(); CONSOLE.PrintLine(\"temporary\"); CONSOLE.RemoveLastLine(); CONSOLE.PrintLine(new string('x', 89))");
                Assert.AreEqual(99, scene.Platform.Text.First().Text.Length);
                Assert.IsFalse(scene.Shows("temporary"));
            }
        }

        [TestMethod]
        [TestCategory("PureFunction")]
        public void PublishedVersionFormattingDistinguishesDevelopmentBuilds() {
            Assert.AreEqual("DEV", ConsoleSettings.DisplayVersion("9.9.9.9"));
            Assert.AreEqual("1.2.3", ConsoleSettings.DisplayVersion("1.2.3"));
        }

        [TestMethod]
        [TestCategory("PlayerIntegration")]
        [DataRow("DEV", true)] [DataRow("1.2.3", false)]
        public void DevelopmentBuildShowsExpressionDiagnosticsWhileReleaseShowsOnlyTheResult(string version, bool debug) {
            var prior = ConsoleSettings.Version;
            try {
                ConsoleSettings.Version = version;
                using (var scene = new ConsoleFixture(debug: null)) {
                    scene.Submit("eval 25");
                    Assert.IsTrue(scene.ShowsOutput("25"));
                    Assert.AreEqual(debug, scene.Shows("Found token"));
                }
            }
            finally { ConsoleSettings.Version = prior; }
        }
    }
}
