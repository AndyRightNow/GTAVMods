using ADModUtils;
using Control = GTA.Control;
using DeveloperConsole;
using GTA.Math;
using GTA.Native;
using GTA;
using GTAVMods.Game;
using GTAVMods.Tests.Support;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using System;
using Thor;

namespace GTAVMods.Tests.PlayerIntegration.ConsoleOperations
{
    [TestClass, TestCategory("PlayerIntegration")]
    public class OpenCloseControlsTests
    {
        [TestMethod]
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
        [DataRow(false)] [DataRow(true)]
        public void FailedConsoleOpeningRestoresControlsAndRetryPreservesOriginalPlayerInput(bool rollbackFails) {
            using (var scene = new ConsoleFixture()) {
                scene.Enabled.Add(Control.CursorY); scene.Enabled.Add(Control.Jump);
                scene.Native.Before = (hash, args) => {
                    if (hash == Hash.ENABLE_CONTROL_ACTION && (Control)(int)args[1] == Control.MoveUpDown) throw new InvalidOperationException("enable navigation");
                    if (rollbackFails && hash == Hash.ENABLE_CONTROL_ACTION && (Control)(int)args[1] == Control.Jump) throw new InvalidOperationException("restore jump");
                };
                if (rollbackFails) Assert.ThrowsExactly<AggregateException>(() => scene.Press(Keys.F8));
                else Assert.ThrowsExactly<InvalidOperationException>(() => scene.Press(Keys.F8));
                Assert.IsFalse(scene.Model.IsOpen); Assert.IsTrue(scene.Enabled.Contains(Control.CursorY));
                Assert.AreEqual(!rollbackFails, scene.Enabled.Contains(Control.Jump));
                scene.Native.Before = null;
                scene.Enabled.Add(Control.Attack);
                scene.Press(Keys.F8); scene.Submit("help"); Assert.IsTrue(scene.Shows("eval:"));
                scene.Press(Keys.Escape);
                CollectionAssert.AreEquivalent(new[] { Control.CursorY, Control.Jump, Control.Attack }, scene.Enabled.ToArray());
            }
        }

        [TestMethod]
        [DataRow(false)] [DataRow(true)]
        public void FailedCloseRetainsOnlyFailedControlAndRetryPreservesOtherScriptsChanges(bool reopen) {
            using (var scene = new ConsoleFixture()) {
                scene.Enabled.Add(Control.CursorY); scene.Enabled.Add(Control.Jump);
                scene.Press(Keys.F8);
                scene.Native.Before = (hash, args) => {
                    if (hash == Hash.ENABLE_CONTROL_ACTION && (Control)(int)args[1] == Control.Jump) throw new InvalidOperationException("restore jump");
                };
                Assert.ThrowsExactly<InvalidOperationException>(() => scene.Press(Keys.Escape));
                Assert.IsFalse(scene.Model.IsOpen); Assert.IsTrue(scene.Enabled.Contains(Control.CursorY));
                scene.Native.Before = null; scene.Enabled.Remove(Control.CursorY); scene.Enabled.Add(Control.Attack);
                if (reopen) { scene.Press(Keys.F8); scene.Press(Keys.Escape); } else scene.Model.Dispose();
                CollectionAssert.AreEquivalent(new[] { Control.Jump, Control.Attack }, scene.Enabled.ToArray());
            }
        }

        [TestMethod]
        public void ScriptReloadRetriesFailedRestorationWithoutCapturingTheAlreadyChangedControls() {
            using (var scene = new ConsoleFixture()) {
                scene.Enabled.Add(Control.Jump); scene.Press(Keys.F8);
                scene.Native.Before = (hash, args) => {
                    if (hash == Hash.ENABLE_CONTROL_ACTION && (Control)(int)args[1] == Control.Jump) throw new InvalidOperationException("restore jump");
                };
                Assert.ThrowsExactly<InvalidOperationException>(() => scene.Model.Dispose());
                Assert.AreSame(scene.Model, DeveloperConsole.DeveloperConsole.Instance);
                scene.Native.Before = null; scene.Native.Calls.Clear(); scene.Model.Dispose();
                Assert.IsTrue(scene.Enabled.Contains(Control.Jump));
                Assert.IsFalse(scene.Native.Calls.Any(c => c.Hash == Hash.IS_CONTROL_ENABLED));
                Assert.IsNull(DeveloperConsole.DeveloperConsole.Instance);
            }
        }

        [TestMethod]
        public void ConsoleBlocksAttackOnEveryFrameEvenWhenTheEngineResetsItsControls() {
            using (var scene = new ConsoleFixture()) {
                scene.Press(Keys.F8);
                for (int frame = 0; frame < 2; frame++) {
                    scene.Enabled.Add(Control.Attack); scene.Enabled.Add(Control.Jump); scene.Draw();
                    Assert.IsFalse(scene.Enabled.Contains(Control.Attack)); Assert.IsFalse(scene.Enabled.Contains(Control.Jump));
                    Assert.IsTrue(scene.Enabled.Contains(Control.MoveUpDown)); Assert.IsTrue(scene.Enabled.Contains(Control.VehicleAccelerate));
                    Assert.IsTrue(scene.Enabled.Contains(Control.VehicleFlyThrottleUp));
                }
            }
        }

        [TestMethod]
        [DataRow("capture disabled X", 1)] [DataRow("capture enabled Y", 2)]
        [DataRow("enable X", 3)] [DataRow("enable Y", 4)]
        [DataRow("read X", 5)] [DataRow("read Y", 6)]
        [DataRow("restore disabled X", 7)] [DataRow("restore enabled Y", 8)]
        public void MouseDiagnosticRecoversFromEachDeviceFailureWithoutLeakingTemporaryInput(string failedOperation, int failedStep) {
            using (var scene = new ConsoleFixture()) {
                scene.Press(Keys.F8); scene.Enabled.Add(Control.CursorY);
                int step = 0;
                scene.Native.Before = (hash, args) => {
                    if ((hash == Hash.IS_CONTROL_ENABLED || hash == Hash.ENABLE_CONTROL_ACTION || hash == Hash.DISABLE_CONTROL_ACTION || hash == Hash.GET_CONTROL_NORMAL)
                        && args.Length > 1 && ((Control)(int)args[1] == Control.CursorX || (Control)(int)args[1] == Control.CursorY)
                        && ++step == failedStep) throw new InvalidOperationException("mouse unavailable: " + failedOperation);
                };
                scene.Enter("eval { return FUNCTIONS.GetMousePos(); }");
                Assert.AreEqual(failedStep == 7, scene.Enabled.Contains(Control.CursorX),
                    "Only a refused X restoration may leave the temporary X enable active.");
                Assert.IsTrue(scene.Enabled.Contains(Control.CursorY), "The successful cleanup paths preserve originally enabled Y.");
                scene.Draw();
                Assert.IsTrue(scene.Shows("C# evaluation failed:"));
                scene.Native.Before = null;
                scene.Analog[Control.CursorX] = .25f; scene.Analog[Control.CursorY] = .5f;
                scene.Evaluate("return FUNCTIONS.GetMousePos()");
                Assert.IsTrue(scene.ShowsOutput("{X=300,Y=450}"));
                Assert.IsFalse(scene.Enabled.Contains(Control.CursorX));
                scene.Press(Keys.Escape);
                Assert.IsEmpty(scene.Enabled);
            }
        }

        [TestMethod]
        public void MouseReadAndRestoreFailureAreReportedAndClosingRetriesPendingDeviceState() {
            using (var scene = new ConsoleFixture()) {
                scene.Press(Keys.F8);
                scene.Native.Before = (hash, args) => {
                    if (hash == Hash.GET_CONTROL_NORMAL) throw new InvalidOperationException("read mouse");
                    if (hash == Hash.DISABLE_CONTROL_ACTION && (Control)(int)args[1] == Control.CursorX) throw new InvalidOperationException("restore mouse");
                };
                // Submission returns an evaluation error; the open-console draw then independently reasserts its controls.
                scene.Platform.Clipboard = "eval { return FUNCTIONS.GetMousePos(); }";
                scene.Press(Keys.Control | Keys.V); scene.Press(Keys.Enter);
                scene.Native.Before = null; scene.Press(Keys.Escape);
                Assert.IsFalse(scene.Enabled.Contains(Control.CursorX));
                scene.Press(Keys.F8); scene.Draw(); Assert.IsTrue(scene.Shows("C# evaluation failed:"));
            }
        }

        [TestMethod]
        public void OpenConsoleProtectsGameplayFromDisableUntilItClosesOrIsDisposed()
        {
            var s = new PlayerSessionFixture();
            s.Frame();
            using (var console = new ConsoleFixture())
            {
                console.Press(Keys.F8);
                s.Chord(Keys.F);
                Assert.AreEqual(30000, s.Game.Entity(10).MaxHealth);
                Assert.AreEqual(WeaponHash.Hammer, s.Game.Entity(10).CurrentWeapon);
                Assert.IsFalse(s.Log.Records.Any(record => record.Name == "ability.disabled"));

                console.Press(Keys.F8);
                s.Chord(Keys.F);
                Assert.AreEqual(200, s.Game.Entity(10).MaxHealth);
                Assert.AreEqual(WeaponHash.Unarmed, s.Game.Entity(10).CurrentWeapon);
                Assert.HasCount(1, s.Log.Records.Where(record => record.Name == "ability.disabled").ToArray());

                s.Chord(Keys.O);
                s.Game.ObserveNextFrame(game => game.ApplyWeaponObservation(10, WeaponHash.Hammer, 20));
                s.Frame();
                Assert.AreEqual(30000, s.Game.Entity(10).MaxHealth);
                console.Press(Keys.F8);
            }

            s.Chord(Keys.F);
            Assert.AreEqual(200, s.Game.Entity(10).MaxHealth);
            Assert.AreEqual(WeaponHash.Unarmed, s.Game.Entity(10).CurrentWeapon);
            Assert.HasCount(2, s.Log.Records.Where(record => record.Name == "ability.disabled").ToArray(),
                "Disposing an open console must restore shortcuts instead of leaving a stale input lock.");
            s.Controller.Abort();
            s.AssertNoErrors();
            s.AssertReleased();
        }
    }
}
