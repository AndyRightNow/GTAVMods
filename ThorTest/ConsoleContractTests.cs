using System;
using System.Linq;
using System.Windows.Forms;
using GTA;
using GTA.Native;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Control = GTA.Control;

namespace ModUtilsTest {
    [TestClass]
    public class ConsoleContractTests {
        [TestMethod]
        [TestCategory("PlayerIntegration")]
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
        [TestCategory("PlayerIntegration")]
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
        [TestCategory("PlayerIntegration")]
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
        [TestCategory("PlayerIntegration")]
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
        [TestCategory("PlayerIntegration")]
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
        [TestCategory("PlayerIntegration")]
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
        [TestCategory("PlayerIntegration")]
        [DataRow(false)] [DataRow(true)]
        public void VehicleLoadingDeadlineUsesElapsedTimeAcrossClockWrap(bool loadedAtDeadline) {
            using (var scene = new ConsoleFixture()) {
                scene.ModelLoaded = false; scene.Host.GameTime = int.MaxValue - 24; scene.Host.FrameMilliseconds = 25;
                scene.Host.OnWait = () => scene.ModelLoaded = loadedAtDeadline && scene.Host.Waits.Count == 2;
                scene.Evaluate("return FUNCTIONS.SpawnVehicleProper(17, POS, 50)");
                Assert.IsTrue(scene.ShowsOutput(loadedAtDeadline ? "23" : "0"));
                Assert.AreEqual(2, scene.Host.Waits.Count); Assert.IsEmpty(scene.RequestedModels);
                Assert.AreEqual(loadedAtDeadline, scene.Native.Calls.Any(c => c.Hash == Hash.CREATE_VEHICLE));
            }
        }

        [TestMethod]
        [TestCategory("PlayerIntegration")]
        [DataRow(false)] [DataRow(true)]
        public void FailedVehicleCreationReleasesTheModelAndLeavesTheConsoleUsable(bool engineThrows) {
            using (var scene = new ConsoleFixture()) {
                scene.CreatedVehicle = 0;
                if (engineThrows) scene.Native.Before = (hash, args) => { if (hash == Hash.CREATE_VEHICLE) throw new InvalidOperationException("creation unavailable"); };
                scene.Evaluate("return FUNCTIONS.SpawnVehicleProper(17, POS)");
                Assert.IsTrue(scene.ShowsOutput(engineThrows ? "[Error] C# evaluation failed: creation unavailable" : "0"));
                Assert.IsEmpty(scene.RequestedModels);
                scene.Native.Before = null; scene.CreatedVehicle = 23;
                scene.Evaluate("return FUNCTIONS.SpawnVehicleProper(17, POS)"); Assert.IsTrue(scene.ShowsOutput("23"));
            }
        }

        [TestMethod]
        [TestCategory("PlayerIntegration")]
        public void CreatedVehicleIsReportedDespiteModelReleaseFailureAndAnotherSpawnWaitsForCleanup() {
            using (var scene = new ConsoleFixture()) {
                scene.Native.Before = (hash, args) => { if (hash == Hash.SET_MODEL_AS_NO_LONGER_NEEDED) throw new InvalidOperationException("release unavailable"); };
                scene.Evaluate("return FUNCTIONS.SpawnVehicleProper(17, POS)"); Assert.IsTrue(scene.ShowsOutput("23"));
                CollectionAssert.AreEqual(new[] { 17 }, scene.RequestedModels.ToArray());
                scene.Submit("clear"); scene.Native.Calls.Clear(); scene.Evaluate("return FUNCTIONS.SpawnVehicleProper(19, POS)");
                Assert.IsFalse(scene.Native.Calls.Any(c => c.Hash == Hash.REQUEST_MODEL || c.Hash == Hash.CREATE_VEHICLE));
                Assert.IsTrue(scene.ShowsOutput("[Error] C# evaluation failed: release unavailable"));
                scene.Submit("clear"); scene.Native.Before = null; scene.Evaluate("return FUNCTIONS.SpawnVehicleProper(19, POS)");
                Assert.IsEmpty(scene.RequestedModels); Assert.IsTrue(scene.ShowsOutput("23"));
            }
        }

        [TestMethod]
        [TestCategory("PlayerIntegration")]
        [DataRow(false)] [DataRow(true)]
        public void FailedSpawnAndFailedReleaseRemainRetryableWhenScriptsReload(bool creationThrows) {
            using (var scene = new ConsoleFixture()) {
                scene.CreatedVehicle = 0; scene.Enabled.Add(Control.Jump);
                scene.Native.Before = (hash, args) => {
                    if (hash == Hash.SET_MODEL_AS_NO_LONGER_NEEDED || (creationThrows && hash == Hash.CREATE_VEHICLE)) throw new InvalidOperationException("engine unavailable");
                };
                scene.Evaluate("return FUNCTIONS.SpawnVehicleProper(17, POS, 0)");
                Assert.IsTrue(scene.Shows("C# evaluation failed:"));
                scene.Native.Before = (hash, args) => {
                    if (hash == Hash.SET_MODEL_AS_NO_LONGER_NEEDED || (hash == Hash.ENABLE_CONTROL_ACTION && (Control)(int)args[1] == Control.Jump)) throw new InvalidOperationException("cleanup unavailable");
                };
                Assert.ThrowsExactly<AggregateException>(() => scene.Model.Dispose());
                Assert.IsTrue(scene.RequestedModels.Contains(17)); Assert.IsFalse(scene.Enabled.Contains(Control.Jump));
                scene.Native.Before = null; scene.Model.Dispose();
                Assert.IsEmpty(scene.RequestedModels); Assert.IsTrue(scene.Enabled.Contains(Control.Jump));
            }
        }

        [TestMethod]
        [TestCategory("PlayerIntegration")]
        public void EntityOwnershipDiagnosticWaitsForEngineConfirmationAndNeverInventsANetworkHandle() {
            using (var scene = new ConsoleFixture()) {
                scene.Native.Returns(Hash.GET_PLAYER_PED, 11); scene.Native.Returns(Hash.NETWORK_GET_NETWORK_ID_FROM_ENTITY, 73);
                scene.Native.Returns(Hash.NETWORK_HAS_CONTROL_OF_ENTITY, false);
                scene.Evaluate("return FUNCTIONS.RequestEntityControl(11, 2)"); Assert.IsTrue(scene.ShowsOutput("0"));
                Assert.AreEqual(2, scene.Host.Waits.Count);
                scene.Submit("clear"); scene.Evaluate("return FUNCTIONS.RequestEntityControl(0, 2)"); Assert.IsTrue(scene.ShowsOutput("0"));
                Assert.AreEqual(2, scene.Host.Waits.Count, "A missing entity must fail without waiting for engine ownership.");
                scene.Submit("clear"); scene.Evaluate("return FUNCTIONS.GetNetworkID(PLAYER.Handle)"); Assert.IsTrue(scene.ShowsOutput("0"));
                Assert.IsFalse(scene.Native.Calls.Any(c => c.Hash == Hash.NETWORK_GET_NETWORK_ID_FROM_ENTITY));
                scene.Native.Returns(Hash.NETWORK_HAS_CONTROL_OF_ENTITY, true);
                scene.Native.Returns(Hash.NETWORK_REQUEST_CONTROL_OF_NETWORK_ID, false);
                scene.Submit("clear"); scene.Evaluate("return FUNCTIONS.GetNetworkID(PLAYER.Handle)"); Assert.IsTrue(scene.ShowsOutput("0"));
                scene.Native.Returns(Hash.NETWORK_REQUEST_CONTROL_OF_NETWORK_ID, true);
                scene.Submit("clear"); scene.Evaluate("return FUNCTIONS.GetNetworkID(PLAYER.Handle)"); Assert.IsTrue(scene.ShowsOutput("73"));
                CollectionAssert.AreEqual(new object[] { 73 }, scene.Native.Calls.Last(c => c.Hash == Hash.NETWORK_REQUEST_CONTROL_OF_NETWORK_ID).Arguments);
            }
        }
    }
}
