using Control = GTA.Control;
using DeveloperConsole;
using GTA.Math;
using GTA.Native;
using GTA;
using GTAVMods.Tests.Support;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Reflection.Emit;
using System.Reflection;
using System.Windows.Forms;
using System;
using static GTAVMods.Tests.Support.ConsoleAssertions;

namespace GTAVMods.Tests.PlayerIntegration.ConsoleOperations
{
    [TestClass, TestCategory("PlayerIntegration")]
    public class PlayerWorldToolsTests
    {
        [TestMethod]
        [DataRow("self.name", "Franklin")]
        [DataRow("self.id", "7")] [DataRow("self.handle", "7")]
        [DataRow("self.pos", "X:1 Y:2 Z:3")] [DataRow("self.position", "X:1 Y:2 Z:3")]
        [DataRow("self.vehicle", "null")]
        public void PlayerCanReadTheirNameIdentityPositionAndAbsentVehicleFromConsole(string alias, string expected) {
            using (var scene = new ConsoleFixture()) {
                scene.Submit("eval " + alias);
                Assert.IsTrue(scene.Platform.Text.Any(t => t.Text == "12:34:56: " + expected));
            }
        }

        [TestMethod]
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

        [TestMethod]
        public void PlayerCanSpawnAPracticeVehicleOnlyAfterTheModelIsObservedReady() {
            using (var scene = new ConsoleFixture()) {
                scene.ModelLoaded = false;
                scene.Host.OnWait = () => scene.ModelLoaded = scene.Host.Waits.Count == 2;
                scene.Evaluate("return FUNCTIONS.SpawnVehicleProper(17, POS)");
                Assert.IsTrue(scene.ShowsOutput("23"));
                Assert.AreEqual(2, scene.Host.Waits.Count);
                Command(scene, Hash.CREATE_VEHICLE, 17, 1f, 2f, 3f, 0f, false, false);
                Assert.IsEmpty(scene.RequestedModels);
                var requested = scene.Native.Calls.FindIndex(c => c.Hash == Hash.REQUEST_MODEL);
                var created = scene.Native.Calls.FindIndex(c => c.Hash == Hash.CREATE_VEHICLE);
                Assert.IsTrue(requested < created);
                scene.Submit("clear"); scene.Native.Calls.Clear();
                scene.ModelLoaded = false; scene.Host.OnWait = null;
                scene.Evaluate("return FUNCTIONS.SpawnVehicleProper(19, POS, 2)");
                Assert.IsTrue(scene.ShowsOutput("0"));
                Assert.IsFalse(scene.Native.Calls.Any(c => c.Hash == Hash.CREATE_VEHICLE));
                Assert.IsEmpty(scene.RequestedModels);
            }
        }

        [TestMethod]
        public void PlayerCanConfigureAPracticeObjectWithoutConfusingItsPhysicsAndProofFlags() {
            using (var scene = new ConsoleFixture()) {
                scene.Native.Returns(Hash.GET_ENTITY_TYPE, 3);
                scene.Evaluate("int objectId = 21; FUNCTIONS.SetEntityRecordsCollisions(objectId, true); FUNCTIONS.SetEntityCollision(objectId, true, false); FUNCTIONS.SetEntityProofs(objectId, true, false, true, false, true, false, true, false); FUNCTIONS.SetEntityLoadColissionFlag(objectId, true); FUNCTIONS.SetEntityGravity(objectId, false); FUNCTIONS.SetEntityInvinc(objectId, true); return FUNCTIONS.GetEntityType(objectId)");
                Assert.IsTrue(scene.ShowsOutput("Prop"));
                Command(scene, Hash.SET_ENTITY_RECORDS_COLLISIONS, 21, true);
                Command(scene, Hash.SET_ENTITY_COLLISION, 21, true, false);
                Command(scene, Hash.SET_ENTITY_PROOFS, 21, true, false, true, false, true, false, true, false);
                Command(scene, Hash.SET_ENTITY_LOAD_COLLISION_FLAG, 21, true);
                Command(scene, Hash.SET_ENTITY_HAS_GRAVITY, 21, false);
                Command(scene, Hash.SET_ENTITY_INVINCIBLE, 21, true);
            }
        }

        [TestMethod]
        public void PlayerCanClearPracticeDebrisAndPlaceAPickupAtTheirObservedPosition() {
            using (var scene = new ConsoleFixture()) {
                scene.Native.Returns(Hash.GET_HASH_KEY, 123);
                scene.Evaluate("FUNCTIONS.ClearAreaOfObjects(POS, 4); FUNCTIONS.ClearAreaOfProjectiles(POS, 4, true); FUNCTIONS.CreateAmbientPickup(\"PICKUP_WEAPON_HAMMER\", POS, 40); return \"Practice area reset\"");
                Assert.IsTrue(scene.ShowsOutput("Practice area reset"));
                Command(scene, Hash.CLEAR_AREA_OF_OBJECTS, 1f, 2f, 3f, 4f, false);
                Command(scene, Hash.CLEAR_AREA_OF_PROJECTILES, 1f, 2f, 3f, 4f, true);
                Command(scene, Hash.GET_HASH_KEY, "PICKUP_WEAPON_HAMMER");
                Command(scene, Hash.CREATE_AMBIENT_PICKUP, 123, 1f, 2f, 3f, 0, 40, 0, false, true);
            }
        }

        [TestMethod]
        public void PlayerCanReadAnalogInputWithoutLeavingTemporaryControlsEnabled() {
            using (var scene = new ConsoleFixture()) {
                scene.Analog[Control.CursorX] = .25f; scene.Analog[Control.CursorY] = -.5f;
                scene.Analog[Control.ScriptLeftAxisX] = -.25f; scene.Analog[Control.ScriptLeftAxisY] = .5f;
                scene.Analog[Control.ScriptRightAxisX] = .75f; scene.Analog[Control.ScriptRightAxisY] = -.75f;
                scene.Analog[Control.MoveLeftRight] = .125f; scene.Analog[Control.MoveUpDown] = -.625f;
                scene.Analog[Control.LookLeftRight] = -.375f; scene.Analog[Control.LookUpDown] = .875f;
                scene.Analog[Control.CursorAccept] = 1; scene.Analog[Control.CursorCancel] = 1; scene.Analog[Control.ScriptSelect] = 1;
                scene.Native.Returns(Hash.GET_CONTROL_VALUE, 121);
                scene.Press(Keys.F8);
                var priorControls = scene.Enabled.ToArray();
                scene.Enter("eval { CONSOLE.PrintLine(\"Mouse: \" + FUNCTIONS.GetMousePos()); CONSOLE.PrintLine(\"Left stick: \" + FUNCTIONS.GetLeftStick()); CONSOLE.PrintLine(\"Right stick: \" + FUNCTIONS.GetRightStick()); CONSOLE.PrintLine(\"Move: \" + FUNCTIONS.GetMoveInputVector()); CONSOLE.PrintLine(\"Look: \" + FUNCTIONS.GetLookInputVector()); return FUNCTIONS.IsLeftMouseClicked() && FUNCTIONS.IsRightMouseClicked() && FUNCTIONS.IsSelectPressed() && !FUNCTIONS.IsControlPressed(Control.Jump) && FUNCTIONS.GetControlValue(Control.Jump) == 121; }");
                CollectionAssert.AreEquivalent(priorControls, scene.Enabled.ToArray(),
                    "Temporary input reads must restore every control before the next console frame.");
                scene.Draw();
                Assert.IsTrue(scene.ShowsOutput("Mouse: {X=300,Y=-450}"));
                Assert.IsTrue(scene.ShowsOutput("Left stick: X:-0.25 Y:0.5"));
                Assert.IsTrue(scene.ShowsOutput("Right stick: X:0.75 Y:-0.75"));
                Assert.IsTrue(scene.ShowsOutput("Move: X:0.125 Y:-0.625"));
                Assert.IsTrue(scene.ShowsOutput("Look: X:-0.375 Y:0.875"));
                Assert.IsTrue(scene.ShowsOutput("True"));
                Command(scene, Hash.GET_CONTROL_VALUE, 0, (int)Control.Jump);
            }
        }

        [TestMethod]
        [DataRow(true, false, true)]
        [DataRow(false, true, true)]
        [DataRow(false, false, false)]
        public void PlayerCanInspectAnAttackButtonEvenWhileConsoleHasDisabledIt(bool disabled, bool normal, bool expected) {
            using (var scene = new ConsoleFixture()) {
                scene.Native.Returns(Hash.IS_DISABLED_CONTROL_PRESSED, disabled); scene.Native.Returns(Hash.IS_CONTROL_PRESSED, normal);
                scene.Evaluate("return FUNCTIONS.IsControlPressedIgnoreDisabled(Control.Attack)");
                Assert.IsTrue(scene.ShowsOutput(expected.ToString()));
                Assert.AreEqual(!disabled, scene.Native.Calls.Any(c => c.Hash == Hash.IS_CONTROL_PRESSED));
            }
        }

        [TestMethod]
        public void LocationDiagnosticsUseGroundAvailabilityCameraDirectionAndScreenBounds() {
            using (var scene = new ConsoleFixture()) {
                scene.Native.Returns(Hash.GET_FIRST_BLIP_INFO_ID, 11); scene.Native.Returns(Hash.IS_WAYPOINT_ACTIVE, true);
                scene.Evaluate("CONSOLE.PrintLine(FUNCTIONS.ValidateScreenPoint(new Vector2(1400,-1)).ToString()); CONSOLE.PrintLine(FUNCTIONS.ValidateScreenPoint(new Vector2(-1,901)).ToString()); CONSOLE.PrintLine(FUNCTIONS.ValidateScreenPoint(new Vector2(1200,900)).ToString()); CONSOLE.PrintLine(FUNCTIONS.ValidateScreenPoint(new Vector2(13,17)).ToString()); CONSOLE.PrintLine(FUNCTIONS.GetGroundPos(new Vector2(3,4)).ToString()); CONSOLE.PrintLine(FUNCTIONS.GetCoordsFromCam(5).ToString()); return FUNCTIONS.IsWaypointActive() && FUNCTIONS.GetFirstBlipInfoID(8) == 11");
                Assert.IsTrue(scene.ShowsOutput("X:1200 Y:0"));
                Assert.IsTrue(scene.ShowsOutput("X:0 Y:900"));
                Assert.IsTrue(scene.ShowsOutput("X:1200 Y:900"));
                Assert.IsTrue(scene.ShowsOutput("X:13 Y:17"));
                Assert.IsTrue(scene.ShowsOutput("X:3 Y:4 Z:43"));
                Assert.IsTrue(scene.ShowsOutput("X:10 Y:26 Z:30"));
                Assert.IsTrue(scene.ShowsOutput("True"));
                Assert.AreEqual(new Vector3(3,4,1000), scene.World.GroundQuery);
                scene.World.GroundLoaded = false; scene.Submit("clear");
                scene.Evaluate("return FUNCTIONS.GetGroundPos(new Vector2(3,4))");
                Assert.IsTrue(scene.ShowsOutput("[Error] C# evaluation failed: Ground collision is not loaded at that location."));
                scene.Camera.GameplayRotation = new Vector3(30,0,90);
                scene.Submit("clear");
                scene.Evaluate("var p = FUNCTIONS.GetCoordsFromCam(5); return string.Format(System.Globalization.CultureInfo.InvariantCulture, \"Camera: X:{0:F3} Y:{1:F3} Z:{2:F3}\", p.X, p.Y, p.Z)");
                Assert.IsTrue(scene.ShowsOutput("Camera: X:4.134 Y:20.000 Z:34.000"));
            }
        }

        [TestMethod]
        [DataRow(true, false, 22)]
        [DataRow(false, true, 22)]
        [DataRow(false, false, 11)]
        public void PlayerEntityDiagnosticSelectsCurrentVehicleOrCharacter(bool inVehicle, bool sitting, int expected) {
            using (var scene = new ConsoleFixture()) {
                scene.Native.Returns(Hash.GET_PLAYER_PED, 11); scene.Native.Returns(Hash.GET_VEHICLE_PED_IS_IN, 22);
                scene.Native.Returns(Hash.IS_PED_IN_ANY_VEHICLE, inVehicle); scene.Native.Returns(Hash.IS_PED_SITTING_IN_ANY_VEHICLE, sitting);
                scene.Evaluate("CONSOLE.PrintLine(\"Player index: \" + FUNCTIONS.GetPlayerIndex(PED.Handle)); return FUNCTIONS.GetPlayerEntity(PLAYER.Handle)");
                Assert.IsTrue(scene.ShowsOutput(expected.ToString()));
                Assert.IsTrue(scene.ShowsOutput("Player index: 37"));
                Command(scene, Hash.NETWORK_GET_PLAYER_INDEX, 11);
                Command(scene, Hash.GET_PLAYER_PED, 7); Command(scene, Hash.IS_PED_IN_ANY_VEHICLE, 11, false);
                if (expected == 22) Command(scene, Hash.GET_VEHICLE_PED_IS_IN, 11, false);
            }
        }
    }
}
