using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using GTA;
using GTA.Math;
using GTA.Native;
using GTAVMods.Game;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Thor;
using Control = GTA.Control;

namespace ModUtilsTest
{
    [TestClass]
    public class ContractScenarioTests
    {
        // A scene supplies engine observations and successful allocations, never
        // ability transitions or simulated motion. All behavior uses the shipped composition.
        private sealed class Scene
        {
            public readonly FakeGame Game = new FakeGame();
            public readonly RecordingLog Log = new RecordingLog();
            public readonly SilentAudio Audio = new SilentAudio();
            public readonly ThorController Controller;
            public readonly List<int> CreatedWeapons = new List<int>();
            private readonly bool initiallyHolding;
            private int nextWeapon = 1000;

            public Scene(int seed = 11, bool holding = true)
            {
                initiallyHolding = holding;
                AddPlayer(10, 200);
                AddPlayer(11, 250);
                Game.AddEntity(20);
                Game.AddEntity(99).Position = new Vector3(90, 80, 70);
                Game.AddEntity(40);
                Game.AddEntity(41);
                Game.World.Vehicles.Enqueue(40);
                Game.World.Drivers.Enqueue(41);
                Game.World.ModelResults[(int)VehicleHash.Thruster] = true;
                Game.Weapons.AssetResults[WeaponHash.Hammer] = true;
                Game.Weapons.AssetResults[WeaponHash.Grenade] = true;
                Game.Entities.CreatedBlips.Enqueue(2000);
                Game.World.Nearby = (position, radius) => Array.Empty<int>();
                Game.World.RaycastObservation = (from, to, flags, ignore) => new RayHit(false, 0, Vector3.Zero);
                Game.World.CapsuleObservation = (from, to, radius, flags) => new RayHit(false, 0, Vector3.Zero);
                Game.Native.Queries[Hash.IS_ENTITY_A_PED] = args => new[] { 10, 11, 60 }.Contains((int)args[0]);
                Game.Native.Returns(Hash.IS_ENTITY_A_VEHICLE, false);
                Game.Native.Queries[Hash.GET_ENTITY_COORDS] = args => Game.Entity((int)args[0]).Position;
                Game.Native.Queries[Hash.GET_ENTITY_SPEED] = args => Game.Entity((int)args[0]).Velocity.Length();
                Game.Native.Queries[Hash.GET_WEAPON_OBJECT_FROM_PED] = args => Game.Entity((int)args[0]).CurrentWeaponObject;
                Game.Native.Queries[Hash.CREATE_WEAPON_OBJECT] = args =>
                {
                    int handle = nextWeapon++;
                    Game.AddEntity(handle).Position = new Vector3((float)args[2], (float)args[3], (float)args[4]);
                    CreatedWeapons.Add(handle);
                    return handle;
                };
                Game.Native.Returns(Hash.IS_ENTITY_PLAYING_ANIM, false);
                Game.Native.Returns(Hash.HAS_ANIM_DICT_LOADED, true);
                Game.Native.Returns(Hash.HAS_NAMED_PTFX_ASSET_LOADED, true);
                Game.Host.OnWait = milliseconds => Game.Host.GameTime = unchecked(Game.Host.GameTime + milliseconds);
                if (holding) Game.ApplyWeaponObservation(10, WeaponHash.Hammer, 20);
                Controller = ThorComposition.Create(Game.Services, Audio, Log, new Random(seed),
                    () => false, () => 0, new ThorSessionInfo("scenario.jsonl", "scenario-build", "scenario-api"));
                Controller.Start();
            }

            private void AddPlayer(int handle, int originalHealth)
            {
                var player = Game.AddEntity(handle);
                player.MaxHealth = player.Health = originalHealth;
                player.Bones[Bone.PHRightHand] = new Vector3(0, 0, 1);
            }

            public void Frame(int elapsed = 16, Keys key = Keys.None, bool aim = false, bool expectFailure = false)
            {
                Game.Input.Keys.Clear();
                Game.Input.Pressed.Clear();
                if (key != Keys.None) Game.Input.Keys.Add(key);
                if (aim) Game.Input.Pressed.Add(Control.Aim);
                Game.AdvanceFrame(elapsed);
                Controller.Tick();
                if (!expectFailure) AssertNoErrors();
            }

            public void Chord(Keys key)
            {
                Game.Input.Keys.Clear();
                Game.Input.Keys.Add(Keys.A);
                Controller.KeyDown(new KeyEventArgs(key | Keys.Control));
            }

            public string State()
            {
                Controller.KeyDown(new KeyEventArgs(Keys.F9));
                return (string)Log.Last("gameplay.snapshot").Data["state"];
            }

            public void AssertNoErrors()
            {
                var errors = Log.Records.Where(record => record.Level == "ERROR" || record.Name == "diagnostics.exception").ToArray();
                Assert.IsEmpty(errors, string.Join(Environment.NewLine, errors.Select(record =>
                    record.Name + ": " + string.Join(", ", record.Data.Values))));
            }

            public void AssertReleased()
            {
                Assert.IsFalse(Game.Observations[40].Exists);
                Assert.IsFalse(Game.Observations[41].Exists);
                Assert.AreEqual(!initiallyHolding, Game.Observations[20].Exists);
                Assert.IsTrue(CreatedWeapons.All(handle => !Game.Observations[handle].Exists));
                Assert.IsEmpty(Game.Ropes.Ropes);
                Assert.IsEmpty(Game.Cameras.Cameras);
                Assert.AreEqual(200, Game.Entity(10).MaxHealth);
                Assert.AreEqual(250, Game.Entity(11).MaxHealth);
                Assert.AreEqual(new Vector3(90, 80, 70), Game.Entity(99).Position);
                Assert.IsFalse(Game.Calls.Any(call => call.Name == "entity.delete" && (int)call.Arguments[0] == 99));
                Assert.IsTrue(Audio.Clips.Values.All(clip => clip.Disposals == 1 && !clip.IsPlaying));
            }
        }

        [TestMethod]
        [DataRow(16)]
        [DataRow(33)]
        [DataRow(97)]
        public void ComposedThrowCameraCatchWhirlAndDisablePreserveOwnershipAcrossFrameSchedules(int frameMs)
        {
            var s = new Scene();
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
        public void ComposedAssetAndAnimationDiagnosticsReachSessionLogWithoutPreventingThrow()
        {
            var s = new Scene();
            s.Frame();
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
            Assert.AreEqual(10000, warning.Interval);
            Assert.AreEqual(dictionary, warning.Data["dictionary"]);
            Assert.IsFalse(string.IsNullOrEmpty((string)warning.Data["animation"]));
            Assert.HasCount(2, s.Game.Calls.Where(call => call.Name == "host.yield").ToArray());
            Assert.IsFalse(s.Game.Native.Calls.Any(call => call.Hash == Hash.TASK_PLAY_ANIM));
            Assert.AreEqual(WeaponHash.Unarmed, s.Game.Entity(10).CurrentWeapon);
            Assert.AreEqual(new Vector3(0, 100, 5), s.Game.Entity(20).Velocity);
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
            var s = new Scene();
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
            var s = new Scene();
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
            s.Chord(Keys.O);
            Assert.IsFalse(s.Game.Observations[20].Exists);
            Assert.AreEqual(WeaponHash.Unarmed, s.Game.Entity(10).CurrentWeapon);
            s.Game.Entities.CreatedBlips.Enqueue(2001);
            s.Frame(expectFailure: true);
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
            var s = new Scene(seed, holding: false);
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
        public void ComposedCleanupRetriesKeepFailedResourcesOwnedWithoutRecreatingThemOrLosingOtherCleanup()
        {
            var s = new Scene();
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
            s.Chord(Keys.O);
            Assert.IsFalse(s.Game.Observations[anchor].Exists);
            Assert.IsFalse(s.Game.Observations[20].Exists);
            s.Game.Entities.CreatedBlips.Enqueue(2001);
            s.Frame(expectFailure: true);
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
            var s = new Scene();
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
            s.Chord(Keys.O);
            Assert.IsFalse(s.Game.Observations[retained].Exists);
            Assert.IsFalse(s.Game.Observations[20].Exists);
            s.Game.Entities.CreatedBlips.Enqueue(2001);
            s.Frame(expectFailure: true);
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
            var s = new Scene();
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
            s.Chord(Keys.O);
            s.Frame(expectFailure: true);
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
        public void ComposedThrowFlightExpiresAtTwoSecondsIncludingSignedClockWrap(int startTime)
        {
            var s = new Scene();
            s.Game.Host.GameTime = startTime;
            s.Frame(0, Keys.U, aim: true);
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
        public void ComposedPowerDecayUsesElapsedMillisecondsWithoutFloatRoundingOrSentinelCollision(int lossTime)
        {
            var s = new Scene();
            s.Game.Host.GameTime = unchecked(lossTime - 1);
            s.Frame(0);
            s.Frame(1, Keys.Y);
            s.Frame(0);
            foreach (var sample in new[] { (elapsed: 5000, maxHealth: 15100), (elapsed: 10000, maxHealth: 100), (elapsed: 10001, maxHealth: 100) })
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
        [DataRow(int.MaxValue - 500)]
        public void LosingAStoredHammerFadesFromHalfPowerWithoutIncreasingOrKeepingItForever(int lossTime)
        {
            var s = new Scene(holding: false);
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
        public void ComposedTargetReturnWaitsTheFullSevenHundredMilliseconds(int startTime)
        {
            var s = new Scene();
            s.Game.AddEntity(60).Position = new Vector3(0, .4f, 0);
            s.Game.Host.GameTime = startTime;
            s.Frame(0);
            s.Game.World.Rays.Enqueue(new RayHit(true, 60, Vector3.Zero));
            s.Frame(16, Keys.T, aim: true);
            s.Frame(16);
            s.Frame(16);
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
    }
}
