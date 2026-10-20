using System;
using System.Linq;
using System.Windows.Forms;
using GTA;
using GTA.Math;
using GTA.Native;
using GTAVMods.Game;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ModUtilsTest
{
    [TestClass, TestCategory("PlayerIntegration")]
    public class PlayerEngineFailureTests
    {
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
        public void MeleeStrikeDamagesOnlyTheStruckPedAndPushesTheStruckVehicle()
        {
            var s = new PlayerSessionFixture();
            s.Frame(0);
            s.Player.InMeleeCombat = true;
            s.Player.Forward = Vector3.UnitY;
            s.Game.AddEntity(60).DamagedBy.Add(10);
            s.Game.Entity(60).CanRagdoll = false;
            s.Game.AddEntity(61).DamagedBy.Add(10);
            s.Vehicles.Add(61);
            s.Game.AddEntity(62);
            s.Game.World.Nearby = (position, radius) => new[] { 10, 20, 60, 61, 62 };
            s.Frame(0);
            CollectionAssert.AreEqual(new object[] { 60, 100 },
                s.Game.Calls.Last(call => call.Name == "ped.damage").Arguments);
            CollectionAssert.AreEqual(new object[] { 61, new Vector3(0, 250, 0) },
                s.Game.Calls.Last(call => call.Name == "entity.force").Arguments);
            Assert.IsTrue(s.Game.Entity(60).CanRagdoll);
            Assert.HasCount(1, s.Game.Calls.Where(call => call.Name == "ped.damage").ToArray());
            Assert.IsFalse(s.Game.Calls.Any(call => call.Name == "entity.force" &&
                new[] { 10, 20, 62 }.Contains((int)call.Arguments[0])));
            Assert.AreEqual(200, s.Player.Health, "Melee forces must not damage or automatically heal the player.");
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
        public void CharacterSwitchDuringMeleeObservationProtectsTheNewPlayerFromPedDamage()
        {
            var s = new PlayerSessionFixture();
            s.Frame(0);
            s.Game.Entity(10).InMeleeCombat = true;
            s.Game.Entity(11).DamagedBy.Add(10);
            s.Game.AddEntity(60).DamagedBy.Add(10);
            s.Game.World.Nearby = (position, radius) =>
            {
                // The world reports the new player after the tick captured the old
                // character. The damage boundary must honor that newer observation.
                s.Game.Host.PlayerPed = 11;
                return new[] { 11, 60 };
            };
            s.Frame(0);
            CollectionAssert.AreEqual(new object[] { 60, 100 },
                s.Game.Calls.Single(call => call.Name == "ped.damage").Arguments);
            Assert.AreEqual(250, s.Game.Entity(11).Health);
            Assert.AreEqual(200, s.Game.Entity(10).Health);
            s.Controller.Abort();
        }

        [TestMethod]
        public void MissingHammerAssetPausesWithoutCreatingAnObjectAndCanRecoverWhenStreamingSucceeds()
        {
            var s = new PlayerSessionFixture(configure: game => game.Weapons.AssetResults[WeaponHash.Hammer] = false);
            s.Frame(0, expectFailure: true);
            Assert.IsFalse(s.Game.Native.Calls.Any(call => call.Hash == Hash.CREATE_WEAPON_OBJECT));
            CollectionAssert.AreEqual(new object[] { WeaponHash.Hammer, 3000 },
                s.Game.Calls.Single(call => call.Name == "weapon.request").Arguments);
            StringAssert.Contains((string)s.Log.Last("tick.exception").Data["exception"], "Could not load weapon asset Hammer");
            Assert.IsTrue(s.Game.Calls.Any(call => call.Name == "host.notify" &&
                ((string)call.Arguments[0]).Contains("paused")));
            int errors = s.Log.Records.Count(record => record.Level == "ERROR");
            s.Game.Weapons.AssetResults[WeaponHash.Hammer] = true;
            s.Game.Entities.CreatedBlips.Enqueue(2001);
            s.Chord(Keys.O);
            s.Frame(0, expectFailure: true);
            Assert.AreEqual(errors, s.Log.Records.Count(record => record.Level == "ERROR"));
            Assert.IsTrue(s.CreatedWeapons.Any(handle => s.Game.Entity(handle).Exists));
            StringAssert.Contains(s.State(), "attached=True");
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
    }
}
