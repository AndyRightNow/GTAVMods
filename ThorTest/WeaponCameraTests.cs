using System;
using System.Collections.Generic;
using System.Linq;
using GTA.Math;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Thor;

namespace ModUtilsTest {
    [TestClass]
    public class WeaponCameraTests {
        private sealed class CameraRandom : Random {
            internal readonly Queue<int> Choices = new Queue<int>();
            internal readonly List<int> Bounds = new List<int>();
            internal CameraRandom(params int[] choices) { foreach (var choice in choices) Choices.Enqueue(choice); }
            public override int Next(int maxValue) {
                Bounds.Add(maxValue);
                var result = Choices.Dequeue();
                Assert.IsTrue(result >= 0 && result < maxValue, "Random choice must satisfy the real API range.");
                return result;
            }
            public override int Next(int minimum, int maximum) {
                Assert.AreEqual(0, minimum);
                return Next(maximum);
            }
            public override double NextDouble() { return .5; }
        }
        private static FakeGame CreateWorld() {
            var game = new FakeGame();
            var weapon = game.AddEntity(20);
            weapon.Position = new Vector3(10, 20, 30);
            weapon.Velocity = new Vector3(0, 0, 10);
            game.Cameras.Creations.Enqueue(30);
            return game;
        }

        [TestMethod]
        [DataRow(0, 0, 5f, -1)]
        [DataRow(1, 1, 70f, 1)]
        [DataRow(1, 2, 70f, 1)]
        public void FollowViewsTrackPositionAtConfiguredDistanceWithoutResettingBeforeTwoSeconds(int sign, int view, float distance, int side) {
            var game = CreateWorld();
            var random = new CameraRandom(sign, view, 1, 1, sign, sign, 0, 1, 1);
            var camera = new WeaponCamera(game.Services, random);
            camera.RenderCamera(20);
            var expectedOffset = new Vector3(1, 1, 0).Normalized * (distance * side);
            AssertVector(game.Entity(20).Position + expectedOffset, game.Cameras.Cameras[30].Position);
            CollectionAssert.AreEqual(new object[] { new Vector3(10, 20, 30), Vector3.Zero, 50f, 30 },
                game.Calls.First().Arguments);
            Assert.IsTrue(game.Cameras.Cameras[30].Active);
            CollectionAssert.AreEqual(new[] { "camera.pointAt", "camera.active", "camera.startRendering" },
                game.Calls.Skip(game.Calls.Count - 3).Select(c => c.Name).ToArray());
            CollectionAssert.AreEqual(new object[] { 30, 20 }, game.Calls.Single(c => c.Name == "camera.pointAt").Arguments);
            game.Entity(20).Position += new Vector3(1, 2, 3);
            game.Host.GameTime = 1999;
            camera.RenderCamera(20);
            AssertVector(game.Entity(20).Position + expectedOffset, game.Cameras.Cameras[30].Position);
            CollectionAssert.AreEqual(new[] { 2, 7, 2, 2, 2 }, random.Bounds);
            game.Host.GameTime = 2000;
            camera.RenderCamera(20);
            CollectionAssert.AreEqual(new[] { 2, 7, 2, 2, 2, 2, 7, 2, 2 }, random.Bounds);
            var resetOffset = new Vector3(1, 1, 0).Normalized * (5 * side);
            AssertVector(game.Entity(20).Position + resetOffset, game.Cameras.Cameras[30].Position);
            camera.DestroyCamera();
        }

        [TestMethod]
        [DataRow(3)] [DataRow(4)] [DataRow(5)] [DataRow(6)]
        public void StationaryViewsHoldLocationWhileContinuingToTrackWeapon(int view) {
            var game = CreateWorld();
            var camera = new WeaponCamera(game.Services, new CameraRandom(1, view, 1, 1, 0, 1, view, 1, 1));
            camera.RenderCamera(20);
            var expected = new Vector3(10, 20, 80) + new Vector3(1, 1, 0).Normalized * 5;
            AssertVector(expected, game.Cameras.Cameras[30].Position);
            game.Calls.Clear();
            game.Entity(20).Position += Vector3.One;
            game.Host.GameTime = 1999;
            camera.RenderCamera(20);
            AssertVector(expected, game.Cameras.Cameras[30].Position);
            Assert.IsFalse(game.Calls.Any(call => call.Name == "camera.position"));
            Assert.IsTrue(game.Calls.Any(call => call.Name == "camera.pointAt"));
            game.Host.GameTime = 2000;
            camera.RenderCamera(20);
            AssertVector(expected + Vector3.One, game.Cameras.Cameras[30].Position);
            camera.DestroyCamera();
        }

        [TestMethod]
        public void CreationFailureDoesNotConsumeRandomnessAndCanRetry() {
            var game = CreateWorld();
            game.Cameras.Creations.Clear();
            game.Cameras.Creations.Enqueue(0);
            game.Cameras.Creations.Enqueue(30);
            var random = new CameraRandom(1, 0, 1, 1);
            var camera = new WeaponCamera(game.Services, random);
            camera.CancelRenderedCamera();
            camera.DestroyCamera();
            Assert.AreEqual(0, game.Calls.Count);
            camera.RenderCamera(20);
            Assert.AreEqual(1, game.Calls.Count);
            Assert.AreEqual("camera.create", game.Calls[0].Name);
            Assert.AreEqual(0, random.Bounds.Count);
            camera.RenderCamera(20);
            Assert.AreEqual(1, game.Cameras.Cameras.Count);
            camera.DestroyCamera();
            Assert.AreEqual(0, game.Cameras.Cameras.Count);
        }

        [TestMethod]
        public void CancelOnlyStopsOwnedRenderingCameraAndDestroyReinitializesItsNextView() {
            var game = CreateWorld();
            var camera = new WeaponCamera(game.Services, new CameraRandom(1, 3, 1, 1, 0, 0, 1, 1));
            camera.RenderCamera(20);
            game.Cameras.RenderingCamera = 99;
            game.Calls.Clear();
            camera.CancelRenderedCamera();
            Assert.AreEqual(0, game.Calls.Count, "Another script's rendering camera must remain untouched.");
            game.Cameras.RenderingCamera = 30;
            camera.DestroyCamera();
            CollectionAssert.AreEqual(new[] { "camera.stopRendering", "camera.active", "camera.delete" },
                game.Calls.Select(c => c.Name).ToArray());
            CollectionAssert.AreEqual(new object[] { 30, false }, game.Calls[1].Arguments);
            Assert.AreEqual(0, game.Cameras.RenderingCamera);
            Assert.AreEqual(0, game.Cameras.Cameras.Count);
            game.Cameras.Creations.Enqueue(31);
            game.Host.GameTime = 100;
            camera.RenderCamera(20);
            AssertVector(new Vector3(10, 20, 30) - new Vector3(1, 1, 0).Normalized * 5,
                game.Cameras.Cameras[31].Position);
            camera.DestroyCamera();
        }

        [TestMethod]
        public void ResetIntervalHandlesSignedGameClockWraparound() {
            var game = CreateWorld();
            game.Host.GameTime = int.MaxValue - 1000;
            var camera = new WeaponCamera(game.Services, new CameraRandom(1, 3, 1, 1, 1, 0, 1, 1));
            camera.RenderCamera(20);
            game.Host.GameTime = int.MinValue + 1000;
            camera.RenderCamera(20);
            AssertVector(new Vector3(10, 20, 30) + new Vector3(1, 1, 0).Normalized * 5,
                game.Cameras.Cameras[30].Position);
            camera.DestroyCamera();
        }

        [TestMethod]
        [DataRow("camera.stopRendering")]
        [DataRow("camera.active")]
        public void FailedRenderingCleanupStillDeletesCameraAndResetsTheNextStationaryView(string operation) {
            var game = CreateWorld();
            var camera = new WeaponCamera(game.Services, new CameraRandom(1, 3, 1, 1, 1, 3, 1, 1));
            camera.RenderCamera(20);
            game.Cameras.RenderingCamera = 30;
            game.Calls.Clear();
            var failure = new InvalidOperationException("rendering cleanup failure");
            game.BeforeCall = call => { if (call.Name == operation) throw failure; };

            var error = Assert.ThrowsExactly<AggregateException>(() => camera.DestroyCamera());

            Assert.AreSame(failure, error.Flatten().InnerExceptions.Single());
            CollectionAssert.AreEqual(new[] { "camera.stopRendering", "camera.active", "camera.delete" },
                game.Calls.Select(call => call.Name).ToArray());
            Assert.IsEmpty(game.Cameras.Cameras);
            game.BeforeCall = null;
            camera.DestroyCamera();
            Assert.HasCount(3, game.Calls);
            game.Cameras.Creations.Enqueue(31);
            game.Host.GameTime = 100;
            camera.RenderCamera(20);
            AssertVector(new Vector3(10, 20, 80) + new Vector3(1, 1, 0).Normalized * 5,
                game.Cameras.Cameras[31].Position);
            camera.DestroyCamera();
        }

        [TestMethod]
        public void FailedCameraDeletionRetainsOwnershipAndRetriesOnlyTheRemainingRelease() {
            var game = CreateWorld();
            var camera = new WeaponCamera(game.Services, new CameraRandom(1, 0, 1, 1));
            camera.RenderCamera(20);
            game.Cameras.RenderingCamera = 30;
            game.Calls.Clear();
            var failure = new InvalidOperationException("camera deletion failure");
            game.BeforeCall = call => { if (call.Name == "camera.delete") throw failure; };

            var error = Assert.ThrowsExactly<AggregateException>(() => camera.DestroyCamera());

            Assert.AreSame(failure, error.Flatten().InnerExceptions.Single());
            Assert.IsTrue(game.Cameras.Cameras.ContainsKey(30));
            Assert.IsFalse(game.Cameras.Cameras[30].Active);
            Assert.AreEqual(0, game.Cameras.RenderingCamera);
            game.BeforeCall = null;
            game.Calls.Clear();
            camera.DestroyCamera();
            camera.DestroyCamera();
            Assert.IsEmpty(game.Cameras.Cameras);
            CollectionAssert.AreEqual(new[] { "camera.delete" }, game.Calls.Select(call => call.Name).ToArray());
        }

        private static void AssertVector(Vector3 expected, Vector3 actual) {
            Assert.AreEqual(expected.X, actual.X, .0001);
            Assert.AreEqual(expected.Y, actual.Y, .0001);
            Assert.AreEqual(expected.Z, actual.Z, .0001);
        }
    }
}
