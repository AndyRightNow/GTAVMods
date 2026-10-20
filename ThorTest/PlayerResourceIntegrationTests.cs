using System;
using System.Linq;
using System.Windows.Forms;
using GTA;
using GTA.Math;
using GTA.Native;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Control = GTA.Control;

namespace ModUtilsTest
{
    [TestClass, TestCategory("PlayerIntegration")]
    public class PlayerResourceIntegrationTests
    {
        [TestMethod]
        public void RemovingThePlayerEntityReleasesHoverObjectsWithoutRestoringADeletedCharacter()
        {
            var s = new PlayerSessionFixture();
            s.Frame(0);
            Assert.AreEqual(30000, s.Player.MaxHealth);
            s.Game.Observations[10].Exists = false;
            s.Game.Calls.Clear();
            s.Frame(0);

            Assert.IsFalse(s.Game.Observations[21].Exists);
            Assert.IsFalse(s.Game.Observations[22].Exists);
            Assert.IsFalse(s.Game.Calls.Any(call =>
                new[] { "entity.health", "ped.maxHealth", "entity.invincible" }.Contains(call.Name) &&
                (int)call.Arguments[0] == 10));
            Assert.IsTrue(s.Game.Observations[20].Exists,
                "The game-owned held object must not be acquired from a deleted character.");
            StringAssert.Contains(s.State(), "attached=False");
            Assert.IsFalse(s.Log.Records.Any(record => record.Name == "cleanup.exception"));
        }

        private static PlayerSessionFixture Whirling(bool grab = false)
        {
            var s = new PlayerSessionFixture();
            s.Player.MaxHealth = 250;
            s.Frame(0);
            if (grab)
            {
                s.Game.AddEntity(60);
                s.Game.World.Nearby = (position, radius) => new[] { 60 };
                s.Game.Input.Pressed.Add(Control.ThrowGrenade);
                s.Tick();
                s.Game.Input.Pressed.Clear();
                Assert.AreEqual(10, s.Game.Entity(60).AttachedTo);
            }
            s.Game.Ropes.Creations.Enqueue(70);
            s.Frame(0, Keys.Z);
            s.Game.Cameras.Creations.Enqueue(80);
            s.Game.Input.Keys.Add(Keys.R);
            s.Tick();
            s.Game.Cameras.RenderingCamera = 80;
            s.Player.Health = 5000;
            Assert.IsTrue(s.Audio.Clips["whirling-hammer.wav"].IsPlaying);
            s.Game.Calls.Clear();
            return s;
        }

        [TestMethod]
        public void MissingMapMarkerDoesNotPreventDropRecallEffectsOrCleanup()
        {
            var s = new PlayerSessionFixture();
            s.Game.Entities.CreatedBlips.Clear();
            s.Game.Entities.CreatedBlips.Enqueue(0);
            s.Frame(0);
            s.Game.Entity(20).CollisionEnabled = false;
            s.Frame(0, Keys.Y);
            Assert.IsTrue(s.Game.Entity(20).CollisionEnabled);
            CollectionAssert.AreEqual(new object[] { 20, 50000f, -1f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f },
                s.LastNative(Hash.SET_OBJECT_PHYSICS_PARAMS));
            CollectionAssert.AreEqual(new object[] { 10, false }, s.LastNative(Hash.GET_WEAPON_OBJECT_FROM_PED));
            Assert.IsFalse(s.Game.Calls.Any(call => call.Name == "blip.name" || call.Name == "blip.friendly"));
            CollectionAssert.AreEquivalent(new[] { 40, 41 }, s.Game.Calls
                .Where(call => call.Name == "entity.noCollision" && (int)call.Arguments[1] == 20)
                .Select(call => (int)call.Arguments[0]).Distinct().ToArray());
            s.Frame(1000);
            Assert.IsTrue(s.Game.Native.Calls.Any(call => call.Hash == Hash.START_PARTICLE_FX_NON_LOOPED_ON_ENTITY &&
                (int)call.Arguments[1] == 20));
            s.Game.Entity(20).Position = new Vector3(0, -30, 1);
            s.Frame(0, Keys.H);
            Assert.AreEqual(Vector3.UnitY * 100, s.Game.Entity(20).Velocity);
            s.Reload();
            s.AssertReleased();
        }

        [TestMethod]
        public void DisablingWhileWhirlingAndGrabbingRestoresThePlayerAndRetriesIndependentFailures()
        {
            var s = Whirling(grab: true);
            s.Game.BeforeCall = call =>
            {
                if (call.Name == "ped.maxHealth" && (int)call.Arguments[1] == 250)
                    throw new InvalidOperationException("restore health failed");
                if (call.Name == "entity.detach" && (int)call.Arguments[0] == 60)
                    throw new InvalidOperationException("release pedestrian failed");
                if (call.Name == "entity.delete" && (int)call.Arguments[0] == 21)
                    throw new InvalidOperationException("release hover hammer failed");
            };
            s.Disable();
            var failure = (string)s.Log.Last("cleanup.exception").Data["exception"];
            foreach (var message in new[] { "restore health failed", "release pedestrian failed", "release hover hammer failed" })
                StringAssert.Contains(failure, message);
            Assert.AreEqual(30000, s.Player.MaxHealth);
            Assert.AreEqual(250, s.Player.Health);
            Assert.AreEqual(10, s.Game.Entity(60).AttachedTo);
            Assert.IsTrue(s.Game.Entity(21).Exists);
            Assert.IsFalse(s.Game.Observations[22].Exists);
            Assert.IsFalse(s.Game.Observations[23].Exists);
            Assert.IsEmpty(s.Game.Ropes.Ropes);
            Assert.IsEmpty(s.Game.Cameras.Cameras);
            Assert.IsFalse(s.Audio.Clips["whirling-hammer.wav"].IsPlaying);

            s.Game.BeforeCall = null;
            s.Chord(Keys.O);
            Assert.AreEqual(250, s.Player.MaxHealth);
            Assert.AreEqual(0, s.Game.Entity(60).AttachedTo);
            Assert.IsFalse(s.Game.Observations[21].Exists);
            Assert.IsFalse(s.Game.Observations[20].Exists);
            Assert.HasCount(2, s.Game.Calls.Where(call => call.Name == "entity.delete" && (int)call.Arguments[0] == 21).ToArray());
            Assert.HasCount(1, s.Game.Calls.Where(call => call.Name == "entity.delete" && (int)call.Arguments[0] == 22).ToArray());
        }

        [TestMethod]
        [DataRow("entity.detach", 23)]
        [DataRow("entity.detach", 20)]
        [DataRow("native.SET_OBJECT_PHYSICS_PARAMS", 20)]
        [DataRow("camera.stopRendering", 0)]
        [DataRow("camera.active", 80)]
        public void ScriptReloadReleasesEveryOwnedResourceWhenOnePreparationOperationFails(string operation, int handle)
        {
            var s = Whirling();
            s.Game.BeforeCall = call =>
            {
                if (call.Name == operation && (handle == 0 || (int)call.Arguments[0] == handle))
                    throw new InvalidOperationException("engine cleanup unavailable");
            };
            s.Reload();
            Assert.IsFalse(s.Game.Observations[20].Exists);
            Assert.IsFalse(s.Game.Observations[21].Exists);
            Assert.IsFalse(s.Game.Observations[22].Exists);
            Assert.IsFalse(s.Game.Observations[23].Exists);
            Assert.IsFalse(s.Game.Observations[40].Exists);
            Assert.IsFalse(s.Game.Observations[41].Exists);
            Assert.IsEmpty(s.Game.Ropes.Ropes);
            Assert.IsEmpty(s.Game.Cameras.Cameras);
            Assert.AreEqual(250, s.Player.MaxHealth);
            Assert.IsTrue(s.Audio.Clips.Values.All(clip => clip.Disposals == 1 && !clip.IsPlaying));
            Assert.IsTrue(s.Log.Records.Any(record => record.Name == "cleanup.exception" &&
                ((string)record.Data["exception"]).Contains("engine cleanup unavailable")));
            Assert.AreEqual("session.end", s.Log.Records.Last().Name);
            s.Game.BeforeCall = null;
            int deletes = s.Game.Calls.Count(call => call.Name == "entity.delete");
            s.Reload();
            Assert.AreEqual(deletes, s.Game.Calls.Count(call => call.Name == "entity.delete"));
            Assert.IsTrue(s.Audio.Clips.Values.All(clip => clip.Disposals == 1));
        }

        [TestMethod]
        public void FailedRopeAndCameraDeletionRemainOwnedUntilTheNextReloadAttempt()
        {
            var s = Whirling();
            s.Game.Ropes.SilentDeletionRefusals.Add(70);
            s.Game.Cameras.SilentDeletionRefusals.Add(80);
            s.Reload();
            Assert.IsTrue(s.Game.Ropes.Ropes.Contains(70));
            Assert.IsTrue(s.Game.Cameras.Cameras.ContainsKey(80));
            Assert.IsFalse(s.Game.Observations[20].Exists);
            Assert.IsFalse(s.Game.Observations[23].Exists);
            Assert.AreEqual(0, s.Game.Cameras.RenderingCamera);
            Assert.IsTrue(s.Audio.Clips.Values.All(clip => clip.Disposals == 1));
            s.Game.Ropes.SilentDeletionRefusals.Clear();
            s.Game.Cameras.SilentDeletionRefusals.Clear();
            s.Game.Calls.Clear();
            s.Reload();
            CollectionAssert.AreEquivalent(new[] { "rope.delete", "camera.delete" },
                s.Game.Calls.Where(call => call.Name.EndsWith(".delete", StringComparison.Ordinal)).Select(call => call.Name).ToArray());
            Assert.IsEmpty(s.Game.Ropes.Ropes);
            Assert.IsEmpty(s.Game.Cameras.Cameras);
        }

        [TestMethod]
        public void CleanupDoesNotAddressHammerOrAnchorAlreadyDeletedByTheGame()
        {
            var s = Whirling();
            s.Game.Observations[20].Exists = false;
            s.Game.Observations[23].Exists = false;
            s.Reload();
            Assert.IsFalse(s.Game.Calls.Any(call => (call.Name == "entity.detach" || call.Name == "entity.delete") &&
                new[] { 20, 23 }.Contains((int)call.Arguments[0])));
            Assert.IsEmpty(s.Game.Ropes.Ropes);
            Assert.IsEmpty(s.Game.Cameras.Cameras);
        }

        [TestMethod]
        [DataRow("native.ACTIVATE_PHYSICS")]
        [DataRow("native.SET_OBJECT_PHYSICS_PARAMS")]
        [DataRow("blip.name")]
        public void DroppingPausesAfterConfigurationFailureAndReloadStillDeletesTheAcquiredHammer(string operation)
        {
            var s = new PlayerSessionFixture();
            s.Frame(0);
            s.Game.BeforeCall = call =>
            {
                if (call.Name == operation) throw new InvalidOperationException("hammer setup unavailable");
            };
            s.Frame(0, Keys.Y, expectFailure: true);
            Assert.IsTrue(s.Game.Entity(20).Exists);
            Assert.AreEqual(200, s.Player.MaxHealth);
            StringAssert.Contains((string)s.Log.Last("tick.exception").Data["exception"], "hammer setup unavailable");
            s.Game.BeforeCall = null;
            s.Reload();
            Assert.IsFalse(s.Game.Observations[20].Exists);
            Assert.HasCount(1, s.Game.Calls.Where(call => call.Name == "entity.delete" && (int)call.Arguments[0] == 20).ToArray());
        }

        [TestMethod]
        public void ANewlyObservedHeldHammerRemainsOwnedWhenRetiringThePreviousObjectFails()
        {
            var s = new PlayerSessionFixture();
            s.DropHammer(Vector3.UnitY);
            s.Game.AddEntity(30);
            s.ObserveHeldHammer(30);
            s.Game.Entities.CreatedBlips.Enqueue(2001);
            s.Game.Entities.SilentDeletionRefusals.Add(20);
            s.Frame(0, Keys.Y, expectFailure: true);
            Assert.IsTrue(s.Game.Entity(20).Exists);
            Assert.IsTrue(s.Game.Entity(30).Exists);
            Assert.AreEqual(200, s.Player.MaxHealth);
            StringAssert.Contains((string)s.Log.Last("tick.exception").Data["exception"], "Entity 20 still exists");
            int allocations = s.CreatedWeapons.Count;
            s.Frame(0, expectFailure: true);
            Assert.AreEqual(allocations, s.CreatedWeapons.Count);
            s.Game.Entities.SilentDeletionRefusals.Clear();
            s.Reload();
            Assert.IsFalse(s.Game.Observations[20].Exists);
            Assert.IsFalse(s.Game.Observations[30].Exists);
            Assert.IsTrue(s.Game.Entity(10).Exists);
            Assert.IsTrue(s.Game.Entity(99).Exists);
        }

        [TestMethod]
        public void CleanupForgetsAnExternallyRemovedRetirementBeforeItsHandleIsReusedByAnUnrelatedObject()
        {
            var s = new PlayerSessionFixture();
            s.DropHammer(Vector3.UnitY);
            s.Game.AddEntity(30);
            s.ObserveHeldHammer(30);
            s.Game.Entities.CreatedBlips.Enqueue(2001);
            s.Game.Entities.SilentDeletionRefusals.Add(20);
            s.Frame(0, Keys.Y, expectFailure: true);
            s.Game.Observations[20].Exists = false;
            s.Game.Entities.SilentDeletionRefusals.Clear();
            s.Game.Entities.SilentDeletionRefusals.Add(30);
            s.Chord(Keys.O);
            Assert.IsFalse(s.Log.Records.Any(record => record.Name == "ability.enabled"));
            Assert.IsTrue(s.Game.Entity(30).Exists);
            var unrelatedPosition = new Vector3(91, 92, 93);
            s.Game.Observations[20] = new EntityObservation { Position = unrelatedPosition };
            s.Game.Entities.SilentDeletionRefusals.Clear();
            s.Game.Calls.Clear();
            s.Chord(Keys.O);
            Assert.IsFalse(s.Game.Observations[30].Exists);
            Assert.AreEqual(unrelatedPosition, s.Game.Entity(20).Position);
            Assert.IsFalse(s.Game.Calls.Any(call => call.Name == "entity.delete" && (int)call.Arguments[0] == 20));
        }

        [TestMethod]
        [DataRow(0)]
        [DataRow(1)]
        [DataRow(2)]
        public void CreationFailurePausesWithoutUsingZeroHandlesAndReleasesEarlierAllocations(int successfulCreations)
        {
            var s = new PlayerSessionFixture(holding: false);
            for (int index = 0; index < successfulCreations; index++) s.WeaponCreations.Enqueue(21 + index);
            s.WeaponCreations.Enqueue(0);
            s.Frame(0, expectFailure: true);
            Assert.HasCount(1, s.Log.Records.Where(record => record.Name == "tick.exception").ToArray());
            Assert.HasCount(successfulCreations, s.CreatedWeapons);
            StringAssert.Contains((string)s.Log.Last("tick.exception").Data["exception"], new[]
            {
                "Could not create the hidden whirling hammer.",
                "Could not create the visible whirling hammer.",
                "The hammer weapon asset could not be created."
            }[successfulCreations]);
            Assert.IsTrue(s.CreatedWeapons.All(handle => !s.Game.Observations[handle].Exists));
            Assert.IsFalse(s.Game.Calls.Any(call => call.Name.StartsWith("entity.", StringComparison.Ordinal) &&
                call.Arguments.Length > 0 && call.Arguments[0] is int && (int)call.Arguments[0] == 0));
            Assert.AreEqual(200, s.Player.MaxHealth);
            int allocations = s.CreatedWeapons.Count;
            s.Frame(0, expectFailure: true);
            Assert.AreEqual(allocations, s.CreatedWeapons.Count);
        }

        [TestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public void FailedWhirlAllocationPausesAndExplicitRetryReleasesPartialRopeBeforePlayingAgain(bool ropeCreated)
        {
            var s = new PlayerSessionFixture();
            s.Frame(0);
            s.Game.Ropes.Creations.Enqueue(ropeCreated ? 70 : 0);
            if (ropeCreated) s.WeaponCreations.Enqueue(0);
            s.Frame(0, Keys.Z, expectFailure: true);
            Assert.HasCount(1, s.Log.Records.Where(record => record.Name == "tick.exception").ToArray());
            StringAssert.Contains((string)s.Log.Last("tick.exception").Data["exception"], ropeCreated
                ? "Could not create the hammer rope anchor." : "Could not create the hammer rope.");
            Assert.AreEqual(ropeCreated ? 70 : 0, s.Game.Calls.Single(call => call.Name == "rope.create").Arguments[6]);
            CollectionAssert.AreEqual(ropeCreated ? new[] { 70 } : Array.Empty<int>(),
                s.Game.Calls.Where(call => call.Name == "rope.delete").Select(call => (int)call.Arguments[0]).ToArray());
            Assert.IsEmpty(s.Game.Ropes.Ropes);
            Assert.IsFalse(s.Game.Calls.Any(call => call.Name == "rope.connect"));
            Assert.IsTrue(s.Game.Entity(20).Exists);
            int failuresBeforeRecovery = FailureCount(s);
            s.Chord(Keys.O);
            Assert.IsFalse(s.Game.Observations[20].Exists);
            s.Game.Entities.CreatedBlips.Enqueue(2001);
            s.Frame(0, expectFailure: true);
            Assert.AreEqual(failuresBeforeRecovery, FailureCount(s),
                "Successful retry must not introduce another gameplay or diagnostics failure.");
            Assert.HasCount(1, s.Log.Records.Where(record => record.Name == "ability.enabled").ToArray());
            Assert.AreEqual(3, s.CreatedWeapons.Count(handle => s.Game.Observations[handle].Exists));
        }

        private static int FailureCount(PlayerSessionFixture session) =>
            session.Log.Records.Count(record => record.Level == "ERROR" || record.Name == "diagnostics.exception");
    }
}
