using System;
using System.Linq;
using GTA;
using GTA.Math;
using GTA.Native;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ModUtilsTest
{
    [TestClass, TestCategory("PureFunction")]
    public class AnimationFormattingTests
    {
        [TestMethod]
        [DataRow(false, false, 90f, "attack_0")]
        [DataRow(true, false, 44.99f, "attack_0")]
        [DataRow(true, false, -45f, "attack_+90")]
        [DataRow(true, false, 45f, "attack_-90")]
        [DataRow(true, false, -134.99f, "attack_+90")]
        [DataRow(true, false, 134.99f, "attack_-90")]
        [DataRow(true, false, -135f, "attack_+180")]
        [DataRow(true, false, 135f, "attack_-180")]
        [DataRow(true, true, -90f, "attack_90")]
        [DataRow(true, true, 90f, "attack_-90")]
        [DataRow(true, true, -179f, "attack_-180")]
        public void DirectionalClipFormattingHonorsSectorBoundariesAndTheCatalogSignConvention(bool angled, bool incomplete, float angle, string expected)
        {
            Assert.AreEqual(expected, Thor.NativeHelper.AnimationNameForAngle("attack_0", angle, angled, incomplete));
        }

    }

    [TestClass, TestCategory("PlayerIntegration")]
    public class PlayerThrowAnimationIntegrationTests
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
    }
}
