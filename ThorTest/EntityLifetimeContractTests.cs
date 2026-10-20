using System;
using System.Collections.Generic;
using System.Linq;
using GTAVMods.Game;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ModUtilsTest
{
    [TestClass]
    public class EntityLifetimeContractTests
    {
        private sealed class RawEntities : IEntityLifetimeBindings, ICameraLifetimeBindings, IRopeLifetimeBindings
        {
            public readonly List<string> Calls = new List<string>();
            public bool Present = true, RefuseDeletion;
            public Exception DeletionFailure, ObservationFailure, ConfirmationFailure;
            public bool Exists(int entity)
            {
                Calls.Add("exists:" + entity);
                if (ObservationFailure != null) throw ObservationFailure;
                return Present;
            }
            public void Delete(int entity)
            {
                Calls.Add("delete:" + entity);
                if (DeletionFailure != null) throw DeletionFailure;
                if (!RefuseDeletion) Present = false;
                ObservationFailure = ConfirmationFailure;
            }
        }

        [TestMethod]
        public void RealAdapterObservesAbsenceAfterDeletionAndSkipsAlreadyAbsentEntities()
        {
            var raw = new RawEntities();
            var entities = new GtaEntities(raw);
            entities.Delete(71);
            Assert.IsFalse(raw.Present);
            CollectionAssert.AreEqual(new[] { "exists:71", "delete:71", "exists:71" }, raw.Calls);
            raw.Calls.Clear();
            entities.Delete(71);
            Assert.IsFalse(entities.Exists(71));
            CollectionAssert.AreEqual(new[] { "exists:71", "exists:71" }, raw.Calls);
        }

        [TestMethod]
        public void RealAdapterTurnsSilentRefusalIntoHandleSpecificFailureAndAllowsRetry()
        {
            var raw = new RawEntities { RefuseDeletion = true };
            var entities = new GtaEntities(raw);
            var failure = Assert.ThrowsExactly<InvalidOperationException>(() => entities.Delete(73));
            StringAssert.Contains(failure.Message, "Entity 73 still exists");
            Assert.IsTrue(raw.Present);
            CollectionAssert.AreEqual(new[] { "exists:73", "delete:73", "exists:73" }, raw.Calls);
            raw.RefuseDeletion = false;
            raw.Calls.Clear();
            entities.Delete(73);
            Assert.IsFalse(raw.Present);
            CollectionAssert.AreEqual(new[] { "exists:73", "delete:73", "exists:73" }, raw.Calls);
        }

        [TestMethod]
        public void RealAdapterPreservesHostExceptionsAndReobservesBeforeEachRetry()
        {
            var deletionFailure = new InvalidOperationException("Host delete failed.");
            var raw = new RawEntities { DeletionFailure = deletionFailure };
            var entities = new GtaEntities(raw);
            Assert.AreSame(deletionFailure, Assert.ThrowsExactly<InvalidOperationException>(() => entities.Delete(79)));
            Assert.IsTrue(raw.Present);
            CollectionAssert.AreEqual(new[] { "exists:79", "delete:79" }, raw.Calls);
            raw.DeletionFailure = null;
            raw.Calls.Clear();
            entities.Delete(79);
            Assert.IsFalse(raw.Present);
            CollectionAssert.AreEqual(new[] { "exists:79", "delete:79", "exists:79" }, raw.Calls);

            raw.Calls.Clear();
            var observationFailure = new InvalidOperationException("Host existence query failed.");
            raw.ObservationFailure = observationFailure;
            Assert.AreSame(observationFailure, Assert.ThrowsExactly<InvalidOperationException>(() => entities.Delete(79)));
            CollectionAssert.AreEqual(new[] { "exists:79" }, raw.Calls);
            raw.ObservationFailure = null;
            raw.Calls.Clear();
            entities.Delete(79);
            CollectionAssert.AreEqual(new[] { "exists:79" }, raw.Calls);

            raw.Present = true;
            raw.Calls.Clear();
            raw.ConfirmationFailure = new InvalidOperationException("Confirmation query failed.");
            Assert.AreSame(raw.ConfirmationFailure, Assert.ThrowsExactly<InvalidOperationException>(() => entities.Delete(79)));
            Assert.IsFalse(raw.Present, "The deletion can succeed even when its confirmation query fails.");
            CollectionAssert.AreEqual(new[] { "exists:79", "delete:79", "exists:79" }, raw.Calls);
            raw.ObservationFailure = null;
            raw.Calls.Clear();
            entities.Delete(79);
            CollectionAssert.AreEqual(new[] { "exists:79" }, raw.Calls, "Retry must observe absence instead of deleting twice.");
        }

        [TestMethod]
        public void FakeUsesTheSameConfirmationContractForRefusalFaultRetryAndAbsentHandles()
        {
            var game = new FakeGame();
            var observation = game.AddEntity(83);
            game.Entities.SilentDeletionRefusals.Add(83);
            var refused = Assert.ThrowsExactly<InvalidOperationException>(() => game.Services.Entities.Delete(83));
            StringAssert.Contains(refused.Message, "Entity 83 still exists");
            Assert.IsTrue(observation.Exists);

            game.Entities.SilentDeletionRefusals.Remove(83);
            var failure = new InvalidOperationException("Delete hook failed.");
            game.BeforeCall = call => { if (call.Name == "entity.delete") throw failure; };
            Assert.AreSame(failure, Assert.ThrowsExactly<InvalidOperationException>(() => game.Services.Entities.Delete(83)));
            Assert.IsTrue(observation.Exists);
            game.BeforeCall = null;
            game.Services.Entities.Delete(83);
            Assert.IsFalse(observation.Exists);
            game.Services.Entities.Delete(83);
            game.Services.Entities.Delete(0);
            Assert.HasCount(3, game.Calls);
            CollectionAssert.AreEqual(new[] { "entity.delete", "entity.delete", "entity.delete" },
                game.Calls.Select(call => call.Name).ToArray());
            foreach (var call in game.Calls) CollectionAssert.AreEqual(new object[] { 83 }, call.Arguments);
        }

        [TestMethod]
        [DataRow("Camera")]
        [DataRow("Rope")]
        public void CameraAndRopeAdaptersConfirmDeletionAndRetainRefusedOrFailedRequests(string resource)
        {
            var raw = new RawEntities { RefuseDeletion = true };
            Action<int> delete = resource == "Camera"
                ? new GtaCameras(raw).Delete
                : new GtaRopes(raw).Delete;
            var refused = Assert.ThrowsExactly<InvalidOperationException>(() => delete(89));
            StringAssert.Contains(refused.Message, resource + " 89 still exists");
            CollectionAssert.AreEqual(new[] { "exists:89", "delete:89", "exists:89" }, raw.Calls);
            Assert.IsTrue(raw.Present);

            raw.RefuseDeletion = false;
            raw.DeletionFailure = new InvalidOperationException("Host deletion failed.");
            raw.Calls.Clear();
            Assert.AreSame(raw.DeletionFailure, Assert.ThrowsExactly<InvalidOperationException>(() => delete(89)));
            CollectionAssert.AreEqual(new[] { "exists:89", "delete:89" }, raw.Calls);
            raw.DeletionFailure = null;
            raw.Calls.Clear();
            delete(89);
            Assert.IsFalse(raw.Present);
            CollectionAssert.AreEqual(new[] { "exists:89", "delete:89", "exists:89" }, raw.Calls);
            raw.Calls.Clear();
            delete(89);
            CollectionAssert.AreEqual(new[] { "exists:89" }, raw.Calls);
        }

        [TestMethod]
        [DataRow(true)]
        [DataRow(false)]
        public void CameraAndRopeFakesShareConfirmationWithoutCrossingHandleDomains(bool camera)
        {
            var game = new FakeGame();
            game.Cameras.Cameras.Add(97, new CameraObservation());
            game.Ropes.Ropes.Add(97);
            var refusals = camera ? game.Cameras.SilentDeletionRefusals : game.Ropes.SilentDeletionRefusals;
            Action<int> delete = camera ? game.Services.Cameras.Delete : game.Services.Ropes.Delete;
            string operation = camera ? "camera.delete" : "rope.delete";
            refusals.Add(97);
            Assert.ThrowsExactly<InvalidOperationException>(() => delete(97));
            Assert.IsTrue(game.Cameras.Cameras.ContainsKey(97));
            Assert.IsTrue(game.Ropes.Ropes.Contains(97));
            refusals.Clear();
            delete(97);
            delete(97);
            delete(0);
            Assert.AreEqual(!camera, game.Cameras.Cameras.ContainsKey(97));
            Assert.AreEqual(camera, game.Ropes.Ropes.Contains(97));
            CollectionAssert.AreEqual(new[] { operation, operation }, game.Calls.Select(call => call.Name).ToArray());
        }
    }
}
