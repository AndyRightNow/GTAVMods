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
    public class SummonRecallCatchTests
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

        [TestMethod]
        public void AnAnimationAlreadyPlayingDoesNotRestartDuringHeldRecall()
        {
            var s = new PlayerSessionFixture();
            s.DropHammer(new Vector3(0, 20, 0));
            s.Game.Native.Returns(Hash.IS_ENTITY_PLAYING_ANIM, true);
            s.Game.Native.Calls.Clear();
            s.Recall();
            s.Recall();
            Assert.IsFalse(s.Game.Native.Calls.Any(call => call.Hash == Hash.TASK_PLAY_ANIM));
            Assert.IsFalse(s.Game.Native.Calls.Any(call => call.Hash == Hash.REQUEST_ANIM_DICT));
            Assert.IsTrue(s.Game.Entity(20).Velocity.Length() > 0);
            Assert.AreEqual(WeaponHash.Unarmed, s.Player.CurrentWeapon);
            s.Controller.Abort();
        }

        [TestMethod]
        public void UnavailableParticleAssetsLeaveRecallUsableWithoutSubmittingUnloadedEffects()
        {
            var s = new PlayerSessionFixture();
            s.DropHammer(new Vector3(0, 20, 0));
            s.Game.Native.Returns(Hash.HAS_NAMED_PTFX_ASSET_LOADED, false);
            s.Game.Native.Calls.Clear();
            s.Frame(1000, Keys.H);
            Assert.IsTrue(s.Game.Entity(20).Velocity.Length() > 0);
            Assert.IsTrue(s.Game.Native.Calls.Any(call => call.Hash == Hash.REQUEST_NAMED_PTFX_ASSET));
            Assert.IsFalse(s.Game.Native.Calls.Any(call => call.Hash == Hash.START_PARTICLE_FX_NON_LOOPED_ON_ENTITY ||
                call.Hash == Hash.START_PARTICLE_FX_NON_LOOPED_AT_COORD || call.Hash == Hash.START_PARTICLE_FX_NON_LOOPED_ON_PED_BONE));
            s.Controller.Abort();
        }

        [TestMethod]
        public void CatchStillTransfersTheHammerWhenItsHandLightningAssetIsUnavailable()
        {
            var s = new PlayerSessionFixture();
            s.DropHammer(Vector3.UnitZ);
            s.Game.Native.Returns(Hash.HAS_NAMED_PTFX_ASSET_LOADED, false);
            s.Game.Native.Calls.Clear();
            s.Game.World.Capsules.Enqueue(new RayHit(true, 10, Vector3.UnitZ));
            s.Game.Host.OnWait = duration => s.ObserveHeldHammer();
            s.Frame(0, Keys.H);
            Assert.AreEqual(WeaponHash.Hammer, s.Player.CurrentWeapon);
            Assert.IsTrue(s.Log.Records.Any(record => record.Name == "hammer.caught"));
            Assert.IsTrue(s.Game.Native.Calls.Any(call => call.Hash == Hash.REQUEST_NAMED_PTFX_ASSET));
            Assert.IsFalse(s.Game.Native.Calls.Any(call => call.Hash == Hash.START_PARTICLE_FX_NON_LOOPED_ON_PED_BONE));
            Assert.AreEqual(1, s.Audio.Clips["catch-hammer.wav"].Plays);
            s.Controller.Abort();
        }

        [TestMethod]
        public void HoldingRecallOnTheFirstUnarmedFrameCreatesAHammerBeforeSteeringIt()
        {
            var s = new PlayerSessionFixture(holding: false);
            s.Frame(0, Keys.H);
            CollectionAssert.AreEqual(new[] { 21, 22, 23 }, s.CreatedWeapons);
            Assert.AreEqual(new Vector3(0, 0, 2000), s.Game.Entity(23).Position);
            Assert.AreEqual(Vector3.Zero, s.Game.Entity(23).Velocity);
            Assert.IsFalse(s.Game.Calls.Any(call => call.Name == "entity.velocity" &&
                (int)call.Arguments[0] == 0));

            s.Game.Entity(23).Position = new Vector3(0, -30, 1);
            s.Frame(0, Keys.H);
            Assert.AreEqual(Vector3.UnitY * 100, s.Game.Entity(23).Velocity);
            Assert.HasCount(3, s.CreatedWeapons);
            s.Reload();
            s.AssertReleased();
        }

        [TestMethod]
        public void AStoredCatchWaitsForWeaponSelectionWithoutRequestingAnotherTransfer()
        {
            var s = new PlayerSessionFixture();
            s.DropHammer(Vector3.UnitZ);
            s.Game.World.Capsules.Enqueue(new RayHit(true, 10, Vector3.UnitZ));
            s.Frame(0, Keys.H);
            Assert.HasCount(1, s.Game.Native.Calls.Where(call => call.Hash == Hash.GIVE_WEAPON_OBJECT_TO_PED).ToArray());
            StringAssert.Contains(s.State(), "return=True");

            s.Game.ApplyWeaponObservation(10, WeaponHash.Pistol, 0);
            s.Player.Weapons.Add(WeaponHash.Hammer);
            s.Game.Calls.Clear();
            s.Frame(0);
            Assert.AreEqual(WeaponHash.Pistol, s.Player.CurrentWeapon);
            Assert.IsFalse(s.Game.Calls.Any(call => call.Name == "entity.velocity" &&
                (int)call.Arguments[0] == 20));
            Assert.HasCount(1, s.Game.Native.Calls.Where(call => call.Hash == Hash.GIVE_WEAPON_OBJECT_TO_PED).ToArray());
            StringAssert.Contains(s.State(), "return=True");

            s.Frame(0, Keys.H);
            Assert.IsTrue(s.Game.Calls.Any(call => call.Name == "weapon.select" &&
                (WeaponHash)call.Arguments[1] == WeaponHash.Hammer));
            s.ObserveHeldHammer();
            s.Frame(0);
            StringAssert.Contains(s.State(), "return=False");
            Assert.AreEqual(WeaponHash.Hammer, s.Player.CurrentWeapon);
        }

        [TestMethod]
        [DataRow(3f, 35f, false, true)]
        [DataRow(3.01f, 40f, false, true)]
        [DataRow(5f, 40f, false, true)]
        [DataRow(10f, 40f, true, false)]
        [DataRow(10.01f, 100f, true, false)]
        [DataRow(140f, 100f, true, false)]
        [DataRow(140.01f, 100f, false, false)]
        public void HoldingRecallSteersTowardTheHandAndUsesTheCorrectApproachBand(float distance, float speed, bool audible, bool upright)
        {
            var s = new PlayerSessionFixture();
            var hand = new Vector3(17, -23, 11);
            s.Player.Bones[Bone.PHRightHand] = hand;
            s.DropHammer(hand - Vector3.UnitY * distance);
            s.Game.Entity(20).InAir = true;
            s.Game.AddEntity(60);
            s.Game.World.Capsules.Enqueue(new RayHit(true, 60, hand));
            s.Frame(0, Keys.H);
            Assert.AreEqual(Vector3.UnitY * speed, s.Game.Entity(20).Velocity);
            Assert.IsFalse(s.Game.Native.Calls.Any(call => call.Hash == Hash.GIVE_WEAPON_OBJECT_TO_PED),
                "Contact with another pedestrian does not complete the player's catch.");
            Assert.AreEqual(audible, s.Audio.Clips.ContainsKey("hammer-close-to-player.wav"));
            Assert.AreEqual("aim_trans_low", s.LastNative(Hash.TASK_PLAY_ANIM)[2]);
            for (int frame = 0; frame < 30; frame++) s.Frame(0, Keys.H);
            Assert.IsTrue((s.Game.Entity(20).Quaternion * Vector3.UnitZ -
                (upright ? Vector3.UnitZ : Vector3.UnitY)).Length() < 0.001f);
            if (audible) Assert.AreEqual(1, s.Audio.Clips["hammer-close-to-player.wav"].Plays);
            s.Reload();
            s.AssertReleased();
        }
    }
}
