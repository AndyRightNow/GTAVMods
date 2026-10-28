using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GTA;
using GTA.Math;
using GTA.Native;
using GTAVMods.Agent;
using GTAVMods.Game;
using GTAVMods.Tests.Support;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static GTAVMods.Tests.Support.AgentScene;

namespace GTAVMods.Tests.PlayerIntegration.AgentOperations
{
    [TestClass, TestCategory("PlayerIntegration")]
    public class WorldStateDecisionTests
    {
        private static Dictionary<string, object> Object(object value) => (Dictionary<string, object>)value;
        private static Dictionary<string, object>[] Rows(object value) => ((IEnumerable)value).Cast<object>().Select(Object).ToArray();
        private static Dictionary<string, object> Read(AgentScene s, params (string Key, object Value)[] fields)
        {
            var response = s.Request("state", fields);
            Assert.AreEqual(true, response["ok"], Protocol.Encode(response));
            return Object(response["result"]);
        }
        private static Dictionary<string, object> Sections(Dictionary<string, object> state) => Object(state["sections"]);
        private static void Arm(AgentScene s)
        {
            s.Game.Entity(10).CurrentWeapon = WeaponHash.Pistol;
            s.Game.Entity(10).Weapons.Add(WeaponHash.Pistol);
            s.Game.Native.Returns(Hash.GET_AMMO_IN_PED_WEAPON, 42);
            s.Game.Native.Returns(Hash.GET_MAX_AMMO_IN_CLIP, 12);
            s.Game.Native.Returns(Hash.IS_PED_SHOOTING, false);
            s.Game.Native.Returns(Hash.IS_PLAYER_FREE_AIMING, true);
        }
        private static void Traffic(AgentScene s)
        {
            s.Game.Entity(10).ModelHash = unchecked((int)PedHash.Franklin);
            s.Game.Entity(30).ModelHash = unchecked((int)PedHash.MexGoon01GMY);
            s.Game.AddEntity(40).Position = new Vector3(30, 2, 3);
            s.Game.AddEntity(99).Exists = false;
            s.Game.World.Nearby = (position, radius) => new[] { 10, 20, 30, 40, 99 };
            s.Game.Native.Queries[Hash.GET_ENTITY_TYPE] = a => (int)a[0] == 20 ? 2 : (int)a[0] == 40 ? 3 : 1;
            s.Game.Native.Returns(Hash.GET_RELATIONSHIP_BETWEEN_PEDS, 5);
            s.Game.Entity(30).CombatAgainst.Add(10);
            s.Game.Entity(30).CurrentWeapon = WeaponHash.MicroSMG;
        }
        private static void Route(AgentScene s)
        {
            s.Blips = new[] { new Blip(90), new Blip(91), new Blip(92) };
            s.Game.Native.Queries[Hash.GET_BLIP_SPRITE] = a => (int)a[0] == 92 ? 8 : 1;
            s.Game.Native.Queries[Hash.DOES_BLIP_HAVE_GPS_ROUTE] = a => (int)a[0] == 91;
            s.Game.Native.Returns(Hash.GET_BLIP_INFO_ID_ENTITY_INDEX, 0);
            s.Game.Native.Returns(Hash.GET_NAME_OF_ZONE, "STRAW");
            s.Game.Native.Queries[Hash.GET_STREET_NAME_FROM_HASH_KEY] = a => (uint)a[0] == 12 ? "Forum Drive" : "Strawberry Avenue";
            s.Game.Native.Returns(Hash.GET_GPS_BLIP_ROUTE_FOUND, true);
            s.Game.Native.Returns(Hash.GET_GPS_BLIP_ROUTE_LENGTH, 980);
        }

        [TestMethod]
        public void CoverEntryAndAimOnlyPeekRemainDistinguishableAfterPauseClearsTransientAimFlags()
        {
            using (var s = new AgentScene())
            {
                Arm(s);
                s.Game.Native.Returns(Hash.GET_PED_BONE_COORDS, Vector3.UnitY);
                s.Game.Native.Returns(Hash.GET_GAMEPLAY_CAM_RELATIVE_HEADING, 0f);
                s.Game.Native.Returns(Hash.GET_GAMEPLAY_CAM_RELATIVE_PITCH, 0f);
                s.Game.Native.Returns(Hash.HAS_ENTITY_CLEAR_LOS_TO_ENTITY, false);
                s.Batch(Step("controls", ("ms", 200), ("inputs", new[] { Step("", ("control", "Cover")) })),
                    Step("aim", ("entity", 20), ("ms", 300)), Step("pause"));
                s.Frame();
                s.Game.Native.Returns(Hash.IS_PED_GOING_INTO_COVER, true);
                var entering = Object(Sections(Read(s))["player"]);
                Assert.AreEqual(true, entering["goingIntoCover"]);
                Assert.AreEqual(false, entering["inCover"], "A cover input is not completed cover entry.");
                s.Frame(200);
                s.Game.Native.Returns(Hash.IS_PED_GOING_INTO_COVER, false);
                s.Game.Native.Returns(Hash.IS_PED_IN_COVER, true);
                s.Game.Native.Returns(Hash.IS_PED_IN_COVER_FACING_LEFT, true);
                s.Frame(); // Tucked cover can hide the target while aim input begins the peek.
                s.Game.Native.Returns(Hash.IS_PED_AIMING_FROM_COVER, true);
                s.Game.Native.Returns(Hash.IS_PED_WEAPON_READY_TO_SHOOT, true);
                s.Game.Native.Returns(Hash.HAS_ENTITY_CLEAR_LOS_TO_ENTITY, true);
                s.Game.Outputs.AimValid = true; s.Game.Outputs.AimEntity = 20;
                s.Frame(100);
                var peeking = Sections(Read(s, ("sections", new[] { "player", "weapons" })));
                Assert.AreEqual(true, Object(peeking["player"])["inCover"]);
                Assert.AreEqual(true, Object(peeking["player"])["aimingFromCover"]);
                Assert.AreEqual(true, Object(peeking["player"])["coverFacingLeft"]);
                Assert.AreEqual(false, Object(peeking["player"])["goingIntoCover"]);
                Assert.AreEqual(true, Object(peeking["weapons"])["readyToShoot"]);
                s.Frame(200); s.Frame();
                s.Game.Native.Returns(Hash.IS_PED_AIMING_FROM_COVER, false);
                s.Game.Native.Returns(Hash.IS_PLAYER_FREE_AIMING, false);
                s.Game.Native.Returns(Hash.IS_PED_WEAPON_READY_TO_SHOOT, false);
                var paused = Sections(Read(s, ("sections", new[] { "player", "weapons" })));
                Assert.AreEqual(false, Object(paused["player"])["aimingFromCover"]);
                Assert.AreEqual(false, Object(paused["weapons"])["readyToShoot"]);
                var result = Object(s.Results.Single(row => (string)row["op"] == "aim")["result"]);
                Assert.AreEqual(2, result["inCoverFrames"]);
                Assert.AreEqual(1, result["aimingFromCoverFrames"]);
                Assert.AreEqual(1, result["weaponReadyFrames"]);
                Assert.AreEqual(1, result["lineOfSightFrames"]);
                Assert.AreEqual(true, Object(result["last"])["aimingFromCover"]);
                Assert.AreEqual(0, result["attackRequestedFrames"]);
                Assert.IsFalse(s.Game.Native.Calls.Any(c => c.Hash == Hash.SET_CONTROL_VALUE_NEXT_FRAME && (int)c.Arguments[1] == (int)Control.Attack),
                    "An aim-only peek must not shoot even after the engine reports a clear target and ready weapon.");
            }
        }

        [TestMethod]
        public void MissionDecisionDistinguishesLoadedCutsceneFromPlaybackAndCompletion()
        {
            using (var s = new AgentScene())
            {
                s.Game.Native.Returns(Hash.IS_CUTSCENE_ACTIVE, true);
                s.Game.Native.Returns(Hash.IS_CUTSCENE_PLAYING, true);
                s.Game.Native.Returns(Hash.IS_PLAYER_CONTROL_ON, false);
                s.Game.Native.Returns(Hash.GET_CUTSCENE_TIME, 700);
                s.Game.Native.Returns(Hash.GET_CUTSCENE_TOTAL_DURATION, 2400);
                var playing = Object(Read(s)["session"]);
                Assert.AreEqual(true, playing["cutscenePlaying"]);
                Assert.AreEqual(false, playing["cutsceneFinished"]);
                Assert.AreEqual(700, playing["cutsceneTime"]); Assert.AreEqual(2400, playing["cutsceneDuration"]);
                Assert.AreEqual(false, playing["playerControl"]);
                s.Game.Native.Returns(Hash.IS_CUTSCENE_PLAYING, false);
                s.Game.Native.Returns(Hash.HAS_CUTSCENE_FINISHED, true);
                s.Game.Native.Returns(Hash.IS_PLAYER_CONTROL_ON, true);
                var finished = Object(Read(s)["session"]);
                Assert.AreEqual(true, finished["cutsceneActive"], "A loaded cutscene may remain active after playback ends.");
                Assert.AreEqual(false, finished["cutscenePlaying"]); Assert.AreEqual(true, finished["cutsceneFinished"]);
                Assert.AreEqual(true, finished["playerControl"]);
            }
        }

        [TestMethod]
        public void QuickPlayerDecisionAvoidsAllWorldScansAndDiscoversNewStateSections()
        {
            using (var s = new AgentScene())
            {
                s.Game.Native.Returns(Hash.IS_MOBILE_PHONE_CALL_ONGOING, true);
                s.Game.Native.Returns(Hash.IS_HELP_MESSAGE_ON_SCREEN, true);
                var state = Read(s);
                Assert.AreEqual("gta.world-state/1", state["schema"]);
                Assert.AreEqual(true, Object(state["session"])["phoneCall"]);
                Assert.AreEqual(25, Object(Sections(state)["player"])["armor"]);
                Assert.HasCount(1, Sections(state));
                Assert.IsTrue(Object(state["cost"]).Values.All(value => (int)value == 0));
                Assert.IsFalse(s.Game.Calls.Any(c => c.Name == "world.nearby" || c.Name == "world.raycast"));
                s.World.StatePack.Register("mission.clock", c => new { time = s.Game.Host.GameTime });
                StringAssert.Contains(Protocol.Encode(s.Request("describe")), "mission.clock");
                var extra = Read(s, ("sections", new[] { "mission.clock" }));
                Assert.AreEqual(0, Object(Sections(extra)["mission.clock"])["time"]);
                Assert.ThrowsExactly<ArgumentException>(() => s.World.StatePack.Register("player", c => null));
                Assert.IsEmpty(Directory.GetFiles(s.Root, "response-*.json"));
            }
        }

        [TestMethod]
        [DataRow(0, true)] [DataRow(99, true)] [DataRow(20, false)]
        public void WalkingDecisionReportsNoCurrentVehicleAndSeparatesWorldHitsFromClearDirections(int vehicle, bool inVehicle)
        {
            using (var s = new AgentScene())
            {
                Traffic(s); Route(s);
                s.Game.Native.Returns(Hash.GET_VEHICLE_PED_IS_IN, vehicle);
                s.Game.Entity(10).InVehicle = inVehicle;
                s.Game.Entity(10).Rotation = new Vector3(0, 0, 90);
                s.Game.World.Rays.Enqueue(new RayHit(true, 0, new Vector3(1, 5, 4)));
                s.Game.World.Rays.Enqueue(new RayHit(false, 777, new Vector3(99, 99, 99)));
                var state = Read(s, ("profile", "walking"), ("sections", new[] { "player", "vehicle", "navigation", "probes" }),
                    ("blipLimit", 1), ("rays", new[] { Step("", ("yaw", 90), ("distance", 9)), Step("", ("yaw", 0), ("pitch", 30), ("distance", 6)) }));
                var sections = Sections(state);
                Assert.IsNull(sections["vehicle"]);
                Assert.AreEqual(inVehicle, Object(sections["player"])["inVehicle"]);
                if (!inVehicle) Assert.AreEqual(0, Object(sections["player"])["vehicle"], "The previous car must not appear as occupied after the player exits.");
                var navigation = Object(sections["navigation"]);
                Assert.AreEqual(91, Rows(navigation["gpsTargets"])[0]["handle"]);
                Assert.AreEqual(91, Rows(navigation["blips"])[0]["handle"]);
                Assert.AreEqual(980, navigation["routeLength"]);
                Assert.AreEqual("Forum Drive", navigation["street"]);
                Assert.AreEqual(s.Game.Entity(10).Position, s.Game.Outputs.StreetPosition);
                var rays = Rows(sections["probes"]);
                Assert.AreEqual(0, rays[0]["entity"], "Terrain is a real hit despite a zero entity handle.");
                Assert.AreEqual(3, Convert.ToInt32(rays[0]["distance"]));
                Assert.IsNull(rays[1]["entity"]); Assert.IsNull(rays[1]["position"]); Assert.IsNull(rays[1]["distance"]);
                var commands = s.Game.Calls.Where(c => c.Name == "world.raycast").ToArray();
                Assert.AreEqual(new Vector3(1, 11, 4), (Vector3)commands[0].Arguments[1]);
                var secondEnd = (Vector3)commands[1].Arguments[1];
                Assert.AreEqual(7f, secondEnd.Z, .001f);
                Assert.AreEqual(10, commands[0].Arguments[3], "Absent or stale vehicles must not cause probes to hit the player.");
                Assert.AreEqual(2, Object(state["cost"])["raycasts"]);
                Assert.AreEqual(0, Object(state["cost"])["nearbyCandidates"]);
            }
        }

        [TestMethod]
        public void FenceClimbDecisionReportsOngoingTraversalWhenTheEngineDoesNotMarkThePlayerInAir()
        {
            using (var s = new AgentScene())
            {
                s.Batch(Step("resume"), Step("controls", ("ms", 500), ("inputs", new[] { Step("", ("control", "Jump")) })), Step("pause"));
                s.Frame(); s.Frame(); s.Frame(500, 500); s.Frame();
                // Explicit engine observations: requesting Jump is not proof that the player has landed.
                s.Game.Native.Returns(Hash.IS_PED_CLIMBING, true);
                var climbing = Object(Sections(Read(s))["player"]);
                Assert.AreEqual(true, climbing["climbing"]); Assert.AreEqual(false, climbing["inAir"]);
                Assert.AreEqual(false, climbing["jumping"]); Assert.AreEqual(false, climbing["falling"]);
                Assert.AreEqual(1, s.Game.Native.Calls.Count(c => c.Hash == Hash.SET_CONTROL_VALUE_NEXT_FRAME));
                s.Game.Native.Returns(Hash.IS_PED_CLIMBING, false);
                s.Game.Native.Returns(Hash.IS_PED_VAULTING, true);
                Assert.AreEqual(true, Object(Sections(Read(s))["player"])["vaulting"]);
                s.Game.Native.Returns(Hash.IS_PED_VAULTING, false);
                s.Game.Native.Returns(Hash.IS_PED_FALLING, true);
                Assert.AreEqual(true, Object(Sections(Read(s))["player"])["falling"]);
                s.Game.Native.Returns(Hash.IS_PED_FALLING, false);
                var landed = Object(Sections(Read(s))["player"]);
                foreach (string flag in new[] { "climbing", "vaulting", "jumping", "falling" }) Assert.AreEqual(false, landed[flag]);
                Assert.AreEqual(1, s.Game.Native.Calls.Count(c => c.Hash == Hash.SET_CONTROL_VALUE_NEXT_FRAME), "Inspection must not repeat the climb input.");
            }
        }

        [TestMethod]
        public void MissionDecisionKeepsColoredContactsVisibleBeforeStaticMapIconsWithoutAGpsRoute()
        {
            using (var s = new AgentScene())
            {
                Route(s);
                s.Blips = new[] { new Blip(90), new Blip(91), new Blip(92), new Blip(93) };
                s.Game.Native.Queries[Hash.GET_BLIP_SPRITE] = a => (int)a[0] == 90 ? 71 : (int)a[0] == 91 ? 6 : (int)a[0] == 92 ? 7 : 293;
                s.Game.Native.Queries[Hash.GET_BLIP_COLOUR] = a => (int)a[0] == 90 ? 0 : 43;
                s.Game.Native.Returns(Hash.DOES_BLIP_HAVE_GPS_ROUTE, false);
                s.Game.Native.Returns(Hash.GET_GPS_BLIP_ROUTE_FOUND, false);
                var navigation = Object(Sections(Read(s, ("sections", new[] { "navigation" }), ("blipLimit", 1)))["navigation"]);
                Assert.AreEqual(293, Rows(navigation["blips"])[0]["sprite"], "The new mission contact must not be hidden behind shops, player or north markers.");
                Assert.AreEqual(4, navigation["totalBlips"]);
                Assert.AreEqual(true, navigation["truncated"]);
                Assert.IsEmpty(Rows(navigation["gpsTargets"]));
            }
        }

        [TestMethod, DataRow(unchecked((int)VehicleHash.Bison), "Bison"), DataRow(123456, null)]
        public void DrivingDecisionIncludesVehicleConditionAndFiltersPedestrianWorkOutOfTrafficScan(int model, string modelName)
        {
            using (var s = new AgentScene())
            {
                Traffic(s); Route(s);
                s.Game.Entity(20).ModelHash = model;
                s.Game.Native.Returns(Hash.GET_VEHICLE_PED_IS_IN, 20);
                s.Game.Entity(10).InVehicle = true;
                s.Game.Native.Returns(Hash.GET_ENTITY_SPEED, 14f);
                s.Game.Native.Returns(Hash.GET_VEHICLE_ENGINE_HEALTH, 347f);
                s.Game.Native.Returns(Hash.GET_VEHICLE_BODY_HEALTH, 890f);
                s.Game.Native.Returns(Hash.IS_VEHICLE_DRIVEABLE, true);
                s.Game.Native.Returns(Hash.IS_VEHICLE_ON_ALL_WHEELS, true);
                s.Game.Native.Returns(Hash.IS_ENTITY_UPSIDEDOWN, false);
                s.Game.Native.Returns(Hash.GET_IS_VEHICLE_ENGINE_RUNNING, true);
                s.Game.Native.Returns(Hash.GET_PED_IN_VEHICLE_SEAT, 10);
                s.Game.Native.Returns(Hash.GET_VEHICLE_NUMBER_OF_PASSENGERS, 1);
                s.Game.Native.Returns(Hash.GET_VEHICLE_MAX_NUMBER_OF_PASSENGERS, 3);
                s.Game.World.RaycastObservation = (from, to, flags, ignored) => new RayHit(false, 0, Vector3.Zero);
                var state = Read(s, ("profile", "driving"), ("types", new[] { "vehicle" }), ("radius", 35));
                var vehicle = Object(Sections(state)["vehicle"]);
                Assert.AreEqual(model, Object(vehicle["entity"])["model"]);
                Assert.AreEqual(modelName, Object(vehicle["entity"])["modelName"], "Known vehicle names aid selection; custom hashes must remain unknown.");
                Assert.AreEqual("Franklin", Object(Object(Sections(state)["player"])["entity"])["modelName"]);
                Assert.AreEqual(347, vehicle["engineHealth"]); Assert.AreEqual(14, vehicle["speed"]);
                Assert.AreEqual(1, vehicle["passengerCount"]);
                var nearby = Object(Sections(state)["nearby"]);
                Assert.AreEqual(1, nearby["total"]); Assert.AreEqual(0, Object(nearby["counts"])["peds"]);
                Assert.AreEqual(20, Object(Rows(nearby["nodes"])[0]["entity"])["handle"]);
                Assert.AreEqual(modelName, Object(Rows(nearby["nodes"])[0]["entity"])["modelName"]);
                Assert.IsFalse(s.Game.Native.Calls.Any(c => c.Hash == Hash.GET_RELATIONSHIP_BETWEEN_PEDS || c.Hash == Hash.HAS_ENTITY_CLEAR_LOS_TO_ENTITY));
                Assert.AreEqual(5, Object(state["cost"])["raycasts"]);
                Assert.AreEqual(20, Object(s.Request("observe")["result"])["vehicle"]);
                s.Game.Entity(10).InVehicle = false;
                Assert.AreEqual(0, Object(s.Request("observe")["result"])["vehicle"], "Leaving the car must also clear occupancy in the compact observation.");
            }
        }

        [TestMethod]
        public void CombatDecisionReturnsAmmoTargetHostilesAndBoundedVisibilityWithExplicitTruncation()
        {
            using (var s = new AgentScene())
            {
                Arm(s); Traffic(s);
                s.Game.Outputs.AimValid = true; s.Game.Outputs.AimEntity = 30;
                s.Game.Native.Queries[Hash.HAS_ENTITY_CLEAR_LOS_TO_ENTITY] = a => (int)a[1] == 30;
                var state = Read(s, ("profile", "combat"), ("lineOfSight", "on"), ("limit", 2));
                var weapons = Object(Sections(state)["weapons"]);
                Assert.AreEqual(42, weapons["totalAmmo"]); Assert.AreEqual(7, weapons["ammoInClip"]); Assert.AreEqual(12, weapons["clipCapacity"]);
                Assert.AreEqual(30, Object(weapons["aimTarget"])["handle"]);
                Assert.IsNull(Object(weapons["aimTarget"])["modelName"], "An untyped aim target must not be mislabeled as a pedestrian.");
                var nearby = Object(Sections(state)["nearby"]);
                Assert.AreEqual(3, nearby["total"]); Assert.AreEqual(true, nearby["truncated"]);
                foreach (string key in new[] { "peds", "vehicles", "objects" }) Assert.AreEqual(1, Object(nearby["counts"])[key]);
                var nodes = Rows(nearby["nodes"]);
                Assert.AreEqual("MexGoon01GMY", Object(nodes[0]["entity"])["modelName"]);
                Assert.AreEqual(true, Object(nodes[0]["ped"])["inCombatWithPlayer"]);
                Assert.AreEqual("MicroSMG", Object(nodes[0]["ped"])["weapon"]);
                Assert.AreEqual(true, nodes[0]["lineOfSight"]); Assert.AreEqual(false, nodes[1]["lineOfSight"]);
                Assert.AreEqual(2, Object(state["cost"])["lineOfSightTests"]);
                Assert.AreEqual(0, Object(state["cost"])["inventoryChecks"]); Assert.AreEqual(0, Object(state["cost"])["raycasts"]);
                Assert.IsTrue(Sections(state).ContainsKey("camera"));
            }
        }

        [TestMethod]
        public void ChaseDecisionDistinguishesAnAnimalFromAHumanRiderWithTheSameHostileRelationship()
        {
            using (var s = new AgentScene())
            {
                Traffic(s);
                s.Game.AddEntity(50).Position = new Vector3(3, 2, 3);
                s.Game.World.Nearby = (position, radius) => new[] { 30, 50 };
                s.Game.Native.Returns(Hash.GET_ENTITY_TYPE, 1);
                s.Game.Native.Queries[Hash.IS_PED_HUMAN] = a => (int)a[0] == 30;
                s.Game.Entity(30).InVehicle = true;
                s.Game.Native.Returns(Hash.GET_VEHICLE_PED_IS_IN, 20);
                var nearby = Object(Sections(Read(s, ("sections", new[] { "nearby" }), ("types", new[] { "ped" })))["nearby"]);
                var nodes = Rows(nearby["nodes"]);
                var animal = Object(nodes.Single(n => (int)Object(n["entity"])["handle"] == 50)["ped"]);
                var rider = Object(nodes.Single(n => (int)Object(n["entity"])["handle"] == 30)["ped"]);
                Assert.AreEqual(5, animal["relationship"]); Assert.AreEqual(5, rider["relationship"]);
                Assert.AreEqual(false, animal["human"], "Relationship alone must not identify a mission enemy.");
                Assert.AreEqual(0, animal["vehicle"]);
                Assert.AreEqual(true, rider["human"]); Assert.AreEqual(20, rider["vehicle"]);
                Assert.AreEqual(true, rider["inVehicle"]);
            }
        }

        [TestMethod]
        [DataRow(false)] [DataRow(true)]
        public void InventoryIsOptInAndInvalidAimOrClipObservationsRemainUnknown(bool vanishedTarget)
        {
            using (var s = new AgentScene())
            {
                Arm(s);
                s.Game.AddEntity(99).Exists = false;
                s.Game.Outputs.ClipValid = false; s.Game.Outputs.AimValid = vanishedTarget; s.Game.Outputs.AimEntity = 99;
                var state = Read(s, ("sections", new[] { "weapons", "inventory" }));
                var weapons = Object(Sections(state)["weapons"]);
                Assert.IsNull(weapons["ammoInClip"]); Assert.IsNull(weapons["aimTarget"]);
                var inventory = Rows(Object(Sections(state)["inventory"])["weapons"]);
                Assert.HasCount(1, inventory); Assert.AreEqual("Pistol", inventory[0]["name"]); Assert.AreEqual(42, inventory[0]["totalAmmo"]);
                Assert.IsGreaterThan(1, (int)Object(state["cost"])["inventoryChecks"]);
            }
        }

        [TestMethod]
        [DataRow(0)] [DataRow(10)]
        public void LoadingOrCharacterReplacementReturnsUnavailableSectionsWithoutQueryingInvalidEntities(int player)
        {
            using (var s = new AgentScene())
            {
                s.Game.Host.PlayerPed = player; s.Game.Entity(10).Exists = false;
                var state = Read(s, ("profile", "full"));
                Assert.AreEqual(false, state["playerAvailable"]);
                Assert.IsTrue(Sections(state).Values.All(value => value == null));
                Assert.IsTrue(Object(state["cost"]).Values.All(value => (int)value == 0));
            }
        }

        [TestMethod]
        [DataRow("{\"profile\":\"unknown\"}", "profile")]
        [DataRow("{\"sections\":[\"unknown\"]}", "section")]
        [DataRow("{\"sections\":[12]}", "strings")]
        [DataRow("{\"sections\":[\" \" ]}", "strings")]
        [DataRow("{\"types\":[\"spaceship\"]}", "entity type")]
        [DataRow("{\"lineOfSight\":\"sometimes\"}", "on or off")]
        [DataRow("{\"rays\":[{\"yaw\":181}]}", "through")]
        [DataRow("{\"rays\":[{\"misspelled\":1}]}", "Unknown argument")]
        public void InvalidStateFiltersRejectTheEntireTurnBeforeAnEarlierAction(string json, string message)
        {
            using (var s = new AgentScene())
            {
                var args = Protocol.Decode(json); args["op"] = "state";
                var reply = s.Batch(Step("pause"), args);
                Assert.AreEqual(false, reply["ok"]); StringAssert.Contains((string)reply["error"], message);
                s.Frame(); Assert.IsFalse(s.Game.Native.Calls.Any());
            }
        }
    }
}
