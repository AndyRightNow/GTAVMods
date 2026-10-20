using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using DeveloperConsole;
using GTA;
using GTA.Math;
using GTA.Native;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ModUtilsTest {
    [TestClass]
    public class ConsoleGameFunctionsTests {
        private static void AssertCall(ConsoleFixture fixture, Action action, Hash hash, params object[] arguments) {
            fixture.Native.Calls.Clear();
            action();
            Assert.AreEqual(1, fixture.Native.Calls.Count, hash.ToString());
            Assert.AreEqual(hash, fixture.Native.Calls[0].Hash);
            CollectionAssert.AreEqual(arguments, fixture.Native.Calls[0].Arguments, hash.ToString());
        }

        [TestMethod]
        public void NativeMappingsPreserveEntityHandlesControlGroupFlagsAndChannelOrder() {
            using (var fixture = new ConsoleFixture(false)) {
                var game = fixture.Functions;
                var color = Color.FromArgb(7, 11, 13, 17);
                var first = new Vector3(1, 2, 3);
                var second = new Vector3(4, 5, 6);
                var third = new Vector3(7, 8, 9);
                AssertCall(fixture, () => game.SetEntityRecordsCollisions(21, true), Hash.SET_ENTITY_RECORDS_COLLISIONS, 21, true);
                AssertCall(fixture, () => game.SetEntityCollision(21, true, false), Hash.SET_ENTITY_COLLISION, 21, true, false);
                AssertCall(fixture, () => game.SetEntityProofs(21, true, false, true, false, true, false, true, false),
                    Hash.SET_ENTITY_PROOFS, 21, true, false, true, false, true, false, true, false);
                AssertCall(fixture, () => game.SetEntityLoadColissionFlag(21, true), Hash.SET_ENTITY_LOAD_COLLISION_FLAG, 21, true);
                AssertCall(fixture, () => game.SetEntityGravity(21, false), Hash.SET_ENTITY_HAS_GRAVITY, 21, false);
                AssertCall(fixture, () => game.SetTextEdge(2, color), Hash.SET_TEXT_EDGE, 2, color.R, color.G, color.B, color.A);
                AssertCall(fixture, () => game.SetTextDropShadow(3, color), Hash.SET_TEXT_DROP_SHADOW, 3, color.R, color.G, color.B, color.A);
                AssertCall(fixture, () => game.DrawLine(first, second, color), Hash.DRAW_LINE, 1f, 2f, 3f, 4f, 5f, 6f, color.R, color.G, color.B, color.A);
                AssertCall(fixture, () => game.DrawBox(first, second, color), Hash.DRAW_BOX, 1f, 2f, 3f, 4f, 5f, 6f, color.R, color.G, color.B, color.A);
                AssertCall(fixture, () => game.DrawPoly(first, second, third, color), Hash.DRAW_POLY,
                    1f, 2f, 3f, 4f, 5f, 6f, 7f, 8f, 9f, color.R, color.G, color.B, color.A);
                AssertCall(fixture, () => game.DisplayRadar(false), Hash.DISPLAY_RADAR, false);
                AssertCall(fixture, () => game.DisplayHud(true), Hash.DISPLAY_HUD, true);
                AssertCall(fixture, () => game.ShowMouseThisFrame(), Hash.SET_MOUSE_CURSOR_THIS_FRAME);
                AssertCall(fixture, () => game.KickPlayer(21), Hash.NETWORK_SESSION_KICK_PLAYER, 21);
                AssertCall(fixture, () => game.SetEntityInvinc(21, true), Hash.SET_ENTITY_INVINCIBLE, 21, true);
                AssertCall(fixture, () => game.SetSPInvinc(21, false), Hash.SET_PLAYER_INVINCIBLE, 21, false);
                AssertCall(fixture, () => game.SetInvincTime(1234), Hash.NETWORK_SET_LOCAL_PLAYER_INVINCIBLE_TIME, 1234);
                AssertCall(fixture, () => game.SetTextRightJustifty(true), Hash.SET_TEXT_RIGHT_JUSTIFY, true);
                AssertCall(fixture, () => game.DisableControlAction(Control.Attack, true), Hash.DISABLE_CONTROL_ACTION, 0, (int)Control.Attack, true);
                AssertCall(fixture, () => game.EnableControlAction(Control.Jump, false), Hash.ENABLE_CONTROL_ACTION, 0, (int)Control.Jump, false);
                AssertCall(fixture, () => game.SetControlActions(true), Hash.ENABLE_ALL_CONTROL_ACTIONS, 0);
                AssertCall(fixture, () => game.SetControlActions(false), Hash.DISABLE_ALL_CONTROL_ACTIONS, 0);
                AssertCall(fixture, () => game.ClearAreaOfObjects(first, 4), Hash.CLEAR_AREA_OF_OBJECTS, 1f, 2f, 3f, 4f, false);
                AssertCall(fixture, () => game.ClearAreaOfProjectiles(first, 4, true), Hash.CLEAR_AREA_OF_PROJECTILES, 1f, 2f, 3f, 4f, true);
                AssertCall(fixture, () => game.ActivateDamageTrackerOnNetworkId(21, false), Hash.ACTIVATE_DAMAGE_TRACKER_ON_NETWORK_ID, 21, false);
                var rectangle = new Point(3, 4);
                game.DrawText("label", rectangle, .3f, color, GTA.UI.Font.ChaletLondon);
                Assert.AreEqual(("label", rectangle, .3f, color, GTA.UI.Font.ChaletLondon), fixture.Platform.Text.Single());
            }
        }

        [TestMethod]
        public void NativeQueriesPassHandlesAndReturnTypedResults() {
            using (var fixture = new ConsoleFixture(false)) {
                var game = fixture.Functions;
                fixture.Native.Returns(Hash.GET_ENTITY_TYPE, 3);
                AssertCall(fixture, () => Assert.AreEqual(GTAFuncs.EntityType.Prop, game.GetEntityType(9)), Hash.GET_ENTITY_TYPE, 9);
                fixture.Native.Returns(Hash.NETWORK_GET_PLAYER_INDEX, 4);
                AssertCall(fixture, () => Assert.AreEqual(4, game.GetPlayerIndex(9)), Hash.NETWORK_GET_PLAYER_INDEX, 9);
                fixture.Native.Returns(Hash.GET_CONTROL_VALUE, 121);
                AssertCall(fixture, () => Assert.AreEqual(121, game.GetControlValue(Control.Jump)), Hash.GET_CONTROL_VALUE, 0, (int)Control.Jump);
                fixture.Native.Returns(Hash.NETWORK_GET_NUM_CONNECTED_PLAYERS, 2);
                AssertCall(fixture, () => Assert.AreEqual(2, game.GetNumNetworkPlayers()), Hash.NETWORK_GET_NUM_CONNECTED_PLAYERS);
                fixture.Native.Returns(Hash.GET_PLAYER_INVINCIBLE, true);
                AssertCall(fixture, () => Assert.IsTrue(game.GetPlayerInvincible(5)), Hash.GET_PLAYER_INVINCIBLE, 5);
                fixture.Native.Returns(Hash.GET_FIRST_BLIP_INFO_ID, 11);
                AssertCall(fixture, () => Assert.AreEqual(11, game.GetFirstBlipInfoID(8)), Hash.GET_FIRST_BLIP_INFO_ID, 8);
                fixture.Native.Returns(Hash.IS_WAYPOINT_ACTIVE, true);
                AssertCall(fixture, () => Assert.IsTrue(game.IsWaypointActive()), Hash.IS_WAYPOINT_ACTIVE);
                fixture.Native.Returns(Hash.GET_PED_GROUP_INDEX, 3);
                AssertCall(fixture, () => Assert.AreEqual(3, game.GetPedGroupIndex(5)), Hash.GET_PED_GROUP_INDEX, 5);
                Assert.AreEqual(fixture.Outputs.Rotation, game.GetEntityQuaternion(5));
                Assert.AreEqual(5, fixture.Outputs.QuaternionEntity);
                Assert.AreEqual(new Vector2(300, 450), game.WorldToScreen(new Vector3(4, 5, 6)));
                Assert.AreEqual(new Vector3(4, 5, 6), fixture.Outputs.Projection);
            }
        }

        [TestMethod]
        public void TextWidthAndHashedStatAndPickupCallsPreserveOrderingAndArguments() {
            using (var fixture = new ConsoleFixture(false)) {
                var game = fixture.Functions;
                Assert.AreEqual(.01f, game.GetTextWidth("abc", GTA.UI.Font.Pricedown, .7f));
                CollectionAssert.AreEqual(new[] { Hash.SET_TEXT_FONT, Hash.SET_TEXT_SCALE,
                    Hash.BEGIN_TEXT_COMMAND_GET_SCREEN_WIDTH_OF_DISPLAY_TEXT,
                    Hash.ADD_TEXT_COMPONENT_SUBSTRING_PLAYER_NAME, Hash.END_TEXT_COMMAND_GET_SCREEN_WIDTH_OF_DISPLAY_TEXT },
                    fixture.Native.Calls.Select(c => c.Hash).ToArray());
                CollectionAssert.AreEqual(new object[] { (int)GTA.UI.Font.Pricedown }, fixture.Native.Calls[0].Arguments);
                CollectionAssert.AreEqual(new object[] { .7f, .7f }, fixture.Native.Calls[1].Arguments);
                CollectionAssert.AreEqual(new object[] { "STRING" }, fixture.Native.Calls[2].Arguments);
                CollectionAssert.AreEqual(new object[] { "abc" }, fixture.Native.Calls[3].Arguments);
                CollectionAssert.AreEqual(new object[] { 1 }, fixture.Native.Calls[4].Arguments);
                fixture.Native.Returns(Hash.GET_HASH_KEY, 123);
                foreach (var test in new (Action Action, string Name, Hash Hash, object[] Expected)[] {
                    (() => game.StatSetInt("number", 17, true), "number", Hash.STAT_SET_INT, new object[] { 123, 17, true }),
                    (() => game.StatSetFloat("float", .7f, false), "float", Hash.STAT_SET_FLOAT, new object[] { 123, .7f, false }),
                    (() => game.StatSetBool("flag", true, false), "flag", Hash.STAT_SET_BOOL, new object[] { 123, true, false }),
                    (() => game.CreateAmbientPickup("pickup", new Vector3(1, 2, 3), 40), "pickup", Hash.CREATE_AMBIENT_PICKUP,
                        new object[] { 123, 1f, 2f, 3f, 0, 40, 0, false, true })
                }) {
                    fixture.Native.Calls.Clear();
                    test.Action();
                    Assert.AreEqual(2, fixture.Native.Calls.Count);
                    Assert.AreEqual(Hash.GET_HASH_KEY, fixture.Native.Calls[0].Hash);
                    CollectionAssert.AreEqual(new object[] { test.Name }, fixture.Native.Calls[0].Arguments);
                    Assert.AreEqual(test.Hash, fixture.Native.Calls[1].Hash);
                    CollectionAssert.AreEqual(test.Expected, fixture.Native.Calls[1].Arguments);
                }
            }
        }

        [TestMethod]
        public void TemporaryControlEnablingRestoresBothStatesIncludingReadFailureAndPreservesAnalogFractions() {
            using (var fixture = new ConsoleFixture(false)) {
                fixture.Native.Queries[Hash.IS_CONTROL_ENABLED] = a => (int)a[1] == (int)Control.CursorX;
                fixture.Native.Queries[Hash.GET_CONTROL_NORMAL] = a => (int)a[1] == (int)Control.CursorX ? .25f : -.5f;
                Assert.AreEqual(new Point(300, -450), fixture.Functions.GetMousePos());
                var restore = fixture.Native.Calls.Skip(fixture.Native.Calls.Count - 2).ToArray();
                Assert.AreEqual(Hash.ENABLE_CONTROL_ACTION, restore[0].Hash);
                Assert.AreEqual(Hash.DISABLE_CONTROL_ACTION, restore[1].Hash);
                fixture.Native.Queries[Hash.GET_CONTROL_NORMAL] = a => throw new InvalidOperationException("device lost");
                fixture.Native.Calls.Clear();
                Assert.ThrowsExactly<InvalidOperationException>(() => fixture.Functions.GetMousePos());
                Assert.AreEqual(Hash.DISABLE_CONTROL_ACTION, fixture.Native.Calls.Last().Hash);
                fixture.Native.Calls.Clear();
                Assert.ThrowsExactly<InvalidOperationException>(() => fixture.Functions.GetControlPoint(Control.CursorX, Control.CursorY));
                Assert.AreEqual(Hash.DISABLE_CONTROL_ACTION, fixture.Native.Calls.Last().Hash);
                fixture.Native.Calls.Clear();
                Assert.ThrowsExactly<InvalidOperationException>(() => fixture.Functions.IsControlPressed(Control.CursorX));
                Assert.AreEqual(Hash.ENABLE_CONTROL_ACTION, fixture.Native.Calls.Last().Hash);
                fixture.Native.Queries[Hash.GET_CONTROL_NORMAL] = a => (int)a[1] == (int)Control.ScriptLeftAxisX ? -.25f : .5f;
                Assert.AreEqual(new Vector2(-.25f, .5f), fixture.Functions.GetLeftStick());
                Assert.AreEqual(new Vector2(.5f, .5f), fixture.Functions.GetRightStick());
                Assert.AreEqual(new Vector2(.5f, .5f), fixture.Functions.GetMoveInputVector());
                Assert.AreEqual(new Vector2(.5f, .5f), fixture.Functions.GetLookInputVector());
                Assert.IsTrue(fixture.Functions.IsLeftMouseClicked());
                Assert.IsTrue(fixture.Functions.IsRightMouseClicked());
                Assert.IsTrue(fixture.Functions.IsSelectPressed());
                fixture.Native.Returns(Hash.GET_CONTROL_NORMAL, 0f);
                Assert.IsFalse(fixture.Functions.IsControlPressed(Control.Jump));
            }
        }

        [TestMethod]
        public void DisabledControlFallbackOnlyQueriesSecondSourceWhenNeeded() {
            using (var fixture = new ConsoleFixture(false)) {
                fixture.Native.Returns(Hash.IS_DISABLED_CONTROL_PRESSED, true);
                Assert.IsTrue(fixture.Functions.IsControlPressedIgnoreDisabled(Control.Attack));
                Assert.AreEqual(1, fixture.Native.Calls.Count);
                fixture.Native.Returns(Hash.IS_DISABLED_CONTROL_PRESSED, false);
                fixture.Native.Returns(Hash.IS_CONTROL_PRESSED, true);
                Assert.IsTrue(fixture.Functions.IsControlPressedIgnoreDisabled(Control.Attack));
                fixture.Native.Returns(Hash.IS_CONTROL_PRESSED, false);
                Assert.IsFalse(fixture.Functions.IsControlPressedIgnoreDisabled(Control.Attack));
            }
        }

        [TestMethod]
        public void ScreenClampingGroundQueriesAndCameraMathUseCorrectAxesAndUnits() {
            using (var fixture = new ConsoleFixture(false)) {
                var game = fixture.Functions;
                Assert.AreEqual(new Vector2(1200, 0), game.ValidateScreenPoint(new Vector2(1400, -1)));
                Assert.AreEqual(new Vector2(0, 900), game.ValidateScreenPoint(new Vector2(-1, 901)));
                Assert.AreEqual(new Vector2(1200, 900), game.ValidateScreenPoint(new Vector2(1200, 900)));
                Assert.AreEqual(new Vector2(13, 17), game.ValidateScreenPoint(new Vector2(13, 17)));
                Assert.AreEqual(new Vector3(3, 4, 43), game.GetGroundPos(new Vector2(3, 4)));
                Assert.AreEqual(new Vector3(3, 4, 1000), fixture.World.GroundQuery);
                fixture.World.GroundLoaded = false;
                Assert.ThrowsExactly<InvalidOperationException>(() => game.GetGroundPos(new Vector2(3, 4)));
                Assert.AreEqual(new Vector3(10, 26, 30), game.GetCoordsFromCam(5));
                fixture.Camera.GameplayRotation = new Vector3(30, 0, 90);
                var result = game.GetCoordsFromCam(5);
                Assert.AreEqual(10 - (5 + Math.Cos(Math.PI / 6)), result.X, .00001);
                Assert.AreEqual(20, result.Y, .00001);
                Assert.AreEqual(34, result.Z, .00001);
            }
        }

        [TestMethod]
        public void PlayerEntityUsesCurrentVehicleObservationAndOtherwisePed() {
            using (var fixture = new ConsoleFixture(false)) {
                fixture.Native.Returns(Hash.GET_PLAYER_PED, 11);
                fixture.Native.Returns(Hash.GET_VEHICLE_PED_IS_IN, 22);
                fixture.Native.Returns(Hash.IS_PED_IN_ANY_VEHICLE, true);
                Assert.AreEqual(22, fixture.Functions.GetPlayerEntity(7));
                Assert.IsFalse(fixture.Native.Calls.Any(c => c.Hash == Hash.IS_PED_SITTING_IN_ANY_VEHICLE));
                CollectionAssert.AreEqual(new object[] { 7 },
                    fixture.Native.Calls.Single(c => c.Hash == Hash.GET_PLAYER_PED).Arguments);
                // false excludes entering-only observations and selects the current, not last, vehicle.
                CollectionAssert.AreEqual(new object[] { 11, false },
                    fixture.Native.Calls.Single(c => c.Hash == Hash.IS_PED_IN_ANY_VEHICLE).Arguments);
                CollectionAssert.AreEqual(new object[] { 11, false },
                    fixture.Native.Calls.Single(c => c.Hash == Hash.GET_VEHICLE_PED_IS_IN).Arguments);
                fixture.Native.Returns(Hash.IS_PED_IN_ANY_VEHICLE, false);
                fixture.Native.Returns(Hash.IS_PED_SITTING_IN_ANY_VEHICLE, true);
                Assert.AreEqual(22, fixture.Functions.GetPlayerEntity(7));
                fixture.Native.Returns(Hash.IS_PED_SITTING_IN_ANY_VEHICLE, false);
                Assert.AreEqual(11, fixture.Functions.GetPlayerEntity(7));
                fixture.Native.Calls.Clear();
                fixture.Functions.SetInSpectatorMode(7, true);
                CollectionAssert.AreEqual(new object[] { true, 11 }, fixture.Native.Calls.Last().Arguments);
            }
        }

        [TestMethod]
        public void EntityControlRequestsYieldUntilSuccessOrElapsedTimeoutAndUseActualNetworkId() {
            using (var fixture = new ConsoleFixture(false)) {
                var attempts = 0;
                fixture.Native.Queries[Hash.NETWORK_HAS_CONTROL_OF_ENTITY] = a => ++attempts == 3;
                Assert.AreEqual(11, fixture.Functions.RequestEntityControl(11, 5));
                CollectionAssert.AreEqual(new[] { 0, 0 }, fixture.Host.Waits);
                Assert.AreEqual(Hash.NETWORK_REQUEST_CONTROL_OF_ENTITY, fixture.Native.Calls[0].Hash);
                fixture.Native.Returns(Hash.NETWORK_HAS_CONTROL_OF_ENTITY, false);
                fixture.Host.Waits.Clear();
                Assert.AreEqual(0, fixture.Functions.RequestEntityControl(11, 2));
                Assert.AreEqual(2, fixture.Host.Waits.Count);
                fixture.Host.Waits.Clear();
                Assert.AreEqual(0, fixture.Functions.RequestEntityControl(11, 0));
                Assert.AreEqual(0, fixture.Host.Waits.Count);
                fixture.Native.Returns(Hash.GET_PLAYER_PED, 11);
                fixture.Native.Returns(Hash.NETWORK_HAS_CONTROL_OF_ENTITY, true);
                fixture.Native.Returns(Hash.NETWORK_GET_NETWORK_ID_FROM_ENTITY, 73);
                Assert.AreEqual(73, fixture.Functions.GetNetworkID(7));
                CollectionAssert.AreEqual(new object[] { 73 }, fixture.Native.Calls.Last().Arguments);
            }
        }

        [TestMethod]
        public void VehicleSpawnRequestsBeforeWaitingAndBoundsModelPolling() {
            using (var fixture = new ConsoleFixture(false)) {
                var attempts = 0;
                fixture.Native.Queries[Hash.HAS_MODEL_LOADED] = a => ++attempts == 3;
                fixture.Native.Returns(Hash.CREATE_VEHICLE, 23);
                Assert.AreEqual(23, fixture.Functions.SpawnVehicleProper(17, new Vector3(1, 2, 3)));
                Assert.AreEqual(Hash.REQUEST_MODEL, fixture.Native.Calls.First().Hash);
                CollectionAssert.AreEqual(new object[] { 17 }, fixture.Native.Calls.First().Arguments);
                Assert.AreEqual(2, fixture.Host.Waits.Count);
                // The pinned SHVDNE World.CreateVehicle supplies heading and both network flags.
                CollectionAssert.AreEqual(new object[] { 17, 1f, 2f, 3f, 0f, false, false },
                    fixture.Native.Calls.Single(c => c.Hash == Hash.CREATE_VEHICLE).Arguments);
                Assert.AreEqual(Hash.SET_MODEL_AS_NO_LONGER_NEEDED, fixture.Native.Calls.Last().Hash);
                fixture.Native.Returns(Hash.HAS_MODEL_LOADED, false);
                fixture.Host.Waits.Clear();
                fixture.Native.Calls.Clear();
                Assert.AreEqual(0, fixture.Functions.SpawnVehicleProper(17, Vector3.Zero));
                Assert.AreEqual(10000, fixture.Host.Waits.Count);
                Assert.IsFalse(fixture.Native.Calls.Any(c => c.Hash == Hash.CREATE_VEHICLE));
                Assert.AreEqual(Hash.SET_MODEL_AS_NO_LONGER_NEEDED, fixture.Native.Calls.Last().Hash);
            }
        }

        [TestMethod]
        public void LegacyStatHelperMapsEveryRequestedStatWithoutRealNetworkAccess() {
            using (var fixture = new ConsoleFixture(false)) {
                fixture.Native.Returns(Hash.GET_PED_GROUP_INDEX, 2);
                var keys = new List<string>();
                fixture.Native.Queries[Hash.GET_HASH_KEY] = args => { keys.Add((string)args[0]); return keys.Count; };
                fixture.Functions.AntiBan();
                CollectionAssert.AreEqual(new[] {
                    "MP2_BAD_SPORT_BITSET", "MP2_CHEAT_BITSET", "MPPLY_OVERALL_BADSPORT", "MPPLY_CHAR_IS_BADSPORT",
                    "MPPLY_BECAME_BADSPORT_NUM", "MPPLY_REPORT_STRENGTH", "MPPLY_COMMEND_STRENGTH", "MPPLY_FRIENDLY",
                    "MPPLY_HELPFUL", "MPPLY_GRIEFING", "MPPLY_OFFENSIVE_LANGUAGE", "MPPLY_OFFENSIVE_UGC",
                    "MPPLY_VC_HATE", "MPPLY_GAME_EXPLOITS", "MPPLY_ISPUNISHED" }, keys);
                var stats = fixture.Native.Calls.Where(c => c.Hash == Hash.STAT_SET_INT || c.Hash == Hash.STAT_SET_FLOAT || c.Hash == Hash.STAT_SET_BOOL).ToArray();
                CollectionAssert.AreEqual(new object[] { 0, 0, 0f, false, 0, 32, 100, 100, 100, 0, 0, 0, 0, 0, 0 },
                    stats.Select(c => c.Arguments[1]).ToArray());
                CollectionAssert.AreEqual(Enumerable.Range(1, 15).ToArray(), stats.Select(c => (int)c.Arguments[0]).ToArray());
                Assert.IsTrue(stats.All(c => (bool)c.Arguments[2]));
                CollectionAssert.AreEqual(new object[] { 11 }, fixture.Native.Calls[0].Arguments);
                Assert.AreEqual(Hash.NETWORK_SET_SCRIPT_IS_SAFE_FOR_NETWORK_GAME, fixture.Native.Calls[1].Hash);
                CollectionAssert.AreEqual(new object[] { 1, true, 1 }, fixture.Native.Calls[2].Arguments);
            }
        }
    }
}
