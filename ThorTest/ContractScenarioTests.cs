using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using GTA;
using GTA.Math;
using GTA.Native;
using GTAVMods.Game;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Thor;
using Control = GTA.Control;
using ADModUtils;

namespace ModUtilsTest
{
    [TestClass, TestCategory("PlayerIntegration")]
    public class ContractScenarioTests
    {
        [TestMethod]
        [DataRow(16)]
        [DataRow(33)]
        [DataRow(97)]
        public void CatchCompletesOnlyAfterTheEngineEquipsTheHammerAndReleasesItsFollowCamera(int frameMs)
        {
            var s = new PlayerSessionFixture();
            s.Frame(frameMs);
            Assert.AreEqual(30000, s.Game.Entity(10).MaxHealth);
            s.Frame(frameMs, Keys.T, aim: true);
            Assert.AreEqual(WeaponHash.Unarmed, s.Game.Entity(10).CurrentWeapon);
            Assert.AreEqual(new Vector3(0, 100, 5), s.Game.Entity(20).Velocity);
            s.Game.Cameras.Creations.Enqueue(80);
            s.Frame(frameMs, Keys.R);
            Assert.IsTrue(s.Game.Cameras.Cameras[80].Active);
            s.Game.Cameras.RenderingCamera = 80;

            // The scene reports contact; the transfer is a request, not an invented success.
            s.Game.World.Capsules.Enqueue(new RayHit(true, 10, Vector3.UnitZ));
            s.Frame(frameMs, Keys.H);
            CollectionAssert.AreEqual(new object[] { 20, 10 },
                s.Game.Native.Calls.Single(call => call.Hash == Hash.GIVE_WEAPON_OBJECT_TO_PED).Arguments);
            Assert.AreEqual(WeaponHash.Unarmed, s.Game.Entity(10).CurrentWeapon);
            int allocationsBeforeCatch = s.CreatedWeapons.Count;
            s.Game.World.Capsules.Enqueue(new RayHit(true, 10, Vector3.UnitZ));
            s.Frame(frameMs, Keys.H);
            Assert.HasCount(2, s.Game.Native.Calls.Where(call => call.Hash == Hash.GIVE_WEAPON_OBJECT_TO_PED).ToArray(),
                "An uncompleted transfer must leave recall usable on the next frame.");
            Assert.AreEqual(allocationsBeforeCatch, s.CreatedWeapons.Count);
            Assert.IsTrue(s.Game.Cameras.Cameras.ContainsKey(80));
            s.Game.ObserveNextFrame(game => game.ApplyWeaponObservation(10, WeaponHash.Hammer, 20));
            s.Frame(frameMs);
            Assert.IsEmpty(s.Game.Cameras.Cameras);
            Assert.AreEqual(0, s.Game.Cameras.RenderingCamera);
            Assert.AreEqual(2, s.Audio.Clips["catch-hammer.wav"].Plays);

            s.Controller.Abort();
            s.AssertNoErrors();
            s.AssertReleased();
        }

        [TestMethod]
        public void DisablingWhirlStopsItsEffectsAndEnablingRetainsTheDroppedHammer()
        {
            const int frameMs = 16;
            var s = new PlayerSessionFixture();
            s.Frame(frameMs);
            s.Game.Ropes.Creations.Enqueue(70);
            s.Frame(frameMs, Keys.Z);
            Assert.IsTrue(s.Game.Ropes.Ropes.Contains(70));
            s.Frame(frameMs, Keys.Z);
            Assert.AreEqual(30000, s.Game.Entity(10).MaxHealth);
            Assert.IsTrue(s.Audio.Clips["whirling-hammer.wav"].IsPlaying);
            s.Chord(Keys.F);
            Assert.IsEmpty(s.Game.Ropes.Ropes);
            Assert.IsFalse(s.Audio.Clips["whirling-hammer.wav"].IsPlaying);
            Assert.AreEqual(200, s.Game.Entity(10).MaxHealth);
            Assert.IsTrue(s.Game.Entity(20).Exists);
            var creations = s.CreatedWeapons.Count;
            s.Frame(frameMs);
            Assert.AreEqual(creations, s.CreatedWeapons.Count);
            s.Chord(Keys.O);
            s.Frame(frameMs);
            Assert.IsTrue(s.Game.Entity(20).Exists, "A normal disable/enable must preserve the existing loose hammer.");
            Assert.IsFalse(s.Game.Calls.Any(call => call.Name == "entity.delete" && (int)call.Arguments[0] == 20));
            s.Controller.Abort();
            s.AssertNoErrors();
            s.AssertReleased();
        }

        [TestMethod]
        public void AssetAndAnimationDiagnosticsReachSessionLogWithoutPreventingThrow()
        {
            var s = new PlayerSessionFixture();
            s.Frame();
            var startup = s.Log.Last("session.start");
            Assert.AreEqual(s.Log.Writer.FilePath, startup.Data["logPath"]);
            Assert.AreEqual(File.GetLastWriteTimeUtc(typeof(ThorController).Assembly.Location).ToString("O"), startup.Data["buildUtc"]);
            Assert.AreEqual(typeof(Script).Assembly.GetName().Version.ToString(), startup.Data["apiVersion"]);
            Assert.AreEqual(0, s.Log.Last("performance").Data["droppedLogRecords"]);
            var created = s.Log.Records.Where(record => record.Name == "asset.weapon.created").ToArray();
            CollectionAssert.AreEqual(s.CreatedWeapons.ToArray(),
                created.Select(record => (int)record.Data["handle"]).ToArray());
            Assert.HasCount(2, created);
            Assert.IsTrue(created.All(record => record.Level == "INFO" && (string)record.Data["weapon"] == "Hammer"));
            Assert.HasCount(2, s.Log.Records.Where(record => record.Name == "asset.weapon.request").ToArray());

            s.Game.Native.Returns(Hash.HAS_ANIM_DICT_LOADED, false);
            s.Game.Host.OnYield = () => s.Game.Host.GameTime = unchecked(s.Game.Host.GameTime + 500);
            s.Frame(16, Keys.T, aim: true);
            var dictionary = (string)s.Game.Native.Calls.Last(call => call.Hash == Hash.REQUEST_ANIM_DICT).Arguments[0];
            var warning = s.Log.Last("animation.timeout." + dictionary);
            Assert.AreEqual("WARN", warning.Level);
            Assert.AreEqual(dictionary, warning.Data["dictionary"]);
            Assert.IsFalse(string.IsNullOrEmpty((string)warning.Data["animation"]));
            Assert.HasCount(2, s.Game.Calls.Where(call => call.Name == "host.yield").ToArray());
            Assert.IsFalse(s.Game.Native.Calls.Any(call => call.Hash == Hash.TASK_PLAY_ANIM));
            Assert.AreEqual(WeaponHash.Unarmed, s.Game.Entity(10).CurrentWeapon);
            Assert.AreEqual(new Vector3(0, 100, 5), s.Game.Entity(20).Velocity);
            s.Controller.Abort();
            Assert.AreEqual(0, s.Log.Last("session.end").Data["droppedLogRecords"]);
            s.AssertNoErrors();
            s.AssertReleased();
        }

        [TestMethod]
        public void OpenConsoleProtectsGameplayFromDisableUntilItClosesOrIsDisposed()
        {
            var s = new PlayerSessionFixture();
            s.Frame();
            using (var console = new ConsoleFixture())
            {
                console.Press(Keys.F8);
                s.Chord(Keys.F);
                Assert.AreEqual(30000, s.Game.Entity(10).MaxHealth);
                Assert.AreEqual(WeaponHash.Hammer, s.Game.Entity(10).CurrentWeapon);
                Assert.IsFalse(s.Log.Records.Any(record => record.Name == "ability.disabled"));

                console.Press(Keys.F8);
                s.Chord(Keys.F);
                Assert.AreEqual(200, s.Game.Entity(10).MaxHealth);
                Assert.AreEqual(WeaponHash.Unarmed, s.Game.Entity(10).CurrentWeapon);
                Assert.HasCount(1, s.Log.Records.Where(record => record.Name == "ability.disabled").ToArray());

                s.Chord(Keys.O);
                s.Game.ObserveNextFrame(game => game.ApplyWeaponObservation(10, WeaponHash.Hammer, 20));
                s.Frame();
                Assert.AreEqual(30000, s.Game.Entity(10).MaxHealth);
                console.Press(Keys.F8);
            }

            s.Chord(Keys.F);
            Assert.AreEqual(200, s.Game.Entity(10).MaxHealth);
            Assert.AreEqual(WeaponHash.Unarmed, s.Game.Entity(10).CurrentWeapon);
            Assert.HasCount(2, s.Log.Records.Where(record => record.Name == "ability.disabled").ToArray(),
                "Disposing an open console must restore shortcuts instead of leaving a stale input lock.");
            s.Controller.Abort();
            s.AssertNoErrors();
            s.AssertReleased();
        }

        [TestMethod]
        [DataRow(Keys.T, false)]
        [DataRow(Keys.T, true)]
        [DataRow(Keys.Y, false)]
        [DataRow(Keys.Z, false)]
        public void FailedDropLeavesHeldHammerOwnedAndRetryableWithoutStartingThrowOrRope(Keys action, bool targeted)
        {
            var s = new PlayerSessionFixture();
            s.Frame();
            s.Frame(16, Keys.Y);
            s.Game.ObserveNextFrame(game => game.ApplyWeaponObservation(10, WeaponHash.Hammer, 20));
            s.Frame();
            if (targeted)
            {
                s.Game.AddEntity(60).Position = new Vector3(0, 10, 0);
                s.Game.World.Rays.Enqueue(new RayHit(true, 60, Vector3.Zero));
            }
            s.Game.Native.Returns(Hash.GET_WEAPON_OBJECT_FROM_PED, 0);
            int firstCall = s.Game.Calls.Count;
            int allocations = s.CreatedWeapons.Count;
            s.Frame(16, action, aim: action == Keys.T);
            var attempt = s.Game.Calls.Skip(firstCall).ToArray();
            Assert.AreEqual(WeaponHash.Hammer, s.Game.Entity(10).CurrentWeapon);
            Assert.AreEqual(20, s.Game.Entity(10).CurrentWeaponObject);
            Assert.AreEqual(Vector3.Zero, s.Game.Entity(20).Velocity);
            Assert.AreEqual(allocations, s.CreatedWeapons.Count);
            Assert.IsEmpty(s.Game.Ropes.Ropes);
            Assert.IsFalse(attempt.Any(call => call.Name == "weapon.remove" && (WeaponHash)call.Arguments[1] == WeaponHash.Hammer));
            Assert.IsFalse(attempt.Any(call => call.Name == "weapon.select"));
            Assert.IsFalse(attempt.Any(call => call.Name == "entity.delete"));
            StringAssert.Contains(s.State(), "holding=True");
            StringAssert.Contains(s.State(), "attacking=False");
            StringAssert.Contains(s.State(), "rope=False");

            s.Game.Native.Queries[Hash.GET_WEAPON_OBJECT_FROM_PED] = args => s.Game.Entity((int)args[0]).CurrentWeaponObject;
            if (action == Keys.Z) s.Game.Ropes.Creations.Enqueue(70);
            s.Frame(16, action, aim: action == Keys.T);
            Assert.AreEqual(WeaponHash.Unarmed, s.Game.Entity(10).CurrentWeapon);
            Assert.IsTrue(s.Game.Entity(20).Exists);
            Assert.AreEqual(targeted, s.State().Contains("attacking=True"));
            Assert.AreEqual(action == Keys.Z, s.State().Contains("rope=True"));
            Assert.AreEqual(action == Keys.T && !targeted ? new Vector3(0, 100, 5) : Vector3.Zero,
                s.Game.Entity(20).Velocity);
            s.Controller.Abort();
            s.AssertNoErrors();
            s.AssertReleased();
        }

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
        [DataRow(1729)]
        [DataRow(8675309)]
        public void SeededLifecycleSequencesRestoreEveryFormerPlayerAndNeverAllocateWhileInactive(int seed)
        {
            var s = new PlayerSessionFixture(seed, holding: false);
            var events = new Random(seed);
            bool enabled = true;
            s.Frame();
            int mainHammer = s.CreatedWeapons.Single(handle => s.Game.Entity(handle).Opacity == 255);
            for (int step = 0; step < 48; step++)
            {
                int oldPlayer = s.Game.Host.PlayerPed;
                switch (events.Next(6))
                {
                    case 0: s.Chord(Keys.F); enabled = false; break;
                    case 1: s.Chord(Keys.O); enabled = true; break;
                    case 2: s.Game.Entity(oldPlayer).Dead = true; break;
                    case 3: s.Game.Entity(oldPlayer).Dead = false; break;
                    case 4: s.Game.Host.PlayerPed = oldPlayer == 10 ? 11 : 10; break;
                }
                int before = s.CreatedWeapons.Count;
                int player = s.Game.Host.PlayerPed;
                bool active = enabled && !s.Game.Entity(player).Dead;
                s.Frame(new[] { 1, 16, 33, 250 }[step % 4]);
                Assert.AreEqual(active, s.State().Contains("attached=True"), "seed " + seed + ", step " + step);
                Assert.AreEqual(active ? 3 : 1, s.CreatedWeapons.Count(handle => s.Game.Observations[handle].Exists),
                    "Only the main hammer and the active pair of hover helpers may remain owned.");
                if (!active) Assert.AreEqual(before, s.CreatedWeapons.Count);
                foreach (int inactivePlayer in new[] { 10, 11 }.Where(handle => !active || handle != player))
                    Assert.AreEqual(inactivePlayer == 10 ? 200 : 250, s.Game.Entity(inactivePlayer).MaxHealth);
                Assert.IsTrue(s.Game.Entity(mainHammer).Exists);
            }
            s.Controller.Abort();
            s.Controller.Abort();
            s.AssertNoErrors();
            s.AssertReleased();
            foreach (int handle in s.CreatedWeapons.Concat(new[] { 40, 41 }))
                Assert.HasCount(1, s.Game.Calls.Where(call => call.Name == "entity.delete" && (int)call.Arguments[0] == handle).ToArray());
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
        public void ExternalHeldObjectLossPausesUntilAConsistentInventoryObservationAllowsRecovery()
        {
            var s = new PlayerSessionFixture();
            s.Frame();
            s.Game.ObserveNextFrame(game => game.Observations[20].Exists = false);
            s.Frame(expectFailure: true);
            int allocations = s.CreatedWeapons.Count;
            s.Frame(expectFailure: true);
            Assert.AreEqual(allocations, s.CreatedWeapons.Count);
            Assert.HasCount(1, s.Log.Records.Where(record => record.Name == "tick.exception").ToArray());
            Assert.HasCount(1, s.Log.Records.Where(record => record.Name == "cleanup.exception").ToArray());
            Assert.IsFalse(s.Log.Records.Any(record => record.Name == "ability.enabled"));
            Assert.AreEqual("missing", s.Log.Last("gameplay.snapshot").Data["hammer"]);

            s.Game.ObserveNextFrame(game => game.ApplyWeaponObservation(10, WeaponHash.Unarmed, 0));
            s.Frame(expectFailure: true);
            int failuresBeforeRecovery = FailureCount(s);
            s.Chord(Keys.O);
            s.Frame(expectFailure: true);
            Assert.AreEqual(failuresBeforeRecovery, FailureCount(s));
            Assert.IsTrue(s.State().Contains("attached=True"));
            Assert.AreEqual(3, s.CreatedWeapons.Count(handle => s.Game.Observations[handle].Exists));
            Assert.IsFalse(s.Game.Calls.Any(call => call.Name == "entity.delete" && (int)call.Arguments[0] == 20),
                "The game already removed that object; cleanup must not delete the stale handle.");
            s.Controller.Abort();
            s.AssertReleased();
            Assert.HasCount(2, s.Log.Records.Where(record => record.Level == "ERROR").ToArray());
        }

        [TestMethod]
        [DataRow(1000)]
        [DataRow(int.MaxValue - 1024)]
        public void ThrowFlightExpiresAtTwoSecondsIncludingSignedClockWrap(int startTime)
        {
            var s = new PlayerSessionFixture();
            s.Game.Host.GameTime = startTime;
            s.Frame(0, Keys.U, aim: true);
            StringAssert.Contains(s.State(), "holding=True");
            StringAssert.Contains(s.State(), "collecting=False");
            Assert.IsFalse(s.Game.Native.Calls.Any(call => call.Hash == Hash.GET_WEAPON_OBJECT_FROM_PED));
            Assert.IsTrue(s.Game.Native.Calls.Any(call => call.Hash == Hash.TASK_PLAY_ANIM));
            Assert.IsTrue(s.Game.Native.Calls.Any(call => call.Hash == Hash.SET_PED_TO_RAGDOLL),
                "Aim+U enters ragdoll before the first flight frame without releasing the held hammer.");
            Assert.AreEqual("INFO", s.Log.Last("hammer.throwFlight").Level);
            StringAssert.Contains((string)s.Log.Last("hammer.throwFlight").Data["state"], "holding=True");
            int started = s.Game.Host.GameTime;
            foreach (int elapsed in new[] { 0, 1, 1999, 2000, 2001 })
            {
                s.Game.Entity(20).Velocity = Vector3.Zero; // Independent engine sample, no motion simulation.
                s.Game.Host.GameTime = unchecked(started + elapsed);
                s.Frame(0);
                Assert.AreEqual(elapsed < 2000, s.State().Contains("throwFlight=True"), "elapsed " + elapsed);
                Assert.AreEqual(elapsed < 2000 ? new Vector3(0, 140, 0) : Vector3.Zero, s.Game.Entity(20).Velocity);
            }
            s.Controller.Abort();
            s.AssertNoErrors();
            s.AssertReleased();
        }

        [TestMethod]
        [DataRow(1000)]
        [DataRow(int.MaxValue - 500)]
        [DataRow(-1)]
        public void DroppedHammerPowerAndHealingExpireAfterTenSecondsAcrossClockBoundaries(int lossTime)
        {
            var s = new PlayerSessionFixture();
            s.Game.Host.GameTime = unchecked(lossTime - 201);
            s.Frame(0);
            s.Game.Entity(10).Health = 10;
            s.Frame(200);
            Assert.AreEqual(3010, s.Game.Entity(10).Health, "Full power heals ten percent of the enhanced maximum every 200 ms.");
            s.Frame(1, Keys.Y);
            Assert.AreEqual(WeaponHash.Unarmed, s.Game.Entity(10).CurrentWeapon);
            Assert.AreEqual(Vector3.Zero, s.Game.Entity(20).Velocity);
            s.Frame(0);
            foreach (var sample in new[] { (elapsed: 5000, maxHealth: 15100, movement: 1.25f), (elapsed: 10000, maxHealth: 100, movement: 1f), (elapsed: 10001, maxHealth: 100, movement: 1f) })
            {
                s.Game.Host.GameTime = unchecked(lossTime + sample.elapsed);
                s.Frame(0);
                Assert.AreEqual(sample.maxHealth, s.Game.Entity(10).MaxHealth, "elapsed " + sample.elapsed);
                Assert.AreEqual(sample.movement, s.Game.Native.Calls.Last(call => call.Hash == Hash.SET_PED_MOVE_RATE_OVERRIDE).Arguments[1]);
            }
            int health = s.Game.Entity(10).Health;
            int jumps = s.Game.Native.Calls.Count(call => call.Hash == Hash.SET_SUPER_JUMP_THIS_FRAME);
            s.Frame(200);
            Assert.AreEqual(health, s.Game.Entity(10).Health, "Expired power must stop healing.");
            Assert.HasCount(jumps, s.Game.Native.Calls.Where(call => call.Hash == Hash.SET_SUPER_JUMP_THIS_FRAME).ToArray(),
                "Expired power must not grant a super jump.");
            s.Controller.Abort();
            s.AssertNoErrors();
            s.AssertReleased();
        }

        [TestMethod]
        [DataRow(1000)]
        [DataRow(int.MaxValue - 500)]
        public void LosingAStoredHammerFadesFromHalfPowerWithoutIncreasingOrKeepingItForever(int lossTime)
        {
            var s = new PlayerSessionFixture(holding: false);
            s.Game.Entity(10).Weapons.Add(WeaponHash.Hammer);
            s.Game.Host.GameTime = unchecked(lossTime - 1);
            s.Frame(0);
            Assert.AreEqual(15100, s.Game.Entity(10).MaxHealth);
            Assert.AreEqual(WeaponHash.Unarmed, s.Game.Entity(10).CurrentWeapon);
            Assert.HasCount(2, s.CreatedWeapons, "Inventory ownership needs only the two hover helpers.");

            s.Game.Entity(10).Weapons.Remove(WeaponHash.Hammer);
            s.Game.Host.GameTime = lossTime;
            s.Frame(0);
            Assert.AreEqual(15100, s.Game.Entity(10).MaxHealth, "Losing a stored hammer must not grant a full-power boost.");
            Assert.HasCount(3, s.CreatedWeapons, "The missing inventory is replaced by one owned loose hammer.");
            foreach (var sample in new[] { (elapsed: 5000, maxHealth: 7600), (elapsed: 10000, maxHealth: 100), (elapsed: 10001, maxHealth: 100) })
            {
                s.Game.Host.GameTime = unchecked(lossTime + sample.elapsed);
                s.Frame(0);
                Assert.AreEqual(sample.maxHealth, s.Game.Entity(10).MaxHealth, "elapsed " + sample.elapsed);
            }
            s.Controller.Abort();
            s.AssertNoErrors();
            s.AssertReleased();
        }

        [TestMethod]
        [DataRow(1000)]
        [DataRow(int.MaxValue - 1024)]
        public void TargetReturnWaitsTheFullSevenHundredMilliseconds(int startTime)
        {
            var s = new PlayerSessionFixture();
            s.Game.AddEntity(60).Position = new Vector3(0, .4f, 0);
            s.Game.Host.GameTime = startTime;
            s.Frame(0);
            s.Game.World.Rays.Enqueue(new RayHit(true, 60, Vector3.Zero));
            s.Frame(16, Keys.T, aim: true);
            StringAssert.Contains(s.State(), "attacking=True");
            StringAssert.Contains(s.State(), "collecting=False");
            s.Frame(16);
            StringAssert.Contains(s.State(), "targets=0");
            s.Frame(16);
            StringAssert.Contains(s.State(), "attacking=False");
            int completed = s.Game.Host.GameTime;
            s.Game.World.CapsuleObservation = (from, to, radius, flags) => new RayHit(true, 10, Vector3.UnitZ);
            s.Game.Host.GameTime = unchecked(completed + 699);
            s.Frame(0);
            Assert.IsFalse(s.Game.Native.Calls.Any(call => call.Hash == Hash.GIVE_WEAPON_OBJECT_TO_PED));
            s.Game.Host.GameTime = unchecked(completed + 700);
            s.Frame(0);
            CollectionAssert.AreEqual(new object[] { 20, 10 },
                s.Game.Native.Calls.Single(call => call.Hash == Hash.GIVE_WEAPON_OBJECT_TO_PED).Arguments);
            StringAssert.Contains(s.State(), "return=True");
            Assert.IsFalse(s.Log.Records.Any(record => record.Name == "hammer.caught"),
                "Submitting a transfer cannot confirm that the engine equipped the hammer.");
            s.Frame(16, Keys.H);
            Assert.HasCount(2, s.Game.Native.Calls.Where(call => call.Hash == Hash.GIVE_WEAPON_OBJECT_TO_PED).ToArray());
            StringAssert.Contains(s.State(), "return=True");

            s.Game.ObserveNextFrame(game => game.ApplyWeaponObservation(10, WeaponHash.Hammer, 20));
            s.Frame(16);
            StringAssert.Contains(s.State(), "return=False");
            StringAssert.Contains(s.State(), "holding=True");
            s.Frame(16);
            Assert.HasCount(1, s.Log.Records.Where(record => record.Name == "hammer.caught").ToArray());
            Assert.HasCount(2, s.Game.Native.Calls.Where(call => call.Hash == Hash.GIVE_WEAPON_OBJECT_TO_PED).ToArray(),
                "An observed catch must stop automatic resubmission.");
            s.Controller.Abort();
            s.AssertNoErrors();
            s.AssertReleased();
        }

        private static int FailureCount(PlayerSessionFixture session) =>
            session.Log.Records.Count(record => record.Level == "ERROR" || record.Name == "diagnostics.exception");
    }
}
