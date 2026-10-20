using System;
using System.Collections.Generic;
using System.Linq;
using DeveloperConsole;
using GTA;
using GTA.Math;
using GTA.Native;
using GTAVMods.Game;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ConsoleModel = DeveloperConsole.DeveloperConsole;

namespace ModUtilsTest {
    internal enum ConsoleInputOperation { Enabled, Normal, Value, Pressed, DisabledPressed, Enable, Disable, EnableAll, DisableAll }
    internal sealed class ConsoleControlState : IConsoleInput {
        internal readonly HashSet<Control> Enabled = new HashSet<Control>();
        internal readonly HashSet<Control> Pressed = new HashSet<Control>();
        internal readonly Dictionary<Control, float> Samples = new Dictionary<Control, float>();
        internal readonly List<(ConsoleInputOperation Operation, Control Control)> Calls =
            new List<(ConsoleInputOperation, Control)>();
        internal Action<ConsoleInputOperation, Control> Before;
        private void Record(ConsoleInputOperation operation, Control control = default(Control)) {
            Calls.Add((operation, control));
            Before?.Invoke(operation, control);
        }
        public bool IsEnabled(Control control) { Record(ConsoleInputOperation.Enabled, control); return Enabled.Contains(control); }
        public float Normal(Control control) { Record(ConsoleInputOperation.Normal, control); return Enabled.Contains(control) ? Samples[control] : 0; }
        public int Value(Control control) { Record(ConsoleInputOperation.Value, control); return 237; }
        public bool IsPressed(Control control) { Record(ConsoleInputOperation.Pressed, control); return Enabled.Contains(control) && Pressed.Contains(control); }
        public bool IsDisabledPressed(Control control) { Record(ConsoleInputOperation.DisabledPressed, control); return Pressed.Contains(control); }
        public void Enable(Control control, bool enabled) {
            Record(ConsoleInputOperation.Enable, control);
            if (enabled) Enabled.Add(control); else Enabled.Remove(control);
        }
        public void Disable(Control control, bool disabled) {
            Record(ConsoleInputOperation.Disable, control);
            if (disabled) Enabled.Remove(control); else Enabled.Add(control);
        }
        public void EnableAll() {
            Record(ConsoleInputOperation.EnableAll);
            foreach (Control control in Enum.GetValues(typeof(Control))) Enabled.Add(control);
        }
        public void DisableAll() { Record(ConsoleInputOperation.DisableAll); Enabled.Clear(); }
    }

    // This native-side probe is deliberately separate from ConsoleControlState. It validates
    // the adapter protocol and stores native integer IDs; behavior tests use the typed fake.
    internal sealed class ConsoleInputNativeProbe : INativeInvoker {
        internal readonly Dictionary<int, bool> States = new Dictionary<int, bool>();
        internal readonly HashSet<int> Pressed = new HashSet<int>();
        internal readonly Dictionary<int, float> Samples = new Dictionary<int, float>();
        public void Call(Hash hash, params object[] arguments) {
            Assert.AreEqual(0, arguments[0], "Console native controls must use input group zero.");
            if (hash == Hash.ENABLE_ALL_CONTROL_ACTIONS || hash == Hash.DISABLE_ALL_CONTROL_ACTIONS) {
                foreach (Control control in Enum.GetValues(typeof(Control)))
                    States[(int)control] = hash == Hash.ENABLE_ALL_CONTROL_ACTIONS;
                return;
            }
            Assert.IsTrue(hash == Hash.ENABLE_CONTROL_ACTION || hash == Hash.DISABLE_CONTROL_ACTION);
            States[(int)arguments[1]] = hash == Hash.ENABLE_CONTROL_ACTION ? (bool)arguments[2] : !(bool)arguments[2];
        }
        public T Call<T>(Hash hash, params object[] arguments) {
            Assert.AreEqual(0, arguments[0]);
            int id = (int)arguments[1];
            bool enabled = States.TryGetValue(id, out var value) && value;
            object result;
            switch (hash) {
                case Hash.IS_CONTROL_ENABLED: result = enabled; break;
                case Hash.GET_CONTROL_NORMAL: result = enabled ? Samples[id] : 0f; break;
                case Hash.GET_CONTROL_VALUE: result = 237; break;
                case Hash.IS_CONTROL_PRESSED: result = enabled && Pressed.Contains(id); break;
                case Hash.IS_DISABLED_CONTROL_PRESSED: result = Pressed.Contains(id); break;
                default: throw new InvalidOperationException("Unexpected console native query: " + hash);
            }
            return (T)result;
        }
    }

    internal sealed class ConsoleStreamingState : IConsoleWorld, IConsoleNetwork {
        internal readonly List<string> Calls = new List<string>();
        internal readonly HashSet<int> Requested = new HashSet<int>();
        internal bool Loaded, HasControl, AcceptRequest = true, AcceptNetworkId = true;
        internal int CreatedVehicle = 23;
        internal Exception CreateFailure, ReleaseFailure;
        public void RequestModel(int model) { Calls.Add("request-model"); Requested.Add(model); }
        public bool ModelLoaded(int model) {
            Assert.IsTrue(Requested.Contains(model), "The caller must request its streaming reference before polling.");
            Calls.Add("model-loaded"); return Loaded;
        }
        public void ReleaseModel(int model) {
            Calls.Add("release-model");
            if (ReleaseFailure != null) throw ReleaseFailure;
            Assert.IsTrue(Requested.Remove(model), "Only owned model references may be released.");
        }
        public int CreateVehicle(int model, Vector3 position) {
            Assert.IsTrue(Loaded && Requested.Contains(model), "Never create with an unconfirmed or released model.");
            Calls.Add("create-vehicle");
            if (CreateFailure != null) throw CreateFailure;
            return CreatedVehicle;
        }
        public bool RequestEntityControl(int entity) { Calls.Add("request-entity"); return AcceptRequest; }
        public bool HasEntityControl(int entity) { Calls.Add("has-control"); return HasControl; }
        public int NetworkId(int entity) { Calls.Add("network-id"); return 73; }
        public bool RequestNetworkIdControl(int networkId) {
            Assert.AreEqual(73, networkId);
            Calls.Add("request-network-id"); return AcceptNetworkId;
        }
        public int Hash(string name) => throw new NotSupportedException();
        public int FirstBlip(int type) => throw new NotSupportedException();
        public bool WaypointActive() => throw new NotSupportedException();
        public void ClearObjects(Vector3 position, float radius, bool flag) => throw new NotSupportedException();
        public void ClearProjectiles(Vector3 position, float radius, bool flag) => throw new NotSupportedException();
        public void AmbientPickup(int hash, Vector3 position, int value) => throw new NotSupportedException();
        public int PlayerCount() => throw new NotSupportedException();
        public void Kick(int player) => throw new NotSupportedException();
        public void LocalInvincibleTime(int milliseconds) => throw new NotSupportedException();
        public void MarkScriptSafe() => throw new NotSupportedException();
        public void RegisterScript() => throw new NotSupportedException();
        public void TrackDamage(int networkId, bool enabled) => throw new NotSupportedException();
        public void Spectate(int ped, bool enabled) => throw new NotSupportedException();
    }

    [TestClass]
    public class ConsoleContractTests {
        private static ConsoleControlState Controls() {
            var controls = new ConsoleControlState();
            controls.Enabled.Add(Control.CursorY);
            controls.Samples[Control.CursorX] = .25f;
            controls.Samples[Control.CursorY] = -.5f;
            return controls;
        }
        private static GTAFuncs Functions(ConsoleFixture fixture, ConsoleControlState controls,
            ConsoleStreamingState streaming = null) {
            streaming = streaming ?? new ConsoleStreamingState();
            return new GTAFuncs(fixture.Game, fixture.Platform, controls, streaming, streaming);
        }

        [TestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public void TypedFakeAndNativeAdapterObeyTheSameInputValueAndStateContract(bool adapter) {
            var state = Controls();
            state.Pressed.Add(Control.CursorX);
            state.Samples[Control.CursorX] = .75f;
            var probe = new ConsoleInputNativeProbe();
            probe.States[(int)Control.CursorY] = true;
            probe.Samples[(int)Control.CursorX] = .75f;
            probe.Pressed.Add((int)Control.CursorX);
            IConsoleInput input = adapter ? (IConsoleInput)new NativeConsoleInput(probe) : state;
            Assert.IsFalse(input.IsEnabled(Control.CursorX));
            Assert.IsTrue(input.IsEnabled(Control.CursorY));
            Assert.AreEqual(0f, input.Normal(Control.CursorX));
            Assert.IsTrue(input.IsDisabledPressed(Control.CursorX));
            input.Enable(Control.CursorX, true);
            Assert.IsTrue(input.IsEnabled(Control.CursorX));
            Assert.AreEqual(.75f, input.Normal(Control.CursorX));
            Assert.AreEqual(237, input.Value(Control.CursorX));
            Assert.IsTrue(input.IsPressed(Control.CursorX));
            Assert.IsTrue(input.IsDisabledPressed(Control.CursorX), "The disabled variant ignores whether input is enabled.");
            input.Disable(Control.CursorX, true);
            Assert.IsFalse(input.IsEnabled(Control.CursorX));
            Assert.AreEqual(0f, input.Normal(Control.CursorX));
            input.EnableAll();
            Assert.IsTrue(input.IsEnabled(Control.Jump));
            Assert.IsFalse(input.IsPressed(Control.Jump), "Enabling input cannot invent a physical button press.");
            input.DisableAll();
            Assert.IsFalse(input.IsEnabled(Control.CursorY));
        }

        [TestMethod]
        [DataRow(1)] [DataRow(2)] [DataRow(3)] [DataRow(4)]
        [DataRow(5)] [DataRow(6)] [DataRow(7)] [DataRow(8)]
        public void TemporaryInputOwnershipSurvivesAFaultAtEveryCaptureEnableReadAndRestoreStep(int failingStep) {
            using (var fixture = new ConsoleFixture(false)) {
                var state = Controls();
                var functions = Functions(fixture, state);
                var failure = new InvalidOperationException("input step " + failingStep);
                state.Before = (operation, control) => { if (state.Calls.Count == failingStep) throw failure; };
                Assert.AreSame(failure, Assert.ThrowsExactly<InvalidOperationException>(() => functions.GetMousePos()));
                if (failingStep <= 2)
                    Assert.IsTrue(state.Calls.All(call => call.Operation == ConsoleInputOperation.Enabled),
                        "Capture failure must happen before any state changes.");
                if (failingStep >= 3)
                    Assert.AreEqual(Control.CursorY, state.Calls.Last().Control,
                        "A failure restoring the first control must not skip the second.");
                state.Before = null;
                state.Calls.Clear();
                functions.RestorePendingControls();
                CollectionAssert.AreEquivalent(new[] { Control.CursorY }, state.Enabled.ToArray());
                Assert.AreEqual(failingStep >= 7 ? 1 : 0, state.Calls.Count,
                    "Only a failed restoration remains owned for retry.");
                functions.RestorePendingControls();
                Assert.AreEqual(failingStep >= 7 ? 1 : 0, state.Calls.Count);
            }
        }

        [TestMethod]
        public void ReadAndRestoreFailuresAreBothReportedAndNextReadRestoresBeforeCapturingAgain() {
            using (var fixture = new ConsoleFixture(false)) {
                var state = Controls();
                var functions = Functions(fixture, state);
                var readFailure = new InvalidOperationException("read");
                var restoreFailure = new InvalidOperationException("restore");
                state.Before = (operation, control) => {
                    if (state.Calls.Count == 5) throw readFailure;
                    if (state.Calls.Count == 6) throw restoreFailure;
                };
                var error = Assert.ThrowsExactly<AggregateException>(() => functions.GetControlPoint(Control.CursorX, Control.CursorY));
                CollectionAssert.AreEqual(new[] { readFailure, restoreFailure }, error.Flatten().InnerExceptions.ToArray());
                Assert.AreEqual(Control.CursorY, state.Calls.Last().Control);
                state.Before = null;
                state.Calls.Clear();
                Assert.AreEqual(new Vector2(.25f, -.5f), functions.GetControlPoint(Control.CursorX, Control.CursorY));
                Assert.AreEqual((ConsoleInputOperation.Disable, Control.CursorX), state.Calls.First());
                CollectionAssert.AreEquivalent(new[] { Control.CursorY }, state.Enabled.ToArray());
            }
        }

        [TestMethod]
        public void OpeningFailureWithSuccessfulRollbackPreservesOriginalErrorAndReleasesSnapshotOwnership() {
            using (var fixture = new ConsoleFixture(false)) {
                var state = Controls();
                state.Enabled.Add(Control.Jump);
                var original = state.Enabled.ToArray();
                var model = new ConsoleModel(fixture.Platform, fixture.Environment, Functions(fixture, state));
                var failure = new InvalidOperationException("enable navigation");
                state.Before = (operation, control) => {
                    if (operation == ConsoleInputOperation.Enable && control == Control.MoveUpDown) throw failure;
                };
                try {
                    Assert.AreSame(failure, Assert.ThrowsExactly<InvalidOperationException>(() => model.ShowConsole(true)));
                    Assert.IsFalse(model.IsOpen);
                    CollectionAssert.AreEquivalent(original, state.Enabled.ToArray());
                    state.Before = null;
                    state.Enabled.Add(Control.Attack);
                    state.Calls.Clear();
                    model.ShowConsole(true);
                    Assert.AreEqual(ConsoleInputOperation.Enabled, state.Calls.First().Operation,
                        "Completed rollback releases the old snapshot; reopening must capture the current state.");
                    model.ShowConsole(false);
                    CollectionAssert.AreEquivalent(original.Concat(new[] { Control.Attack }).ToArray(), state.Enabled.ToArray(),
                        "A control enabled after rollback must survive the next open/close cycle.");
                } finally { state.Before = null; model.Dispose(); }
            }
        }

        [TestMethod]
        public void OpeningFailureAndFailedRollbackKeepOriginalSnapshotForRetryAndClosing() {
            using (var fixture = new ConsoleFixture(false)) {
                var state = Controls();
                state.Enabled.Add(Control.Jump);
                var original = state.Enabled.ToArray();
                var model = new ConsoleModel(fixture.Platform, fixture.Environment, Functions(fixture, state));
                var openFailure = new InvalidOperationException("enable navigation");
                var restoreFailure = new InvalidOperationException("restore jump");
                state.Before = (operation, control) => {
                    if (operation == ConsoleInputOperation.Enable && control == Control.MoveUpDown) throw openFailure;
                    if (operation == ConsoleInputOperation.Enable && control == Control.Jump) throw restoreFailure;
                };
                try {
                    var error = Assert.ThrowsExactly<AggregateException>(() => model.ShowConsole(true));
                    CollectionAssert.AreEquivalent(new[] { openFailure, restoreFailure }, error.Flatten().InnerExceptions.ToArray());
                    Assert.IsFalse(model.IsOpen);
                    Assert.IsTrue(state.Enabled.Contains(Control.CursorY), "Another control must restore even if Jump fails.");
                    Assert.IsFalse(state.Enabled.Contains(Control.Jump));
                    state.Before = null;
                    state.Calls.Clear();
                    model.ShowConsole(true);
                    Assert.AreNotEqual(ConsoleInputOperation.Enabled, state.Calls.First().Operation,
                        "A retained original snapshot restores before the next capture.");
                    Assert.IsTrue(model.IsOpen);
                    model.ShowConsole(false);
                    CollectionAssert.AreEquivalent(original, state.Enabled.ToArray());
                } finally { state.Before = null; model.Dispose(); }
            }
        }

        [TestMethod]
        public void FailedCloseKeepsOwnershipAndDisposeRetriesWithoutRecapturingMutatedControls() {
            using (var fixture = new ConsoleFixture(false)) {
                var state = Controls();
                state.Enabled.Add(Control.Jump);
                var model = new ConsoleModel(fixture.Platform, fixture.Environment, Functions(fixture, state));
                model.ShowConsole(true);
                var failure = new InvalidOperationException("restore jump");
                state.Before = (operation, control) => {
                    if (operation == ConsoleInputOperation.Enable && control == Control.Jump) throw failure;
                };
                try {
                    Assert.AreSame(failure, Assert.ThrowsExactly<InvalidOperationException>(() => model.Dispose()));
                    Assert.IsFalse(model.IsOpen);
                    Assert.AreSame(model, ConsoleModel.Instance, "A failed dispose must remain retryable.");
                    Assert.IsTrue(state.Enabled.Contains(Control.CursorY));
                    state.Before = null;
                    state.Calls.Clear();
                    model.Dispose();
                    CollectionAssert.AreEquivalent(new[] { Control.CursorY, Control.Jump }, state.Enabled.ToArray());
                    Assert.IsFalse(state.Calls.Any(call => call.Operation == ConsoleInputOperation.Enabled));
                    Assert.IsNull(ConsoleModel.Instance);
                } finally { state.Before = null; model.Dispose(); }
            }
        }

        [TestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public void FailedCloseRetryPreservesExternalChangesToControlsAlreadyRestored(bool reopen) {
            using (var fixture = new ConsoleFixture(false)) {
                var state = Controls();
                state.Enabled.Add(Control.Jump);
                var model = new ConsoleModel(fixture.Platform, fixture.Environment, Functions(fixture, state));
                var failure = new InvalidOperationException("restore jump");
                try {
                    model.ShowConsole(true);
                    state.Before = (operation, control) => {
                        if (operation == ConsoleInputOperation.Enable && control == Control.Jump) throw failure;
                    };
                    Assert.AreSame(failure, Assert.ThrowsExactly<InvalidOperationException>(() => model.ShowConsole(false)));
                    Assert.IsTrue(state.Enabled.Contains(Control.CursorY));
                    state.Before = null;
                    state.Enabled.Remove(Control.CursorY);
                    state.Enabled.Add(Control.Attack);
                    if (reopen) {
                        model.ShowConsole(true);
                        model.ShowConsole(false);
                    } else model.Dispose();
                    CollectionAssert.AreEquivalent(new[] { Control.Attack, Control.Jump }, state.Enabled.ToArray(),
                        "Retry owns only Jump; later external changes to restored controls must survive.");
                } finally { state.Before = null; model.Dispose(); }
            }
        }

        [TestMethod]
        public void OpenConsoleReappliesItsAllowedControlsOnEachFrame() {
            using (var fixture = new ConsoleFixture(false)) {
                var state = Controls();
                using (var model = new ConsoleModel(fixture.Platform, fixture.Environment, Functions(fixture, state))) {
                    model.ShowConsole(true);
                    foreach (var frame in new[] { 0, 1 }) {
                        if (frame == 1) state.EnableAll(); // A new engine frame does not retain our prior disable commands.
                        model.Tick();
                        Assert.IsTrue(state.Enabled.Contains(Control.MoveUpDown));
                        Assert.IsTrue(state.Enabled.Contains(Control.VehicleAccelerate));
                        Assert.IsTrue(state.Enabled.Contains(Control.VehicleFlyThrottleUp));
                        Assert.IsFalse(state.Enabled.Contains(Control.Attack));
                        Assert.IsFalse(state.Enabled.Contains(Control.Jump));
                    }
                }
            }
        }

        [TestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public void StandaloneControlCaptureAndClosedConsoleCleanupFlushPendingTemporaryChanges(bool closeConsole) {
            using (var fixture = new ConsoleFixture(false)) {
                var state = Controls();
                var functions = Functions(fixture, state);
                var model = new ConsoleModel(fixture.Platform, fixture.Environment, functions);
                var failure = new InvalidOperationException("restore cursor X");
                try {
                    state.Before = (operation, control) => {
                        if (operation == ConsoleInputOperation.Disable && control == Control.CursorX) throw failure;
                    };
                    Assert.AreSame(failure, Assert.ThrowsExactly<InvalidOperationException>(() => functions.GetMousePos()));
                    Assert.IsTrue(state.Enabled.Contains(Control.CursorX));
                    state.Before = null;
                    if (closeConsole) model.ShowConsole(false);
                    else CollectionAssert.AreEquivalent(new[] { Control.CursorY }, functions.CaptureEnabledControls());
                    CollectionAssert.AreEquivalent(new[] { Control.CursorY }, state.Enabled.ToArray());
                } finally { state.Before = null; model.Dispose(); }
            }
        }

        [TestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public void LegacyBulkControlHelperRestoresAfterPartialDisableAndStillSupportsExplicitEnableList(bool rollbackFails) {
            using (var fixture = new ConsoleFixture(false)) {
                var state = Controls();
                var functions = Functions(fixture, state);
                var failure = new InvalidOperationException("disable cursor Y");
                var restoreFailure = new InvalidOperationException("restore cursor Y");
                state.Before = (operation, control) => {
                    if (operation == ConsoleInputOperation.Disable && control == Control.CursorY) throw failure;
                    if (rollbackFails && operation == ConsoleInputOperation.Enable && control == Control.CursorY) throw restoreFailure;
                };
                if (rollbackFails) {
                    var error = Assert.ThrowsExactly<AggregateException>(() => functions.DisableAllControls());
                    CollectionAssert.AreEqual(new[] { failure, restoreFailure }, error.Flatten().InnerExceptions.ToArray());
                } else {
                    Assert.AreSame(failure, Assert.ThrowsExactly<InvalidOperationException>(() => functions.DisableAllControls()));
                }
                CollectionAssert.AreEquivalent(new[] { Control.CursorY }, state.Enabled.ToArray());
                state.Before = null;
                CollectionAssert.AreEquivalent(new[] { Control.CursorY }, functions.DisableAllControls());
                Assert.IsEmpty(state.Enabled);
                functions.EnableControls(new List<Control> { Control.Jump, Control.Attack });
                CollectionAssert.AreEquivalent(new[] { Control.Jump, Control.Attack }, state.Enabled.ToArray());
            }
        }

        [TestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public void ModelDeadlineUsesElapsedMillisecondsAndSignedWrapWithoutCreatingAnUnloadedVehicle(bool atDeadline) {
            using (var fixture = new ConsoleFixture(false)) {
                var streaming = new ConsoleStreamingState();
                var functions = Functions(fixture, Controls(), streaming);
                fixture.Host.GameTime = int.MaxValue - 24;
                fixture.Host.FrameMilliseconds = 25;
                fixture.Host.OnWait = () => streaming.Loaded = atDeadline && fixture.Host.Waits.Count == 2;
                Assert.AreEqual(atDeadline ? 23 : 0, functions.SpawnVehicleProper(17, new Vector3(1, 2, 3), 50));
                Assert.AreEqual(2, fixture.Host.Waits.Count);
                Assert.AreEqual(atDeadline ? 1 : 0, streaming.Calls.Count(call => call == "create-vehicle"));
                Assert.AreEqual("request-model", streaming.Calls.First());
                Assert.AreEqual("release-model", streaming.Calls.Last());
                Assert.IsEmpty(streaming.Requested);
            }
        }

        [TestMethod]
        public void CreationFailureAndZeroHandleStillReleaseTheOwnedModel() {
            using (var fixture = new ConsoleFixture(false)) {
                var streaming = new ConsoleStreamingState { Loaded = true };
                var functions = Functions(fixture, Controls(), streaming);
                var failure = new InvalidOperationException("create failed");
                streaming.CreateFailure = failure;
                Assert.AreSame(failure, Assert.ThrowsExactly<InvalidOperationException>(() => functions.SpawnVehicleProper(17, Vector3.Zero)));
                Assert.IsEmpty(streaming.Requested);
                streaming.CreateFailure = null;
                streaming.CreatedVehicle = 0;
                Assert.AreEqual(0, functions.SpawnVehicleProper(17, Vector3.Zero));
                Assert.IsEmpty(streaming.Requested);
                Assert.AreEqual(0, fixture.Host.Waits.Count);
            }
        }

        [TestMethod]
        public void CreatedVehicleRemainsAccessibleWhenModelReleaseFailsAndNextSpawnMustFlushFirst() {
            using (var fixture = new ConsoleFixture(false)) {
                var releaseFailure = new InvalidOperationException("release model");
                var streaming = new ConsoleStreamingState { Loaded = true, ReleaseFailure = releaseFailure };
                var functions = Functions(fixture, Controls(), streaming);
                Assert.AreEqual(23, functions.SpawnVehicleProper(17, Vector3.Zero),
                    "The caller must receive ownership of a successfully created vehicle.");
                CollectionAssert.AreEquivalent(new[] { 17 }, streaming.Requested.ToArray());
                streaming.Calls.Clear();
                Assert.AreSame(releaseFailure, Assert.ThrowsExactly<InvalidOperationException>(() =>
                    functions.SpawnVehicleProper(19, Vector3.UnitX)));
                CollectionAssert.AreEqual(new[] { "release-model" }, streaming.Calls,
                    "A second spawn cannot acquire another model or create another vehicle until cleanup succeeds.");
                streaming.ReleaseFailure = null;
                streaming.Calls.Clear();
                Assert.AreEqual(23, functions.SpawnVehicleProper(19, Vector3.UnitX));
                CollectionAssert.AreEqual(new[] { "release-model", "request-model", "model-loaded", "create-vehicle", "release-model" },
                    streaming.Calls);
                Assert.IsEmpty(streaming.Requested);
            }
        }

        [TestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public void NoCreatedVehicleReportsReleaseFailureAndExplicitFlushCanRetryWithoutAnotherSpawn(bool loaded) {
            using (var fixture = new ConsoleFixture(false)) {
                var releaseFailure = new InvalidOperationException("release model");
                var streaming = new ConsoleStreamingState { Loaded = loaded, CreatedVehicle = 0, ReleaseFailure = releaseFailure };
                var functions = Functions(fixture, Controls(), streaming);
                Assert.AreSame(releaseFailure, Assert.ThrowsExactly<InvalidOperationException>(() =>
                    functions.SpawnVehicleProper(17, Vector3.Zero, 0)));
                CollectionAssert.AreEquivalent(new[] { 17 }, streaming.Requested.ToArray());
                streaming.Calls.Clear();
                Assert.AreSame(releaseFailure, Assert.ThrowsExactly<InvalidOperationException>(functions.ReleasePendingModel));
                streaming.ReleaseFailure = null;
                functions.ReleasePendingModel();
                functions.ReleasePendingModel();
                CollectionAssert.AreEqual(new[] { "release-model", "release-model" }, streaming.Calls);
                Assert.IsEmpty(streaming.Requested);
            }
        }

        [TestMethod]
        public void CreationAndReleaseFailuresPreserveBothOriginalExceptionsAndOnlyPendingModelOwnership() {
            using (var fixture = new ConsoleFixture(false)) {
                var createFailure = new InvalidOperationException("create vehicle");
                var releaseFailure = new InvalidOperationException("release model");
                var streaming = new ConsoleStreamingState {
                    Loaded = true, CreateFailure = createFailure, ReleaseFailure = releaseFailure
                };
                var functions = Functions(fixture, Controls(), streaming);
                var error = Assert.ThrowsExactly<AggregateException>(() => functions.SpawnVehicleProper(17, Vector3.Zero));
                CollectionAssert.AreEqual(new[] { createFailure, releaseFailure }, error.InnerExceptions.ToArray());
                CollectionAssert.AreEquivalent(new[] { 17 }, streaming.Requested.ToArray());
                streaming.ReleaseFailure = null;
                functions.ReleasePendingModel();
                Assert.IsEmpty(streaming.Requested);
                Assert.AreEqual(1, streaming.Calls.Count(call => call == "create-vehicle"));
            }
        }

        [TestMethod]
        public void DisposalAttemptsControlAndModelCleanupIndependentlyAndRetainsOnlyFailedOwnership() {
            using (var fixture = new ConsoleFixture(false)) {
                var controls = Controls();
                var controlFailure = new InvalidOperationException("restore cursor Y");
                var modelFailure = new InvalidOperationException("release model");
                var streaming = new ConsoleStreamingState { Loaded = true, ReleaseFailure = modelFailure };
                var functions = Functions(fixture, controls, streaming);
                var model = new ConsoleModel(fixture.Platform, fixture.Environment, functions);
                try {
                    model.ShowConsole(true);
                    Assert.AreEqual(23, functions.SpawnVehicleProper(17, Vector3.Zero));
                    controls.Before = (operation, control) => {
                        if (operation == ConsoleInputOperation.Enable && control == Control.CursorY) throw controlFailure;
                    };
                    var error = Assert.ThrowsExactly<AggregateException>(model.Dispose);
                    CollectionAssert.AreEqual(new[] { controlFailure, modelFailure }, error.InnerExceptions.ToArray());
                    Assert.AreEqual(2, streaming.Calls.Count(call => call == "release-model"));
                    Assert.AreSame(model, ConsoleModel.Instance);
                    Assert.IsFalse(model.IsOpen);
                    controls.Before = null;
                    streaming.ReleaseFailure = null;
                    model.Dispose();
                    CollectionAssert.AreEquivalent(new[] { Control.CursorY }, controls.Enabled.ToArray());
                    Assert.IsEmpty(streaming.Requested);
                    Assert.IsNull(ConsoleModel.Instance);
                    model.Dispose();
                    Assert.AreEqual(3, streaming.Calls.Count(call => call == "release-model"));
                } finally {
                    controls.Before = null;
                    streaming.ReleaseFailure = null;
                    model.Dispose();
                }
            }
        }

        [TestMethod]
        public void NetworkRequestsNeverClaimOwnershipOnAcceptanceAloneAndSkipDependentRequestsAfterFailure() {
            using (var fixture = new ConsoleFixture(false)) {
                fixture.Native.Returns(Hash.GET_PLAYER_PED, 11);
                var streaming = new ConsoleStreamingState();
                var functions = Functions(fixture, Controls(), streaming);
                fixture.Host.FrameMilliseconds = 10;
                Assert.AreEqual(0, functions.RequestEntityControl(11, 20));
                Assert.AreEqual(2, fixture.Host.Waits.Count);
                Assert.AreEqual(0, functions.RequestEntityControl(0, 20));
                Assert.AreEqual(0, functions.GetNetworkID(7));
                Assert.IsFalse(streaming.Calls.Contains("network-id"));
                Assert.IsFalse(streaming.Calls.Contains("request-network-id"));
                streaming.AcceptRequest = false;
                fixture.Host.Waits.Clear();
                fixture.Host.OnWait = () => streaming.HasControl = fixture.Host.Waits.Count == 2;
                Assert.AreEqual(11, functions.RequestEntityControl(11, 20),
                    "Confirmed ownership wins over an earlier rejected request.");
                streaming.AcceptNetworkId = false;
                Assert.AreEqual(0, functions.GetNetworkID(7));
                streaming.AcceptNetworkId = true;
                Assert.AreEqual(73, functions.GetNetworkID(7));
            }
        }
    }
}
