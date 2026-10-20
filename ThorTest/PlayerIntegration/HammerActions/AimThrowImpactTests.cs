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
        private sealed class ThrowChoice : Random
        {
            private readonly int action;
            public ThrowChoice(int action) { this.action = action; }
            public override int Next(int maximum) => maximum == 5 ? action : maximum == 3 ? Math.Max(0, action - 2) : 0;
            public override int Next(int minimum, int maximum) => Next(maximum);
            public override double NextDouble() => .5;
        }

        [TestMethod]
        [DataRow(0, 0f, "anim@melee@machete@streamed_core@", "small_melee_wpn_short_range_0", 800)]
        [DataRow(1, 0f, "anim@melee@machete@streamed_core@", "plyr_walking_attack_a", 1000)]
        [DataRow(2, 0f, "melee@small_wpn@streamed_core_fps", "small_melee_wpn_short_range_0", 600)]
        [DataRow(2, -90f, "melee@small_wpn@streamed_core_fps", "small_melee_wpn_short_range_+90", 600)]
        [DataRow(2, 90f, "melee@small_wpn@streamed_core_fps", "small_melee_wpn_short_range_-90", 400)]
        [DataRow(2, -179f, "melee@small_wpn@streamed_core_fps", "small_melee_wpn_short_range_+180", 500)]
        [DataRow(2, 179f, "melee@small_wpn@streamed_core_fps", "small_melee_wpn_short_range_-180", 800)]
        [DataRow(3, 0f, "melee@small_wpn@streamed_core_fps", "small_melee_wpn_long_range_0", 1000)]
        [DataRow(3, -90f, "melee@small_wpn@streamed_core_fps", "small_melee_wpn_long_range_+90", 700)]
        [DataRow(3, 90f, "melee@small_wpn@streamed_core_fps", "small_melee_wpn_long_range_-90", 700)]
        [DataRow(3, -179f, "melee@small_wpn@streamed_core_fps", "small_melee_wpn_long_range_+180", 1000)]
        [DataRow(3, 179f, "melee@small_wpn@streamed_core_fps", "small_melee_wpn_long_range_-180", 800)]
        [DataRow(4, 0f, "melee@small_wpn@streamed_core", "small_melee_wpn_long_range_0", 1000)]
        [DataRow(4, -90f, "melee@small_wpn@streamed_core", "small_melee_wpn_long_range_+90", 600)]
        [DataRow(4, 90f, "melee@small_wpn@streamed_core", "small_melee_wpn_long_range_-90", 700)]
        [DataRow(4, -179f, "melee@small_wpn@streamed_core", "small_melee_wpn_long_range_+180", 1100)]
        [DataRow(4, 179f, "melee@small_wpn@streamed_core", "small_melee_wpn_long_range_-180", 800)]
        public void AimAndThrowWaitsForTheChosenAnimationBeforeReleasingTheHammer(int action, float angle, string dictionary, string clip, int delay)
        {
            var s = new PlayerSessionFixture(random: new ThrowChoice(action));
            s.Frame(0);
            double radians = angle * Math.PI / 180;
            s.Game.Cameras.GameplayDirection = new Vector3(-(float)Math.Sin(radians), (float)Math.Cos(radians), 0);
            s.Game.Host.OnWait = milliseconds =>
            {
                Assert.AreEqual(delay, milliseconds);
                Assert.AreEqual(WeaponHash.Hammer, s.Player.CurrentWeapon);
                Assert.AreEqual(Vector3.Zero, s.Game.Entity(20).Velocity);
                CollectionAssert.AreEqual(new object[] { 10, dictionary, clip, 8f, 1f, -1,
                    (int)AnimationFlags.None, -8f, 0, 0, 0 }, s.LastNative(Hash.TASK_PLAY_ANIM));
            };
            s.Throw();
            Assert.AreEqual(WeaponHash.Unarmed, s.Player.CurrentWeapon);
            Assert.AreEqual(s.Game.Cameras.GameplayDirection * 100 + Vector3.UnitZ * 5, s.Game.Entity(20).Velocity);
            CollectionAssert.AreEqual(new object[] { delay }, s.Game.Calls.Single(call => call.Name == "host.wait").Arguments);
        }


        [TestMethod]
        [DataRow(0)]
        [DataRow(int.MaxValue - 100)]
        public void UnavailableThrowAnimationTimesOutAndStillReleasesThePlayersHammer(int gameTime)
        {
            var s = new PlayerSessionFixture();
            s.Frame(0);
            s.Game.Host.GameTime = gameTime;
            s.Game.Native.Returns(Hash.HAS_ANIM_DICT_LOADED, false);
            int yields = 0;
            s.Game.Host.OnYield = () =>
            {
                Assert.IsTrue(++yields <= 2, "A missing clip must not hang the script.");
                s.Game.Host.GameTime = unchecked(s.Game.Host.GameTime + 500);
            };
            s.Game.Native.Calls.Clear();
            s.Throw();
            Assert.AreEqual(2, yields);
            Assert.IsFalse(s.Game.Native.Calls.Any(call => call.Hash == Hash.TASK_PLAY_ANIM));
            Assert.AreEqual(WeaponHash.Unarmed, s.Player.CurrentWeapon);
            Assert.AreEqual(new Vector3(0, 100, 5), s.Game.Entity(20).Velocity);
            var warning = s.Log.Records.Single(record => record.Name.StartsWith("animation.timeout."));
            Assert.AreEqual("WARN", warning.Level);
            Assert.IsFalse(string.IsNullOrEmpty((string)warning.Data["animation"]));
            s.Controller.Abort();
        }

        [TestMethod]
        public void TargetCollectionRejectsInvalidHitsCapsAtFifteenAndClearsWhenAimReleased()
        {
            var f = new PlayerSessionFixture();
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
            CollectionAssert.AreEqual(new object[] { new Vector3(27, -23, 11), new Vector3(100017, -23, 11),
                IntersectFlags.Objects | IntersectFlags.Peds | IntersectFlags.Vehicles | IntersectFlags.Map, 0 },
                f.Game.Calls.First(call => call.Name == "world.raycast").Arguments);
            Assert.IsTrue(f.Game.Native.Calls.Any(call => call.Hash == Hash.SHOW_HUD_COMPONENT_THIS_FRAME));
            for (int id = 61; id <= 76; id++)
            {
                f.Game.AddEntity(id).Position = Vector3.UnitX * id;
                if (id == 61) f.Vehicles.Add(id); else f.Peds.Add(id);
                f.Game.World.Rays.Enqueue(new RayHit(true, id, Vector3.Zero));
                f.Tick();
            }
            StringAssert.Contains(f.State(), "targets=15");
            Assert.IsFalse(f.Game.Calls.Where(call => call.Name == "world.marker")
                .Any(call => (Vector3)call.Arguments[1] == new Vector3(76, 0, 2)));
            var firstMarker = f.Game.Calls.First(call => call.Name == "world.marker").Arguments;
            Assert.AreEqual(new Vector3(61, 0, 2), firstMarker[1]);
            Assert.AreEqual(System.Drawing.Color.Red, firstMarker[5]);
            f.Game.Input.Pressed.Clear();
            f.Tick();
            StringAssert.Contains(f.State(), "targets=15",
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
            var s = new PlayerSessionFixture();
            s.Player.Walking = movement == 0;
            s.Player.Sprinting = movement == 1;
            s.Player.Running = movement == 2;
            s.Player.InAir = movement == 3;
            s.Game.Cameras.GameplayDirection = Vector3.UnitX;
            s.Game.Entity(20).Velocity = new Vector3(3, 4, 5);
            s.Throw();
            Assert.AreEqual(new Vector3(103, 4, 10), s.Game.Entity(20).Velocity);
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
            var s = new PlayerSessionFixture();
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
            CollectionAssert.AreEqual(new object[] { 60, 100 }, s.Game.Calls.Single(call => call.Name == "ped.damage").Arguments);
            Assert.AreEqual("scr_meth_pipe_smoke", s.Game.Native.Calls.Single(call =>
                call.Hash == Hash.START_PARTICLE_FX_NON_LOOPED_ON_ENTITY).Arguments[0]);
            var trail = s.Game.Native.Calls.Where(call => call.Hash == Hash.START_PARTICLE_FX_NON_LOOPED_AT_COORD)
                .Select(call => new Vector3((float)call.Arguments[1], (float)call.Arguments[2], (float)call.Arguments[3])).ToArray();
            Assert.HasCount(64, trail);
            Assert.AreEqual(-8f, trail.Min(point => point.Y), 0.0001f);
            Assert.AreEqual(0f, trail.Max(point => point.Y), 0.0001f);
            Assert.IsTrue(trail.All(point => Math.Sqrt(point.X * point.X + point.Z * point.Z) <= .24001));
            Assert.AreEqual(Vector3.Zero, trail[0]);
            Assert.AreEqual(Vector3.UnitY * -8, trail[61]);
            var particles = s.Game.Native.Calls.Where(call => call.Hash == Hash.START_PARTICLE_FX_NON_LOOPED_AT_COORD).ToArray();
            for (int index = 0; index < trail.Length; index += 4)
            {
                var midpoint = (trail[index] + trail[index + 1]) / 2;
                Assert.IsTrue((trail[index + 2] - midpoint).Length() < .0001f);
                Assert.IsTrue((trail[index + 3] - midpoint).Length() < .0001f);
                Assert.AreEqual(trail[index].DistanceTo(trail[index + 1]) * .4f, (float)particles[index].Arguments[7], .0001f);
                if (index > 0) Assert.AreEqual(trail[index - 3], trail[index]);
            }
        }

        [TestMethod]
        [DataRow(false, .5f, true)]
        [DataRow(false, .501f, false)]
        [DataRow(true, 2f, true)]
        [DataRow(true, 2.001f, false)]
        public void AimedThrowKeepsChasingUntilTheObservedTargetIsWithinContactRange(bool vehicle, float distance, bool reached)
        {
            var s = new PlayerSessionFixture();
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
            Assert.AreEqual(reached ? 0 : 100, s.Game.Entity(20).Velocity.Length(), .001f);
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
