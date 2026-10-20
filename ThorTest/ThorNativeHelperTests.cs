using System.Linq;
using GTA;
using GTA.Native;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Thor;

namespace ModUtilsTest
{
    [TestClass]
    public class ThorNativeHelperTests
    {
        [TestMethod]
        [DataRow(AnimationActions.CallingForWeapon, "weapons@first_person@aim_lt@p_m_zero@projectile@misc@sticky_bomb@aim_trans@lt_to_rng", "aim_trans_low", 0)]
        [DataRow(AnimationActions.ThrowHammer1, "anim@melee@machete@streamed_core@", "small_melee_wpn_short_range_0", 800)]
        [DataRow(AnimationActions.ThrowHammer2, "anim@melee@machete@streamed_core@", "plyr_walking_attack_a", 1000)]
        [DataRow(AnimationActions.ThrowHammer3, "melee@small_wpn@streamed_core_fps", "small_melee_wpn_short_range_0", 600)]
        [DataRow(AnimationActions.ThrowHammer4, "melee@small_wpn@streamed_core_fps", "small_melee_wpn_long_range_0", 1000)]
        [DataRow(AnimationActions.ThrowHammer5, "melee@small_wpn@streamed_core", "small_melee_wpn_long_range_0", 1000)]
        [DataRow(AnimationActions.CatchingWeapon1, "melee@small_wpn@streamed_core", "melee_damage_back", 0)]
        [DataRow(AnimationActions.GroundAttack1, "melee@small_wpn@streamed_core_fps", "ground_attack_on_spot", 0)]
        [DataRow(AnimationActions.GroundAttack2, "melee@knife@streamed_core", "ground_attack_on_spot", 0)]
        [DataRow(AnimationActions.GroundAttack3, "melee@small_wpn@streamed_core", "ground_attack_0", 0)]
        [DataRow(AnimationActions.GroundAttack4, "melee@small_wpn@streamed_core", "ground_attack_on_spot", 0)]
        [DataRow(AnimationActions.SummonThunder, "anim@mp_fm_event@intro", "beast_transform", 0)]
        [DataRow(AnimationActions.WhirlingHammer, "melee@small_wpn@streamed_core", "dodge_generic_centre", 0)]
        [DataRow(AnimationActions.ThrowTwoHandHammer1, "melee@large_wpn@streamed_core_fps", "short_0_attack", 800)]
        [DataRow(AnimationActions.ThrowTwoHandHammer2, "melee@large_wpn@streamed_core_fps", "long_0_attack", 1150)]
        public void NamedActionsRequestTheirAssetPlayTheExpectedClipAndResolveReleaseDelay(
            AnimationActions action, string expectedDictionary, string expectedClip, int expectedWait)
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
            Assert.AreEqual(expectedWait, effects.Operations.GetAnimationWaitTimeByDictNameAndAnimName(dictionary, clip));
        }

        [TestMethod]
        public void DirectionalThrowSelectionExcludesClipsWithoutDirectionalVariants()
        {
            var effects = new Thor.NativeHelper(new FakeGame().Services);
            var candidates = new[] { AnimationActions.ThrowHammer1, AnimationActions.ThrowHammer2,
                AnimationActions.ThrowHammer3, AnimationActions.ThrowHammer4, AnimationActions.ThrowHammer5,
                AnimationActions.ThrowTwoHandHammer1, AnimationActions.ThrowTwoHandHammer2 };
            CollectionAssert.AreEqual(new[] { AnimationActions.ThrowHammer3, AnimationActions.ThrowHammer4,
                AnimationActions.ThrowHammer5, AnimationActions.ThrowTwoHandHammer1, AnimationActions.ThrowTwoHandHammer2 },
                candidates.Where(action => effects.Operations.DoesAnimationActionHaveAngles((uint)action)).ToArray());
            Assert.AreEqual("ground_attack_0", effects.GetAnimationNameForAngle(AnimationActions.GroundAttack3, 180),
                "The numeric ground-attack clip suffix does not imply supported directional variants.");
        }
    }
}
