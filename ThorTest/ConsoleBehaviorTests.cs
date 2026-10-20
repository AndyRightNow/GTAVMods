using System;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using DeveloperConsole;
using GTA.Native;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ConsoleModel = DeveloperConsole.DeveloperConsole;
using Control = GTA.Control;

namespace ModUtilsTest {
    [TestClass]
    public class ConsoleBehaviorTests {
        [TestMethod]
        public void RegistrationAttachesBeforeAndAfterConstructionAndUnregistersOnlyOwner() {
            var first = new object();
            var second = new object();
            var firstCalls = 0;
            var secondCalls = 0;
            ConsoleModel.RegisterConsoleScript(first, console => firstCalls++);
            using (var fixture = new ConsoleFixture()) {
                Assert.AreSame(fixture.Model, ConsoleModel.Instance);
                Assert.AreEqual(1, firstCalls);
                ConsoleModel.RegisterConsoleScript(second, console => secondCalls++);
                Assert.AreEqual(1, secondCalls);
                ConsoleModel.UnregisterConsoleScript(first);
                Assert.AreSame(second, ConsoleModel.RegisteredScripts.Single().Key);
                using (var replacement = new ConsoleFixture()) {
                    Assert.AreEqual(2, secondCalls);
                    fixture.Model.Dispose();
                    Assert.AreSame(replacement.Model, ConsoleModel.Instance);
                    Assert.AreEqual(1, ConsoleModel.RegisteredScripts.Count);
                }
                Assert.IsNull(ConsoleModel.Instance);
                Assert.AreEqual(0, ConsoleModel.RegisteredScripts.Count);
            }
        }

        [TestMethod]
        public void OpeningClosingAndAbortRestoreOriginalControlSetAndAvoidRepeatedCapture() {
            using (var fixture = new ConsoleFixture()) {
                var originallyEnabled = new[] { Control.Attack, Control.Jump };
                fixture.Native.Queries[Hash.IS_CONTROL_ENABLED] = args => originallyEnabled.Contains((Control)(int)args[1]);
                Assert.IsFalse(fixture.Model.IsOpen);
                fixture.Model.Tick();
                Assert.AreEqual(0, fixture.Native.Calls.Count);
                var hiddenKey = new KeyEventArgs(Keys.B);
                fixture.Model.KeyDown(hiddenKey);
                Assert.IsFalse(hiddenKey.SuppressKeyPress);
                fixture.Press(Keys.F8);
                Assert.IsTrue(fixture.Model.IsOpen);
                var captures = fixture.Native.Calls.Count(c => c.Hash == Hash.IS_CONTROL_ENABLED);
                fixture.Model.ShowConsole(true);
                Assert.AreEqual(captures, fixture.Native.Calls.Count(c => c.Hash == Hash.IS_CONTROL_ENABLED));
                fixture.Native.Calls.Clear();
                fixture.Press(Keys.F8);
                Assert.IsFalse(fixture.Model.IsOpen);
                Assert.AreEqual(Hash.DISABLE_ALL_CONTROL_ACTIONS, fixture.Native.Calls[0].Hash);
                CollectionAssert.AreEquivalent(originallyEnabled.Select(c => (int)c).ToArray(),
                    fixture.Native.Calls.Skip(1).Select(c => (int)c.Arguments[1]).ToArray());
                fixture.Model.ShowConsole(true);
                fixture.Press(Keys.Escape);
                Assert.IsFalse(fixture.Model.IsOpen);
                fixture.Press(Keys.Escape);
                fixture.Model.ShowConsole(true);
                fixture.Native.Calls.Clear();
                fixture.Model.Dispose();
                Assert.AreEqual(3, fixture.Native.Calls.Count);
            }
        }

        [TestMethod]
        public void CursorDeleteBackspaceAndClipboardRespectInsertionPositionAndBoundaries() {
            using (var fixture = new ConsoleFixture()) {
                fixture.Model.ShowConsole(true);
                fixture.Press(Keys.Up);
                fixture.Press(Keys.Down);
                fixture.Press(Keys.Delete);
                fixture.Press(Keys.Back);
                fixture.Press(Keys.Left);
                fixture.Press(Keys.Right);
                fixture.Model.Input = "abc";
                fixture.Press(Keys.Delete);
                Assert.AreEqual("abc", fixture.Model.Input, "Delete at end must be a no-op.");
                fixture.Press(Keys.Left);
                fixture.Press(Keys.Delete);
                Assert.AreEqual("ab", fixture.Model.Input);
                fixture.Press(Keys.Left);
                fixture.Press(Keys.Left);
                fixture.Press(Keys.Left);
                fixture.Press(Keys.Back);
                Assert.AreEqual("ab", fixture.Model.Input, "Backspace at start must be a no-op.");
                fixture.Press(Keys.Right);
                fixture.Press(Keys.Back);
                Assert.AreEqual("b", fixture.Model.Input);
                fixture.Platform.Clipboard = "xy";
                fixture.Press(Keys.Control | Keys.V);
                Assert.AreEqual("xyb", fixture.Model.Input);
                fixture.Press(Keys.Control | Keys.C);
                Assert.AreEqual("xyb", fixture.Platform.Clipboard);
                fixture.Model.Input = null;
                Assert.AreEqual("", fixture.Model.Input);
                fixture.Press(Keys.Right);
                fixture.Type(Keys.C, "c");
                fixture.Type(Keys.V, "v");
                Assert.AreEqual("cv", fixture.Model.Input);
            }
        }

        [TestMethod]
        public void TextInputFiltersRepeatedKeysDeadKeysControlCharactersAndBackgroundDelivery() {
            using (var fixture = new ConsoleFixture()) {
                fixture.Model.ShowConsole(true);
                fixture.Platform.Translation = null;
                fixture.Press(Keys.A);
                fixture.Platform.Translation = "";
                fixture.Press(Keys.A);
                fixture.Platform.Translation = "a";
                fixture.Press(Keys.A | Keys.Shift | Keys.Alt);
                Assert.AreEqual((Keys.A, true, true), fixture.Platform.LastTranslation);
                fixture.Press(Keys.A);
                Assert.AreEqual("a", fixture.Model.Input);
                fixture.Model.KeyUp(new KeyEventArgs(Keys.A));
                fixture.Type(Keys.A, "a");
                Assert.AreEqual("aa", fixture.Model.Input);
                fixture.Platform.IsForeground = false;
                fixture.Type(Keys.B, "b");
                fixture.Platform.IsForeground = true;
                fixture.Type(Keys.Tab, "\t");
                Assert.AreEqual("aa", fixture.Model.Input);
                fixture.Platform.CapsLockState = 1;
                fixture.Type(Keys.B, "b");
                fixture.Type(Keys.C, "C");
                fixture.Type(Keys.D1, "1");
                fixture.Platform.CapsLockState = unchecked((short)0x8000);
                fixture.Type(Keys.D, "d");
                Assert.AreEqual("aaBc1D", fixture.Model.Input);
            }
        }

        [TestMethod]
        public void CommandExecutionValidatesArgumentsReportsErrorsAndCyclesHistory() {
            using (var fixture = new ConsoleFixture()) {
                fixture.Model.ShowConsole(true);
                var calls = 0;
                var command = new CommandDispatcher.Command("set", "set number", args => {
                    Assert.AreEqual("set", args.CommandName);
                    Assert.AreEqual(0, args.ArgIndex);
                    Assert.AreEqual(12, args.Tokens.Single().Eval);
                    args.ArgIndex = 2;
                    Assert.AreEqual(2, args.ArgIndex);
                    calls++;
                });
                command.AddArgumentSet(new CommandDispatcher.CommandArgument("number", "value", typeof(int)));
                fixture.Model.CommandDispatcher.RegisterCommand(command);
                fixture.Model.Input = "set 12";
                fixture.Press(Keys.Enter);
                Assert.AreEqual(1, calls);
                Assert.AreEqual("", fixture.Model.Input);
                fixture.Model.Input = "set true";
                fixture.Press(Keys.Enter);
                Assert.AreEqual(1, calls);
                fixture.Draw();
                Assert.IsTrue(fixture.Platform.Text.Any(t => t.Text.Contains("Provided arguments are not valid")));
                fixture.Press(Keys.Up);
                Assert.AreEqual("set true", fixture.Model.Input);
                fixture.Press(Keys.Up);
                fixture.Press(Keys.Up);
                Assert.AreEqual("set 12", fixture.Model.Input);
                fixture.Press(Keys.Down);
                Assert.AreEqual("set true", fixture.Model.Input);
                fixture.Press(Keys.Down);
                Assert.AreEqual("", fixture.Model.Input);
                fixture.Press(Keys.Up);
                Assert.AreEqual("set true", fixture.Model.Input, "Down past newest resets the history cursor.");
                foreach (var invalid in new[] { "", "123 set", "\"set\"", "missing { throw null; }" }) {
                    fixture.Model.Input = invalid;
                    fixture.Model.RunCommand();
                }
                Assert.AreEqual(0, fixture.Environment.Compilations, "Unknown commands must not execute argument code.");
                fixture.Draw();
                Assert.IsTrue(fixture.Platform.Text.Any(t => t.Text.Contains("Command 'missing' not found!")));
                fixture.Model.ClearLines();
                fixture.Model.PrintCommandInfo("set");
                fixture.Model.PrintCommandInfo("absent");
                var status = new CommandDispatcher.Command("status", "show status", args => { });
                fixture.Model.PrintCommandInfo(status);
                status.AddArgumentSet();
                fixture.Model.PrintCommandInfo(status);
                fixture.Draw();
                Assert.IsTrue(fixture.Platform.Text.Any(t => t.Text.Contains("System.Int32 number")));
                Assert.IsTrue(fixture.Platform.Text.Any(t => t.Text.Contains("Failed to print info")));
                Assert.IsTrue(fixture.Platform.Text.Any(t => t.Text.Contains("Description: show status")));
                Assert.IsTrue(fixture.Platform.Text.Any(t => t.Text.EndsWith("status ")));
            }
        }

        [TestMethod]
        public void OutputHasTimestampColorsAndDebugPolicyAndClearsSafely() {
            using (var fixture = new ConsoleFixture()) {
                fixture.Model.ShowConsole(true);
                fixture.Model.ClearLines();
                fixture.Model.PrintWarning("warn");
                fixture.Model.PrintError("error");
                fixture.Model.PrintLine("custom", Color.Purple);
                fixture.Model.PrintDebug("hidden");
                fixture.Model.Debug = true;
                DefaultCommands.OnConsoleAttached(fixture.Model);
                fixture.Draw();
                Assert.IsTrue(fixture.Platform.Text.Any(t => t.Text == "12:34:56: [Warning] warn" && t.Color == Color.FromArgb(ConsoleSettings.TextAlpha, Color.Yellow)));
                Assert.IsTrue(fixture.Platform.Text.Any(t => t.Text == "12:34:56: [Error] error" && t.Color == Color.FromArgb(ConsoleSettings.TextAlpha, Color.Red)));
                Assert.IsTrue(fixture.Platform.Text.Any(t => t.Text == "12:34:56: custom" && t.Color == Color.Purple));
                Assert.IsFalse(fixture.Platform.Text.Any(t => t.Text.Contains("hidden")));
                Assert.IsTrue(fixture.Platform.Text.Any(t => t.Text.Contains("DefaultCommands loaded")));
                fixture.Model.RemoveLastLine();
                fixture.Draw();
                Assert.IsFalse(fixture.Platform.Text.Any(t => t.Text.Contains("DefaultCommands loaded")));
                fixture.Model.ClearLines();
                fixture.Model.RemoveLastLine();
                fixture.Draw();
                Assert.AreEqual(2, fixture.Platform.Text.Count, "Only version and input remain.");
            }
        }

        [TestMethod]
        public void RenderingScrollsOnlyWithinHistoryAndBlinksAtStrictSixHundredMillisecondBoundary() {
            using (var fixture = new ConsoleFixture()) {
                fixture.Model.ShowConsole(true);
                fixture.Model.ClearLines();
                for (var i = 0; i < 20; i++) fixture.Model.PrintLine("line" + i);
                fixture.Draw();
                Assert.AreEqual(new Size(1200, 300), fixture.Platform.Rectangles[0].Size);
                Assert.IsTrue(fixture.Platform.Text.Any(t => t.Text.EndsWith("line19")));
                Assert.IsFalse(fixture.Platform.Text.Any(t => t.Text.EndsWith("line4")));
                for (var i = 0; i < 25; i++) fixture.Press(Keys.PageUp);
                fixture.Draw();
                Assert.IsTrue(fixture.Platform.Text.Any(t => t.Text.EndsWith("line0")));
                Assert.IsFalse(fixture.Platform.Text.Any(t => t.Text.EndsWith("line19")));
                for (var i = 0; i < 25; i++) fixture.Press(Keys.PageDown);
                fixture.Draw();
                Assert.IsTrue(fixture.Platform.Text.Any(t => t.Text.EndsWith("line19")));
                Assert.AreEqual(new Point(1190, 69), fixture.Platform.Rectangles[1].Position);
                Assert.AreEqual(new Size(8, 206), fixture.Platform.Rectangles[1].Size);
                fixture.Platform.GameTime = 600;
                fixture.Draw();
                Assert.AreEqual("--> ", fixture.Platform.Text.Last().Text);
                fixture.Platform.GameTime = 601;
                fixture.Draw();
                Assert.AreEqual("--> |", fixture.Platform.Text.Last().Text);
                fixture.Platform.GameTime = 1202;
                fixture.Draw();
                Assert.AreEqual("--> ", fixture.Platform.Text.Last().Text);
                for (var i = 0; i < 25; i++) fixture.Press(Keys.PageUp);
                for (var i = 0; i < 10; i++) fixture.Model.RemoveLastLine();
                fixture.Draw();
                Assert.IsTrue(fixture.Platform.Text.Any(t => t.Text.EndsWith("line0")));
                Assert.AreEqual(2, fixture.Platform.Rectangles.Count);
            }
        }

        [TestMethod]
        public void LongTextMeasuresEachChunkAndAdvancesUsingScreenPixels() {
            using (var fixture = new ConsoleFixture()) {
                fixture.Model.ShowConsole(true);
                fixture.Model.ClearLines();
                fixture.Model.PrintLine(new string('x', 200));
                fixture.Native.Calls.Clear();
                fixture.Draw();
                var chunks = fixture.Platform.Text.Take(3).ToArray();
                CollectionAssert.AreEqual(new[] { 99, 99, 12 }, chunks.Select(t => t.Text.Length).ToArray());
                CollectionAssert.AreEqual(new[] { 5, 17, 29 }, chunks.Select(t => t.Position.X).ToArray());
                var measured = fixture.Native.Calls.Where(c => c.Hash == Hash.ADD_TEXT_COMPONENT_SUBSTRING_PLAYER_NAME)
                    .Select(c => (string)c.Arguments[0]).Take(3).ToArray();
                CollectionAssert.AreEqual(chunks.Select(t => t.Text).ToArray(), measured);
            }
        }

        [TestMethod]
        public void KeyboardTranslationSetsModifiersAndRejectsDeadOrMultiCharacterKeys() {
            var raw = new ConsoleKeyboard();
            var keyboard = new NativeMethods(raw);
            Assert.AreEqual("a", keyboard.GetCharsFromKeys(Keys.A, false, false));
            Assert.AreEqual((uint)Keys.A, raw.Key);
            Assert.IsTrue(raw.State.All(b => b == 0));
            keyboard.GetCharsFromKeys(Keys.B, true, true);
            Assert.AreEqual(255, raw.State[(int)Keys.ShiftKey]);
            Assert.AreEqual(255, raw.State[(int)Keys.ControlKey]);
            Assert.AreEqual(255, raw.State[(int)Keys.Menu]);
            raw.Characters = -1;
            Assert.IsNull(keyboard.GetCharsFromKeys(Keys.OemQuotes, false, false));
            raw.Characters = 2;
            Assert.IsNull(keyboard.GetCharsFromKeys(Keys.A, false, false));
            raw.Window = IntPtr.Zero;
            Assert.IsFalse(keyboard.ApplicationIsActivated());
            Assert.AreEqual(0, raw.ProcessQueries);
            raw.Window = new IntPtr(4);
            Assert.IsTrue(keyboard.ApplicationIsActivated());
            raw.ForegroundProcess = 99;
            Assert.IsFalse(keyboard.ApplicationIsActivated());
            raw.Characters = 1;
            var clipboard = new ConsoleClipboard { Text = "copied" };
            var platform = new LiveConsolePlatform(keyboard, clipboard);
            Assert.AreEqual(1, platform.CapsLockState);
            Assert.AreEqual(0x14, raw.StateKey);
            Assert.IsFalse(platform.IsForeground);
            Assert.AreEqual("a", platform.TranslateKey(Keys.A, false, false));
            Assert.IsTrue((DateTime.Now - platform.Now).Duration() < TimeSpan.FromSeconds(1));
            Assert.AreEqual("copied", platform.ReadClipboard());
            platform.WriteClipboard("pasted");
            Assert.AreEqual("pasted", clipboard.Text);
        }

        [TestMethod]
        public void ClipboardOperationsUseStaJoinCompletionAndPropagateFailure() {
            var apartment = ApartmentState.Unknown;
            ClipboardThread.Run(() => apartment = Thread.CurrentThread.GetApartmentState());
            Assert.AreEqual(ApartmentState.STA, apartment);
            var failure = new InvalidOperationException("clipboard locked");
            var error = Assert.ThrowsExactly<InvalidOperationException>(() => ClipboardThread.Run(() => throw failure));
            Assert.AreSame(failure, error.InnerException);
        }

        [TestMethod]
        public void DevelopmentVersionFlagUsesPublishedVersionValue() {
            Assert.AreEqual("DEV", ConsoleSettings.DisplayVersion("9.9.9.9"));
            Assert.AreEqual("1.2.3", ConsoleSettings.DisplayVersion("1.2.3"));
            var version = ConsoleSettings.Version;
            try {
                ConsoleSettings.Version = "DEV";
                Assert.IsTrue(ConsoleSettings.IsDevBuild);
                ConsoleSettings.Version = "1.2.3";
                Assert.IsFalse(ConsoleSettings.IsDevBuild);
            } finally { ConsoleSettings.Version = version; }
        }
    }
}
