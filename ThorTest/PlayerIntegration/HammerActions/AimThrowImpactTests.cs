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

namespace GTAVMods.Tests.PlayerIntegration.HammerActions
{
    [TestClass, TestCategory("PlayerIntegration")]
    public class AimThrowImpactTests
    {
        [TestMethod]
        [DataRow(137)]
        [DataRow(0)]
        public void AThrowKeepsTheHammerUntilTheConfiguredWindupHasCompleted(int releaseDelay)
        {
            var tuning = new GameplayTuning();
            int action = (int)AnimationActions.ThrowHammer1;
            string dictionary = tuning.Animation.Dictionaries[action];
            string clip = tuning.Animation.Names[action];
            tuning.Animation.ReleaseDelays = new Dictionary<string, Dictionary<string, int>> {
                { dictionary, new Dictionary<string, int> { { clip, releaseDelay }, { "unused-clip", 811 } } },
                { "unused-dictionary", new Dictionary<string, int> { { clip, 733 } } }
            };
            tuning.Animation.BlendInSpeed = 3.25f;
            tuning.Animation.BlendOutSpeed = -2.5f;
            tuning.Animation.PlaybackRate = .75f;
            var s = new PlayerSessionFixture(random: new FixedRandom(0), tuning: tuning);
            s.Frame(0);
            int started = s.Game.Host.GameTime;
            bool resumed = false;
            int releases = 0;
            s.Game.BeforeCall = call =>
            {
                if (call.Name != "weapon.remove" || (WeaponHash)call.Arguments[1] != WeaponHash.Hammer) return;
                Assert.IsTrue(resumed, "The hammer cannot leave the hand while the host is still waiting.");
                Assert.IsTrue(unchecked(s.Game.Host.GameTime - started) >= releaseDelay);
                releases++;
            };
            s.Game.Host.OnWait = requested =>
            {
                Assert.AreEqual(releaseDelay, requested, "The selected clip's supplied delay controls release.");
                var animation = s.LastNative(Hash.TASK_PLAY_ANIM);
                Assert.AreEqual(dictionary, animation[1]);
                Assert.AreEqual(clip, animation[2]);
                Assert.AreEqual(tuning.Animation.BlendInSpeed, animation[3]);
                Assert.AreEqual(tuning.Animation.BlendOutSpeed, animation[4]);
                Assert.AreEqual(tuning.Animation.PlaybackRate, animation[7]);
                Assert.AreEqual(WeaponHash.Hammer, s.Player.CurrentWeapon);
                Assert.AreEqual(Vector3.Zero, s.Game.Entity(20).Velocity);
                // The host may resume on a later frame; exact wake-up time is not promised.
                s.Game.Host.GameTime = unchecked(started + requested + 23);
                resumed = true;
            };
            s.Throw();
            Assert.IsTrue(resumed, "A skipped wait must not make this scenario pass.");
            Assert.AreEqual(1, releases);
            Assert.AreEqual(WeaponHash.Unarmed, s.Player.CurrentWeapon);
            Assert.IsTrue(Vector3.Dot(s.Game.Entity(20).Velocity, s.Game.Cameras.GameplayDirection) > 0);
            s.Game.BeforeCall = null;
            s.Reload();
            s.AssertReleased();
        }

        [TestMethod]
        [DataRow(0)]
        [DataRow(int.MaxValue - 30)]
        public void UnavailableThrowAnimationTimesOutAndStillReleasesThePlayersHammer(int gameTime)
        {
            var tuning = new GameplayTuning();
            tuning.Animation.LoadTimeoutMs = 73;
            var s = new PlayerSessionFixture(tuning: tuning);
            s.Frame(0);
            s.Game.Host.GameTime = gameTime;
            s.Game.Native.Returns(Hash.HAS_ANIM_DICT_LOADED, false);
            int yields = 0;
            s.Game.Host.OnYield = () =>
            {
                Assert.IsTrue(unchecked(s.Game.Host.GameTime - gameTime) < tuning.Animation.LoadTimeoutMs,
                    "The script must stop streaming once the configured deadline passes.");
                Assert.AreEqual(WeaponHash.Hammer, s.Player.CurrentWeapon);
                Assert.AreEqual(Vector3.Zero, s.Game.Entity(20).Velocity);
                s.Game.Host.GameTime = unchecked(s.Game.Host.GameTime + 40);
                yields++;
            };
            s.Game.Native.Calls.Clear();
            s.Game.Host.OnWait = duration =>
            {
                Assert.IsTrue(unchecked(s.Game.Host.GameTime - gameTime) >= tuning.Animation.LoadTimeoutMs,
                    "The streaming deadline must pass before the fallback animation windup starts.");
                s.Game.Host.GameTime = unchecked(s.Game.Host.GameTime + duration);
            };
            s.Throw();
            Assert.IsTrue(yields > 0, "An unavailable asset must first be given a chance to load.");
            Assert.IsTrue(unchecked(s.Game.Host.GameTime - gameTime) >= tuning.Animation.LoadTimeoutMs,
                "Fallback must not release the hammer before the streaming deadline.");
            Assert.IsFalse(s.Game.Native.Calls.Any(call => call.Hash == Hash.TASK_PLAY_ANIM));
            Assert.AreEqual(WeaponHash.Unarmed, s.Player.CurrentWeapon);
            Assert.IsTrue(Vector3.Dot(s.Game.Entity(20).Velocity, s.Game.Cameras.GameplayDirection) > 0);
            var warning = s.Log.Records.Single(record => record.Name.StartsWith("animation.timeout."));
            Assert.AreEqual("WARN", warning.Level);
            Assert.IsFalse(string.IsNullOrEmpty((string)warning.Data["animation"]));
            s.Reload();
        }

        [TestMethod]
        public void AimingTracksAtMostTheConfiguredTargetsAndClearsThemWhenAimIsReleased()
        {
            var tuning = new GameplayTuning();
            tuning.Ability.MaxTargets = 3;
            tuning.Ability.AimRayStartDistance = 4;
            tuning.Ability.AimRayDistance = 90;
            tuning.Ability.TargetMarkerOffset = new Vector3(0, 0, 7);
            tuning.Ability.TargetMarkerScale = new Vector3(2, 3, 4);
            tuning.Ability.TargetMarkerColor = System.Drawing.Color.Cyan;
            var f = new PlayerSessionFixture(tuning: tuning);
            f.Tick();
            f.Game.Input.Pressed.Add(Control.Aim);
            f.Game.Cameras.GameplayPosition = new Vector3(17, -23, 11);
            f.Game.Cameras.GameplayDirection = Vector3.UnitX;
            f.Game.AddEntity(60); f.Peds.Remove(60);
            foreach (var hit in new[] { new RayHit(false, 0, Vector3.Zero), new RayHit(true, 0, Vector3.Zero),
                new RayHit(true, 10, Vector3.Zero), new RayHit(true, 60, Vector3.Zero) })
            {
                f.Game.World.Rays.Enqueue(hit);
                f.Tick();
            }
            Assert.IsFalse(f.Game.Calls.Any(call => call.Name == "world.marker"));
            CollectionAssert.AreEqual(new object[] { new Vector3(21, -23, 11), new Vector3(107, -23, 11),
                IntersectFlags.Objects | IntersectFlags.Peds | IntersectFlags.Vehicles | IntersectFlags.Map, 0, ShapeTestOptions.Default },
                f.Game.Calls.First(call => call.Name == "world.raycast").Arguments);
            Assert.IsTrue(f.Game.Native.Calls.Any(call => call.Hash == Hash.SHOW_HUD_COMPONENT_THIS_FRAME));
            for (int id = 61; id <= 61 + tuning.Ability.MaxTargets; id++)
            {
                f.Game.AddEntity(id).Position = Vector3.UnitX * id;
                if (id == 61) f.Vehicles.Add(id); else f.Peds.Add(id);
                f.Game.World.Rays.Enqueue(new RayHit(true, id, Vector3.Zero));
                f.Tick();
            }
            StringAssert.Contains(f.State(), "targets=" + tuning.Ability.MaxTargets);
            Assert.IsFalse(f.Game.Calls.Where(call => call.Name == "world.marker")
                .Any(call => (Vector3)call.Arguments[1] == new Vector3(61 + tuning.Ability.MaxTargets, 0, 7)));
            var firstMarker = f.Game.Calls.First(call => call.Name == "world.marker").Arguments;
            Assert.AreEqual(new Vector3(61, 0, 7), firstMarker[1]);
            Assert.AreEqual(tuning.Ability.TargetMarkerScale, firstMarker[4]);
            Assert.AreEqual(tuning.Ability.TargetMarkerColor, firstMarker[5]);
            f.Game.Input.Pressed.Clear();
            f.Tick();
            StringAssert.Contains(f.State(), "targets=" + tuning.Ability.MaxTargets,
                "An unpressed sample without a release edge does not discard the active selection.");
            f.Game.Input.Released.Add(Control.Aim);
            var before = f.Game.Calls.Count(call => call.Name == "world.marker");
            f.Tick(); f.Tick();
            Assert.AreEqual(before, f.Game.Calls.Count(call => call.Name == "world.marker"));
            StringAssert.Contains(f.State(), "targets=0");
            StringAssert.Contains(f.State(), "collecting=False");
        }

        [TestMethod]
        [DataRow(0)]
        [DataRow(1)]
        [DataRow(2)]
        [DataRow(3)]
        public void ThrowingWhileMovingUsesUpperBodyAnimationAndAddsToTheObservedHammerMomentum(int movement)
        {
            var tuning = new GameplayTuning();
            tuning.Ability.ThrowSpeed = 37;
            tuning.Ability.ThrowVelocityOffset = new Vector3(-1, 2, 3);
            var s = new PlayerSessionFixture(tuning: tuning);
            s.Player.Walking = movement == 0;
            s.Player.Sprinting = movement == 1;
            s.Player.Running = movement == 2;
            s.Player.InAir = movement == 3;
            s.Game.Cameras.GameplayDirection = Vector3.UnitX;
            s.Game.Entity(20).Velocity = new Vector3(3, 4, 5);
            s.Throw();
            Assert.AreEqual(new Vector3(39, 6, 8), s.Game.Entity(20).Velocity);
            Assert.AreEqual(WeaponHash.Unarmed, s.Player.CurrentWeapon);
            var animation = s.LastNative(Hash.TASK_PLAY_ANIM);
            Assert.AreEqual((int)(AnimationFlags.UpperBodyOnly | AnimationFlags.Secondary), animation[6]);
            StringAssert.EndsWith((string)animation[2], "+90");
            int animations = s.Game.Native.Calls.Count(call => call.Hash == Hash.TASK_PLAY_ANIM);
            s.Throw();
            Assert.HasCount(animations, s.Game.Native.Calls.Where(call => call.Hash == Hash.TASK_PLAY_ANIM).ToArray(),
                "Continuing to press throw after release cannot restart a held-weapon animation.");
        }

        [TestMethod]
        public void AThrownHammerDamagesBystandersButPreservesItsOwnerAndNavigationHelpers()
        {
            var tuning = new GameplayTuning();
            tuning.Hammer.ImpactDamage = 23;
            tuning.Hammer.ImpactRadius = 4;
            var s = new PlayerSessionFixture(tuning: tuning);
            s.Frame(0);
            s.Throw();
            s.Game.AddEntity(60);
            s.Game.AddEntity(61);
            // A null entry in SHVDNE's nearby-entity array crosses the handle boundary as zero.
            s.Game.World.Nearby = (position, radius) => new[] { 0, 10, 20, 40, 41, 60, 61 };
            s.Game.Entity(20).Velocity = Vector3.UnitY * 100;
            s.Game.Calls.Clear();
            s.Game.Native.Calls.Clear();
            s.Frame(0);
            CollectionAssert.AreEqual(new[] { 60, 61 }, s.Game.Calls.Where(call => call.Name == "entity.force")
                .Select(call => (int)call.Arguments[0]).ToArray());
            CollectionAssert.AreEqual(new object[] { 60, tuning.Hammer.ImpactDamage }, s.Game.Calls.Single(call => call.Name == "ped.damage").Arguments);
            Assert.IsTrue(s.Game.Calls.Any(call => call.Name == "world.nearby" &&
                (float)call.Arguments[1] == tuning.Hammer.ImpactRadius));
            s.Reload();
        }

        [TestMethod]
        [DataRow(false, true)]
        [DataRow(false, false)]
        [DataRow(true, true)]
        [DataRow(true, false)]
        public void AimedThrowKeepsChasingUntilTheObservedTargetIsWithinContactRange(bool vehicle, bool reached)
        {
            var tuning = new GameplayTuning();
            tuning.Hammer.PedTargetReachDistance = 1.25f;
            tuning.Hammer.VehicleTargetReachDistance = 3.5f;
            tuning.Hammer.CruiseSpeed = 47;
            float reach = vehicle ? tuning.Hammer.VehicleTargetReachDistance : tuning.Hammer.PedTargetReachDistance;
            float distance = reach + (reached ? 0 : .01f);
            var s = new PlayerSessionFixture(tuning: tuning);
            s.Frame(0);
            s.Game.AddEntity(60).Position = new Vector3(10, 0, 1);
            if (vehicle) { s.Peds.Remove(60); s.Vehicles.Add(60); }
            s.Game.World.Rays.Enqueue(new RayHit(true, 60, Vector3.Zero));
            s.Frame(0, Keys.T, aim: true);
            Assert.AreEqual(Vector3.Zero, s.Game.Entity(20).Velocity);
            s.Game.Entity(20).Position = s.Game.Entity(60).Position - Vector3.UnitY * distance;
            s.Frame(0);
            Assert.AreEqual(reached, s.Log.Records.Any(record => record.Name == "hammer.targetReached"));
            StringAssert.Contains(s.State(), "targets=" + (reached ? "0" : "1"));
            Assert.AreEqual(reached ? 0 : tuning.Hammer.CruiseSpeed, s.Game.Entity(20).Velocity.Length(), .001f);
        }

        [TestMethod]
        public void ADeletedTargetIsForgottenAndManualRecallCancelsTheRemainingAttack()
        {
            var s = new PlayerSessionFixture();
            s.Frame(0);
            foreach (int handle in new[] { 60, 61 })
            {
                s.Game.AddEntity(handle).Position = Vector3.UnitY * handle;
                s.Peds.Add(handle);
                s.Game.World.Rays.Enqueue(new RayHit(true, handle, Vector3.Zero));
                s.Frame(0, aim: true);
            }
            s.Frame(0, Keys.T, aim: true);
            s.Game.Observations[60].Exists = false;
            s.Frame(0);
            StringAssert.Contains(s.State(), "targets=1");
            s.Frame(0, Keys.H);
            StringAssert.Contains(s.State(), "targets=0");
            StringAssert.Contains(s.State(), "attacking=False");
        }
    }
}
