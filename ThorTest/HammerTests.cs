using System;
using System.Collections.Generic;
using System.Linq;
using GTA;
using GTA.Math;
using GTA.Native;
using GTAVMods.Game;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ModUtilsTest
{
    [TestClass]
    public class HammerTests
    {
        [TestMethod]
        public void CreationConfiguresPhysicsAndReplacementReleasesOnlyOwnedHammer()
        {
            var f = new HammerFixture();
            Assert.AreEqual(Vector3.Zero, f.Hammer.Velocity);
            Assert.AreEqual(Vector3.Zero, f.Hammer.Position);
            Assert.IsFalse(f.Hammer.IsMoving);
            f.Hammer.Velocity = Vector3.UnitY;
            f.WeaponCreations.Enqueue(20); f.Game.Entities.CreatedBlips.Enqueue(50);
            f.Hammer.Init(null);
            Assert.AreEqual(20, f.Hammer.WeaponObject);
            var creation = f.LastNative(Hash.CREATE_WEAPON_OBJECT);
            Assert.AreEqual(2000f, creation[4]);
            Assert.AreEqual(50000f, f.LastNative(Hash.SET_OBJECT_PHYSICS_PARAMS)[1]);
            Assert.IsTrue(f.Game.Entity(20).CollisionEnabled);
            f.Hammer.Init(Vector3.UnitX);
            Assert.HasCount(1, f.Game.Native.Calls.Where(call => call.Hash == Hash.CREATE_WEAPON_OBJECT).ToArray());
            f.Hammer.WeaponObject = 20;
            Assert.IsFalse(f.Game.Calls.Any(call => call.Name == "entity.delete"));
            f.Game.AddEntity(30);
            f.WeaponCreations.Enqueue(30); f.Game.Entities.CreatedBlips.Enqueue(51);
            f.Hammer.Init(new Vector3(1, 2, 3), true);
            Assert.IsFalse(f.Game.Observations[20].Exists);
            Assert.AreEqual(30, f.Hammer.WeaponObject);
            f.Hammer.Dispose(); f.Hammer.Dispose();
            Assert.IsFalse(f.Game.Observations[30].Exists);
            Assert.AreEqual(0, f.Hammer.WeaponObject);
        }

        [TestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public void FailedWeaponCreationNotifiesAndPropagatesWithoutTakingOwnership(bool loaded)
        {
            var f = new HammerFixture();
            f.Game.Weapons.AssetResults[WeaponHash.Hammer] = loaded;
            if (loaded) f.WeaponCreations.Enqueue(0);
            var error = Assert.Throws<InvalidOperationException>(() => f.Hammer.Init(null));
            Assert.AreEqual(loaded ? "The hammer weapon asset could not be created." : "Could not load weapon asset Hammer", error.Message);
            Assert.AreEqual(0, f.Hammer.WeaponObject);
            Assert.HasCount(1, f.Game.Calls.Where(call => call.Name == "host.notify").ToArray());
        }

        [TestMethod]
        [DataRow(3f, 35f)]
        [DataRow(3.01f, 40f)]
        [DataRow(10f, 40f)]
        [DataRow(10.01f, 100f)]
        public void RecallSlowsOnlyWithinTheTwoCatchDistanceBands(float distance, float expectedSpeed)
        {
            var f = new HammerFixture();
            f.LooseHammer(Vector3.Zero);
            f.ClearPath();
            f.Hammer.FindWaysToMoveToCoord(Vector3.UnitY * distance, true);
            Assert.AreEqual(Vector3.UnitY * expectedSpeed, f.Hammer.Velocity);
            f.ClearPath();
            f.Hammer.FindWaysToMoveToCoord(Vector3.UnitY * distance, false);
            Assert.AreEqual(Vector3.UnitY * 100, f.Hammer.Velocity);
        }

        [TestMethod]
        public void ObstructedRecallWaitsForPathfinderThenFollowsItAndReturnsToDirectFlight()
        {
            var f = new HammerFixture();
            f.LooseHammer(new Vector3(1, 1, 1));
            Action closedPath = () =>
            {
                f.Game.World.Capsules.Enqueue(new RayHit(true, 0, new Vector3(2, 1, 1)));
                f.Game.World.Capsules.Enqueue(new RayHit(true, 0, Vector3.Zero));
                f.ClearPath(); f.ClearPath();
            };
            closedPath();
            f.Hammer.FindWaysToMoveToCoord(new Vector3(0, 100, 0), true);
            Assert.AreEqual(Vector3.Zero, f.Hammer.Velocity);
            f.Pathfinder.OnTick(20);
            closedPath(); f.Hammer.FindWaysToMoveToCoord(new Vector3(0, 100, 0), true);
            Assert.AreEqual(new Vector3(1, 1, 1), f.Game.Entity(40).Position);
            f.Game.Entity(40).Position = new Vector3(5, 3, 1);
            f.Game.Entity(40).Velocity = new Vector3(1, 2, 3);
            closedPath(); f.Hammer.FindWaysToMoveToCoord(new Vector3(0, 100, 0), true);
            Assert.AreEqual(new Vector3(3, 3, 3), f.Hammer.Velocity);
            Assert.HasCount(1, f.Game.Calls.Where(call => call.Name == "ped.missionPosition").ToArray());
            f.ClearPath(); f.Hammer.FindWaysToMoveToCoord(new Vector3(1, 11, 1), true);
            Assert.AreEqual(Vector3.UnitY * 40, f.Hammer.Velocity);
        }

        [TestMethod]
        [DataRow(false, false, false)]
        [DataRow(false, true, false)]
        [DataRow(false, false, true)]
        public void NearbyObstacleUsesDirectFlightOnlyWhenAllThreeAirChecksAreClear(bool x, bool y, bool z)
        {
            var f = new HammerFixture();
            f.LooseHammer(Vector3.Zero);
            f.Game.World.Capsules.Enqueue(new RayHit(true, 0, Vector3.UnitY));
            foreach (bool blocked in new[] { x, y, z }) f.Game.World.Capsules.Enqueue(new RayHit(blocked, 0, Vector3.Zero));
            f.Hammer.FindWaysToMoveToCoord(Vector3.UnitY * 10, true);
            Assert.AreEqual(y || z ? Vector3.Zero : Vector3.UnitY * 40, f.Hammer.Velocity);
            f.Game.World.Capsules.Enqueue(new RayHit(true, 0, Vector3.UnitY * 21));
            f.Hammer.FindWaysToMoveToCoord(Vector3.UnitY * 10, true);
            Assert.AreEqual(Vector3.UnitY * 40, f.Hammer.Velocity);
        }

        [TestMethod]
        public void TargetChasingStopsWhenEmptyAndRemovesOnlyReachedPedOrVehicle()
        {
            var f = new HammerFixture();
            var targets = new HashSet<int>();
            Assert.IsFalse(f.Hammer.MoveToTargets(targets));
            f.LooseHammer(Vector3.UnitX);
            f.Hammer.Velocity = Vector3.UnitY;
            Assert.IsFalse(f.Hammer.MoveToTargets(targets));
            Assert.AreEqual(Vector3.Zero, f.Hammer.Velocity);
            f.Game.AddEntity(60).Position = new Vector3(1, 0.5f, 0); f.Peds.Add(60);
            targets.Add(60);
            Assert.IsTrue(f.Hammer.MoveToTargets(targets)); Assert.IsEmpty(targets);
            f.Game.AddEntity(61).Position = new Vector3(1, 2, 0); f.Vehicles.Add(61);
            targets.Add(61);
            f.Hammer.MoveToTargets(targets); Assert.IsEmpty(targets);
            f.Game.AddEntity(62).Position = new Vector3(1, 2, 0);
            targets.Add(62); f.ClearPath();
            f.Hammer.MoveToTargets(targets);
            Assert.IsTrue(targets.Contains(62));
            Assert.AreEqual(Vector3.UnitY * 100, f.Hammer.Velocity);
            f.Game.Entity(62).Position = Vector3.Zero; f.ClearPath();
            f.Hammer.MoveToTargets(targets);
            Assert.IsTrue(targets.Contains(62));
        }

        [TestMethod]
        public void MovingHammerDamagesNearbyPedButExcludesPlayerHammerAndPathfinderResources()
        {
            var f = new HammerFixture();
            f.Hammer.ApplyForcesToNearbyEntities();
            f.LooseHammer(Vector3.Zero);
            f.Hammer.ApplyForcesToNearbyEntities();
            f.Pathfinder.OnTick(20);
            f.Game.AddEntity(60); f.Peds.Add(60); f.Game.AddEntity(61);
            f.Game.World.Nearby = (position, radius) => new[] { 10, 20, 40, 41, 60, 61 };
            f.Hammer.Velocity = Vector3.UnitY * 100;
            f.Hammer.ApplyForcesToNearbyEntities();
            CollectionAssert.AreEqual(new[] { 60, 61 }, f.Game.Calls.Where(call => call.Name == "entity.force").Select(call => (int)call.Arguments[0]).ToArray());
            var damage = f.Game.Calls.Single(call => call.Name == "ped.damage");
            CollectionAssert.AreEqual(new object[] { 60, 100 }, damage.Arguments);
            var plane = new ADModUtils.Plane(Vector3.UnitZ, Vector3.Zero, new Random(1));
            f.Hammer.Whirl(plane);
            f.Hammer.ApplyForcesToNearbyEntities();
            Assert.HasCount(2, f.Game.Calls.Where(call => call.Name == "entity.force").ToArray());
            f.Game.Observations[20].Exists = false;
            f.Hammer.ApplyForcesToNearbyEntities();
            Assert.IsFalse(f.Hammer.IsMoving);
        }

        [TestMethod]
        public void RopeAttachmentRoutesVelocityToHandAnchorAndReleasesBothOwnedResources()
        {
            var f = new HammerFixture();
            f.Hammer.AttachHammerRopeTo(10, Bone.PHRightHand);
            Assert.IsFalse(f.Game.Calls.Any(call => call.Name == "rope.create"));
            f.LooseHammer(Vector3.UnitY);
            f.Game.AddEntity(30); f.WeaponCreations.Enqueue(30); f.Game.Ropes.Creations.Enqueue(70);
            f.Hammer.AttachHammerRopeTo(10, Bone.PHRightHand);
            Assert.AreEqual(10, f.Game.Entity(30).AttachedTo);
            Assert.IsFalse(f.Game.Entity(30).Visible);
            var connection = f.Game.Calls.Single(call => call.Name == "rope.connect").Arguments;
            Assert.AreEqual(new Vector3(0, 1, -0.15f), connection[4]);
            f.Hammer.Velocity = new Vector3(2, 3, 4);
            Assert.AreEqual(new Vector3(2, 3, 4), f.Hammer.Velocity);
            Assert.AreEqual(Vector3.Zero, f.Game.Entity(20).Velocity);
            f.Hammer.OnTick();
            f.Hammer.StopEffects(); f.Hammer.DetachRope();
            Assert.IsEmpty(f.Game.Ropes.Ropes);
            Assert.IsFalse(f.Game.Observations[30].Exists);
            Assert.IsTrue(f.Game.Observations[20].Exists);
            f.Hammer.Velocity = Vector3.UnitX;
            Assert.AreEqual(Vector3.UnitX, f.Game.Entity(20).Velocity);
        }

        [TestMethod]
        public void WhirlingMovesWithinItsPlaneAndLerpsOnlyWhenRequested()
        {
            var f = new HammerFixture();
            f.LooseHammer(Vector3.UnitX);
            var plane = new ADModUtils.Plane(Vector3.UnitZ, Vector3.Zero, new Random(1));
            f.Hammer.Whirl(plane);
            var immediate = f.Hammer.Velocity;
            Assert.AreEqual(500f, Vector3.Dot(immediate, Vector3.UnitX), 0.01f);
            Assert.AreEqual(1500f, Math.Abs(immediate.Y), 0.01f);
            f.Hammer.Velocity = Vector3.Zero;
            f.Hammer.Whirl(plane, lerp: true);
            Assert.AreEqual(immediate * 0.25f, f.Hammer.Velocity);
            f.Game.AddEntity(30).Position = Vector3.UnitX;
            f.Hammer.Whirl(plane, false, 30);
            Assert.AreEqual(0.3f, f.Game.Entity(30).Position.Length(), 0.0001f);
            Assert.AreEqual(0f, f.Game.Entity(30).Position.Z, 0.0001f);
            Assert.AreEqual(Vector3.UnitX, f.Game.Entity(20).Position);
            Assert.AreEqual(-Vector3.UnitZ, f.Hammer.ConvertToHammerHeadDirection(Vector3.UnitY));
        }

        [TestMethod]
        public void MissingOrDeletedHammerAndTargetDoNotIssueInvalidMovementQueries()
        {
            var f = new HammerFixture();
            f.Hammer.FindWaysToMoveToCoord(Vector3.UnitY, true);
            f.Hammer.ShowParticleFx();
            Assert.IsFalse(f.Game.Calls.Any(call => call.Name == "world.capsule"));
            f.LooseHammer(Vector3.UnitX);
            f.Game.AddEntity(60).Exists = false;
            var targets = new HashSet<int> { 60 };
            Assert.IsTrue(f.Hammer.MoveToTargets(targets));
            Assert.IsEmpty(targets);
            f.Game.Observations[20].Exists = false;
            f.Hammer.OnTick();
            Assert.IsFalse(f.Game.Calls.Any(call => call.Name == "world.raycast"));
        }

        [TestMethod]
        public void ATargetOutsideItsContactRadiusContinuesChasing()
        {
            var f = new HammerFixture();
            f.LooseHammer(Vector3.Zero);
            f.Game.AddEntity(60).Position = Vector3.UnitX; f.Peds.Add(60);
            f.Game.AddEntity(61).Position = Vector3.UnitY * 3; f.Vehicles.Add(61);
            foreach (int target in new[] { 60, 61 })
            {
                var targets = new HashSet<int> { target };
                f.ClearPath();
                f.Hammer.MoveToTargets(targets);
                Assert.IsTrue(targets.Contains(target));
                Assert.AreEqual(100f, f.Hammer.Velocity.Length(), 0.001f);
            }
        }

        [TestMethod]
        public void LostTerrainStartsBoundedIdleMotionAndSummonOrTerrainRecoveryStopsIt()
        {
            var f = new HammerFixture();
            f.LooseHammer(Vector3.Zero);
            f.Game.Entity(20).HeightAboveGround = 51;
            f.ClearTerrainRay(); f.Hammer.OnTick();
            f.Game.Host.GameTime = 999;
            f.ClearTerrainRay(); f.Hammer.OnTick();
            Assert.AreEqual(Vector3.Zero, f.Hammer.Velocity);
            f.Game.Host.GameTime = 1000;
            f.ClearTerrainRay(); f.Hammer.OnTick();
            Assert.IsTrue(f.Hammer.Velocity.Length() > 0 && f.Hammer.Velocity.Length() <= 30);
            f.Hammer.SetSummonStatus(true);
            var before = f.Game.Calls.Count(call => call.Name == "entity.velocity" && (int)call.Arguments[0] == 20);
            f.ClearTerrainRay(); f.Hammer.OnTick();
            Assert.AreEqual(before, f.Game.Calls.Count(call => call.Name == "entity.velocity" && (int)call.Arguments[0] == 20));
            f.Hammer.SetSummonStatus(false);
            f.Game.World.Rays.Enqueue(new RayHit(true, 0, Vector3.Zero)); f.Hammer.OnTick();
            before = f.Game.Calls.Count(call => call.Name == "entity.velocity" && (int)call.Arguments[0] == 20);
            f.ClearTerrainRay(); f.Hammer.OnTick();
            Assert.AreEqual(before, f.Game.Calls.Count(call => call.Name == "entity.velocity" && (int)call.Arguments[0] == 20));
            f.Game.Entity(20).HeightAboveGround = 102;
            f.ClearTerrainRay(); f.Hammer.OnTick();
            f.Game.Observations[20].Exists = false;
            f.Hammer.OnTick();
            Assert.AreEqual(0, f.Hammer.WeaponObject);
            Assert.IsFalse(f.Game.Calls.Any(call => call.Name == "entity.velocity" && (int)call.Arguments[0] == 0));
        }

        [TestMethod]
        public void AirborneHammerPointsAlongMotionAndTurnsUpWhenCloseToCatchingHand()
        {
            var f = new HammerFixture();
            f.LooseHammer(Vector3.Zero);
            f.Hammer.PointAt(Vector3.UnitX, false);
            Assert.IsTrue((f.Game.Entity(20).Quaternion * Vector3.UnitZ - Vector3.UnitX).Length() < 0.0001f);
            f.Game.Entity(20).InAir = true; f.Hammer.Velocity = Vector3.UnitY;
            f.Hammer.SetSummonStatus(false);
            f.ClearTerrainRay(); f.Hammer.OnTick();
            var moving = f.Game.Entity(20).Quaternion;
            f.Hammer.SetSummonStatus(true, false);
            f.ClearTerrainRay(); f.Hammer.OnTick();
            f.Hammer.SetSummonStatus(true, true);
            f.ClearTerrainRay(); f.Hammer.OnTick();
            Assert.AreNotEqual(moving, f.Game.Entity(20).Quaternion);
            Assert.AreEqual(1f, f.Game.Entity(20).Quaternion.Length(), 0.0001f);
            f.Game.Entity(20).InAir = false;
            var grounded = f.Game.Entity(20).Quaternion;
            f.ClearTerrainRay(); f.Hammer.OnTick();
            Assert.AreEqual(grounded, f.Game.Entity(20).Quaternion);
        }
    }
}
