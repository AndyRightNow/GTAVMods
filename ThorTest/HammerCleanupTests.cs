using System;
using System.Linq;
using GTA;
using GTA.Math;
using GTA.Native;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ModUtilsTest
{
    [TestClass]
    public class HammerCleanupTests
    {
        private static HammerFixture AttachedHammer()
        {
            var f = new HammerFixture();
            f.LooseHammer(Vector3.UnitY);
            f.Game.AddEntity(30);
            f.WeaponCreations.Enqueue(30);
            f.Game.Ropes.Creations.Enqueue(70);
            f.Hammer.AttachHammerRopeTo(10, Bone.PHRightHand);
            f.Game.Cameras.Creations.Enqueue(80);
            f.Hammer.RenderWeaponTrackCam();
            f.Game.Cameras.RenderingCamera = 80;
            f.Hammer.Whirl(new ADModUtils.Plane(Vector3.UnitZ, Vector3.Zero, new Random(1)));
            f.Hammer.OnTick();
            Assert.IsTrue(f.Audio.Clips["whirling-hammer.wav"].IsPlaying);
            f.Game.Calls.Clear();
            return f;
        }

        [TestMethod]
        public void SilentCameraAndRopeRefusalsKeepOnlyThoseHandlesForCleanupRetry()
        {
            var f = AttachedHammer();
            f.Game.Ropes.SilentDeletionRefusals.Add(70);
            f.Game.Cameras.SilentDeletionRefusals.Add(80);
            var error = Assert.ThrowsExactly<AggregateException>(() => f.Hammer.Dispose());

            CollectionAssert.AreEquivalent(new[]
            {
                "Rope 70 still exists after its deletion request.",
                "Camera 80 still exists after its deletion request."
            }, error.Flatten().InnerExceptions.Select(exception => exception.Message).ToArray());
            Assert.IsTrue(f.Game.Ropes.Ropes.Contains(70));
            Assert.IsTrue(f.Game.Cameras.Cameras.ContainsKey(80));
            Assert.IsFalse(f.Game.Observations[20].Exists);
            Assert.IsFalse(f.Game.Observations[30].Exists);
            Assert.AreEqual(0, f.Game.Cameras.RenderingCamera);
            Assert.AreEqual(1, f.Audio.Clips["whirling-hammer.wav"].Disposals);

            f.Game.Ropes.SilentDeletionRefusals.Clear();
            f.Game.Cameras.SilentDeletionRefusals.Clear();
            f.Game.Calls.Clear();
            f.Hammer.Dispose();
            f.Hammer.Dispose();
            CollectionAssert.AreEqual(new[] { "rope.delete:70", "camera.delete:80" }, Deletions(f));
            Assert.IsEmpty(f.Game.Ropes.Ropes);
            Assert.IsEmpty(f.Game.Cameras.Cameras);
            Assert.AreEqual(1, f.Audio.Clips["whirling-hammer.wav"].Disposals);
        }

        [TestMethod]
        [DataRow("entity.detach", 30)]
        [DataRow("entity.detach", 20)]
        [DataRow("native.SET_OBJECT_PHYSICS_PARAMS", 20)]
        public void CleanupContinuesAfterPreparationFailureAndReportsTheOriginalError(string operation, int entity)
        {
            var f = AttachedHammer();
            var failure = new InvalidOperationException("scripted native failure");
            f.Game.BeforeCall = call =>
            {
                if (call.Name == operation && (int)call.Arguments[0] == entity) throw failure;
            };

            var error = Assert.ThrowsExactly<AggregateException>(() => f.Hammer.Dispose());

            Assert.AreSame(failure, error.Flatten().InnerExceptions.Single());
            Assert.IsFalse(f.Game.Observations[30].Exists);
            Assert.IsFalse(f.Game.Observations[20].Exists);
            Assert.AreEqual(0, f.Hammer.WeaponObject);
            Assert.IsEmpty(f.Game.Ropes.Ropes);
            Assert.IsEmpty(f.Game.Cameras.Cameras);
            Assert.AreEqual(0, f.Game.Cameras.RenderingCamera);
            Assert.AreEqual(1, f.Audio.Clips["whirling-hammer.wav"].Disposals);
            CollectionAssert.AreEqual(new[] { "entity.delete:30", "rope.delete:70", "entity.delete:20", "camera.delete:80" },
                Deletions(f));
            Assert.IsTrue(f.Game.Entity(10).Exists);
            Assert.IsTrue(f.Game.Entity(40).Exists);
            Assert.IsTrue(f.Game.Entity(41).Exists);

            f.Game.BeforeCall = null;
            f.Hammer.Dispose();
            Assert.HasCount(4, Deletions(f));
            Assert.AreEqual(1, f.Audio.Clips["whirling-hammer.wav"].Disposals);
        }

        [TestMethod]
        public void MultipleCleanupFailuresRetainOwnershipAndRetryWithoutRepeatingSuccessfulAudioDisposal()
        {
            var f = AttachedHammer();
            var anchorFailure = new InvalidOperationException("anchor delete");
            var detachFailure = new InvalidOperationException("anchor detach");
            var ropeFailure = new InvalidOperationException("rope delete");
            var hammerFailure = new InvalidOperationException("hammer delete");
            var cameraFailure = new InvalidOperationException("camera delete");
            f.Game.BeforeCall = call =>
            {
                if (call.Name == "entity.detach" && (int)call.Arguments[0] == 30) throw detachFailure;
                if (call.Name == "entity.delete") throw (int)call.Arguments[0] == 30 ? anchorFailure : hammerFailure;
                if (call.Name == "rope.delete") throw ropeFailure;
                if (call.Name == "camera.delete") throw cameraFailure;
            };

            var error = Assert.ThrowsExactly<AggregateException>(() => f.Hammer.Dispose());

            CollectionAssert.AreEquivalent(new[] { detachFailure, anchorFailure, ropeFailure, hammerFailure, cameraFailure },
                error.Flatten().InnerExceptions.ToArray());
            Assert.IsTrue(f.Game.Entity(30).Exists);
            Assert.AreEqual(10, f.Game.Entity(30).AttachedTo);
            Assert.IsTrue(f.Game.Entity(20).Exists);
            Assert.AreEqual(0, f.Hammer.WeaponObject);
            Assert.IsTrue(f.Game.Ropes.Ropes.Contains(70));
            Assert.IsTrue(f.Game.Cameras.Cameras.ContainsKey(80));
            Assert.AreEqual(1, f.Audio.Clips["whirling-hammer.wav"].Disposals);
            Assert.HasCount(4, Deletions(f));
            f.Game.BeforeCall = null;
            f.Hammer.Dispose();

            Assert.IsFalse(f.Game.Observations[30].Exists);
            Assert.IsFalse(f.Game.Observations[20].Exists);
            Assert.AreEqual(0, f.Hammer.WeaponObject);
            Assert.IsEmpty(f.Game.Ropes.Ropes);
            Assert.IsEmpty(f.Game.Cameras.Cameras);
            Assert.AreEqual(1, f.Audio.Clips["whirling-hammer.wav"].Disposals);
            Assert.HasCount(8, Deletions(f));
            f.Hammer.Dispose();
            Assert.HasCount(8, Deletions(f));
        }

        [TestMethod]
        public void CleanupForgetsEntitiesAlreadyRemovedByTheGameAndStillReleasesRopeAndCamera()
        {
            var f = AttachedHammer();
            f.Game.Observations[20].Exists = false;
            f.Game.Observations[30].Exists = false;

            f.Hammer.Dispose();
            f.Hammer.Dispose();

            Assert.AreEqual(0, f.Hammer.WeaponObject);
            CollectionAssert.AreEqual(new[] { "rope.delete:70", "camera.delete:80" }, Deletions(f));
            Assert.IsFalse(f.Game.Calls.Any(call => call.Name == "entity.detach"));
            Assert.IsFalse(f.Game.Calls.Any(call => call.Name == "native.SET_OBJECT_PHYSICS_PARAMS"),
                "Cleanup must not reconfigure physics on a hammer already removed by the engine.");
            Assert.IsEmpty(f.Game.Ropes.Ropes);
            Assert.IsEmpty(f.Game.Cameras.Cameras);
            Assert.AreEqual(1, f.Audio.Clips["whirling-hammer.wav"].Disposals);
        }

        [TestMethod]
        [DataRow("native.ACTIVATE_PHYSICS")]
        [DataRow("native.SET_OBJECT_PHYSICS_PARAMS")]
        [DataRow("blip.name")]
        public void InitializationFailureAfterCreationKeepsTheNewHammerOwnedForCleanup(string operation)
        {
            var f = new HammerFixture();
            f.WeaponCreations.Enqueue(20);
            f.Game.Entities.CreatedBlips.Enqueue(50);
            var failure = new InvalidOperationException("setup failed");
            f.Game.BeforeCall = call => { if (call.Name == operation) throw failure; };

            var error = Assert.ThrowsExactly<AggregateException>(() => f.Hammer.Init(Vector3.UnitY));

            Assert.AreSame(failure, error.Flatten().InnerExceptions.Single());
            Assert.HasCount(1, f.Game.Native.Calls.Where(call => call.Hash == Hash.CREATE_WEAPON_OBJECT).ToArray());
            Assert.AreEqual(20, f.Hammer.WeaponObject);
            Assert.IsTrue(f.Game.Entity(20).Exists);
            Assert.HasCount(1, f.Game.Calls.Where(call => call.Name == "host.notify").ToArray());
            f.Game.BeforeCall = null;
            f.Hammer.Dispose();
            f.Hammer.Dispose();
            Assert.IsFalse(f.Game.Observations[20].Exists);
            CollectionAssert.AreEqual(new[] { "entity.delete:20" }, Deletions(f));
        }

        [TestMethod]
        public void ReplacementRetainsBothHandlesWhenOldDeletionAndNewConfigurationFailThenDisposesEachOnce()
        {
            var f = new HammerFixture();
            f.LooseHammer(Vector3.UnitY);
            f.Game.AddEntity(60);
            f.WeaponCreations.Enqueue(60);
            var oldDeleteFailure = new InvalidOperationException("old hammer deletion");
            var newSetupFailure = new InvalidOperationException("new hammer activation");
            f.Game.BeforeCall = call =>
            {
                if (call.Name == "entity.delete" && (int)call.Arguments[0] == 20) throw oldDeleteFailure;
                if (call.Name == "native.ACTIVATE_PHYSICS" && (int)call.Arguments[0] == 60) throw newSetupFailure;
            };

            var error = Assert.ThrowsExactly<AggregateException>(() => f.Hammer.Init(Vector3.UnitX, true));

            CollectionAssert.AreEquivalent(new[] { oldDeleteFailure, newSetupFailure },
                error.Flatten().InnerExceptions.ToArray());
            Assert.AreEqual(60, f.Hammer.WeaponObject);
            Assert.IsTrue(f.Game.Entity(20).Exists);
            Assert.IsTrue(f.Game.Entity(60).Exists);

            f.Game.BeforeCall = null;
            f.Game.Calls.Clear();
            f.Hammer.Dispose();
            f.Hammer.Dispose();

            Assert.AreEqual(0, f.Hammer.WeaponObject);
            Assert.IsFalse(f.Game.Observations[20].Exists);
            Assert.IsFalse(f.Game.Observations[60].Exists);
            CollectionAssert.AreEquivalent(new[] { "entity.delete:20", "entity.delete:60" }, Deletions(f));
        }

        private static string[] Deletions(HammerFixture f) => f.Game.Calls
            .Where(call => call.Name == "entity.delete" || call.Name == "rope.delete" || call.Name == "camera.delete")
            .Select(call => call.Name + ":" + call.Arguments[0]).ToArray();
    }
}
