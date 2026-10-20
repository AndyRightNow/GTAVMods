using System;
using System.Linq;
using GTA;
using GTA.Math;
using GTA.Native;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Thor;

namespace ModUtilsTest
{
    [TestClass]
    public class ThorNativeHelperTests
    {
        [TestMethod]
        [DataRow(AnimationActions.CallingForWeapon, "weapons@first_person@aim_lt@p_m_zero@projectile@misc@sticky_bomb@aim_trans@lt_to_rng", "aim_trans_low")]
        [DataRow(AnimationActions.CatchingWeapon1, "melee@small_wpn@streamed_core", "melee_damage_back")]
        [DataRow(AnimationActions.GroundAttack1, "melee@small_wpn@streamed_core_fps", "ground_attack_on_spot")]
        [DataRow(AnimationActions.GroundAttack2, "melee@knife@streamed_core", "ground_attack_on_spot")]
        [DataRow(AnimationActions.GroundAttack3, "melee@small_wpn@streamed_core", "ground_attack_0")]
        [DataRow(AnimationActions.GroundAttack4, "melee@small_wpn@streamed_core", "ground_attack_on_spot")]
        [DataRow(AnimationActions.SummonThunder, "anim@mp_fm_event@intro", "beast_transform")]
        [DataRow(AnimationActions.WhirlingHammer, "melee@small_wpn@streamed_core", "dodge_generic_centre")]
        public void NamedActionsRequestTheirAssetAndPlayTheExpectedClip(
            AnimationActions action, string expectedDictionary, string expectedClip)
        {
            var game = new FakeGame();
            game.Native.Returns(Hash.HAS_ANIM_DICT_LOADED, true);
            var effects = new Thor.NativeHelper(game.Services);
            string dictionary = effects.Operations.GetAnimationDictNameByAction((uint)action);
            string clip = effects.GetAnimationNameForAngle(action, 0);
            effects.Operations.PlayPlayerAnimation(27, dictionary, clip, AnimationFlags.UpperBodyOnly, 333, false);
            CollectionAssert.AreEqual(new[] { Hash.REQUEST_ANIM_DICT, Hash.HAS_ANIM_DICT_LOADED, Hash.TASK_PLAY_ANIM },
                game.Native.Calls.Select(call => call.Hash).ToArray());
            CollectionAssert.AreEqual(new object[] { expectedDictionary }, game.Native.Calls[0].Arguments);
            CollectionAssert.AreEqual(new object[] { 27, expectedDictionary, expectedClip, 8f, 1f, 333,
                (int)AnimationFlags.UpperBodyOnly, -8f, 0, 0, 0 }, game.Native.Calls[2].Arguments);
        }

        [TestMethod]
        [DataRow(AnimationActions.ThrowHammer1, 0f, "anim@melee@machete@streamed_core@", "small_melee_wpn_short_range_0", 800)]
        [DataRow(AnimationActions.ThrowHammer2, 0f, "anim@melee@machete@streamed_core@", "plyr_walking_attack_a", 1000)]
        [DataRow(AnimationActions.ThrowHammer3, 0f, "melee@small_wpn@streamed_core_fps", "small_melee_wpn_short_range_0", 600)]
        [DataRow(AnimationActions.ThrowHammer3, -90f, "melee@small_wpn@streamed_core_fps", "small_melee_wpn_short_range_+90", 600)]
        [DataRow(AnimationActions.ThrowHammer3, 90f, "melee@small_wpn@streamed_core_fps", "small_melee_wpn_short_range_-90", 400)]
        [DataRow(AnimationActions.ThrowHammer3, -179f, "melee@small_wpn@streamed_core_fps", "small_melee_wpn_short_range_+180", 500)]
        [DataRow(AnimationActions.ThrowHammer3, 179f, "melee@small_wpn@streamed_core_fps", "small_melee_wpn_short_range_-180", 800)]
        [DataRow(AnimationActions.ThrowHammer4, 0f, "melee@small_wpn@streamed_core_fps", "small_melee_wpn_long_range_0", 1000)]
        [DataRow(AnimationActions.ThrowHammer4, -90f, "melee@small_wpn@streamed_core_fps", "small_melee_wpn_long_range_+90", 700)]
        [DataRow(AnimationActions.ThrowHammer4, 90f, "melee@small_wpn@streamed_core_fps", "small_melee_wpn_long_range_-90", 700)]
        [DataRow(AnimationActions.ThrowHammer4, -179f, "melee@small_wpn@streamed_core_fps", "small_melee_wpn_long_range_+180", 1000)]
        [DataRow(AnimationActions.ThrowHammer4, 179f, "melee@small_wpn@streamed_core_fps", "small_melee_wpn_long_range_-180", 800)]
        [DataRow(AnimationActions.ThrowHammer5, 0f, "melee@small_wpn@streamed_core", "small_melee_wpn_long_range_0", 1000)]
        [DataRow(AnimationActions.ThrowHammer5, -90f, "melee@small_wpn@streamed_core", "small_melee_wpn_long_range_+90", 600)]
        [DataRow(AnimationActions.ThrowHammer5, 90f, "melee@small_wpn@streamed_core", "small_melee_wpn_long_range_-90", 700)]
        [DataRow(AnimationActions.ThrowHammer5, -179f, "melee@small_wpn@streamed_core", "small_melee_wpn_long_range_+180", 1100)]
        [DataRow(AnimationActions.ThrowHammer5, 179f, "melee@small_wpn@streamed_core", "small_melee_wpn_long_range_-180", 800)]
        [DataRow(AnimationActions.ThrowTwoHandHammer1, 0f, "melee@large_wpn@streamed_core_fps", "short_0_attack", 800)]
        [DataRow(AnimationActions.ThrowTwoHandHammer1, -90f, "melee@large_wpn@streamed_core_fps", "short_90_attack", 800)]
        [DataRow(AnimationActions.ThrowTwoHandHammer1, 90f, "melee@large_wpn@streamed_core_fps", "short_-90_attack", 800)]
        [DataRow(AnimationActions.ThrowTwoHandHammer1, -179f, "melee@large_wpn@streamed_core_fps", "short_-180_attack", 700)]
        [DataRow(AnimationActions.ThrowTwoHandHammer2, 0f, "melee@large_wpn@streamed_core_fps", "long_0_attack", 1150)]
        [DataRow(AnimationActions.ThrowTwoHandHammer2, -90f, "melee@large_wpn@streamed_core_fps", "long_90_attack", 1300)]
        [DataRow(AnimationActions.ThrowTwoHandHammer2, 90f, "melee@large_wpn@streamed_core_fps", "long_-90_attack", 1000)]
        [DataRow(AnimationActions.ThrowTwoHandHammer2, 179f, "melee@large_wpn@streamed_core_fps", "long_-180_attack", 1000)]
        public void ThrowWaitsForTheSelectedDirectionalClipBeforeReleasingTheHammer(
            AnimationActions action, float angle, string dictionary, string clip, int delay)
        {
            var f = Throwing(action);
            double radians = angle * Math.PI / 180;
            f.Game.Cameras.GameplayDirection = new Vector3(-(float)Math.Sin(radians), (float)Math.Cos(radians), 0);
            f.Game.Host.OnWait = milliseconds =>
            {
                Assert.AreEqual(delay, milliseconds);
                Assert.AreEqual(WeaponHash.Hammer, f.Player.CurrentWeapon, "The hammer stays held throughout the animation wind-up.");
                Assert.AreEqual(Vector3.Zero, f.Game.Entity(20).Velocity);
                CollectionAssert.AreEqual(new object[] { 10, dictionary, clip, 8f, 1f, -1,
                    (int)AnimationFlags.None, -8f, 0, 0, 0 }, f.LastNative(Hash.TASK_PLAY_ANIM));
            };

            f.Ability.ThrowWeapon();

            CollectionAssert.AreEqual(new object[] { dictionary }, f.LastNative(Hash.REQUEST_ANIM_DICT));
            CollectionAssert.AreEqual(new object[] { delay }, f.Game.Calls.Single(call => call.Name == "host.wait").Arguments);
            Assert.AreEqual(WeaponHash.Unarmed, f.Player.CurrentWeapon);
            Assert.AreEqual(f.Game.Cameras.GameplayDirection * 100 + Vector3.UnitZ * 5, f.Game.Entity(20).Velocity);
        }

        [TestMethod]
        [DataRow(AnimationActions.ThrowHammer1, 90f, "small_melee_wpn_short_range_0")]
        [DataRow(AnimationActions.GroundAttack3, 180f, "ground_attack_0")]
        [DataRow(AnimationActions.ThrowHammer3, 44.99f, "small_melee_wpn_short_range_0")]
        [DataRow(AnimationActions.ThrowHammer3, -45f, "small_melee_wpn_short_range_+90")]
        [DataRow(AnimationActions.ThrowHammer3, 45f, "small_melee_wpn_short_range_-90")]
        [DataRow(AnimationActions.ThrowHammer4, -134.99f, "small_melee_wpn_long_range_+90")]
        [DataRow(AnimationActions.ThrowHammer4, 134.99f, "small_melee_wpn_long_range_-90")]
        [DataRow(AnimationActions.ThrowHammer4, -135f, "small_melee_wpn_long_range_+180")]
        [DataRow(AnimationActions.ThrowHammer4, 135f, "small_melee_wpn_long_range_-180")]
        public void DirectionalAnimationRequestsPlayTheClipForTheBoundarySector(AnimationActions action, float angle, string clip)
        {
            var game = new FakeGame();
            game.Native.Returns(Hash.HAS_ANIM_DICT_LOADED, true);
            var effects = new Thor.NativeHelper(game.Services);
            effects.Operations.PlayPlayerAnimation(27, effects.Operations.GetAnimationDictNameByAction((uint)action),
                effects.GetAnimationNameForAngle(action, angle), AnimationFlags.None, 333, false);
            var playback = game.Native.Calls.Single(call => call.Hash == Hash.TASK_PLAY_ANIM);
            Assert.AreEqual(27, playback.Arguments[0]);
            Assert.AreEqual(clip, playback.Arguments[2]);
            Assert.AreEqual(333, playback.Arguments[5]);
        }

        [TestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public void MissingThrowTimingReleasesTheHammerWithoutAnInventedDelay(bool missingDictionary)
        {
            var f = Throwing(AnimationActions.ThrowHammer1);
            const string dictionary = "anim@melee@machete@streamed_core@";
            const string clip = "small_melee_wpn_short_range_0";
            var timings = f.Effects.Operations.AnimationWaitTime;
            var clips = timings[dictionary];
            int originalDelay = clips[clip];
            try
            {
                if (missingDictionary) timings.Remove(dictionary);
                else clips.Remove(clip);

                f.Ability.ThrowWeapon();

                CollectionAssert.AreEqual(new object[] { 0 }, f.Game.Calls.Single(call => call.Name == "host.wait").Arguments);
                Assert.AreEqual(clip, f.LastNative(Hash.TASK_PLAY_ANIM)[2]);
                Assert.AreEqual(WeaponHash.Unarmed, f.Player.CurrentWeapon);
                Assert.AreEqual(new Vector3(0, 100, 5), f.Game.Entity(20).Velocity);
            }
            finally
            {
                clips[clip] = originalDelay;
                timings[dictionary] = clips;
            }
        }

        private static HammerFixture Throwing(AnimationActions action)
        {
            var f = new HammerFixture(new FixedRandom(choice: 0));
            // The weapon exposes its permitted throw actions; choose one configured
            // animation so every release delay is checked through the ability.
            for (int i = 0; i < f.Hammer.ThrowActions.Length; i++) f.Hammer.ThrowActions[i] = action;
            f.HoldHammer();
            f.Ability.ApplyOn(10);
            f.Game.Entities.CreatedBlips.Enqueue(50);
            return f;
        }
    }
}
