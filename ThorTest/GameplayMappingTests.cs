using System;
using System.Drawing;
using System.Linq;
using GTA;
using GTA.Math;
using GTA.Native;
using GTA.UI;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ModUtilsTest
{
    [TestClass]
    public class GameplayMappingTests
    {
        private static void LastCall(FakeGame game, Hash hash, params object[] expected)
        {
            var call = game.Native.Calls.Last();
            Assert.AreEqual(hash, call.Hash);
            CollectionAssert.AreEqual(expected, call.Arguments);
        }

        [TestMethod]
        public void GameplayReadsKeepEntityIdentityAndDropReturnsTheEngineResult()
        {
            var game = new FakeGame();
            game.Native.Returns(Hash.GET_ENTITY_COORDS, new Vector3(-3, 17, 0.25f));
            game.Native.Returns(Hash.GET_ENTITY_SPEED, 52.5f);
            game.Native.Returns(Hash.GET_WEAPON_OBJECT_FROM_PED, 71);
            Assert.AreEqual(new Vector3(-3, 17, 0.25f), game.Services.Gameplay.Position(400));
            LastCall(game, Hash.GET_ENTITY_COORDS, 400, false);
            Assert.AreEqual(52.5f, game.Services.Gameplay.Speed(400));
            LastCall(game, Hash.GET_ENTITY_SPEED, 400);
            Assert.AreEqual(71, game.Services.Gameplay.DropWeaponObjectFromPed(20));
            LastCall(game, Hash.GET_WEAPON_OBJECT_FROM_PED, 20, false);
            game.Native.Returns(Hash.GET_WEAPON_OBJECT_FROM_PED, 0);
            Assert.AreEqual(0, game.Services.Gameplay.DropWeaponObjectFromPed(20), "No weapon must remain zero, not a cached prior handle.");
        }

        [TestMethod]
        public void GameplayCommandsKeepPlayerPedAndWeaponHandlesInTheirDistinctNativeSlots()
        {
            var game = new FakeGame();
            var gameplay = game.Services.Gameplay;
            gameplay.SetFreezeWhileWaitingForCollision(400, false);
            LastCall(game, Hash.SET_ENTITY_SHOULD_FREEZE_WAITING_ON_COLLISION, 400, false);
            gameplay.ActivatePhysics(400);
            LastCall(game, Hash.ACTIVATE_PHYSICS, 400);
            gameplay.SetLockOnRange(3, 7.5f);
            LastCall(game, Hash.SET_PLAYER_LOCKON_RANGE_OVERRIDE, 3, 7.5f);
            gameplay.SetMoveRate(20, 1.25f);
            LastCall(game, Hash.SET_PED_MOVE_RATE_OVERRIDE, 20, 1.25f);
            gameplay.SetSwimRate(3, 1.4f);
            LastCall(game, Hash.SET_SWIM_MULTIPLIER_FOR_PLAYER, 3, 1.4f);
            gameplay.SuperJumpThisFrame(3);
            LastCall(game, Hash.SET_SUPER_JUMP_THIS_FRAME, 3);
            gameplay.SetCanBeDamaged(20, true);
            LastCall(game, Hash.SET_ENTITY_CAN_BE_DAMAGED, 20, true);
            gameplay.DisablePainAudio(20, false);
            LastCall(game, Hash.DISABLE_PED_PAIN_AUDIO, 20, false);
            gameplay.ShowHudComponentThisFrame(HudComponent.Reticle);
            LastCall(game, Hash.SHOW_HUD_COMPONENT_THIS_FRAME, 14);
            gameplay.GiveWeaponObjectToPed(400, 20);
            LastCall(game, Hash.GIVE_WEAPON_OBJECT_TO_PED, 400, 20);
            gameplay.LightningFlash();
            LastCall(game, Hash.FORCE_LIGHTNING_FLASH);
            CollectionAssert.AreEqual(new[]
            {
                Hash.SET_ENTITY_SHOULD_FREEZE_WAITING_ON_COLLISION, Hash.ACTIVATE_PHYSICS,
                Hash.SET_PLAYER_LOCKON_RANGE_OVERRIDE, Hash.SET_PED_MOVE_RATE_OVERRIDE,
                Hash.SET_SWIM_MULTIPLIER_FOR_PLAYER, Hash.SET_SUPER_JUMP_THIS_FRAME,
                Hash.SET_ENTITY_CAN_BE_DAMAGED, Hash.DISABLE_PED_PAIN_AUDIO,
                Hash.SHOW_HUD_COMPONENT_THIS_FRAME, Hash.GIVE_WEAPON_OBJECT_TO_PED, Hash.FORCE_LIGHTNING_FLASH
            }, game.Native.Calls.Select(call => call.Hash).ToArray());
        }

        [TestMethod]
        public void AnimationMappingPreservesIndependentDurationsSpeedsFlagsAndFailureObservations()
        {
            var game = new FakeGame();
            var animations = game.Services.Animations;
            game.Native.Returns(Hash.IS_ENTITY_PLAYING_ANIM, false);
            Assert.IsFalse(animations.IsPlaying(20, "move", "jump"));
            LastCall(game, Hash.IS_ENTITY_PLAYING_ANIM, 20, "move", "jump", 3);
            animations.Play(20, "move", "jump", AnimationFlags.UpperBodyOnly, 731, 4.25f, 2.5f, 0.75f);
            LastCall(game, Hash.TASK_PLAY_ANIM, 20, "move", "jump", 4.25f, 2.5f, 731, 16, 0.75f, 0, 0, 0);
            animations.Stop(20, "move", "jump", 1.25f);
            LastCall(game, Hash.STOP_ANIM_TASK, 20, "move", "jump", 1.25f);
            game.Services.Gameplay.SetRagdoll(20, GTA.RagdollType.Balance, 75, 250);
            LastCall(game, Hash.SET_PED_TO_RAGDOLL, 20, 75, 250, 2, 0, 0, 0);
        }

        [TestMethod]
        public void ParticleMappingDistinguishesOffsetRotationBoneAndNativeHandleFailure()
        {
            var game = new FakeGame();
            var particles = game.Services.Particles;
            var offset = new Vector3(-2, 4, 0.5f);
            var rotation = new Vector3(30, -60, 90);
            particles.PlayOnEntity("sparks", 400, offset, rotation, 0.75f);
            LastCall(game, Hash.START_PARTICLE_FX_NON_LOOPED_ON_ENTITY, "sparks", 400,
                -2f, 4f, 0.5f, 30f, -60f, 90f, 0.75f, 0, 0, 0);
            particles.PlayOnPedBone("sparks", 20, Bone.SkelHead, offset, rotation, 0.75f);
            LastCall(game, Hash.START_PARTICLE_FX_NON_LOOPED_ON_PED_BONE, "sparks", 20,
                -2f, 4f, 0.5f, 30f, -60f, 90f, 31086, 0.75f, 0, 0, 0);
            game.Native.Returns(Hash.START_PARTICLE_FX_LOOPED_ON_PED_BONE, 91);
            Assert.AreEqual(91, particles.StartLoopedOnPedBone("sparks", 20, Bone.SkelHead, offset, rotation, 0.75f));
            LastCall(game, Hash.START_PARTICLE_FX_LOOPED_ON_PED_BONE, "sparks", 20,
                -2f, 4f, 0.5f, 30f, -60f, 90f, 31086, 0.75f, 0, 0, 0);
            game.Native.Returns(Hash.START_PARTICLE_FX_LOOPED_AT_COORD, 0);
            Assert.AreEqual(0, particles.StartLoopedAt("sparks", offset, rotation, 0.75f));
            LastCall(game, Hash.START_PARTICLE_FX_LOOPED_AT_COORD, "sparks",
                -2f, 4f, 0.5f, 30f, -60f, 90f, 0.75f, 0, 0, 0, false);
            particles.SetLoopedColor(91, Color.FromArgb(64, 51, 153, 255));
            LastCall(game, Hash.SET_PARTICLE_FX_LOOPED_COLOUR, 91, 0.2f, 0.6f, 1f, 0);
        }

        [TestMethod]
        public void NativeFailureStopsAtTheRequestedOperationAndPreservesOriginalError()
        {
            var game = new FakeGame();
            var failure = new InvalidOperationException("Engine rejected activation.");
            game.BeforeCall = call => { throw failure; };
            Assert.AreSame(failure, Assert.ThrowsExactly<InvalidOperationException>(() =>
                game.Services.Gameplay.ActivatePhysics(400)));
            Assert.HasCount(1, game.Native.Calls);
            LastCall(game, Hash.ACTIVATE_PHYSICS, 400);
        }

        [TestMethod]
        public void PhysicsMappingIncludesAllDampingAndTrailingBoundParameters()
        {
            var game = new FakeGame();
            game.Services.Gameplay.SetObjectPhysics(400, 250, 1.5f,
                new Vector3(2, 3, 4), new Vector3(5, 6, 7), 0.025f, 8.5f, 0.75f);
            LastCall(game, Hash.SET_OBJECT_PHYSICS_PARAMS,
                400, 250f, 1.5f, 2f, 3f, 4f, 5f, 6f, 7f, 0.025f, 8.5f, 0.75f);
            Assert.HasCount(1, game.Native.Calls);
        }
    }
}
