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
using Thor;

namespace GTAVMods.Tests.PlayerIntegration.HammerActions
{
    [TestClass, TestCategory("PlayerIntegration")]
    public class ObstacleNavigationTests
    {
        [TestMethod]
        public void BlockedRecallUsesInvisibleHelpersWithoutHurtingThePlayerOrNearbyBystanders()
        {
            var profile = new GameplayTuning();
            profile.Navigation.HelperHealth = 137;
            profile.Navigation.RetargetDistance = 7;
            profile.Navigation.MissionSpeed = 113;
            profile.Navigation.StoppingRange = 2.3f;
            profile.Navigation.FlightHeight = 17;
            var s = new PlayerSessionFixture(tuning: profile);
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
                Assert.AreEqual(profile.Navigation.HelperHealth, s.Game.Entity(helper).Health);
            }
            CollectionAssert.AreEqual(new object[] { 41, 40, VehicleSeat.Driver },
                s.Game.Calls.Last(call => call.Name == "ped.intoVehicle").Arguments);
            var mission = s.Game.Calls.Last(call => call.Name == "ped.missionPosition");
            CollectionAssert.AreEqual(new object[] { 41, 40, new Vector3(0, 0, 1), VehicleMissionType.GoTo,
                profile.Navigation.MissionSpeed, profile.Navigation.StoppingRange, profile.Navigation.Orientation,
                profile.Navigation.FlightHeight, profile.Navigation.MinTerrainHeight, profile.Navigation.SlowDownDistance,
                HeliMissionFlags.StartEngineImmediately }, mission.Arguments);
            Assert.AreEqual(new Vector3(2, 4, 6), s.Game.Entity(20).Velocity,
                "Recall uses the observed guide velocity before this frame's guide acceleration request.");
            Assert.IsTrue(s.Game.Calls.Any(call => call.Name == "entity.noCollision" &&
                (int)call.Arguments[0] == 40 && (int)call.Arguments[1] == 20));
            Assert.AreEqual(new Vector3(90, 80, 70), s.Game.Entity(99).Position);
            int missions = s.Game.Calls.Count(call => call.Name == "ped.missionPosition");
            s.Player.Bones[Bone.PHRightHand] = new Vector3(profile.Navigation.RetargetDistance, 0, 1);
            s.Recall();
            Assert.AreEqual(missions, s.Game.Calls.Count(call => call.Name == "ped.missionPosition"));
            s.Player.Bones[Bone.PHRightHand] = new Vector3(profile.Navigation.RetargetDistance + 1, 0, 1);
            s.Recall();
            Assert.AreEqual(missions + 1, s.Game.Calls.Count(call => call.Name == "ped.missionPosition"),
                "Moving beyond the configured route threshold must update the route during the same held recall.");
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
            var profile = new GameplayTuning();
            profile.Hammer.CruiseSpeed = 81;
            profile.Hammer.OpenAirProbeRadius = 7;
            profile.Hammer.ObstacleProbeRadius = 0.17f;
            profile.Navigation.PositionCorrection = 0.25f;
            var s = new PlayerSessionFixture(tuning: profile);
            var center = new Vector3(12, -8, 30);
            s.Player.Bones[Bone.PHRightHand] = center + Vector3.UnitY * 30;
            s.DropHammer(center);
            s.Game.World.CapsuleObservation = (from, to, radius, flags) =>
            {
                if (flags == IntersectFlags.Peds) return new RayHit(false, 0, Vector3.Zero);
                if (from == center) return new RayHit(true, 0, center + Vector3.UnitY * 5);
                var axis = to - from;
                return new RayHit(axis.X != 0 ? x : axis.Y != 0 ? y : z, 0, Vector3.Zero);
            };
            s.Frame(0, Keys.H);
            Assert.AreEqual(x || y || z, s.Game.Calls.Any(call => call.Name == "ped.missionPosition"));
            Assert.AreEqual(x || y || z ? Vector3.Zero : Vector3.UnitY * profile.Hammer.CruiseSpeed, s.Game.Entity(20).Velocity);
            var probes = s.Game.Calls.Where(call => call.Name == "world.capsule" &&
                (IntersectFlags)call.Arguments[3] == IntersectFlags.Map && (Vector3)call.Arguments[0] != center).ToArray();
            Assert.HasCount(3, probes);
            for (int i = 0; i < 3; i++)
            {
                var from = (Vector3)probes[i].Arguments[0];
                var to = (Vector3)probes[i].Arguments[1];
                Assert.AreEqual(center, (from + to) / 2);
                Assert.AreEqual(profile.Hammer.OpenAirProbeRadius, from.DistanceTo(center), .001f);
                Assert.AreEqual(profile.Hammer.OpenAirProbeRadius, probes[i].Arguments[2]);
                for (int j = 0; j < i; j++)
                    Assert.AreEqual(0, Vector3.Dot(to - from,
                        (Vector3)probes[j].Arguments[1] - (Vector3)probes[j].Arguments[0]), .001f,
                        "The enclosing probes must span independent perpendicular directions.");
            }
            s.Game.Entity(40).Position = center + new Vector3(4, 2, 0);
            s.Game.Entity(40).Velocity = new Vector3(1, 2, 3);
            s.Frame(0, Keys.H);
            if (x || y || z) Assert.AreEqual(new Vector3(1, 2, 3) + new Vector3(4, 2, 0) * profile.Navigation.PositionCorrection, s.Game.Entity(20).Velocity);
            s.Game.World.CapsuleObservation = (from, to, radius, flags) => new RayHit(false, 0, Vector3.Zero);
            s.Frame(0, Keys.H);
            Assert.AreEqual(Vector3.UnitY * profile.Hammer.CruiseSpeed, s.Game.Entity(20).Velocity);
        }

        [TestMethod]
        [DataRow(1007)]
        [DataRow(int.MaxValue - 100)]
        public void LostTerrainRecoveryStaysBoundedAndRecallResumesDirectedTravel(int startTime)
        {
            var tuning = new Thor.GameplayTuning();
            tuning.Hammer.RecoveryHeightJump = 12;
            tuning.Hammer.IdleIntervalMs = 137;
            tuning.Hammer.IdleIntervalJitterMs = 84;
            tuning.Hammer.IdleMaxSpeed = 12;
            tuning.Hammer.IdleVelocityBlend = 1;
            tuning.Hammer.CruiseSpeed = 17;
            const double initialFraction = .25, nextFraction = .75;
            var random = new RecoveryRandom { Fraction = initialFraction };
            var s = new PlayerSessionFixture(random: random, tuning: tuning);
            s.Game.Host.GameTime = startTime;
            s.DropHammer(Vector3.Zero);
            foreach (float height in new[] { 6f, 12f, 24f })
            {
                s.Game.Entity(20).HeightAboveGround = height;
                s.Frame(17);
                Assert.AreEqual(Vector3.Zero, s.Game.Entity(20).Velocity);
            }
            s.Game.Entity(20).HeightAboveGround = 37;
            s.Frame(0);
            s.Frame(80);
            s.Frame(tuning.Hammer.IdleIntervalMs - 81);
            Assert.AreEqual(Vector3.Zero, s.Game.Entity(20).Velocity, "Recovery waits for its initial interval.");
            s.Frame(4);
            float firstDrift = s.Game.Entity(20).Velocity.Length();
            float firstSpeedBound = (float)initialFraction * tuning.Hammer.IdleMaxSpeed;
            Assert.IsTrue(firstDrift > 0 && firstDrift <= firstSpeedBound,
                "A frame crossing the deadline starts bounded drift even without landing on an exact time multiple.");

            // The later sample would choose a larger drift, exposing an early refresh.
            int jitter = (int)(initialFraction * tuning.Hammer.IdleIntervalJitterMs);
            random.Fraction = nextFraction;
            s.Frame(tuning.Hammer.IdleIntervalMs);
            s.Frame(jitter - 1);
            Assert.IsTrue(s.Game.Entity(20).Velocity.Length() <= firstSpeedBound + .001f,
                "The nonzero jitter must postpone the next direction change beyond the base interval.");
            s.Frame(4);
            float nextDrift = s.Game.Entity(20).Velocity.Length();
            Assert.IsTrue(nextDrift > firstSpeedBound && nextDrift <= nextFraction * tuning.Hammer.IdleMaxSpeed,
                "The first frame after the jittered deadline adopts the next bounded drift.");

            s.Player.Bones[Bone.PHRightHand] = Vector3.UnitY * 70;
            s.Frame(0, Keys.H);
            Assert.AreEqual(Vector3.UnitY * tuning.Hammer.CruiseSpeed, s.Game.Entity(20).Velocity,
                "Recall cancels wandering and immediately travels towards the player's hand.");
            Assert.IsTrue(s.Game.Entity(20).Persistent);
        }

        private sealed class RecoveryRandom : Random
        {
            public double Fraction;
            public override int Next(int maxValue) => 0;
            public override int Next(int minValue, int maxValue) => minValue;
            public override double NextDouble() => Fraction;
        }
    }
}
