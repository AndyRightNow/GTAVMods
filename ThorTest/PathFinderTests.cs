using System;
using System.Linq;
using GTA;
using GTA.Math;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Finder = Thor.PathFinder.PathFinder;

namespace ModUtilsTest {
    [TestClass]
    public class PathFinderTests {
        private static FakeGame CreateWorld() {
            var game = new FakeGame();
            game.AddEntity(10);
            game.AddEntity(20);
            game.AddEntity(30).Velocity = new Vector3(2, 4, 6);
            game.AddEntity(40);
            game.World.ModelResults[(int)VehicleHash.Thruster] = true;
            game.World.Vehicles.Enqueue(30);
            game.World.Drivers.Enqueue(40);
            return game;
        }

        [TestMethod]
        public void InitialTickCreatesHiddenProtectedHelpersAndMaintainsVelocityCollisionAndDriver() {
            var game = CreateWorld();
            var finder = new Finder(game.Services);
            Assert.IsNull(finder.Position);
            Assert.AreEqual(Vector3.Zero, finder.GetTargetVelocity(Vector3.One));
            Assert.IsFalse(finder.IsRelatedEntity(0), "Zero is never an owned engine entity.");
            Assert.IsFalse(finder.IsRelatedEntity(20));
            finder.SetNoCollision(20);
            finder.Dispose();
            Assert.AreEqual(0, game.Calls.Count);
            finder.OnTick(20);
            Assert.AreEqual(new Vector3(500, 500, 500), finder.Position);
            Assert.IsTrue(finder.IsRelatedEntity(30));
            Assert.IsTrue(finder.IsRelatedEntity(40));
            Assert.IsFalse(finder.IsRelatedEntity(20));
            Assert.AreEqual(1, game.Calls.Count(c => c.Name == "world.requestModel"));
            CollectionAssert.AreEqual(new object[] { (int)VehicleHash.Thruster, 3000 },
                game.Calls.First(c => c.Name == "world.requestModel").Arguments);
            CollectionAssert.AreEqual(new[] { "world.requestModel", "world.createVehicle", "world.createDriver", "world.releaseModel" },
                game.Calls.Take(4).Select(c => c.Name).ToArray());
            foreach (var entity in new[] { 30, 40 }) {
                Assert.IsFalse(game.Entity(entity).Visible);
                Assert.IsTrue(game.Entity(entity).Invincible);
                Assert.AreEqual(100, game.Entity(entity).Health);
            }
            Assert.AreEqual(2.1, game.Entity(30).Velocity.X, .00001);
            Assert.AreEqual(4.2, game.Entity(30).Velocity.Y, .00001);
            Assert.AreEqual(6.3, game.Entity(30).Velocity.Z, .00001);
            var velocity = finder.GetTargetVelocity(new Vector3(1, 2, 3));
            Assert.AreEqual(251.6, velocity.X, .0001);
            Assert.AreEqual(253.2, velocity.Y, .0001);
            Assert.AreEqual(254.8, velocity.Z, .0001);
            var collision = game.Calls.Where(c => c.Name == "entity.noCollision").Select(c => c.Arguments).ToArray();
            Assert.AreEqual(4, collision.Length);
            CollectionAssert.AreEqual(new object[] { 30, 20, true }, collision[0]);
            CollectionAssert.AreEqual(new object[] { 30, 10, true }, collision[1]);
            CollectionAssert.AreEqual(new object[] { 40, 20, true }, collision[2]);
            CollectionAssert.AreEqual(new object[] { 40, 10, true }, collision[3]);
            game.Calls.Clear();
            finder.SetNoCollision(0);
            Assert.AreEqual(0, game.Calls.Count);
            game.Entity(30).Driver = 0;
            finder.OnTick(20);
            Assert.AreEqual(0, game.Calls.Count(c => c.Name == "world.requestModel"), "Existing helpers must be reused.");
            CollectionAssert.AreEqual(new object[] { 40, 30, VehicleSeat.Driver },
                game.Calls.Single(c => c.Name == "ped.intoVehicle").Arguments);
            finder.UpdateStartPosition(new Vector3(1, 2, 3));
            Assert.AreEqual(new Vector3(1, 2, 3), finder.Position);
            finder.Dispose();
            Assert.IsFalse(game.Observations[30].Exists);
            Assert.IsFalse(game.Observations[40].Exists);
            Assert.IsNull(finder.Position);
            Assert.IsFalse(finder.IsRelatedEntity(30));
            var deletes = game.Calls.Count(c => c.Name == "entity.delete");
            finder.Dispose();
            Assert.AreEqual(deletes, game.Calls.Count(c => c.Name == "entity.delete"));
        }

        [TestMethod]
        public void PositionTargetsUseFifteenMeterThresholdAndTransitionToAndFromPedTargets() {
            var game = CreateWorld();
            game.AddEntity(50);
            game.AddEntity(51);
            var finder = new Finder(game.Services);
            finder.OnTick(20);
            game.Calls.Clear();
            var originNeighbor = new Vector3(3, 4, 0);
            finder.UpdateCurrentTarget(originNeighbor);
            CollectionAssert.AreEqual(new[] { "ped.clearTasks", "ped.missionPosition" }, game.Calls.Select(c => c.Name).ToArray());
            CollectionAssert.AreEqual(new object[] { 40, 30, originNeighbor, VehicleMissionType.GoTo, 100000f,
                1f, -1, 5, -1, -1f, HeliMissionFlags.StartEngineImmediately }, game.Calls.Last().Arguments);
            game.Calls.Clear();
            finder.UpdateCurrentTarget(originNeighbor);
            finder.UpdateCurrentTarget(new Vector3(18, 4, 0));
            Assert.AreEqual(0, game.Calls.Count, "Identical and exactly fifteen meters remain the same target.");
            finder.UpdateCurrentTarget(new Vector3(19, 4, 0));
            Assert.AreEqual(1, game.Calls.Count(c => c.Name == "ped.missionPosition"));
            game.Calls.Clear();
            finder.UpdateCurrentTarget(0);
            Assert.AreEqual(0, game.Calls.Count);
            finder.UpdateCurrentTarget(50);
            CollectionAssert.AreEqual(new object[] { 40, 30, 50, VehicleMissionType.Follow, 100000f,
                1f, -1, 5, -1, -1f, HeliMissionFlags.StartEngineImmediately }, game.Calls.Last().Arguments);
            game.Calls.Clear();
            finder.UpdateCurrentTarget(50);
            Assert.AreEqual(0, game.Calls.Count);
            finder.UpdateCurrentTarget(51);
            Assert.AreEqual(1, game.Calls.Count(c => c.Name == "ped.clearTasks"));
            game.Calls.Clear();
            finder.UpdateCurrentTarget(originNeighbor);
            Assert.AreEqual(1, game.Calls.Count(c => c.Name == "ped.missionPosition"),
                "Switching away from a ped must allow a position close to the world origin.");
            finder.Dispose();
        }

        [TestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public void MissingVehicleOrDriverIsCleanedUpAndRecreatedOnNextTick(bool missingDriver) {
            var game = CreateWorld();
            var finder = new Finder(game.Services);
            finder.OnTick(20);
            game.Observations[missingDriver ? 40 : 30].Exists = false;
            game.AddEntity(31);
            game.AddEntity(41);
            game.World.Vehicles.Enqueue(31);
            game.World.Drivers.Enqueue(41);
            game.Calls.Clear();
            finder.OnTick(20);
            Assert.IsFalse(game.Observations[30].Exists);
            Assert.IsFalse(game.Observations[40].Exists);
            CollectionAssert.AreEqual(new object[] { missingDriver ? 30 : 40 },
                game.Calls.Single(c => c.Name == "entity.delete").Arguments);
            Assert.IsTrue(finder.IsRelatedEntity(31));
            Assert.IsTrue(finder.IsRelatedEntity(41));
            Assert.AreEqual(1, game.Calls.Count(c => c.Name == "world.createVehicle"));
            finder.Dispose();
        }

        [TestMethod]
        public void ModelFailureLeavesNoResourcesAndCanRetry() {
            var game = CreateWorld();
            var finder = new Finder(game.Services);
            game.World.ModelResults[(int)VehicleHash.Thruster] = false;
            StringAssert.Contains(Assert.ThrowsExactly<InvalidOperationException>(() => finder.OnTick(20)).Message, "model");
            Assert.AreEqual(1, game.Calls.Count);
            Assert.IsNull(finder.Position);
            game.World.ModelResults[(int)VehicleHash.Thruster] = true;
            finder.OnTick(20);
            Assert.IsTrue(finder.IsRelatedEntity(30));
            finder.Dispose();
        }

        [TestMethod]
        public void MissingCollisionPartnerIsIgnoredWithoutTouchingLiveHelpers() {
            var game = CreateWorld();
            var finder = new Finder(game.Services);
            finder.OnTick(20);
            game.Observations[20].Exists = false;
            game.Calls.Clear();
            finder.SetNoCollision(20);
            Assert.AreEqual(0, game.Calls.Count);
            Assert.IsTrue(game.Entity(30).Exists);
            Assert.IsTrue(game.Entity(40).Exists);
            finder.Dispose();
        }

        [TestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public void PartialCreationFailureReleasesModelAndDeletesAnyCreatedVehicle(bool driverFailure) {
            var game = CreateWorld();
            game.World.Vehicles.Clear();
            game.World.Vehicles.Enqueue(driverFailure ? 30 : 0);
            game.World.Drivers.Clear();
            game.World.Drivers.Enqueue(0);
            var finder = new Finder(game.Services);
            var failure = Assert.ThrowsExactly<InvalidOperationException>(() => finder.OnTick(20));
            StringAssert.Contains(failure.Message, driverFailure ? "driver" : "vehicle");
            Assert.AreEqual("world.releaseModel", game.Calls.Last().Name);
            Assert.AreEqual(driverFailure ? 1 : 0, game.Calls.Count(c => c.Name == "entity.delete"));
            Assert.IsNull(finder.Position);
            Assert.IsFalse(finder.IsRelatedEntity(0));
            finder.Dispose();
        }

        [TestMethod]
        [DataRow(30)]
        [DataRow(40)]
        public void FailedHelperDeletionStillReleasesTheOtherHelperAndRetainsOnlyFailedOwnership(int failedHandle) {
            var game = CreateWorld();
            var finder = new Finder(game.Services);
            finder.OnTick(20);
            game.Calls.Clear();
            var failure = new InvalidOperationException("helper deletion failure");
            game.BeforeCall = call => {
                if (call.Name == "entity.delete" && (int)call.Arguments[0] == failedHandle) throw failure;
            };

            var error = Assert.ThrowsExactly<AggregateException>(() => finder.Dispose());

            Assert.AreSame(failure, error.Flatten().InnerExceptions.Single());
            CollectionAssert.AreEqual(new[] { 40, 30 },
                game.Calls.Where(call => call.Name == "entity.delete").Select(call => (int)call.Arguments[0]).ToArray());
            Assert.IsTrue(finder.IsRelatedEntity(failedHandle));
            var releasedHandle = failedHandle == 30 ? 40 : 30;
            Assert.IsFalse(finder.IsRelatedEntity(releasedHandle));
            Assert.IsFalse(game.Observations[releasedHandle].Exists);
            game.BeforeCall = null;
            game.Calls.Clear();
            finder.Dispose();
            finder.Dispose();
            Assert.IsFalse(game.Observations[failedHandle].Exists);
            Assert.IsFalse(finder.IsRelatedEntity(failedHandle));
            CollectionAssert.AreEqual(new[] { failedHandle },
                game.Calls.Where(call => call.Name == "entity.delete").Select(call => (int)call.Arguments[0]).ToArray());
        }

        [TestMethod]
        public void FailedPartialCreationCleanupReportsBothFailuresAndReleasesOldVehicleBeforeRetry() {
            var game = CreateWorld();
            game.World.Drivers.Clear();
            game.World.Drivers.Enqueue(0);
            var finder = new Finder(game.Services);
            var failure = new InvalidOperationException("vehicle deletion failure");
            game.BeforeCall = call => { if (call.Name == "entity.delete") throw failure; };

            var error = Assert.ThrowsExactly<AggregateException>(() => finder.OnTick(20));

            var failures = error.Flatten().InnerExceptions;
            Assert.HasCount(2, failures);
            StringAssert.Contains(failures[0].Message, "driver");
            Assert.AreSame(failure, failures[1]);
            Assert.AreEqual("world.releaseModel", game.Calls.Last().Name);
            Assert.IsTrue(finder.IsRelatedEntity(30));
            game.BeforeCall = null;
            game.Calls.Clear();
            game.AddEntity(31);
            game.AddEntity(41);
            game.World.Vehicles.Enqueue(31);
            game.World.Drivers.Enqueue(41);
            finder.OnTick(20);
            Assert.AreEqual("entity.delete", game.Calls.First().Name);
            Assert.AreEqual(30, game.Calls.First().Arguments[0]);
            Assert.IsFalse(game.Observations[30].Exists);
            Assert.IsTrue(finder.IsRelatedEntity(31));
            Assert.IsTrue(finder.IsRelatedEntity(41));
            finder.Dispose();
        }
    }
}
