using System;
using System.IO;
using System.Linq;
using GTA;
using GTA.Math;
using GTA.Native;
using GTAVMods.Agent;
using GTAVMods.Tests.Support;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static GTAVMods.Tests.Support.AgentScene;

namespace GTAVMods.Tests.PlayerIntegration.AgentOperations
{
    [TestClass, TestCategory("PlayerIntegration")]
    public class ReactiveControlTests
    {
        [TestMethod]
        public void DriverCanObserveWhileHoldingThrottleThenReplaceTheWholeInputSetWithoutGapsOrStaleSteering()
        {
            using (var s = new AgentScene())
            {
                string token = Guid.NewGuid().ToString("N");
                s.Request("lease", ("action", "acquire"), ("token", token), ("ms", 5000));
                var update = Step("controls.update", ("inputs", new[] {
                    Step("", ("control", "VehicleAccelerate"), ("value", .8)),
                    Step("", ("control", "VehicleMoveLeftRight"), ("value", -.25)) }));
                Assert.AreEqual(false, s.Request("action", ("steps", new[] { update }))["ok"], "Another controller cannot replace held input.");
                Assert.AreEqual(true, s.Request("action", ("token", token), ("steps", new[] { update }))["ok"]);
                s.Frame(0, 100);
                s.Game.Entity(10).Position = new Vector3(7, 8, 9);
                StringAssert.Contains(Protocol.Encode(s.Request("state", ("sections", new[] { "player" }))), "\"x\":7,\"y\":8,\"z\":9");
                s.Frame(0, 100);
                Assert.AreEqual(4, s.Game.Native.Calls.Count(c => c.Hash == Hash.SET_CONTROL_VALUE_NEXT_FRAME));
                StringAssert.Contains(s.Status, "\"inputs\":{\"active\":true,\"count\":2");
                var brake = Step("controls.update", ("ttlMs", 500), ("inputs", new[] { Step("", ("control", "VehicleBrake"), ("value", .6)) }));
                Assert.AreEqual(true, s.Request("action", ("token", token), ("steps", new[] { brake }))["ok"]);
                s.Frame(0, 100);
                CollectionAssert.AreEqual(new object[] { 0, (int)Control.VehicleBrake, .6f }, s.Game.Native.Calls.Last(c => c.Hash == Hash.SET_CONTROL_VALUE_NEXT_FRAME).Arguments);
                Assert.AreEqual(5, s.Game.Native.Calls.Count(c => c.Hash == Hash.SET_CONTROL_VALUE_NEXT_FRAME), "The old throttle/steering set must be replaced, not merged.");
                Assert.AreEqual(true, s.Request("action", ("token", token), ("steps", new[] { Step("controls.release") }))["ok"]);
                s.Frame(0, 100);
                Assert.AreEqual(5, s.Game.Native.Calls.Count(c => c.Hash == Hash.SET_CONTROL_VALUE_NEXT_FRAME));
                StringAssert.Contains(s.Status, "\"paused\":false");
            }
        }

        [TestMethod]
        public void StaleDrivingInputsPauseAtTheirOwnWallDeadlineDespiteHealthyLeaseAndResumeDoesNotReplayThem()
        {
            using (var s = new AgentScene())
            {
                string token = Guid.NewGuid().ToString("N");
                s.Request("lease", ("action", "acquire"), ("token", token), ("ms", 5000));
                var update = Step("controls.update", ("ttlMs", 500), ("inputs", new[] { Step("", ("control", "VehicleAccelerate")) }));
                s.Request("action", ("token", token), ("steps", new[] { update }));
                s.Frame(0, 499);
                s.Request("lease", ("action", "heartbeat"), ("token", token), ("ms", 5000));
                s.Frame(0, 1);
                Assert.AreEqual(1, s.Game.Native.Calls.Count(c => c.Hash == Hash.SET_CONTROL_VALUE_NEXT_FRAME));
                Assert.AreEqual(true, s.Game.Native.Calls.Last(c => c.Hash == Hash.SET_GAME_PAUSED).Arguments[0]);
                Assert.AreEqual(false, s.Request("action", ("token", token), ("steps", new[] { update }))["ok"]);
                s.Request("action", ("token", token), ("steps", new[] { Step("resume") }));
                s.Frame(0, 100);
                Assert.AreEqual(1, s.Game.Native.Calls.Count(c => c.Hash == Hash.SET_CONTROL_VALUE_NEXT_FRAME));
                s.Request("action", ("token", token), ("steps", new[] { update }));
                s.Frame(0, 300);
                s.Request("action", ("token", token), ("steps", new[] { update }));
                s.Frame(0, 300);
                Assert.AreEqual(3, s.Game.Native.Calls.Count(c => c.Hash == Hash.SET_CONTROL_VALUE_NEXT_FRAME));
                StringAssert.Contains(s.Status, "\"paused\":false");
            }
        }

        [TestMethod]
        public void InvalidInputReplacementPreservesHeldThrottleAndATimedBrakeTakesOverWithoutCompetingInputs()
        {
            using (var s = new AgentScene())
            {
                var inputs = new[] { Step("", ("control", "VehicleAccelerate")) };
                s.Request("action", ("steps", new[] { Step("controls.update", ("inputs", inputs)) }));
                Assert.AreEqual(false, s.Batch(Step("pause"), Step("controls.update", ("ttlMs", 0), ("inputs", inputs)))["ok"]);
                Assert.AreEqual(false, s.Request("action", ("steps", new[] { Step("controls.update", ("inputs", new[] { Step("", ("control", "Typo")) })) }))["ok"]);
                s.Frame();
                Assert.AreEqual((int)Control.VehicleAccelerate, s.Game.Native.Calls.Last(c => c.Hash == Hash.SET_CONTROL_VALUE_NEXT_FRAME).Arguments[1]);
                StringAssert.Contains(s.Status, "\"paused\":false");
                s.Request("action", ("steps", new[] { Step("controls", ("ms", 200), ("inputs", new[] { Step("", ("control", "VehicleBrake")) })) }));
                s.Frame(); s.Frame(100); s.Frame(100);
                Assert.AreEqual(1, s.Game.Native.Calls.Count(c => c.Hash == Hash.SET_CONTROL_VALUE_NEXT_FRAME && (int)c.Arguments[1] == (int)Control.VehicleAccelerate));
                Assert.AreEqual(2, s.Game.Native.Calls.Count(c => c.Hash == Hash.SET_CONTROL_VALUE_NEXT_FRAME && (int)c.Arguments[1] == (int)Control.VehicleBrake));
                StringAssert.Contains(s.Status, "\"inputs\":{\"active\":false,\"count\":0");
            }
        }

        [TestMethod]
        public void LostPythonHeartbeatCancelsInputPausesThePlayerAndRejectsLateActionsUntilAnExplicitNewLease()
        {
            using (var s = new AgentScene())
            {
                string token = Guid.NewGuid().ToString("N"), other = Guid.NewGuid().ToString("N");
                Assert.AreEqual(true, s.Request("lease", ("action", "acquire"), ("token", token), ("ms", 1000))["ok"]);
                Assert.AreEqual(false, s.Batch(Step("resume"))["ok"]);
                Assert.AreEqual(false, s.Request("lease", ("action", "acquire"), ("token", other))["ok"]);
                Assert.AreEqual(false, s.Request("lease", ("action", "release"), ("token", other))["ok"]);
                Assert.AreEqual(true, s.Request("batch", ("token", token), ("steps", new[] { Step("controls", ("ms", 5000), ("inputs", new[] { Step("", ("control", "MoveUpDown")) })) }))["ok"]);
                s.Frame(); s.Frame(0, 999);
                Assert.AreEqual(true, s.Request("lease", ("action", "heartbeat"), ("token", token), ("ms", 1000))["ok"]);
                s.Frame(0, 999); StringAssert.Contains(s.Status, "\"state\":\"running\"");
                s.Frame(0, 1);
                StringAssert.Contains(s.Status, "\"state\":\"cancelled\""); StringAssert.Contains(s.Status, "\"paused\":true");
                int inputs = s.Game.Native.Calls.Count(c => c.Hash == Hash.SET_CONTROL_VALUE_NEXT_FRAME);
                s.Frame(0, 1000); Assert.AreEqual(inputs, s.Game.Native.Calls.Count(c => c.Hash == Hash.SET_CONTROL_VALUE_NEXT_FRAME));
                Assert.AreEqual(false, s.Request("lease", ("action", "heartbeat"), ("token", token))["ok"]);
                Assert.AreEqual(false, s.Request("batch", ("token", token), ("steps", new[] { Step("resume") }))["ok"]);
                Assert.AreEqual(true, s.Request("state", ("token", token))["ok"], "Inspection remains available after expiry.");
                Assert.AreEqual(true, s.Request("lease", ("action", "acquire"), ("token", other))["ok"]);
                Assert.AreEqual(false, s.Request("lease", ("action", "release"), ("token", token))["ok"]);
                Assert.AreEqual(true, s.Request("lease", ("action", "release"), ("token", other))["ok"]);
                StringAssert.Contains(s.Status, "\"paused\":true");
                Assert.AreEqual(true, s.Batch(Step("resume"))["ok"]); s.Frame();
                StringAssert.Contains(s.Status, "\"paused\":false");
            }
        }

        [TestMethod]
        public void InvalidLeaseRequestsCannotClaimControlAndAnExpiredOwnerCanReleaseWithoutResuming()
        {
            using (var s = new AgentScene())
            {
                string token = Guid.NewGuid().ToString("N");
                Assert.AreEqual(false, s.Request("lease", ("action", "acquire"), ("token", "bad"))["ok"]);
                Assert.AreEqual(false, s.Request("lease", ("action", "misspelled"), ("token", token))["ok"]);
                Assert.AreEqual(false, s.Request("lease", ("action", "heartbeat"), ("token", token))["ok"]);
                s.Request("lease", ("action", "acquire"), ("token", token), ("ms", 1000));
                s.Frame(0, 1000);
                Assert.AreEqual(true, s.Request("lease", ("action", "release"), ("token", token))["ok"]);
                StringAssert.Contains(s.Status, "\"paused\":true");
            }
        }

        [TestMethod]
        public void AimTracksObservedHeadFromCoverStopsShootingBehindObstaclesAndEndsWhenTheTargetDies()
        {
            using (var s = new AgentScene())
            {
                s.Game.Native.Returns(Hash.GET_PED_BONE_COORDS, new Vector3(-10, 0, 10));
                s.Game.Native.Returns(Hash.GET_GAMEPLAY_CAM_RELATIVE_HEADING, 12f);
                s.Game.Native.Returns(Hash.GET_GAMEPLAY_CAM_RELATIVE_PITCH, -3f);
                s.Game.Native.Returns(Hash.HAS_ENTITY_CLEAR_LOS_TO_ENTITY, true);
                s.Game.Cameras.GameplayRotation = new Vector3(5, 0, 30);
                Assert.AreEqual(true, s.Batch(Step("aim", ("entity", 20), ("shoot", true), ("ms", 500)))["ok"]);
                s.Frame();
                var heading = s.Game.Native.Calls.Last(c => c.Hash == Hash.SET_GAMEPLAY_CAM_RELATIVE_HEADING);
                Assert.AreEqual(72f, heading.Arguments[0]);
                Assert.AreEqual(37f, s.Game.Native.Calls.Last(c => c.Hash == Hash.SET_GAMEPLAY_CAM_RELATIVE_PITCH).Arguments[0]);
                CollectionAssert.AreEqual(new object[] { 20, 31086, 0f, 0f, 0f }, s.Game.Native.Calls.Last(c => c.Hash == Hash.GET_PED_BONE_COORDS).Arguments);
                Assert.AreEqual(1, s.Game.Native.Calls.Count(c => c.Hash == Hash.SET_CONTROL_VALUE_NEXT_FRAME && (int)c.Arguments[1] == (int)Control.Attack));
                s.Game.Native.Returns(Hash.HAS_ENTITY_CLEAR_LOS_TO_ENTITY, false); s.Frame(200);
                Assert.AreEqual(1, s.Game.Native.Calls.Count(c => c.Hash == Hash.SET_CONTROL_VALUE_NEXT_FRAME && (int)c.Arguments[1] == (int)Control.Attack));
                s.Game.Entity(20).Dead = true; s.Frame(100);
                StringAssert.Contains(s.Status, "completed"); StringAssert.Contains(Protocol.Encode(s.Results.Single()), "\"targetAvailable\":false");
            }
        }

        [TestMethod]
        public void VehicleFireHoldsAimThroughPreparationTracksRaisedTargetAndKeepsTheFireGate()
        {
            using (var s = new AgentScene())
            {
                s.Game.Entity(10).InVehicle = true;
                s.Game.Entity(20).Position = new Vector3(0, 10, 0);
                s.Game.Native.Returns(Hash.GET_ENTITY_TYPE, 2);
                s.Game.Native.Returns(Hash.GET_GAMEPLAY_CAM_RELATIVE_HEADING, 0f);
                s.Game.Native.Returns(Hash.GET_GAMEPLAY_CAM_RELATIVE_PITCH, 0f);
                s.Game.Native.Returns(Hash.HAS_ENTITY_CLEAR_LOS_TO_ENTITY, true);
                s.Game.Outputs.AimValid = true; s.Game.Outputs.AimEntity = 20;
                s.Request("action", ("steps", new[] { Step("controls.update", ("ttlMs", 1000), ("inputs", new[] {
                    Step("", ("control", "VehicleAccelerate"), ("value", .8)),
                    Step("", ("control", "VehicleMoveLeftRight"), ("value", -.4)) })) }));
                Assert.AreEqual(true, s.Batch(Step("aim", ("entity", 20), ("height", 10), ("warmupMs", 100),
                    ("ms", 400), ("shoot", true), ("requireAimTarget", true), ("keepInputs", true)))["ok"]);
                s.Frame(); s.Frame(99);
                Assert.AreEqual(2, s.Game.Native.Calls.Count(c => c.Hash == Hash.SET_CONTROL_VALUE_NEXT_FRAME && (int)c.Arguments[1] == (int)Control.VehicleAim));
                Assert.IsFalse(s.Game.Native.Calls.Any(c => c.Hash == Hash.SET_CONTROL_VALUE_NEXT_FRAME && ((int)c.Arguments[1] == (int)Control.Attack || (int)c.Arguments[1] == (int)Control.VehicleAttack)));
                Assert.AreEqual(45f, s.Game.Native.Calls.Last(c => c.Hash == Hash.SET_GAMEPLAY_CAM_RELATIVE_PITCH).Arguments[0]);
                Assert.IsFalse(s.Game.Native.Calls.Any(c => c.Hash == Hash.GET_PED_BONE_COORDS), "A vehicle must not be queried as a ped skeleton.");
                s.Frame(1);
                Assert.AreEqual(1, s.Game.Native.Calls.Count(c => c.Hash == Hash.SET_CONTROL_VALUE_NEXT_FRAME && (int)c.Arguments[1] == (int)Control.VehicleAttack));
                s.Game.Outputs.AimEntity = 30; s.Frame(50);
                Assert.AreEqual(1, s.Game.Native.Calls.Count(c => c.Hash == Hash.SET_CONTROL_VALUE_NEXT_FRAME && (int)c.Arguments[1] == (int)Control.VehicleAttack));
                s.Frame(250);
                var result = (System.Collections.Generic.Dictionary<string, object>)s.Results.Single()["result"];
                Assert.AreEqual(1, result["attackRequestedFrames"]);
                Assert.AreEqual(100, result["warmupMs"]);
                Assert.AreEqual(5, s.Game.Native.Calls.Count(c => c.Hash == Hash.SET_CONTROL_VALUE_NEXT_FRAME && (int)c.Arguments[1] == (int)Control.VehicleAccelerate), "Throttle must remain present on every aiming frame, including completion.");
                Assert.AreEqual(5, s.Game.Native.Calls.Count(c => c.Hash == Hash.SET_CONTROL_VALUE_NEXT_FRAME && (int)c.Arguments[1] == (int)Control.VehicleMoveLeftRight));
                int aimFrames = s.Game.Native.Calls.Count(c => c.Hash == Hash.SET_CONTROL_VALUE_NEXT_FRAME && (int)c.Arguments[1] == (int)Control.VehicleAim);
                s.Frame(100);
                Assert.AreEqual(6, s.Game.Native.Calls.Count(c => c.Hash == Hash.SET_CONTROL_VALUE_NEXT_FRAME && (int)c.Arguments[1] == (int)Control.VehicleAccelerate), "The policy still owns driving after the burst returns.");
                Assert.AreEqual(aimFrames, s.Game.Native.Calls.Count(c => c.Hash == Hash.SET_CONTROL_VALUE_NEXT_FRAME && (int)c.Arguments[1] == (int)Control.VehicleAim));
                s.Request("action", ("steps", new[] { Step("controls.release") }));
                int inputs = s.Game.Native.Calls.Count(c => c.Hash == Hash.SET_CONTROL_VALUE_NEXT_FRAME);
                s.Frame(100);
                Assert.AreEqual(inputs, s.Game.Native.Calls.Count(c => c.Hash == Hash.SET_CONTROL_VALUE_NEXT_FRAME));
            }
        }

        [TestMethod]
        public void HeldInputExpiryStopsAnOngoingAimBurstWithoutWaitingForItsGameTimeDeadline()
        {
            using (var s = new AgentScene())
            {
                s.Game.Entity(10).InVehicle = true;
                s.Game.Native.Returns(Hash.GET_PED_BONE_COORDS, Vector3.UnitY);
                s.Game.Native.Returns(Hash.GET_GAMEPLAY_CAM_RELATIVE_HEADING, 0f);
                s.Game.Native.Returns(Hash.GET_GAMEPLAY_CAM_RELATIVE_PITCH, 0f);
                s.Game.Native.Returns(Hash.HAS_ENTITY_CLEAR_LOS_TO_ENTITY, true);
                s.Game.Outputs.AimValid = true; s.Game.Outputs.AimEntity = 20;
                s.Request("action", ("steps", new[] { Step("controls.update", ("ttlMs", 100), ("inputs", new[] { Step("", ("control", "VehicleAccelerate")) })) }));
                s.Batch(Step("aim", ("entity", 20), ("ms", 2000), ("keepInputs", true), ("shoot", true), ("requireAimTarget", true)));
                s.Frame(); s.Frame(50, 100);
                StringAssert.Contains(s.Status, "\"paused\":true");
                int inputs = s.Game.Native.Calls.Count(c => c.Hash == Hash.SET_CONTROL_VALUE_NEXT_FRAME);
                s.Frame(0, 1);
                StringAssert.Contains(s.Status, "\"state\":\"failed\"");
                Assert.AreEqual(inputs, s.Game.Native.Calls.Count(c => c.Hash == Hash.SET_CONTROL_VALUE_NEXT_FRAME), "Expired driving input must also stop aiming/fire while simulation is paused.");
                StringAssert.Contains(s.Status, "Resume before advancing simulation time");
            }
        }

        [TestMethod]
        public void AimWithoutFireExpiresOrCancelsWithoutLeavingHeldControlsAndRejectsInvalidTargets()
        {
            using (var s = new AgentScene())
            {
                s.Game.Native.Returns(Hash.GET_PED_BONE_COORDS, Vector3.UnitY);
                s.Game.Native.Returns(Hash.GET_GAMEPLAY_CAM_RELATIVE_HEADING, 0f); s.Game.Native.Returns(Hash.GET_GAMEPLAY_CAM_RELATIVE_PITCH, 0f);
                s.Game.Native.Returns(Hash.HAS_ENTITY_CLEAR_LOS_TO_ENTITY, true);
                Assert.AreEqual(false, s.Batch(Step("aim", ("entity", 20), ("keepInputs", "yes")))["ok"]);
                Assert.AreEqual(false, s.Batch(Step("aim", ("entity", 20), ("shoot", "yes")))["ok"]);
                Assert.AreEqual(false, s.Batch(Step("aim", ("entity", 20), ("requireAimTarget", "yes")))["ok"]);
                Assert.AreEqual(false, s.Batch(Step("aim", ("entity", 20), ("ms", 10), ("warmupMs", 11)))["ok"]);
                Assert.AreEqual(false, s.Batch(Step("aim", ("entity", 20), ("height", 21)))["ok"]);
                Assert.AreEqual(false, s.Batch(Step("aim"))["ok"]);
                s.Batch(Step("aim", ("entity", 20), ("ms", 10))); s.Frame(); s.Frame(10);
                StringAssert.Contains(s.Status, "completed");
                Assert.IsFalse(s.Game.Native.Calls.Any(c => c.Hash == Hash.SET_CONTROL_VALUE_NEXT_FRAME && (int)c.Arguments[1] == (int)Control.Attack));
                s.Batch(Step("aim", ("entity", 20))); s.Frame(); s.Request("cancel");
                int inputs = s.Game.Native.Calls.Count(c => c.Hash == Hash.SET_CONTROL_VALUE_NEXT_FRAME); s.Frame(100);
                Assert.AreEqual(inputs, s.Game.Native.Calls.Count(c => c.Hash == Hash.SET_CONTROL_VALUE_NEXT_FRAME));
                s.Game.Entity(20).Exists = false;
                s.Batch(Step("aim", ("entity", 20))); s.Frame();
                StringAssert.Contains(s.Status, "completed");
            }
        }

        [TestMethod]
        public void GatedFireWaitsForTheObservedTargetAndReportsObstructionsUnknownAimAndRequestedControls()
        {
            using (var s = new AgentScene())
            {
                s.Game.Native.Returns(Hash.GET_PED_BONE_COORDS, Vector3.UnitY);
                s.Game.Native.Returns(Hash.GET_GAMEPLAY_CAM_RELATIVE_HEADING, 0f);
                s.Game.Native.Returns(Hash.GET_GAMEPLAY_CAM_RELATIVE_PITCH, 0f);
                s.Game.Native.Returns(Hash.HAS_ENTITY_CLEAR_LOS_TO_ENTITY, true);
                s.Game.Outputs.AimValid = true; s.Game.Outputs.AimEntity = 30; // A vehicle obstructs the aim.
                Assert.AreEqual(true, s.Batch(Step("aim", ("entity", 20), ("shoot", true), ("requireAimTarget", true), ("ms", 500)))["ok"]);
                s.Frame();
                Assert.IsFalse(s.Game.Native.Calls.Any(c => c.Hash == Hash.SET_CONTROL_VALUE_NEXT_FRAME && (int)c.Arguments[1] == (int)Control.Attack));
                s.Game.Outputs.AimValid = false; s.Frame(100); // Unknown is not a clear shot.
                s.Game.Outputs.AimValid = true; s.Game.Entity(30).Exists = false; s.Frame(100); // Stale aim handle.
                Assert.IsFalse(s.Game.Native.Calls.Any(c => c.Hash == Hash.SET_CONTROL_VALUE_NEXT_FRAME && (int)c.Arguments[1] == (int)Control.Attack));
                s.Game.Outputs.AimEntity = 20; s.Frame(100);
                CollectionAssert.AreEqual(new object[] { 0, (int)Control.Attack, 1f }, s.Game.Native.Calls.Single(c => c.Hash == Hash.SET_CONTROL_VALUE_NEXT_FRAME && (int)c.Arguments[1] == (int)Control.Attack).Arguments);
                s.Game.Native.Returns(Hash.HAS_ENTITY_CLEAR_LOS_TO_ENTITY, false); s.Frame(50); // Matching aim does not override LOS.
                s.Frame(150);
                var result = (System.Collections.Generic.Dictionary<string, object>)s.Results.Single()["result"];
                Assert.AreEqual(5, result["frames"]);
                Assert.AreEqual(4, result["lineOfSightFrames"]);
                Assert.AreEqual(2, result["aimTargetMatchFrames"]);
                Assert.AreEqual(2, result["unknownAimTargetFrames"]);
                Assert.AreEqual(1, result["attackRequestedFrames"]);
                StringAssert.Contains(Protocol.Encode(result["last"]), "\"lineOfSight\":false,\"aimTarget\":20,\"attackRequested\":false");
                int inputs = s.Game.Native.Calls.Count(c => c.Hash == Hash.SET_CONTROL_VALUE_NEXT_FRAME);
                s.Frame(100);
                Assert.AreEqual(inputs, s.Game.Native.Calls.Count(c => c.Hash == Hash.SET_CONTROL_VALUE_NEXT_FRAME), "No controls remain held after duration completion.");
            }
        }

        [TestMethod]
        public void GenericNativeTaskPreservesFloatArgumentsAndCommandSubmissionDoesNotInventMovement()
        {
            using (var s = new AgentScene())
            {
                var floats = new[] { 11, 12, 13, 2 };
                object[] args = new object[] { 10 }.Concat(floats.Select(v => (object)Step("", ("type", "float"), ("value", v)))).Concat(new object[] { -1, 0.2, false, 0f }).ToArray();
                Assert.AreEqual(true, s.Batch(Step("native", ("name", "TASK_FOLLOW_NAV_MESH_TO_COORD"), ("args", args)), Step("state"))["ok"]);
                s.Frame(); s.Frame();
                CollectionAssert.AreEqual(new object[] { 10, 11f, 12f, 13f, 2f, -1, .2f, false, 0 }, s.Game.Native.Calls.Single(c => c.Hash == Hash.TASK_FOLLOW_NAV_MESH_TO_COORD).Arguments);
                StringAssert.Contains(Protocol.Encode(s.Results.Last()), "\"position\":{\"x\":1,\"y\":2,\"z\":3}");
            }
        }

        [TestMethod]
        [DataRow("bool", "GET_MISSION_FLAG")]
        [DataRow("int", "GET_ENTITY_TYPE")]
        [DataRow("uint", "GET_HASH_KEY")]
        [DataRow("float", "GET_ENTITY_SPEED")]
        [DataRow("string", "GET_NAME_OF_ZONE")]
        [DataRow("vector3", "GET_ENTITY_COORDS")]
        public void AgentCanQueryAdditionalTypedGameStateWithoutCompilingCode(string result, string name)
        {
            using (var s = new AgentScene())
            {
                var hash = (Hash)Enum.Parse(typeof(Hash), name);
                s.Game.Native.Returns(Hash.GET_HASH_KEY, 0xF1234567u); s.Game.Native.Returns(Hash.GET_ENTITY_SPEED, 4.25f);
                s.Game.Native.Returns(Hash.GET_NAME_OF_ZONE, "VESPU"); s.Game.Native.Returns(Hash.GET_ENTITY_COORDS, new Vector3(11, 22, 33));
                Assert.AreEqual(true, s.Batch(Step("native", ("name", name), ("returns", result), ("args", Array.Empty<object>())))["ok"]);
                s.Frame(); StringAssert.Contains(s.Status, "completed");
                string expected = result == "bool" ? "true" : result == "int" ? "1" : result == "uint" ? "4045620583" : result == "float" ? "4.25" : result == "string" ? "\"VESPU\"" : "{\"x\":11,\"y\":22,\"z\":33}";
                Assert.AreEqual(expected, Protocol.Encode(s.Results.Single()["result"]));
            }
        }

        [TestMethod]
        public void InvalidNativeInvocationCannotRunEarlierBatchActions()
        {
            using (var s = new AgentScene())
            {
                Assert.AreEqual(false, s.Batch(Step("resume"), Step("native", ("name", "WAIT"), ("returns", "pointer"), ("args", Array.Empty<object>())))["ok"]);
                Assert.AreEqual(false, s.Batch(Step("native", ("name", "WAIT")))["ok"]);
                Assert.AreEqual(false, s.Batch(Step("native", ("name", "WAIT"), ("args", 3)))["ok"]);
                Assert.AreEqual(false, s.Batch(Step("native", ("name", "WAIT"), ("args", Enumerable.Repeat(1, 33).ToArray())))["ok"]);
                Assert.IsFalse(s.Game.Native.Calls.Any());
            }
        }
    }
}
