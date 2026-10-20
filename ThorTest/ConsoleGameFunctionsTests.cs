using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using DeveloperConsole;
using GTA;
using GTA.Math;
using GTA.Native;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Control = GTA.Control;

namespace ModUtilsTest {
    [TestClass]
    public class ConsoleGameFunctionsTests {
        private static void Command(ConsoleFixture scene, Hash native, params object[] arguments) {
            var calls = scene.Native.Calls.Where(c => c.Hash == native).ToArray();
            Assert.IsTrue(calls.Any(c => c.Arguments.SequenceEqual(arguments)), native + " must receive the requested player command.");
        }

        [TestMethod]
        [TestCategory("PlayerIntegration")]
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
        [TestCategory("PlayerIntegration")]
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
        [TestCategory("PlayerIntegration")]
        public void PlayerCanAnnotatePracticeGeometryWithExactCoordinatesAndColors() {
            using (var scene = new ConsoleFixture()) {
                scene.Evaluate("Color c = Color.FromArgb(7, 11, 13, 17); FUNCTIONS.DrawLine(new Vector3(1,2,3), new Vector3(4,5,6), c); FUNCTIONS.DrawBox(new Vector3(1,2,3), new Vector3(4,5,6), c); FUNCTIONS.DrawPoly(new Vector3(1,2,3), new Vector3(4,5,6), new Vector3(7,8,9), c); return \"Practice boundary drawn\"");
                Assert.IsTrue(scene.ShowsOutput("Practice boundary drawn"));
                Command(scene, Hash.DRAW_LINE, 1f, 2f, 3f, 4f, 5f, 6f, (byte)11, (byte)13, (byte)17, (byte)7);
                Command(scene, Hash.DRAW_BOX, 1f, 2f, 3f, 4f, 5f, 6f, (byte)11, (byte)13, (byte)17, (byte)7);
                Command(scene, Hash.DRAW_POLY, 1f, 2f, 3f, 4f, 5f, 6f, 7f, 8f, 9f, (byte)11, (byte)13, (byte)17, (byte)7);
            }
        }

        [TestMethod]
        [TestCategory("PlayerIntegration")]
        public void ScreenshotSetupChangesHudAndTextStylingWithoutChangingPlayerIdentity() {
            using (var scene = new ConsoleFixture()) {
                scene.Native.Returns(Hash.GET_PLAYER_INVINCIBLE, true);
                scene.Evaluate("FUNCTIONS.DisplayRadar(false); FUNCTIONS.DisplayHud(false); FUNCTIONS.ShowMouseThisFrame(); FUNCTIONS.SetTextEdge(2, Color.FromArgb(7,11,13,17)); FUNCTIONS.SetTextDropShadow(3, Color.FromArgb(7,11,13,17)); FUNCTIONS.SetTextRightJustifty(true); FUNCTIONS.SetSPInvinc(PLAYER.Handle, true); return FUNCTIONS.GetPlayerInvincible(PLAYER.Handle)");
                Assert.IsTrue(scene.ShowsOutput("True"));
                Command(scene, Hash.DISPLAY_RADAR, false); Command(scene, Hash.DISPLAY_HUD, false);
                Command(scene, Hash.SET_MOUSE_CURSOR_THIS_FRAME);
                Command(scene, Hash.SET_TEXT_EDGE, 2, (byte)11, (byte)13, (byte)17, (byte)7);
                Command(scene, Hash.SET_TEXT_DROP_SHADOW, 3, (byte)11, (byte)13, (byte)17, (byte)7);
                Command(scene, Hash.SET_TEXT_RIGHT_JUSTIFY, true);
                Command(scene, Hash.SET_PLAYER_INVINCIBLE, 7, true);
            }
        }

        [TestMethod]
        [TestCategory("PlayerIntegration")]
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
        [TestCategory("PlayerIntegration")]
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
        [TestCategory("PlayerIntegration")]
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
        [TestCategory("PlayerIntegration")]
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
        [TestCategory("PlayerIntegration")]
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
