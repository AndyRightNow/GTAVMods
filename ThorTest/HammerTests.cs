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
            f.Game.Entity(20).CollisionEnabled = false;
            f.WeaponCreations.Enqueue(20); f.Game.Entities.CreatedBlips.Enqueue(50);
            f.Hammer.Init(null);
            Assert.AreEqual(20, f.Hammer.WeaponObject);
            var creation = f.LastNative(Hash.CREATE_WEAPON_OBJECT);
            Assert.AreEqual(2000f, creation[4]);
            Assert.AreEqual(50000f, f.LastNative(Hash.SET_OBJECT_PHYSICS_PARAMS)[1]);
            Assert.IsTrue(f.Game.Entity(20).CollisionEnabled);
            CollectionAssert.AreEqual(new object[] { 50, true }, f.Game.Calls.Single(call => call.Name == "blip.friendly").Arguments);
            f.Hammer.Init(Vector3.UnitX);
            Assert.HasCount(1, f.Game.Native.Calls.Where(call => call.Hash == Hash.CREATE_WEAPON_OBJECT).ToArray());
            f.Hammer.WeaponObject = 20;
            Assert.IsFalse(f.Game.Calls.Any(call => call.Name == "entity.delete"));
            f.Pathfinder.OnTick(20);
            f.Game.AddEntity(30);
            f.WeaponCreations.Enqueue(30); f.Game.Entities.CreatedBlips.Enqueue(51);
            f.Hammer.Init(new Vector3(1, 2, 3), true);
            CollectionAssert.AreEqual(new object[] { 1f, 2f, 3f }, f.LastNative(Hash.CREATE_WEAPON_OBJECT).Skip(2).Take(3).ToArray());
            Assert.IsFalse(f.Game.Observations[20].Exists);
            Assert.AreEqual(30, f.Hammer.WeaponObject);
            CollectionAssert.AreEquivalent(new[] { 40, 41 }, f.Game.Calls
                .Where(call => call.Name == "entity.noCollision" && (int)call.Arguments[1] == 30 && (bool)call.Arguments[2])
                .Select(call => (int)call.Arguments[0]).ToArray(),
                "Replacing the hammer also excludes it from collisions with existing pathfinder helpers.");
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
            var diagnostic = f.Log.Last("hammer.createFailed");
            Assert.AreEqual("ERROR", diagnostic.Level);
            StringAssert.Contains((string)diagnostic.Data["exception"], error.Message);
            StringAssert.Contains((string)f.Game.Calls.Single(call => call.Name == "host.notify").Arguments[0], "log");
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
            var transitions = f.Log.Records.Where(record => record.Name == "hammer.pathObstruction").ToArray();
            CollectionAssert.AreEqual(new[] { true, true, false }, transitions.Select(record => (bool)record.Data["blocked"]).ToArray());
            Assert.IsTrue(transitions.All(record => record.Level == "INFO" && record.Interval == 500));
            Assert.AreEqual(new Vector3(1, 11, 1).ToString(), transitions.Last().Data["target"]);
        }

        [TestMethod]
        [DataRow(false, false, false)]
        [DataRow(false, true, false)]
        [DataRow(false, false, true)]
        public void NearbyObstacleUsesDirectFlightOnlyWhenAllThreeAirChecksAreClear(bool x, bool y, bool z)
        {
            var f = new HammerFixture();
            f.LooseHammer(Vector3.Zero);
            f.Game.World.Capsules.Enqueue(new RayHit(true, 0, Vector3.UnitY * 20));
            foreach (bool blocked in new[] { x, y, z }) f.Game.World.Capsules.Enqueue(new RayHit(blocked, 0, Vector3.Zero));
            f.Hammer.FindWaysToMoveToCoord(Vector3.UnitY * 30, true);
            Assert.AreEqual(y || z ? Vector3.Zero : Vector3.UnitY * 100, f.Hammer.Velocity);
            f.Game.World.Capsules.Enqueue(new RayHit(true, 0, Vector3.UnitY * 21));
            f.Hammer.FindWaysToMoveToCoord(Vector3.UnitY * 30, true);
            Assert.AreEqual(Vector3.UnitY * 100, f.Hammer.Velocity);
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
            var reached = f.Log.Records.Where(record => record.Name == "hammer.targetReached").ToArray();
            CollectionAssert.AreEqual(new[] { 60, 61 }, reached.Select(record => (int)record.Data["target"]).ToArray());
            Assert.IsTrue(reached.All(record => record.Level == "INFO" && (int)record.Data["remaining"] == 0));
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
            Assert.IsFalse(f.Hammer.IsMoving);
            f.Hammer.ApplyForcesToNearbyEntities();
            f.Pathfinder.OnTick(20);
            f.Game.AddEntity(60); f.Peds.Add(60); f.Game.AddEntity(61);
            f.Game.World.Nearby = (position, radius) => new[] { 10, 20, 40, 41, 60, 61 };
            f.Hammer.Velocity = Vector3.UnitY * 100;
            f.Hammer.ApplyForcesToNearbyEntities();
            CollectionAssert.AreEqual(new[] { 60, 61 }, f.Game.Calls.Where(call => call.Name == "entity.force").Select(call => (int)call.Arguments[0]).ToArray());
            var damage = f.Game.Calls.Single(call => call.Name == "ped.damage");
            CollectionAssert.AreEqual(new object[] { 60, 100 }, damage.Arguments);
            f.Hammer.ShowParticleFx();
            var smoke = f.Game.Native.Calls.Single(call => call.Hash == Hash.START_PARTICLE_FX_NON_LOOPED_ON_ENTITY);
            Assert.AreEqual("scr_meth_pipe_smoke", smoke.Arguments[0]); Assert.AreEqual(20, smoke.Arguments[1]);
            Assert.IsTrue(f.Game.Native.Calls.Any(call => call.Hash == Hash.REQUEST_NAMED_PTFX_ASSET &&
                (string)call.Arguments[0] == "scr_familyscenem"));
            var trail = f.Game.Native.Calls.Where(call => call.Hash == Hash.START_PARTICLE_FX_NON_LOOPED_AT_COORD)
                .Select(call => new Vector3((float)call.Arguments[1], (float)call.Arguments[2], (float)call.Arguments[3])).ToArray();
            Assert.AreEqual(-8f, trail.Min(point => point.Y), 0.0001f);
            Assert.AreEqual(0f, trail.Max(point => point.Y), 0.0001f);
            Assert.IsTrue(trail.All(point => Math.Sqrt(point.X * point.X + point.Z * point.Z) <= 0.24001),
                "The cosmetic lightning trails behind the hammer within three percent of its eight-metre length.");
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
            f.Game.Entity(20).Rotation = new Vector3(10, 20, 30);
            f.Game.AddEntity(30).CollisionEnabled = false; f.WeaponCreations.Enqueue(30); f.Game.Ropes.Creations.Enqueue(70);
            f.Hammer.AttachHammerRopeTo(10, Bone.PHRightHand);
            CollectionAssert.AreEqual(new object[] { RopeType.ThickRope, new Vector3(0, 0, 1),
                Vector3.Zero, 0.19f, 0f, false, 70 }, f.Game.Calls.Single(call => call.Name == "rope.create").Arguments);
            Assert.IsFalse(f.Log.Records.Any(record => record.Name == "rope.detach"),
                "Initial attachment has no previous rope to detach.");
            Assert.AreEqual(10, f.Game.Entity(30).AttachedTo);
            Assert.IsFalse(f.Game.Entity(30).Visible);
            Assert.IsTrue(f.Game.Entity(30).CollisionEnabled);
            Assert.AreEqual(Vector3.Zero, f.Game.Entity(20).Rotation);
            CollectionAssert.AreEquivalent(new[] { 20, 30 }, f.Game.Calls
                .Where(call => call.Name == "entity.noCollision" && (int)call.Arguments[1] == 10 && (bool)call.Arguments[2])
                .Select(call => (int)call.Arguments[0]).ToArray());
            CollectionAssert.AreEqual(new object[] { 70 }, f.Game.Calls.Single(call => call.Name == "rope.physics").Arguments);
            var connection = f.Game.Calls.Single(call => call.Name == "rope.connect").Arguments;
            Assert.AreEqual(new Vector3(0, 1, -0.15f), connection[4]);
            f.Hammer.Velocity = new Vector3(2, 3, 4);
            Assert.AreEqual(new Vector3(2, 3, 4), f.Hammer.Velocity);
            Assert.AreEqual(Vector3.Zero, f.Game.Entity(20).Velocity);
            f.Game.AddEntity(31); f.WeaponCreations.Enqueue(31); f.Game.Ropes.Creations.Enqueue(71);
            f.Hammer.AttachHammerRopeTo(10, Bone.PHRightHand);
            Assert.IsFalse(f.Game.Observations[30].Exists);
            CollectionAssert.AreEqual(new[] { 71 }, f.Game.Ropes.Ropes.ToArray());
            Assert.HasCount(1, f.Log.Records.Where(record => record.Name == "rope.detach").ToArray());
            f.Hammer.Whirl(new ADModUtils.Plane(Vector3.UnitZ, Vector3.Zero, new Random(1)));
            f.Hammer.OnTick();
            f.Hammer.OnTick();
            Assert.AreEqual(1, f.Audio.Clips["whirling-hammer.wav"].Plays, "An ongoing whirl must not restart its audio each frame.");
            f.Hammer.StopEffects(); f.Hammer.DetachRope();
            f.ClearTerrainRay(); f.Hammer.OnTick();
            Assert.IsFalse(f.Audio.Clips["whirling-hammer.wav"].IsPlaying);
            var attached = f.Log.Last("rope.attach");
            Assert.AreEqual("INFO", attached.Level);
            Assert.AreEqual(10, attached.Data["ped"]);
            Assert.AreEqual(Bone.PHRightHand.ToString(), attached.Data["bone"]);
            Assert.HasCount(2, f.Log.Records.Where(record => record.Name == "rope.detach").ToArray());
            Assert.AreEqual("INFO", f.Log.Last("rope.detach").Level);
            Assert.IsEmpty(f.Game.Ropes.Ropes);
            Assert.IsFalse(f.Game.Observations[30].Exists);
            Assert.IsFalse(f.Game.Observations[31].Exists);
            Assert.IsTrue(f.Game.Observations[20].Exists);
            f.Hammer.Velocity = Vector3.UnitX;
            Assert.AreEqual(Vector3.UnitX, f.Game.Entity(20).Velocity);
            f.Hammer.Whirl(new ADModUtils.Plane(Vector3.UnitZ, Vector3.Zero, new Random(1)));
            f.ClearTerrainRay(); f.Hammer.OnTick();
            Assert.IsTrue(f.Audio.Clips["whirling-hammer.wav"].IsPlaying);
            f.ClearTerrainRay(); f.Hammer.OnTick();
            Assert.IsFalse(f.Audio.Clips["whirling-hammer.wav"].IsPlaying, "A completed unroped whirl stops its sound next frame.");
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
        public void WhirlingIsIndependentOfWorldOriginAndKeepsTheHammerInItsOrbitalPlane()
        {
            var f = new HammerFixture();
            var offset = new Vector3(17, -23, 11);
            var normal = new Vector3(0, 3, 4).Normalized;
            var radial = Vector3.UnitX * 2;
            var originPlane = new ADModUtils.Plane(normal, Vector3.Zero, new Random(7));
            var translatedPlane = new ADModUtils.Plane(normal, offset, new Random(7));
            f.LooseHammer(radial);
            f.Hammer.Whirl(originPlane);
            var velocity = f.Hammer.Velocity;
            var rotation = f.Game.Entity(20).Rotation;
            Assert.IsTrue((rotation - new Vector3(-90, 0, -90)).Length() < 0.001f,
                "The hammer head points radially outward, including its model's pitch offset.");
            Assert.AreEqual(1000f, velocity.X, 0.01f, "The outward component depends on radius.");
            Assert.AreEqual(0f, Vector3.Dot(velocity, normal), 0.01f, "An in-plane hammer must not accelerate out of the plane.");
            Assert.AreEqual(3000f, (velocity - Vector3.UnitX * velocity.X).Length(), 0.01f,
                "The orbital component is perpendicular to the radius; either plane basis is valid.");

            f.Game.Entity(20).Position = offset + radial;
            f.Hammer.Whirl(translatedPlane);
            Assert.IsTrue((f.Hammer.Velocity - velocity).Length() < 0.02f,
                "Translating the player and hammer together must not change whirling velocity.");
            Assert.IsTrue((f.Game.Entity(20).Rotation - rotation).Length() < 0.001f);

            f.Hammer.Whirl(translatedPlane, physically: false);
            var displaced = f.Game.Entity(20).Position - offset;
            Assert.AreEqual(0.3f, displaced.Length(), 0.0001f);
            Assert.AreEqual(0f, Vector3.Dot(displaced, normal), 0.0001f);
            var cosmeticRotation = f.Game.Entity(20).Rotation;
            double pitch = (cosmeticRotation.X + 90) * Math.PI / 180;
            double yaw = cosmeticRotation.Z * Math.PI / 180;
            var headDirection = new Vector3((float)(-Math.Sin(yaw) * Math.Cos(pitch)),
                (float)(Math.Cos(yaw) * Math.Cos(pitch)), (float)Math.Sin(pitch));
            Assert.AreEqual(1f, Vector3.Dot(displaced.Normalized, headDirection), 0.0001f,
                "The cosmetic hammer head must point outward from the orbit's center.");
            f.Game.Entity(20).Position = radial;
            f.Hammer.Whirl(originPlane, physically: false);
            Assert.IsTrue((f.Game.Entity(20).Position - displaced).Length() < 0.0001f);
        }

        [TestMethod]
        public void StationaryHammerLightningRepeatsOncePerSecondAndFollowsReplacementOwnership()
        {
            var f = new HammerFixture();
            f.LooseHammer(new Vector3(12, -8, 30));
            f.Game.World.RaycastObservation = (from, to, flags, ignore) => new RayHit(true, 0, Vector3.Zero);
            foreach (int time in new[] { 0, 999, 1000, 1000, 1999, 2000 })
            {
                f.Game.Host.GameTime = time;
                f.Hammer.OnTick();
            }
            CollectionAssert.AreEqual(new[] { 20, 20 }, f.Game.Native.Calls
                .Where(call => call.Hash == Hash.START_PARTICLE_FX_NON_LOOPED_ON_ENTITY)
                .Select(call => (int)call.Arguments[1]).ToArray());

            f.Game.AddEntity(30); f.Game.Entities.CreatedBlips.Enqueue(51);
            f.Hammer.WeaponObject = 30;
            f.Game.Host.GameTime = 2999; f.Hammer.OnTick();
            f.Game.Host.GameTime = 3000; f.Hammer.OnTick();
            f.Hammer.WeaponObject = 0;
            f.Game.Host.GameTime = 5000; f.Hammer.OnTick();
            CollectionAssert.AreEqual(new[] { 20, 20, 30 }, f.Game.Native.Calls
                .Where(call => call.Hash == Hash.START_PARTICLE_FX_NON_LOOPED_ON_ENTITY)
                .Select(call => (int)call.Arguments[1]).ToArray(),
                "Retiring the hammer cancels its scheduled effect without retargeting it to handle zero.");
        }

        [TestMethod]
        public void ObstacleAirProbesEncloseTheHammerAlongAllThreeWorldAxes()
        {
            var f = new HammerFixture();
            var center = new Vector3(12, -8, 30);
            f.LooseHammer(center);
            f.Game.World.Capsules.Enqueue(new RayHit(true, 0, center + Vector3.UnitY));
            for (int i = 0; i < 3; i++) f.ClearPath();
            f.Hammer.FindWaysToMoveToCoord(center + Vector3.UnitY * 15, true);
            var probes = f.Game.Calls.Where(call => call.Name == "world.capsule").Skip(1).ToArray();
            Assert.HasCount(3, probes);
            var axes = new[] { Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ };
            for (int i = 0; i < probes.Length; i++)
            {
                CollectionAssert.AreEqual(new object[] { center - axes[i] * 10, center + axes[i] * 10, 10f, IntersectFlags.Map },
                    probes[i].Arguments);
            }
            Assert.AreEqual(Vector3.UnitY * 100, f.Hammer.Velocity);
        }

        [TestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public void FailedRetirementCanBeReadoptedOrForgottenWithoutDeletingAReusedHandle(bool removedByEngine)
        {
            var f = new HammerFixture();
            f.LooseHammer(Vector3.UnitY);
            f.Game.AddEntity(30); f.Game.Entities.CreatedBlips.Enqueue(51);
            f.Game.Entities.SilentDeletionRefusals.Add(20);
            Assert.ThrowsExactly<AggregateException>(() => f.Hammer.WeaponObject = 30);
            Assert.AreEqual(30, f.Hammer.WeaponObject);
            Assert.IsTrue(f.Game.Entity(20).Exists);
            f.Game.Entities.SilentDeletionRefusals.Clear();

            if (removedByEngine)
            {
                f.Game.Observations[20].Exists = false;
                f.Hammer.WeaponObject = 30;
                f.Game.Observations[20] = new EntityObservation { Position = new Vector3(91, 92, 93) };
                f.Hammer.WeaponObject = 0;
                Assert.AreEqual(new Vector3(91, 92, 93), f.Game.Entity(20).Position,
                    "A later unrelated entity reusing the retired number is no longer ours to delete.");
            }
            else
            {
                f.Game.Entities.CreatedBlips.Enqueue(52);
                f.Hammer.WeaponObject = 20;
                Assert.IsTrue(f.Game.Entity(20).Exists, "Readopting a failed retirement must cancel its pending deletion.");
                Assert.IsFalse(f.Game.Observations[30].Exists);
                f.Game.Observations[30] = new EntityObservation { Position = new Vector3(91, 92, 93) };
                f.Hammer.WeaponObject = 0;
                Assert.AreEqual(new Vector3(91, 92, 93), f.Game.Entity(30).Position);
            }
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
            foreach (float height in new[] { 25f, 50f, 100f })
            {
                f.Game.Entity(20).HeightAboveGround = height;
                f.ClearTerrainRay(); f.Hammer.OnTick();
                f.Game.Host.GameTime += 1000;
            }
            Assert.AreEqual(Vector3.Zero, f.Hammer.Velocity,
                "A gradual change, including exactly fifty metres, does not indicate lost terrain.");
            f.Game.Entity(20).HeightAboveGround = 151;
            f.ClearTerrainRay(); f.Hammer.OnTick();
            Assert.IsTrue(f.Game.Entity(20).Persistent);
            CollectionAssert.AreEqual(new object[] { 20, false }, f.LastNative(Hash.SET_ENTITY_SHOULD_FREEZE_WAITING_ON_COLLISION));
            CollectionAssert.AreEqual(new object[] { Vector3.Zero, new Vector3(0, 0, -1000), IntersectFlags.Map, 0 },
                f.Game.Calls.Last(call => call.Name == "world.raycast").Arguments);
            f.Game.Host.GameTime = 3999;
            f.ClearTerrainRay(); f.Hammer.OnTick();
            Assert.AreEqual(Vector3.Zero, f.Hammer.Velocity);
            f.Game.Host.GameTime = 4000;
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
            f.Game.Entity(20).HeightAboveGround = 202;
            f.ClearTerrainRay(); f.Hammer.OnTick();
            f.Game.Observations[20].Exists = false;
            f.Hammer.OnTick();
            Assert.AreEqual(0, f.Hammer.WeaponObject);
            Assert.IsFalse(f.Game.Calls.Any(call => call.Name == "entity.velocity" && (int)call.Arguments[0] == 0));
        }

        [TestMethod]
        [DataRow(0.25, 7.5f)]
        [DataRow(0.999999999, 30f)]
        public void IdleMotionConvergesWithoutOvershootEvenWhenRandomFloatRoundsUp(double sample, float expectedSpeed)
        {
            var f = new HammerFixture(new FixedRandom(0, sample));
            f.LooseHammer(Vector3.Zero);
            f.Game.Entity(20).HeightAboveGround = 51;
            f.Game.World.RaycastObservation = (from, to, flags, ignore) => new RayHit(false, 0, Vector3.Zero);
            f.Hammer.OnTick();
            f.Hammer.Velocity = Vector3.UnitX * 30;
            for (int time = 1000; time <= 1200; time++)
            {
                f.Game.Host.GameTime = time;
                f.Hammer.OnTick();
                Assert.IsTrue(f.Hammer.Velocity.Length() <= 30.001f, "Idle steering must interpolate within the speed bound.");
            }
            Assert.AreEqual(expectedSpeed, f.Hammer.Velocity.Length(), 0.001f,
                "The selected speed scales the uniform sample over the zero-to-thirty metre-per-second range.");
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
            for (int frame = 0; frame < 30; frame++) { f.ClearTerrainRay(); f.Hammer.OnTick(); }
            Assert.IsTrue((f.Game.Entity(20).Quaternion * Vector3.UnitZ - Vector3.UnitY).Length() < 0.001f,
                "The hammer head converges to its direction of motion.");
            f.Hammer.SetSummonStatus(true, false);
            for (int frame = 0; frame < 30; frame++) { f.ClearTerrainRay(); f.Hammer.OnTick(); }
            Assert.IsTrue((f.Game.Entity(20).Quaternion * Vector3.UnitZ - Vector3.UnitY).Length() < 0.001f);
            f.Hammer.SetSummonStatus(true, true);
            for (int frame = 0; frame < 30; frame++) { f.ClearTerrainRay(); f.Hammer.OnTick(); }
            Assert.IsTrue((f.Game.Entity(20).Quaternion * Vector3.UnitZ - Vector3.UnitZ).Length() < 0.001f,
                "Within catching distance the hammer head turns upright.");
            Assert.AreEqual(1f, f.Game.Entity(20).Quaternion.Length(), 0.0001f);
            f.Game.Entity(20).InAir = false;
            var grounded = f.Game.Entity(20).Quaternion;
            f.ClearTerrainRay(); f.Hammer.OnTick();
            Assert.AreEqual(grounded, f.Game.Entity(20).Quaternion);
        }

        [TestMethod]
        public void AimingUsesTheModelBasisAndInterpolatesFromAnyObservedPoseWithoutResettingTheEntity()
        {
            var f = new HammerFixture();
            f.LooseHammer(Vector3.Zero);
            var initial = new Quaternion(0.5f, 0.5f, 0, (float)Math.Sqrt(0.5));
            foreach (var direction in new[] { Vector3.UnitX, -Vector3.UnitY, Vector3.UnitZ, new Vector3(2, -3, 4).Normalized })
            {
                f.Game.Entity(20).Quaternion = initial;
                f.Game.Entity(20).Forward = initial * Vector3.UnitY;
                f.Game.Calls.Clear();
                f.Hammer.PointAt(direction, shouldLerp: false);
                var aimed = f.Game.Entity(20).Quaternion;
                Assert.IsTrue((aimed * Vector3.UnitZ - direction).Length() < 0.0001f,
                    "The hammer head follows the requested world direction regardless of its prior orientation.");
                Assert.AreEqual(1f, aimed.Length(), 0.0001f);
                Assert.HasCount(1, f.Game.Calls.Where(call => call.Name == "entity.quaternion").ToArray(),
                    "Aiming submits one final pose and never temporarily sends a zero quaternion.");

                f.Game.Entity(20).Quaternion = initial;
                f.Hammer.PointAt(direction);
                var interpolated = f.Game.Entity(20).Quaternion;
                Assert.AreEqual(1f, interpolated.Length(), 0.0001f);
                Assert.IsTrue((interpolated * Vector3.UnitZ - direction).Length() <
                    (initial * Vector3.UnitZ - direction).Length(),
                    "Smoothing must turn the previously observed pose toward the target.");
            }
        }
    }
}
