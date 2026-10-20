using System;
using System.Linq;
using System.Windows.Forms;
using GTA;
using GTA.Math;
using GTAVMods.Game;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ModUtilsTest
{
    [TestClass, TestCategory("PlayerIntegration")]
    public class PlayerRecallNavigationTests
    {
        [TestMethod]
        public void BlockedRecallUsesInvisibleHelpersWithoutHurtingThePlayerOrNearbyBystanders()
        {
            var s = new PlayerSessionFixture();
            s.DropHammer(new Vector3(0, 20, 0));
            s.Game.Entity(40).Velocity = new Vector3(2, 4, 6);
            s.Game.Entity(40).Driver = 0;
            s.Game.World.CapsuleObservation = (from, to, radius, flags) =>
                new RayHit(true, 0, from + Vector3.UnitY);
            s.Recall();
            foreach (int helper in new[] { 40, 41 })
            {
                Assert.IsFalse(s.Game.Entity(helper).Visible);
                Assert.IsTrue(s.Game.Entity(helper).Invincible);
                Assert.AreEqual(100, s.Game.Entity(helper).Health);
            }
            CollectionAssert.AreEqual(new object[] { 41, 40, VehicleSeat.Driver },
                s.Game.Calls.Last(call => call.Name == "ped.intoVehicle").Arguments);
            var mission = s.Game.Calls.Last(call => call.Name == "ped.missionPosition");
            CollectionAssert.AreEqual(new object[] { 41, 40, new Vector3(0, 0, 1), VehicleMissionType.GoTo,
                100000f, 1f, -1, 5, -1, -1f, HeliMissionFlags.StartEngineImmediately }, mission.Arguments);
            Assert.AreEqual(new Vector3(2, 4, 6), s.Game.Entity(20).Velocity,
                "Recall uses the observed guide velocity before this frame's guide acceleration request.");
            Assert.IsTrue(s.Game.Calls.Any(call => call.Name == "entity.noCollision" &&
                (int)call.Arguments[0] == 40 && (int)call.Arguments[1] == 20));
            Assert.AreEqual(new Vector3(90, 80, 70), s.Game.Entity(99).Position);
            int missions = s.Game.Calls.Count(call => call.Name == "ped.missionPosition");
            s.Player.Bones[Bone.PHRightHand] = new Vector3(15, 0, 1);
            s.Recall();
            Assert.AreEqual(missions, s.Game.Calls.Count(call => call.Name == "ped.missionPosition"));
            s.Player.Bones[Bone.PHRightHand] = new Vector3(16, 0, 1);
            s.Recall();
            Assert.AreEqual(missions + 1, s.Game.Calls.Count(call => call.Name == "ped.missionPosition"),
                "Moving beyond fifteen metres must update the route during the same held recall.");
            s.Controller.Abort();
            Assert.IsFalse(s.Game.Observations[40].Exists);
            Assert.IsFalse(s.Game.Observations[41].Exists);
            Assert.IsTrue(s.Game.Entity(99).Exists);
        }

        [TestMethod]
        [DataRow(40)]
        [DataRow(41)]
        public void DespawnedNavigationHelperIsReplacedAndRecallRemainsAvailable(int vanished)
        {
            var s = new PlayerSessionFixture();
            s.DropHammer(new Vector3(0, 20, 0));
            s.Game.Observations[vanished].Exists = false;
            s.Game.AddEntity(42);
            s.Game.AddEntity(43);
            s.Game.World.Vehicles.Enqueue(42);
            s.Game.World.Drivers.Enqueue(43);
            s.Recall();
            Assert.IsFalse(s.Game.Observations[40].Exists);
            Assert.IsFalse(s.Game.Observations[41].Exists);
            Assert.AreEqual(42, s.Log.Last("pathfinder.created").Data["vehicle"]);
            Assert.AreEqual(43, s.Log.Last("pathfinder.created").Data["driver"]);
            Assert.IsTrue(s.Game.Entity(20).Velocity.Length() > 0);
            s.Controller.Abort();
            Assert.IsFalse(s.Game.Observations[42].Exists);
            Assert.IsFalse(s.Game.Observations[43].Exists);
        }

        [TestMethod]
        [DataRow("model")]
        [DataRow("vehicle")]
        [DataRow("driver")]
        public void NavigationAllocationFailurePausesCleanlyAndEnableRetriesTheAbility(string failure)
        {
            var s = new PlayerSessionFixture(configure: game =>
            {
                if (failure == "model") game.World.ModelResults[(int)VehicleHash.Thruster] = false;
                if (failure == "vehicle") { game.World.Vehicles.Clear(); game.World.Vehicles.Enqueue(0); }
                if (failure == "driver") { game.World.Drivers.Clear(); game.World.Drivers.Enqueue(0); }
            });
            s.Frame(0, expectFailure: true);
            Assert.IsTrue(s.Log.Records.Any(record => record.Level == "ERROR"));
            if (failure != "model")
                Assert.IsTrue(s.Game.Calls.Any(call => call.Name == "world.releaseModel"));
            if (failure == "driver") Assert.IsFalse(s.Game.Observations[40].Exists);
            s.Game.World.ModelResults[(int)VehicleHash.Thruster] = true;
            s.Game.World.Vehicles.Clear();
            s.Game.World.Drivers.Clear();
            s.Game.AddEntity(42);
            s.Game.AddEntity(43);
            s.Game.World.Vehicles.Enqueue(42);
            s.Game.World.Drivers.Enqueue(43);
            s.Game.Entities.CreatedBlips.Enqueue(2001);
            int previousFailures = s.Log.Records.Count(record => record.Level == "ERROR");
            s.Chord(Keys.O);
            s.Frame(0, expectFailure: true);
            Assert.IsTrue(s.Log.Records.Any(record => record.Name == "pathfinder.created"),
                string.Join(Environment.NewLine, s.Log.Records.Select(record => record.Name + ": " + string.Join(", ", record.Data.Values))));
            Assert.AreEqual(42, s.Log.Last("pathfinder.created").Data["vehicle"]);
            Assert.AreEqual(previousFailures, s.Log.Records.Count(record => record.Level == "ERROR"),
                "Recovery must not hide a second gameplay error.");
            StringAssert.Contains(s.State(), "attached=True", "The retried ability must be attached to the player again.");
            s.Controller.Abort();
            Assert.IsFalse(s.Game.Observations[42].Exists);
            Assert.IsFalse(s.Game.Observations[43].Exists);
        }

        [TestMethod]
        [DataRow(40)]
        [DataRow(41)]
        [DataRow(0)]
        public void ReloadRetriesFailedHelperDeletionWithoutLosingEitherOwnedHandle(int failed)
        {
            var s = new PlayerSessionFixture();
            s.Frame(0);
            int[] failures = failed == 0 ? new[] { 40, 41 } : new[] { failed };
            s.Game.BeforeCall = call =>
            {
                if (call.Name == "entity.delete" && failures.Contains((int)call.Arguments[0]))
                    throw new InvalidOperationException("helper release unavailable");
            };
            s.Controller.Abort();
            foreach (int handle in new[] { 40, 41 })
                Assert.AreEqual(failures.Contains(handle), s.Game.Observations[handle].Exists);
            Assert.AreEqual("ERROR", s.Log.Last("cleanup.exception").Level);
            Assert.AreEqual(200, s.Player.MaxHealth);
            s.Game.BeforeCall = null;
            s.Controller.Abort();
            Assert.IsFalse(s.Game.Observations[40].Exists);
            Assert.IsFalse(s.Game.Observations[41].Exists);
            int deletions = s.Game.Calls.Count(call => call.Name == "entity.delete");
            s.Controller.Abort();
            Assert.AreEqual(deletions, s.Game.Calls.Count(call => call.Name == "entity.delete"));
        }

        [TestMethod]
        public void FailedDriverCreationAndVehicleRollbackPreserveBothErrorsAndRetryTheOwnedVehicleBeforeAllocating()
        {
            var s = new PlayerSessionFixture(configure: game =>
            {
                game.World.Drivers.Clear();
                game.World.Drivers.Enqueue(0);
                game.BeforeCall = call =>
                {
                    if (call.Name == "entity.delete" && (int)call.Arguments[0] == 40)
                        throw new InvalidOperationException("navigation vehicle release unavailable");
                };
            });
            s.Frame(0, expectFailure: true);
            string diagnostic = (string)s.Log.Last("tick.exception").Data["exception"];
            StringAssert.Contains(diagnostic, "Could not create the pathfinder driver");
            StringAssert.Contains(diagnostic, "navigation vehicle release unavailable");
            Assert.IsTrue(s.Game.Entity(40).Exists);
            Assert.IsTrue(s.Game.Calls.Any(call => call.Name == "world.releaseModel"));
            int errors = s.Log.Records.Count(record => record.Level == "ERROR");

            s.Game.BeforeCall = null;
            s.Game.AddEntity(42);
            s.Game.AddEntity(43);
            s.Game.World.Vehicles.Enqueue(42);
            s.Game.World.Drivers.Enqueue(43);
            s.Game.Entities.CreatedBlips.Enqueue(2001);
            s.Game.Calls.Clear();
            s.Chord(Keys.O);
            s.Frame(0, expectFailure: true);
            Assert.IsFalse(s.Game.Observations[40].Exists);
            Assert.IsTrue(s.Game.Calls.FindIndex(call => call.Name == "entity.delete" && (int)call.Arguments[0] == 40) <
                s.Game.Calls.FindIndex(call => call.Name == "world.createVehicle"),
                "An old owned helper must be retired before its replacement is allocated.");
            Assert.AreEqual(errors, s.Log.Records.Count(record => record.Level == "ERROR"));
            Assert.AreEqual(42, s.Log.Last("pathfinder.created").Data["vehicle"]);
            s.Controller.Abort();
            Assert.IsFalse(s.Game.Observations[42].Exists);
            Assert.IsFalse(s.Game.Observations[43].Exists);
        }
    }
}
