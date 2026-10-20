using Control = GTA.Control;
using GTA.Math;
using GTA.Native;
using GTA;
using GTAVMods.Game;
using GTAVMods.Tests.Support;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Linq;
using System.Windows.Forms;
using System;

namespace GTAVMods.Tests.PlayerIntegration.HammerActions
{
    [TestClass, TestCategory("PlayerIntegration")]
    public class ObstacleNavigationTests
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
        [DataRow(false, false, false)]
        [DataRow(true, false, false)]
        [DataRow(false, true, false)]
        [DataRow(false, false, true)]
        public void RecallUsesTheNavigationHelperOnlyWhenTheNearbyObstructionEnclosesTheHammer(bool x, bool y, bool z)
        {
            var s = new PlayerSessionFixture();
            var center = new Vector3(12, -8, 30);
            s.Player.Bones[Bone.PHRightHand] = center + Vector3.UnitY * 30;
            s.DropHammer(center);
            s.Game.World.CapsuleObservation = (from, to, radius, flags) =>
            {
                if (flags == IntersectFlags.Peds) return new RayHit(false, 0, Vector3.Zero);
                if (radius == 0.1f) return new RayHit(true, 0, center + Vector3.UnitY * 20);
                var axis = to - from;
                return new RayHit(axis.X != 0 ? x : axis.Y != 0 ? y : z, 0, Vector3.Zero);
            };
            s.Frame(0, Keys.H);
            Assert.AreEqual(x || y || z, s.Game.Calls.Any(call => call.Name == "ped.missionPosition"));
            Assert.AreEqual(x || y || z ? Vector3.Zero : Vector3.UnitY * 100, s.Game.Entity(20).Velocity);
            var probes = s.Game.Calls.Where(call => call.Name == "world.capsule" && (float)call.Arguments[2] == 10).ToArray();
            Assert.HasCount(3, probes);
            for (int i = 0; i < 3; i++)
            {
                var axis = new[] { Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ }[i];
                CollectionAssert.AreEqual(new object[] { center - axis * 10, center + axis * 10, 10f, IntersectFlags.Map }, probes[i].Arguments);
            }
            s.Game.Entity(40).Position = center + new Vector3(4, 2, 0);
            s.Game.Entity(40).Velocity = new Vector3(1, 2, 3);
            s.Frame(0, Keys.H);
            if (x || y || z) Assert.AreEqual(new Vector3(3, 3, 3), s.Game.Entity(20).Velocity);
            s.Game.World.CapsuleObservation = (from, to, radius, flags) => new RayHit(false, 0, Vector3.Zero);
            s.Frame(0, Keys.H);
            Assert.AreEqual(Vector3.UnitY * 100, s.Game.Entity(20).Velocity);
        }

        [TestMethod]
        public void LostTerrainRecoveryStaysBoundedAndRecallResumesDirectedTravel()
        {
            var s = new PlayerSessionFixture(random: new FixedRandom(0, .25));
            s.DropHammer(Vector3.Zero);
            foreach (float height in new[] { 25f, 50f, 100f })
            {
                s.Game.Entity(20).HeightAboveGround = height;
                s.Frame(1000);
                Assert.AreEqual(Vector3.Zero, s.Game.Entity(20).Velocity);
            }
            s.Game.Entity(20).HeightAboveGround = 151;
            s.Frame(0);
            s.Frame(1000);
            for (int frame = 0; frame < 200; frame++)
            {
                s.Frame(1);
                Assert.IsTrue(s.Game.Entity(20).Velocity.Length() <= 30.001f);
            }
            Assert.AreEqual(7.5f, s.Game.Entity(20).Velocity.Length(), .001f);
            s.Player.Bones[Bone.PHRightHand] = Vector3.UnitY * 20;
            s.Frame(0, Keys.H);
            Assert.AreEqual(Vector3.UnitY * 100, s.Game.Entity(20).Velocity);
            Assert.IsTrue(s.Game.Entity(20).Persistent);
        }
    }
}
