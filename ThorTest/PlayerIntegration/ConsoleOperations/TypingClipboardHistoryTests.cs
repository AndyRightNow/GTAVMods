using Control = GTA.Control;
using DeveloperConsole;
using GTA.Native;
using GTAVMods.Tests.Support;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using System;

namespace GTAVMods.Tests.PlayerIntegration.ConsoleOperations
{
    [TestClass, TestCategory("PlayerIntegration")]
    public class TypingClipboardHistoryTests
    {
        [TestMethod]
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
                scene.Press(Keys.Right); scene.Platform.Time = 10000; scene.Draw();
                Assert.IsTrue(scene.Shows("ev|al 15"));
                for (int i = 0; i < "eval 15".Length; i++) scene.Press(Keys.Right);
                for (int i = 0; i < "eval 15".Length; i++) scene.Press(Keys.Back);
                scene.Press(Keys.Right); Assert.AreEqual("", scene.Model.Input);
                scene.Type(Keys.C, "c"); scene.Type(Keys.V, "v");
                Assert.AreEqual("cv", scene.Model.Input);
            }
        }

        [TestMethod]
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
                Assert.IsFalse(scene.Platform.Text.Last().Text.Contains("|"));
                scene.Platform.Time += 10000; scene.Draw(); Assert.IsTrue(scene.Platform.Text.Last().Text.Contains("|"));
                scene.Draw(); Assert.IsTrue(scene.Platform.Text.Last().Text.Contains("|"), "A redraw without elapsed time must not flicker the cursor.");
                scene.Platform.Time += 10000; scene.Draw(); Assert.IsFalse(scene.Platform.Text.Last().Text.Contains("|"));
                scene.Submit("clear"); scene.Draw(); Assert.AreEqual(2, scene.Platform.Text.Count);
            }
        }
    }
}
