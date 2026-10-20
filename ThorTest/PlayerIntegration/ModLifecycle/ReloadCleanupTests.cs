using ADModUtils;
using Control = GTA.Control;
using GTA.Math;
using GTA.Native;
using GTA;
using GTAVMods.Game;
using GTAVMods.Tests.Support;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using System;
using Thor;
using static GTAVMods.Tests.Support.PlayerScenarios;
using static GTAVMods.Tests.Support.SessionAssertions;

namespace GTAVMods.Tests.PlayerIntegration.ModLifecycle
{
    [TestClass, TestCategory("PlayerIntegration")]
    public class ReloadCleanupTests
    {
        [TestMethod]
        public void CleanupAfterFailedDropRestoresPlayerAndRetainsReleaseForExplicitRetry()
        {
            var s = new PlayerSessionFixture();
            s.Frame();
            s.Game.Native.Returns(Hash.GET_WEAPON_OBJECT_FROM_PED, 0);
            s.Chord(Keys.F);
            Assert.AreEqual(200, s.Game.Entity(10).MaxHealth);
            Assert.AreEqual(WeaponHash.Hammer, s.Game.Entity(10).CurrentWeapon);
            Assert.IsTrue(s.Game.Entity(20).Exists);
            Assert.IsTrue(s.CreatedWeapons.All(handle => !s.Game.Observations[handle].Exists));
            Assert.HasCount(1, s.Log.Records.Where(record => record.Name == "cleanup.exception").ToArray());
            StringAssert.Contains((string)s.Log.Last("cleanup.exception").Data["exception"], "release the held hammer",
                "A failed release must leave an actionable cleanup diagnostic.");
            StringAssert.Contains(s.State(), "attached=True");
            int allocations = s.CreatedWeapons.Count;
            s.Chord(Keys.O);
            s.Frame(expectFailure: true);
            Assert.AreEqual(allocations, s.CreatedWeapons.Count);
            Assert.IsFalse(s.Log.Records.Any(record => record.Name == "ability.enabled"));

            s.Game.Native.Queries[Hash.GET_WEAPON_OBJECT_FROM_PED] = args => s.Game.Entity((int)args[0]).CurrentWeaponObject;
            int failuresBeforeRecovery = FailureCount(s);
            s.Chord(Keys.O);
            Assert.IsFalse(s.Game.Observations[20].Exists);
            Assert.AreEqual(WeaponHash.Unarmed, s.Game.Entity(10).CurrentWeapon);
            s.Game.Entities.CreatedBlips.Enqueue(2001);
            s.Frame(expectFailure: true);
            Assert.AreEqual(failuresBeforeRecovery, FailureCount(s));
            Assert.HasCount(1, s.Log.Records.Where(record => record.Name == "ability.enabled").ToArray());
            Assert.AreEqual(3, s.CreatedWeapons.Count(handle => s.Game.Observations[handle].Exists));
            s.Controller.Abort();
            s.AssertReleased();
            Assert.HasCount(2, s.Log.Records.Where(record => record.Level == "ERROR").ToArray());
        }

        [TestMethod]
        public void CleanupRetriesKeepFailedResourcesOwnedWithoutRecreatingThemOrLosingOtherCleanup()
        {
            var s = new PlayerSessionFixture();
            s.Game.Ropes.Creations.Enqueue(70);
            s.Frame(16, Keys.Z);
            s.Frame(16, Keys.Z);
            int anchor = s.CreatedWeapons.Single(handle => s.Game.Entity(handle).AttachedTo == 10);
            var hover = s.CreatedWeapons.Where(handle => handle != anchor).ToArray();
            bool failHealth = true, failAnchor = true;
            s.Game.BeforeCall = call =>
            {
                if (failHealth && call.Name == "ped.maxHealth" && (int)call.Arguments[1] == 200)
                    throw new InvalidOperationException("original player health unavailable");
                if (failAnchor && call.Name == "entity.delete" && (int)call.Arguments[0] == anchor)
                    throw new InvalidOperationException("rope anchor deletion unavailable");
            };
            s.Chord(Keys.F);
            Assert.IsEmpty(s.Game.Ropes.Ropes);
            Assert.IsTrue(hover.All(handle => !s.Game.Observations[handle].Exists));
            Assert.IsFalse(s.Audio.Clips["whirling-hammer.wav"].IsPlaying);
            Assert.IsTrue(s.Game.Entity(anchor).Exists);
            Assert.IsTrue(s.Game.Entity(20).Exists);

            s.Chord(Keys.O);
            s.Frame(expectFailure: true);
            Assert.AreEqual(30000, s.Game.Entity(10).MaxHealth);
            failHealth = false;
            s.Chord(Keys.O);
            s.Frame(expectFailure: true);
            Assert.AreEqual(200, s.Game.Entity(10).MaxHealth);
            Assert.AreEqual(3, s.CreatedWeapons.Count);
            Assert.IsFalse(s.Log.Records.Any(record => record.Name == "ability.enabled"));
            Assert.HasCount(3, s.Log.Records.Where(record => record.Name == "cleanup.exception").ToArray());
            Assert.IsFalse(s.Log.Records.Any(record => record.Name == "tick.exception"));

            failAnchor = false;
            int failuresBeforeRecovery = FailureCount(s);
            s.Chord(Keys.O);
            Assert.IsFalse(s.Game.Observations[anchor].Exists);
            Assert.IsFalse(s.Game.Observations[20].Exists);
            s.Game.Entities.CreatedBlips.Enqueue(2001);
            s.Frame(expectFailure: true);
            Assert.AreEqual(failuresBeforeRecovery, FailureCount(s));
            Assert.IsTrue(s.State().Contains("attached=True"));
            Assert.AreEqual(3, s.CreatedWeapons.Count(handle => s.Game.Observations[handle].Exists));
            Assert.HasCount(1, s.Log.Records.Where(record => record.Name == "ability.enabled").ToArray());
            s.Controller.Abort();
            s.AssertReleased();
            Assert.HasCount(3, s.Log.Records.Where(record => record.Level == "ERROR").ToArray());
            foreach (int handle in hover)
                Assert.HasCount(1, s.Game.Calls.Where(call => call.Name == "entity.delete" && (int)call.Arguments[0] == handle).ToArray());
            Assert.HasCount(4, s.Game.Calls.Where(call => call.Name == "entity.delete" && (int)call.Arguments[0] == anchor).ToArray());
        }

        [TestMethod]
        public void SilentDeletionRefusalKeepsCleanupOwnedWhileOtherResourcesReleaseAndRetryCanRecover()
        {
            var s = new PlayerSessionFixture();
            s.Frame();
            int retained = s.CreatedWeapons.First();
            int independent = s.CreatedWeapons.Last();
            s.Game.Entities.SilentDeletionRefusals.Add(retained);

            s.Chord(Keys.F);
            Assert.IsTrue(s.Game.Observations[retained].Exists);
            Assert.IsFalse(s.Game.Observations[independent].Exists);
            Assert.AreEqual(200, s.Game.Entity(10).MaxHealth);
            Assert.AreEqual(WeaponHash.Unarmed, s.Game.Entity(10).CurrentWeapon);
            StringAssert.Contains((string)s.Log.Last("cleanup.exception").Data["exception"],
                "Entity " + retained + " still exists after its deletion request.");
            s.Chord(Keys.O);
            s.Frame(expectFailure: true);
            Assert.AreEqual(2, s.CreatedWeapons.Count);
            Assert.IsFalse(s.Log.Records.Any(record => record.Name == "ability.enabled"));
            Assert.IsTrue(s.Game.Observations[retained].Exists);

            s.Game.Entities.SilentDeletionRefusals.Clear();
            int failuresBeforeRecovery = FailureCount(s);
            s.Chord(Keys.O);
            Assert.IsFalse(s.Game.Observations[retained].Exists);
            Assert.IsFalse(s.Game.Observations[20].Exists);
            s.Game.Entities.CreatedBlips.Enqueue(2001);
            s.Frame(expectFailure: true);
            Assert.AreEqual(failuresBeforeRecovery, FailureCount(s));
            Assert.HasCount(1, s.Log.Records.Where(record => record.Name == "ability.enabled").ToArray());
            Assert.AreEqual(3, s.CreatedWeapons.Count(handle => s.Game.Observations[handle].Exists));
            s.Controller.Abort();
            s.AssertReleased();
            Assert.HasCount(2, s.Log.Records.Where(record => record.Level == "ERROR").ToArray());
            Assert.HasCount(3, s.Game.Calls.Where(call => call.Name == "entity.delete" && (int)call.Arguments[0] == retained).ToArray(),
                "Two refused deletions must retain ownership for the successful third attempt.");
            Assert.HasCount(1, s.Game.Calls.Where(call => call.Name == "entity.delete" && (int)call.Arguments[0] == independent).ToArray(),
                "Successfully released resources must not be deleted again during retry.");
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
        public void ReloadReleasesIndependentResourcesWhenOneOwnedObjectCannotBeDeleted()
        {
            var s = new PlayerSessionFixture();
            s.Game.Ropes.Creations.Enqueue(70);
            s.Frame(0, Keys.Z);
            s.Frame(0, Keys.Z);
            Assert.IsNotEmpty(s.Audio.Clips, "Whirling starts audio that reload must release.");
            s.Game.Entities.SilentDeletionRefusals.Add(21);
            s.Reload();
            Assert.IsTrue(s.Game.Entity(21).Exists);
            Assert.IsFalse(s.Game.Observations[22].Exists);
            Assert.AreEqual(200, s.Player.MaxHealth);
            Assert.IsTrue(s.Audio.Clips.Values.All(clip => clip.Disposals == 1));
            Assert.AreEqual("script-aborted-or-reloaded", s.Log.Last("session.stopping").Data["reason"]);
            Assert.AreEqual("session.end", s.Log.Records.Last().Name);
            s.Game.Entities.SilentDeletionRefusals.Clear();
            s.Reload();
            Assert.IsFalse(s.Game.Observations[21].Exists);
            Assert.IsTrue(s.Audio.Clips.Values.All(clip => clip.Disposals == 1));
        }
    }
}
