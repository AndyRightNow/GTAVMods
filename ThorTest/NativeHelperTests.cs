using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using ADModUtils;
using GTA;
using GTA.Math;
using GTA.Native;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ModUtilsTest
{
    [TestClass]
    public class NativeHelperTests
    {
        private static NativeHelper Helper(FakeGame game) => new NativeHelper(game.Services,
            new[] { "dict" }, new[] { "anim" },
            new Dictionary<string, Dictionary<string, int>> { { "dict", new Dictionary<string, int> { { "anim", 250 } } } },
            new[] { true }, new[] { false }, new[] { "assets" }, new[] { "spark" }, 200, 300);

        private static void AssertCall(FakeGame game, Hash hash, params object[] arguments)
        {
            var call = game.Native.Calls.Single(item => item.Hash == hash);
            CollectionAssert.AreEqual(arguments, call.Arguments);
        }

        [TestMethod]
        public void ConfigurationResolvesRequestedAnimationAndMissingWaits()
        {
            var helper = Helper(new FakeGame());
            Assert.AreEqual("dict", helper.GetAnimationDictNameByAction(0));
            Assert.AreEqual("anim", helper.GetAnimationNameByAction(0));
            Assert.AreEqual(250, helper.GetAnimationWaitTimeByDictNameAndAnimName("dict", "anim"));
            Assert.AreEqual(0, helper.GetAnimationWaitTimeByDictNameAndAnimName("dict", "missing"));
            Assert.AreEqual(0, helper.GetAnimationWaitTimeByDictNameAndAnimName("missing", "anim"));
            Assert.IsTrue(helper.DoesAnimationActionHaveAngles(0));
            Assert.IsFalse(helper.DoesAnimationActionHaveAnglesAndIncompletePlusOrMinusSign(0));
            Assert.AreEqual("assets", helper.GetParticleSetName(0));
            Assert.AreEqual("spark", helper.GetParticleName(0));
            Assert.AreEqual(IntersectFlags.Objects | IntersectFlags.Peds | IntersectFlags.Vehicles | IntersectFlags.Map, NativeHelper.IntersectAllObjects);
        }

        [TestMethod]
        public void ForcesScaleDamageForOtherPedsButNeverDamageThePlayerOrVehicle()
        {
            var game = new FakeGame(); game.AddEntity(10); game.AddEntity(20); game.AddEntity(30);
            game.Native.Queries[Hash.IS_ENTITY_A_PED] = arguments => (int)arguments[0] != 30;
            var helper = Helper(game);
            helper.ApplyForcesAndDamages(20, new Vector3(1, 2, 0), 50);
            var damage = game.Calls.Single(call => call.Name == "ped.damage");
            CollectionAssert.AreEqual(new object[] { 20, 100 }, damage.Arguments);
            AssertCall(game, Hash.SET_PED_TO_RAGDOLL, 20, 100, 100, 0, 0, 0, 0);
            CollectionAssert.AreEqual(new object[] { 20, new Vector3(150, 300, 0) }, game.Calls.Single(call => call.Name == "entity.force").Arguments);
            helper.ApplyForcesAndDamages(10, Vector3.UnitZ);
            helper.ApplyForcesAndDamages(30, Vector3.UnitX);
            Assert.AreEqual(1, game.Calls.Count(call => call.Name == "ped.damage"));
            Assert.AreEqual(3, game.Native.Calls.Count(call => call.Hash == Hash.CLEAR_ENTITY_LAST_DAMAGE_ENTITY));
            CollectionAssert.AreEqual(new object[] { 30, new Vector3(300, 0, 0) }, game.Calls.Last(call => call.Name == "entity.force").Arguments);
        }

        [TestMethod]
        public void AnimationSkipsAlreadyPlayingThenRequestsLoadsAndPlaysExactFlags()
        {
            var game = new FakeGame(); var helper = Helper(game);
            game.Native.Returns(Hash.IS_ENTITY_PLAYING_ANIM, true);
            helper.PlayPlayerAnimation(10, "dict", "anim", AnimationFlags.UpperBodyOnly);
            Assert.HasCount(1, game.Native.Calls);
            AssertCall(game, Hash.IS_ENTITY_PLAYING_ANIM, 10, "dict", "anim", 3);
            game.Native.Calls.Clear(); game.Calls.Clear();
            game.Native.Returns(Hash.IS_ENTITY_PLAYING_ANIM, false);
            var responses = new Queue<bool>(new[] { false, true });
            game.Native.Queries[Hash.HAS_ANIM_DICT_LOADED] = arguments => responses.Dequeue();
            game.Host.OnYield = () => game.Host.GameTime++;
            helper.PlayPlayerAnimation(10, "dict", "anim", AnimationFlags.UpperBodyOnly, 250);
            AssertCall(game, Hash.REQUEST_ANIM_DICT, "dict");
            AssertCall(game, Hash.TASK_PLAY_ANIM, 10, "dict", "anim", 8f, 1f, 250, (int)AnimationFlags.UpperBodyOnly, -8f, 0, 0, 0);
            Assert.AreEqual(1, game.Calls.Count(call => call.Name == "host.yield"));
            helper.ClearPlayerAnimation(10, "dict", "anim");
            AssertCall(game, Hash.STOP_ANIM_TASK, 10, "dict", "anim", 3);
        }

        [TestMethod]
        public void AnimationTimeoutStopsAtOneSecondIncludingGameTimeWrap()
        {
            var game = new FakeGame();
            game.Host.GameTime = int.MaxValue - 100;
            game.Host.OnYield = () => game.Host.GameTime = unchecked(game.Host.GameTime + 500);
            game.Native.Returns(Hash.HAS_ANIM_DICT_LOADED, false);
            Helper(game).PlayPlayerAnimation(10, "unavailable", "anim", AnimationFlags.None, checkIsPlaying: false);
            Assert.AreEqual(2, game.Calls.Count(call => call.Name == "host.yield"));
            Assert.AreEqual(3, game.Native.Calls.Count(call => call.Hash == Hash.HAS_ANIM_DICT_LOADED));
            Assert.IsFalse(game.Native.Calls.Any(call => call.Hash == Hash.TASK_PLAY_ANIM || call.Hash == Hash.IS_ENTITY_PLAYING_ANIM));
        }

        [TestMethod]
        public void WeaponCreationPassesCurrentNativeSignatureAndRejectsAssetTimeout()
        {
            var game = new FakeGame(); var helper = Helper(game);
            game.Weapons.AssetResults[WeaponHash.Hammer] = true;
            game.Native.Returns(Hash.CREATE_WEAPON_OBJECT, 42);
            Assert.AreEqual(42, helper.CreateWeaponObject(WeaponHash.Hammer, 3, new Vector3(1, 2, 3), false, 0.5f));
            CollectionAssert.AreEqual(new object[] { WeaponHash.Hammer, 3000 }, game.Calls.Single(call => call.Name == "weapon.request").Arguments);
            AssertCall(game, Hash.CREATE_WEAPON_OBJECT, (int)WeaponHash.Hammer, 3, 1f, 2f, 3f, false, 0.5f, false, false, 0);
            game.Native.Returns(Hash.CREATE_WEAPON_OBJECT, 0);
            Assert.AreEqual(0, helper.CreateWeaponObject(WeaponHash.Hammer, 1, Vector3.Zero));
            game.Weapons.AssetResults[WeaponHash.Hammer] = false;
            Assert.ThrowsExactly<InvalidOperationException>(() => helper.CreateWeaponObject(WeaponHash.Hammer, 1, Vector3.Zero));
            Assert.AreEqual(2, game.Native.Calls.Count(call => call.Hash == Hash.CREATE_WEAPON_OBJECT));
        }

        [TestMethod]
        public void EntityAndPhysicsHelpersPreserveComponentAndParameterOrder()
        {
            var game = new FakeGame(); game.AddEntity(10);
            var helper = Helper(game);
            game.Outputs.DamageBones[10] = Tuple.Create(false, 0);
            Assert.AreEqual(Bone.IKRoot, helper.GetLastDamagedBone(10));
            game.Outputs.DamageBones[10] = Tuple.Create(true, (int)Bone.SkelHead);
            Assert.AreEqual(Bone.SkelHead, helper.GetLastDamagedBone(10));
            game.Native.Returns(Hash.IS_ENTITY_A_VEHICLE, true);
            Assert.IsTrue(helper.IsVehicle(20)); AssertCall(game, Hash.IS_ENTITY_A_VEHICLE, 20);
            helper.SetEntityVelocity(10, new Vector3(1, -2, 3));
            AssertCall(game, Hash.SET_ENTITY_VELOCITY, 10, 1f, -2f, 3f);
            helper.SetPedWeaponVisible(10, false);
            AssertCall(game, Hash.SET_PED_CURRENT_WEAPON_VISIBLE, 10, false, 0, 0, 0);
            helper.SetObjectPhysicsParams(20, 12, 2, 3, 4, 5, 6, 7, 8);
            AssertCall(game, Hash.SET_OBJECT_PHYSICS_PARAMS, 20, 12f, 2f, 3f, 4f, 5f, 6f, 7f, 8f, 0f);
        }

        [TestMethod]
        public void DrawingPreservesCoordinatesAlphaAndLineOrder()
        {
            var game = new FakeGame(); var helper = Helper(game);
            var start = new Vector3(1, 2, 3); var end = new Vector3(4, 5, 6);
            var color = Color.FromArgb(40, 10, 20, 30);
            helper.DrawLine(start, end, color);
            AssertCall(game, Hash.DRAW_LINE, 1f, 2f, 3f, 4f, 5f, 6f, (byte)10, (byte)20, (byte)30, (byte)40);
            helper.DrawBox(start, end, color);
            AssertCall(game, Hash.DRAW_BOX, 1f, 2f, 3f, 4f, 5f, 6f, (byte)10, (byte)20, (byte)30, (byte)40);
            game.Native.Calls.Clear();
            var line = new Line(start, end, color);
            Assert.AreEqual(start, line.Start); Assert.AreEqual(end, line.End);
            StringAssert.Contains(line.ToString(), "Color " + color);
            var lines = new List<Line> { line, new Line(end, start, color) };
            helper.DrawLines(ref lines);
            Assert.HasCount(2, game.Native.Calls);
            CollectionAssert.AreEqual(new object[] { 1f, 2f, 3f, 4f, 5f, 6f, (byte)10, (byte)20, (byte)30, (byte)40 }, game.Native.Calls[0].Arguments);
            CollectionAssert.AreEqual(new object[] { 4f, 5f, 6f, 1f, 2f, 3f, (byte)10, (byte)20, (byte)30, (byte)40 }, game.Native.Calls[1].Arguments);
        }

        [TestMethod]
        [DataRow("entity", false)] [DataRow("entity", true)]
        [DataRow("offset", false)] [DataRow("offset", true)]
        [DataRow("bone", false)] [DataRow("bone", true)]
        [DataRow("coordinate", false)] [DataRow("coordinate", true)]
        [DataRow("loop-bone", false)] [DataRow("loop-bone", true)]
        [DataRow("loop-coordinate", false)] [DataRow("loop-coordinate", true)]
        public void ParticlesRequireLoadedAssetsAndPreserveOverloadArguments(string overload, bool loaded)
        {
            var game = new FakeGame(); var helper = Helper(game);
            game.Native.Returns(Hash.HAS_NAMED_PTFX_ASSET_LOADED, loaded);
            game.Native.Returns(Hash.START_PARTICLE_FX_LOOPED_ON_PED_BONE, 71);
            game.Native.Returns(Hash.START_PARTICLE_FX_LOOPED_AT_COORD, 72);
            var position = new Vector3(1, 2, 3); var rotation = new Vector3(4, 5, 6);
            Hash expectedHash; object[] expected;
            switch (overload)
            {
                case "entity":
                    helper.PlayParticleFx("assets", "spark", 10, 0.5f);
                    expectedHash = Hash.START_PARTICLE_FX_NON_LOOPED_ON_ENTITY;
                    expected = new object[] { "spark", 10, 0f, 0f, 0f, 0f, 0f, 0f, 0.5f, 0, 0, 0 }; break;
                case "offset":
                    helper.PlayParticleFx("assets", "spark", 10, position, 0.5f);
                    expectedHash = Hash.START_PARTICLE_FX_NON_LOOPED_ON_ENTITY;
                    expected = new object[] { "spark", 10, 1f, 2f, 3f, 0f, 0f, 0f, 0.5f, 0, 0, 0 }; break;
                case "bone":
                    helper.PlayParticleFx("assets", "spark", 10, Bone.PHRightHand, 0.5f);
                    expectedHash = Hash.START_PARTICLE_FX_NON_LOOPED_ON_PED_BONE;
                    expected = new object[] { "spark", 10, 0f, 0f, 0f, 0f, 0f, 0f, (int)Bone.PHRightHand, 0.5f, 0, 0, 0 }; break;
                case "coordinate":
                    helper.PlayParticleFx("assets", "spark", position, rotation, 0.5f);
                    expectedHash = Hash.START_PARTICLE_FX_NON_LOOPED_AT_COORD;
                    expected = new object[] { "spark", 1f, 2f, 3f, 4f, 5f, 6f, 0.5f, 0, 0, 0 }; break;
                case "loop-bone":
                    Assert.AreEqual(loaded ? 71 : 0, helper.PlayParticleFxLooped("assets", "spark", 10, Bone.PHRightHand, 0.5f));
                    expectedHash = Hash.START_PARTICLE_FX_LOOPED_ON_PED_BONE;
                    expected = new object[] { "spark", 10, 0f, 0f, 0f, 0f, 0f, 0f, (int)Bone.PHRightHand, 0.5f, 0, 0, 0 }; break;
                default:
                    Assert.AreEqual(loaded ? 72 : 0, helper.PlayParticleFxLooped("assets", "spark", position, rotation, 0.5f));
                    expectedHash = Hash.START_PARTICLE_FX_LOOPED_AT_COORD;
                    expected = new object[] { "spark", 1f, 2f, 3f, 4f, 5f, 6f, 0.5f, 0, 0, 0 }; break;
            }
            AssertCall(game, Hash.REQUEST_NAMED_PTFX_ASSET, "assets");
            AssertCall(game, Hash.HAS_NAMED_PTFX_ASSET_LOADED, "assets");
            if (loaded)
            {
                CollectionAssert.AreEqual(new[] { Hash.REQUEST_NAMED_PTFX_ASSET, Hash.HAS_NAMED_PTFX_ASSET_LOADED, Hash.USE_PARTICLE_FX_ASSET, expectedHash }, game.Native.Calls.Select(call => call.Hash).ToArray());
                AssertCall(game, Hash.USE_PARTICLE_FX_ASSET, "assets"); AssertCall(game, expectedHash, expected);
            }
            else Assert.HasCount(2, game.Native.Calls);
        }

        [TestMethod]
        public void LoopedParticleUpdatesAndStopsTheHandleReturnedByItsStart()
        {
            var game = new FakeGame();
            game.Native.Returns(Hash.HAS_NAMED_PTFX_ASSET_LOADED, true);
            game.Native.Returns(Hash.START_PARTICLE_FX_LOOPED_ON_PED_BONE, 71);
            game.Native.Returns(Hash.START_PARTICLE_FX_LOOPED_AT_COORD, 72);
            var effect = new ParticleFxLooped("assets", "spark", Helper(game), game.Native);
            effect.Start(10, Bone.PHRightHand); effect.Color = Color.FromArgb(10, 20, 30); effect.Stop();
            AssertCall(game, Hash.SET_PARTICLE_FX_LOOPED_COLOUR, 71, (byte)10, (byte)20, (byte)30, 0);
            AssertCall(game, Hash.STOP_PARTICLE_FX_LOOPED, 71, 0);
            game.Native.Calls.Clear();
            effect.Start(new Vector3(1, 2, 3), new Vector3(4, 5, 6)); effect.Stop();
            AssertCall(game, Hash.STOP_PARTICLE_FX_LOOPED, 72, 0);
        }
    }
}
