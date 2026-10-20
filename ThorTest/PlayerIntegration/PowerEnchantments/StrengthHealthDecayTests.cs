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

namespace GTAVMods.Tests.PlayerIntegration.PowerEnchantments
{
    [TestClass, TestCategory("PlayerIntegration")]
    public class StrengthHealthDecayTests
    {
        [TestMethod]
        [DataRow(1000)]
        [DataRow(int.MaxValue - 500)]
        [DataRow(-1)]
        public void DroppedHammerPowerAndHealingExpireAfterTenSecondsAcrossClockBoundaries(int lossTime)
        {
            var s = new PlayerSessionFixture();
            s.Game.Host.GameTime = unchecked(lossTime - 201);
            s.Frame(0);
            s.Game.Entity(10).Health = 10;
            s.Frame(200);
            Assert.AreEqual(3010, s.Game.Entity(10).Health, "Full power heals ten percent of the enhanced maximum every 200 ms.");
            s.Frame(1, Keys.Y);
            Assert.AreEqual(WeaponHash.Unarmed, s.Game.Entity(10).CurrentWeapon);
            Assert.AreEqual(Vector3.Zero, s.Game.Entity(20).Velocity);
            s.Frame(0);
            foreach (var sample in new[] { (elapsed: 5000, maxHealth: 15100, movement: 1.25f), (elapsed: 10000, maxHealth: 100, movement: 1f), (elapsed: 10001, maxHealth: 100, movement: 1f) })
            {
                s.Game.Host.GameTime = unchecked(lossTime + sample.elapsed);
                s.Frame(0);
                Assert.AreEqual(sample.maxHealth, s.Game.Entity(10).MaxHealth, "elapsed " + sample.elapsed);
                Assert.AreEqual(sample.movement, s.Game.Native.Calls.Last(call => call.Hash == Hash.SET_PED_MOVE_RATE_OVERRIDE).Arguments[1]);
            }
            int health = s.Game.Entity(10).Health;
            int jumps = s.Game.Native.Calls.Count(call => call.Hash == Hash.SET_SUPER_JUMP_THIS_FRAME);
            s.Frame(200);
            Assert.AreEqual(health, s.Game.Entity(10).Health, "Expired power must stop healing.");
            Assert.HasCount(jumps, s.Game.Native.Calls.Where(call => call.Hash == Hash.SET_SUPER_JUMP_THIS_FRAME).ToArray(),
                "Expired power must not grant a super jump.");
            s.Controller.Abort();
            s.AssertNoErrors();
            s.AssertReleased();
        }

        [TestMethod]
        [DataRow(1000)]
        [DataRow(int.MaxValue - 500)]
        public void LosingAStoredHammerFadesFromHalfPowerWithoutIncreasingOrKeepingItForever(int lossTime)
        {
            var s = new PlayerSessionFixture(holding: false);
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
        public void StoredHammerGrantsHalfPowerAndRecallSelectsItWithoutSpawningLooseHammer()
        {
            var f = new PlayerSessionFixture();
            f.Player.Invincible = f.Player.BulletProof = f.Player.CollisionProof = f.Player.ExplosionProof =
                f.Player.FireProof = f.Player.MeleeProof = f.Player.CanSufferCriticalHits = true;
            f.Player.CurrentWeapon = WeaponHash.Pistol;
            f.Player.CurrentWeaponObject = 0;
            f.Game.Host.GameTime = 1;
            f.Tick();
            Assert.AreEqual(15100, f.Player.MaxHealth);
            Assert.AreEqual(1.25f, f.LastNative(Hash.SET_PED_MOVE_RATE_OVERRIDE)[1]);
            CollectionAssert.AreEqual(new object[] { f.Game.Host.PlayerHandle, 1.25f }, f.LastNative(Hash.SET_SWIM_MULTIPLIER_FOR_PLAYER));
            Assert.IsFalse(f.Player.BulletProof);
            Assert.IsFalse(f.Player.Invincible || f.Player.CollisionProof || f.Player.ExplosionProof ||
                f.Player.FireProof || f.Player.MeleeProof || f.Player.CanSufferCriticalHits || f.Player.DiesOnLowHealth,
                "Half power permits ordinary damage while preventing critical-hit and low-health instant deaths.");
            CollectionAssert.AreEqual(new object[] { 10, true }, f.LastNative(Hash.SET_ENTITY_CAN_BE_DAMAGED));
            Assert.HasCount(1, f.Game.Native.Calls.Where(call => call.Hash == Hash.SET_SUPER_JUMP_THIS_FRAME).ToArray());
            Assert.AreEqual(0, f.CreatedWeapons.Count(handle => f.Game.Observations[handle].Opacity == 255));
            f.Game.Input.Keys.Add(Keys.H);
            f.Tick();
            Assert.AreEqual(WeaponHash.Hammer, f.Player.CurrentWeapon);
        }

        [TestMethod]
        public void RopeWhirlMaintainsFullPowerUntilReleasedAndSuppressesLooseHammerTrails()
        {
            var f = new PlayerSessionFixture();
            f.Game.Input.Keys.Add(Keys.Z);
            f.Game.Entities.CreatedBlips.Enqueue(50);
            f.WeaponCreations.Enqueue(21); f.WeaponCreations.Enqueue(22); f.WeaponCreations.Enqueue(30);
            // Hover helpers must be created before the hand anchor.
            f.Game.Ropes.Creations.Enqueue(70);
            f.Tick();
            Assert.IsFalse((f.Player.CurrentWeapon == WeaponHash.Hammer));
            StringAssert.Contains(f.State(), "rope=True");
            f.Game.Host.GameTime = 1; f.Tick();
            f.Game.Host.GameTime = 1001;
            f.Tick();
            Assert.IsTrue((f.Game.Entity(20).Velocity.Length() > 0));
            Assert.AreEqual(30000, f.Player.MaxHealth);
            Assert.AreEqual(1, f.Audio.Clips["whirling-hammer.wav"].Plays);
            Assert.AreEqual("dodge_generic_centre", f.LastNative(Hash.TASK_PLAY_ANIM)[2]);
            Assert.AreEqual((int)(AnimationFlags.UpperBodyOnly | AnimationFlags.Secondary), f.LastNative(Hash.TASK_PLAY_ANIM)[6]);
            var particlesBefore = f.Game.Native.Calls.Count(call => call.Hash == Hash.START_PARTICLE_FX_NON_LOOPED_AT_COORD);
            f.Game.Host.GameTime = 12000; f.Tick();
            Assert.AreEqual(particlesBefore, f.Game.Native.Calls.Count(call => call.Hash == Hash.START_PARTICLE_FX_NON_LOOPED_AT_COORD));
            Assert.AreEqual(30000, f.Player.MaxHealth, "A rope remains a full-power source beyond the ordinary loss grace period.");
            f.Game.Input.Keys.Clear();
            f.Tick();
            Assert.IsEmpty(f.Game.Ropes.Ropes);
            Assert.IsFalse(f.Game.Observations[30].Exists);
            StringAssert.Contains(f.State(), "rope=False");
            f.Game.Host.GameTime = 12001; f.Tick();
            Assert.AreEqual(30000, f.Player.MaxHealth, "The loss clock starts when the last active power source is released.");
            f.Game.Host.GameTime = 17001; f.Tick();
            Assert.AreEqual(15100, f.Player.MaxHealth);
        }
    }
}
