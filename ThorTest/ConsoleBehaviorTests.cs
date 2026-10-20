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
        public void RegisteredCommandsRunBeforeAndAfterConsoleReplacementAndUnregisteredOwnersStayDetached() {
            var first = new object();
            var second = new object();
            ConsoleModel.RegisterConsoleScript(first, console => console.CommandDispatcher.RegisterCommand(
                new CommandDispatcher.Command("first", "first owner", args => console.PrintLine("first command ran"))));
            using (var fixture = new ConsoleFixture()) {
                fixture.Model.ShowConsole(true);
                Assert.AreSame(fixture.Model, ConsoleModel.Instance);
                fixture.Model.Input = "first";
                fixture.Press(Keys.Enter);
                ConsoleModel.RegisterConsoleScript(second, console => console.CommandDispatcher.RegisterCommand(
                    new CommandDispatcher.Command("second", "second owner", args => console.PrintLine("second command ran"))));
                fixture.Model.Input = "second";
                fixture.Press(Keys.Enter);
                fixture.Draw();
                Assert.HasCount(1, fixture.Platform.Text.Where(line => line.Text.EndsWith("first command ran")).ToArray());
                Assert.HasCount(1, fixture.Platform.Text.Where(line => line.Text.EndsWith("second command ran")).ToArray());
                ConsoleModel.UnregisterConsoleScript(first);
                Assert.AreSame(second, ConsoleModel.RegisteredScripts.Single().Key);
                using (var replacement = new ConsoleFixture()) {
                    fixture.Model.Dispose();
                    Assert.AreSame(replacement.Model, ConsoleModel.Instance);
                    replacement.Model.ShowConsole(true);
                    replacement.Model.Input = "first";
                    replacement.Press(Keys.Enter);
                    replacement.Model.Input = "second";
                    replacement.Press(Keys.Enter);
                    replacement.Draw();
                    Assert.IsTrue(replacement.Platform.Text.Any(line => line.Text.Contains("Command 'first' not found!")));
                    Assert.IsFalse(replacement.Platform.Text.Any(line => line.Text.EndsWith("first command ran")));
                    Assert.HasCount(1, replacement.Platform.Text.Where(line => line.Text.EndsWith("second command ran")).ToArray(),
                        "Disposing the superseded console must preserve commands attached to its replacement.");
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
                var toggle = new KeyEventArgs(Keys.F8);
                fixture.Model.KeyDown(toggle);
                Assert.IsTrue(toggle.SuppressKeyPress);
                Assert.IsTrue(fixture.Model.IsOpen);
                Assert.AreEqual("", fixture.Model.Input, "The toggle key must not also be inserted as text.");
                fixture.Draw();
                Assert.IsTrue(fixture.Platform.Text.Any(line => line.Text.Contains("F8") && line.Text.Contains("F4")),
                    "The startup message must distinguish the two console shortcuts.");
                var captures = fixture.Native.Calls.Count(c => c.Hash == Hash.IS_CONTROL_ENABLED);
                fixture.Model.ShowConsole(true);
                Assert.AreEqual(captures, fixture.Native.Calls.Count(c => c.Hash == Hash.IS_CONTROL_ENABLED));
                fixture.Native.Calls.Clear();
                fixture.Press(Keys.F8);
                Assert.IsFalse(fixture.Model.IsOpen);
                CollectionAssert.AreEquivalent(originallyEnabled.Select(c => (int)c).ToArray(),
                    fixture.Native.Calls.Where(c => c.Hash == Hash.ENABLE_CONTROL_ACTION).Select(c => (int)c.Arguments[1]).ToArray());
                fixture.Model.ShowConsole(true);
                var escape = new KeyEventArgs(Keys.Escape);
                fixture.Model.KeyDown(escape);
                Assert.IsTrue(escape.SuppressKeyPress);
                Assert.IsFalse(fixture.Model.IsOpen);
                fixture.Press(Keys.Escape);
                fixture.Model.ShowConsole(true);
                fixture.Native.Calls.Clear();
                fixture.Model.Dispose();
                Assert.AreEqual(Enum.GetValues(typeof(Control)).Length, fixture.Native.Calls.Count);
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
                fixture.Model.Input = "abcd";
                fixture.Press(Keys.Left);
                fixture.Press(Keys.Left);
                fixture.Platform.GameTime = 601;
                fixture.Draw();
                Assert.AreEqual("--> ab|cd", fixture.Platform.Text.Last().Text);
                fixture.Press(Keys.Delete);
                fixture.Type(Keys.X, "X");
                Assert.AreEqual("abXd", fixture.Model.Input,
                    "Deleting at the cursor must leave it before the following character.");
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
                var typed = new KeyEventArgs(Keys.E);
                fixture.Platform.Translation = "e";
                fixture.Model.KeyDown(typed);
                Assert.IsTrue(typed.SuppressKeyPress);
                fixture.Model.KeyUp(typed);
                fixture.Model.Input = "aa";
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
                fixture.Platform.CapsLockState = 0;
                fixture.Platform.Translation = "z";
                fixture.Press(Keys.Z);
                fixture.Model.ShowConsole(false);
                fixture.Model.ShowConsole(true);
                fixture.Press(Keys.Z);
                Assert.AreEqual("aaBc1Dzz", fixture.Model.Input,
                    "Closing must release key-repeat ownership even when a key-up event was not delivered.");
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
                fixture.Draw();
                Assert.IsTrue(fixture.Platform.Text.Any(t => t.Text.EndsWith("--> set 12")),
                    "Submitted commands remain visible after the input is cleared.");
                fixture.Model.Input = "set true";
                fixture.Press(Keys.Enter);
                Assert.AreEqual(1, calls);
                fixture.Draw();
                Assert.IsTrue(fixture.Platform.Text.Any(t => t.Text.Contains("Provided arguments are not valid")));
                Assert.IsTrue(fixture.Platform.Text.Any(t => t.Text.Contains("System.Int32 number")),
                    "Rejected arguments should show the accepted signature.");
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
                fixture.Press(Keys.Up);
                fixture.Type(Keys.X, "x");
                fixture.Press(Keys.Up);
                Assert.AreEqual("set true", fixture.Model.Input, "Editing a recalled command starts a new history traversal.");
                foreach (var invalid in new[] { "", "123 set", "\"set\"", "missing { throw null; }" }) {
                    fixture.Model.ClearLines();
                    fixture.Model.Input = invalid;
                    fixture.Model.RunCommand();
                    fixture.Draw();
                    Assert.IsTrue(fixture.Platform.Text.Any(t =>
                        t.Color == Color.FromArgb(ConsoleSettings.TextAlpha, Color.Red) && t.Text.Contains("not found")),
                        "Invalid command input must produce an actionable error.");
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
                fixture.Model.ClearLines();
                fixture.Model.PrintCommandInfo(new CommandDispatcher.Command("probe", "read diagnostics", args => { }));
                fixture.Draw();
                Assert.IsTrue(fixture.Platform.Text.Any(t => t.Text.Contains("probe")),
                    "Help for a command without arguments must still identify the command.");
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
                var rows = fixture.Platform.Text.Take(15).ToArray();
                CollectionAssert.AreEqual(Enumerable.Range(5, 15).Select(i => "12:34:56: line" + i).ToArray(),
                    rows.Select(t => t.Text).ToArray());
                Assert.AreEqual(5, rows[0].Position.Y);
                Assert.AreEqual(257, rows[14].Position.Y);
                Assert.IsTrue(rows.Zip(rows.Skip(1), (a, b) => b.Position.Y - a.Position.Y).All(gap => gap == 18),
                    "Rows use evenly spaced baselines within the output area.");
                var separator = fixture.Platform.Rectangles.Last();
                Assert.IsTrue(separator.Position.Y > rows.Last().Position.Y && separator.Position.Y < 300);
                Assert.IsTrue(fixture.Platform.Text.Last().Position.Y > separator.Position.Y);
                var version = fixture.Platform.Text.Single(t => t.Text == ConsoleSettings.Version);
                Assert.IsTrue(version.Position.X > 0 && version.Position.X < 1200);
                Assert.IsTrue(version.Position.Y > 0 && version.Position.Y < separator.Position.Y);
                Assert.IsTrue(fixture.Platform.Text.Any(t => t.Text.EndsWith("line19")));
                Assert.IsFalse(fixture.Platform.Text.Any(t => t.Text.EndsWith("line4")));
                for (var i = 0; i < 25; i++) fixture.Press(Keys.PageUp);
                fixture.Draw();
                Assert.IsTrue(fixture.Platform.Text.Any(t => t.Text.EndsWith("line0")));
                Assert.IsFalse(fixture.Platform.Text.Any(t => t.Text.EndsWith("line19")));
                Assert.AreEqual(new Point(1190, 1), fixture.Platform.Rectangles[1].Position);
                fixture.Press(Keys.PageDown);
                fixture.Draw();
                CollectionAssert.AreEqual(Enumerable.Range(1, 15).Select(i => "12:34:56: line" + i).ToArray(),
                    fixture.Platform.Text.Take(15).Select(t => t.Text).ToArray(),
                    "One step down from the oldest page must immediately move the visible window.");
                Assert.IsTrue(fixture.Platform.Rectangles[1].Position.Y > 1 && fixture.Platform.Rectangles[1].Position.Y < 69);
                for (var i = 0; i < 25; i++) fixture.Press(Keys.PageDown);
                fixture.Draw();
                Assert.IsTrue(fixture.Platform.Text.Any(t => t.Text.EndsWith("line19")));
                Assert.AreEqual(17, fixture.Platform.Text.Count, "Scrolling below the newest page must not discard a row.");
                Assert.AreEqual(new Point(1190, 69), fixture.Platform.Rectangles[1].Position);
                Assert.AreEqual(new Size(8, 206), fixture.Platform.Rectangles[1].Size);
                fixture.Platform.GameTime = 600;
                fixture.Draw();
                Assert.AreEqual("--> ", fixture.Platform.Text.Last().Text);
                fixture.Platform.GameTime = 601;
                fixture.Draw();
                Assert.AreEqual("--> |", fixture.Platform.Text.Last().Text);
                fixture.Platform.GameTime = 602;
                fixture.Draw();
                Assert.AreEqual("--> |", fixture.Platform.Text.Last().Text, "A draw one millisecond later must not toggle again.");
                fixture.Platform.GameTime = 1202;
                fixture.Draw();
                Assert.AreEqual("--> ", fixture.Platform.Text.Last().Text);
                for (var i = 0; i < 25; i++) fixture.Press(Keys.PageUp);
                fixture.Model.RemoveLastLine();
                fixture.Press(Keys.PageDown);
                fixture.Draw();
                Assert.IsTrue(fixture.Platform.Text.First().Text.EndsWith("line1"),
                    "Removing a row while at the oldest page must clamp scroll ownership to the remaining range.");
                for (var i = 0; i < 4; i++) fixture.Model.RemoveLastLine();
                fixture.Draw();
                Assert.AreEqual(2, fixture.Platform.Rectangles.Count, "An exactly full page needs no scrollbar.");
                for (var i = 0; i < 5; i++) fixture.Model.RemoveLastLine();
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
                fixture.Model.ClearLines();
                fixture.Model.PrintLine(new string('x', 89));
                fixture.Draw();
                Assert.AreEqual(99, fixture.Platform.Text.First().Text.Length);
                Assert.AreEqual(3, fixture.Platform.Text.Count,
                    "A line ending exactly at a chunk boundary must not draw an extra empty chunk.");
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
            raw.ForegroundProcess = 42;
            Assert.IsTrue(platform.IsForeground);
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
