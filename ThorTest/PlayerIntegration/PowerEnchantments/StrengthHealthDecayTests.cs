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
        public void DroppedHammerPowerAndHealingExpireAfterTheConfiguredGracePeriodAcrossClockBoundaries(int lossTime)
        {
            var profile = new GameplayTuning();
            profile.Ability.PowerFadeMs = 1200;
            profile.Ability.FullPowerMaxHealth = 8000;
            profile.Ability.HealthRecoveryIntervalMs = 37;
            profile.Ability.HealthRecoveryFraction = 0.2f;
            profile.Ability.MovementMultiplier = 1.8f;
            var s = new PlayerSessionFixture(tuning: profile);
            s.Game.Host.GameTime = unchecked(lossTime - profile.Ability.HealthRecoveryIntervalMs - 1);
            s.Frame(0);
            s.Game.Entity(10).Health = 10;
            s.Frame(profile.Ability.HealthRecoveryIntervalMs);
            Assert.AreEqual(10 + (int)(profile.Ability.FullPowerMaxHealth * profile.Ability.HealthRecoveryFraction), s.Player.Health);
            s.Frame(1, Keys.Y);
            Assert.AreEqual(WeaponHash.Unarmed, s.Game.Entity(10).CurrentWeapon);
            Assert.AreEqual(Vector3.Zero, s.Game.Entity(20).Velocity);
            s.Frame(0);
            int fullHealth = s.Player.MaxHealth;
            s.Game.Host.GameTime = unchecked(lossTime + (int)profile.Ability.PowerFadeMs / 2);
            s.Frame(0);
            int fadingHealth = s.Player.MaxHealth;
            float fadingMovement = (float)s.LastNative(Hash.SET_PED_MOVE_RATE_OVERRIDE)[1];
            Assert.IsTrue(fadingHealth < fullHealth && fadingHealth > profile.Ability.UnpoweredMaxHealth);
            Assert.IsTrue(fadingMovement > 1 && fadingMovement < profile.Ability.MovementMultiplier);
            s.Game.Host.GameTime = unchecked(lossTime + (int)profile.Ability.PowerFadeMs);
            s.Frame(0);
            Assert.AreEqual(profile.Ability.UnpoweredMaxHealth, s.Player.MaxHealth);
            Assert.AreEqual(1f, s.LastNative(Hash.SET_PED_MOVE_RATE_OVERRIDE)[1]);
            s.Frame(1);
            Assert.AreEqual(profile.Ability.UnpoweredMaxHealth, s.Player.MaxHealth);
            int health = s.Game.Entity(10).Health;
            int jumps = s.Game.Native.Calls.Count(call => call.Hash == Hash.SET_SUPER_JUMP_THIS_FRAME);
            s.Frame(profile.Ability.HealthRecoveryIntervalMs);
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
        public void LosingAStoredHammerFadesFromItsReducedPowerWithoutIncreasingOrKeepingItForever(int lossTime)
        {
            var profile = new GameplayTuning();
            profile.Ability.PowerFadeMs = 1300;
            profile.Ability.FullPowerMaxHealth = 7000;
            profile.Ability.StoredPowerFraction = 0.3f;
            var s = new PlayerSessionFixture(holding: false, tuning: profile);
            s.Game.Entity(10).Weapons.Add(WeaponHash.Hammer);
            s.Game.Host.GameTime = unchecked(lossTime - 1);
            s.Frame(0);
            int storedHealth = s.Player.MaxHealth;
            Assert.AreEqual(profile.Ability.UnpoweredMaxHealth +
                (int)(profile.Ability.FullPowerMaxHealth * profile.Ability.StoredPowerFraction), storedHealth);
            Assert.IsTrue(storedHealth > profile.Ability.UnpoweredMaxHealth && storedHealth < profile.Ability.FullPowerMaxHealth);
            Assert.AreEqual(WeaponHash.Unarmed, s.Game.Entity(10).CurrentWeapon);
            Assert.HasCount(2, s.CreatedWeapons, "Inventory ownership needs only the two hover helpers.");

            s.Game.Entity(10).Weapons.Remove(WeaponHash.Hammer);
            s.Game.Host.GameTime = lossTime;
            s.Frame(0);
            Assert.AreEqual(storedHealth, s.Game.Entity(10).MaxHealth, "Losing a stored hammer must not grant a full-power boost.");
            Assert.HasCount(3, s.CreatedWeapons, "The missing inventory is replaced by one owned loose hammer.");
            s.Game.Host.GameTime = unchecked(lossTime + (int)profile.Ability.PowerFadeMs / 2);
            s.Frame(0);
            Assert.IsTrue(s.Player.MaxHealth < storedHealth && s.Player.MaxHealth > profile.Ability.UnpoweredMaxHealth);
            s.Game.Host.GameTime = unchecked(lossTime + (int)profile.Ability.PowerFadeMs);
            s.Frame(0);
            Assert.AreEqual(profile.Ability.UnpoweredMaxHealth, s.Player.MaxHealth);
            s.Frame(1);
            Assert.AreEqual(profile.Ability.UnpoweredMaxHealth, s.Player.MaxHealth);
            s.Controller.Abort();
            s.AssertNoErrors();
            s.AssertReleased();
        }

        [TestMethod]
        public void StoredHammerGrantsConfiguredReducedPowerAndRecallSelectsItWithoutSpawningLooseHammer()
        {
            var profile = new GameplayTuning();
            profile.Ability.FullPowerMaxHealth = 9000;
            profile.Ability.StoredPowerFraction = 0.4f;
            profile.Ability.MovementMultiplier = 1.75f;
            profile.Ability.PowerFadeMs = 1400;
            var f = new PlayerSessionFixture(tuning: profile);
            f.Player.Invincible = f.Player.BulletProof = f.Player.CollisionProof = f.Player.ExplosionProof =
                f.Player.FireProof = f.Player.MeleeProof = f.Player.CanSufferCriticalHits = true;
            f.Player.CurrentWeapon = WeaponHash.Pistol;
            f.Player.CurrentWeaponObject = 0;
            f.Game.Host.GameTime = 1;
            f.Tick();
            Assert.AreEqual(profile.Ability.UnpoweredMaxHealth +
                (int)(profile.Ability.FullPowerMaxHealth * profile.Ability.StoredPowerFraction), f.Player.MaxHealth);
            float storedMovement = (float)f.LastNative(Hash.SET_PED_MOVE_RATE_OVERRIDE)[1];
            Assert.AreEqual(1 + (profile.Ability.MovementMultiplier - 1) * profile.Ability.StoredPowerFraction,
                storedMovement, .0001f);
            CollectionAssert.AreEqual(new object[] { f.Game.Host.PlayerHandle, storedMovement }, f.LastNative(Hash.SET_SWIM_MULTIPLIER_FOR_PLAYER));
            Assert.IsFalse(f.Player.BulletProof);
            Assert.IsFalse(f.Player.Invincible || f.Player.CollisionProof || f.Player.ExplosionProof ||
                f.Player.FireProof || f.Player.MeleeProof || f.Player.CanSufferCriticalHits || f.Player.DiesOnLowHealth,
                "Reduced power permits ordinary damage while preventing critical-hit and low-health instant deaths.");
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
            var profile = new GameplayTuning();
            profile.Ability.FullPowerMaxHealth = 9000;
            profile.Ability.StoredPowerFraction = 0.4f;
            profile.Ability.MovementMultiplier = 1.75f;
            profile.Ability.PowerFadeMs = 1400;
            var f = new PlayerSessionFixture(tuning: profile);
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
            Assert.AreEqual(profile.Ability.FullPowerMaxHealth, f.Player.MaxHealth);
            Assert.AreEqual(1, f.Audio.Clips["whirling-hammer.wav"].Plays);
            Assert.AreEqual((int)(AnimationFlags.UpperBodyOnly | AnimationFlags.Secondary), f.LastNative(Hash.TASK_PLAY_ANIM)[6]);
            var particlesBefore = f.Game.Native.Calls.Count(call => call.Hash == Hash.START_PARTICLE_FX_NON_LOOPED_AT_COORD);
            f.Game.Host.GameTime = (int)profile.Ability.PowerFadeMs + 2000; f.Tick();
            Assert.AreEqual(particlesBefore, f.Game.Native.Calls.Count(call => call.Hash == Hash.START_PARTICLE_FX_NON_LOOPED_AT_COORD));
            Assert.AreEqual(profile.Ability.FullPowerMaxHealth, f.Player.MaxHealth, "A rope remains a full-power source beyond the ordinary loss grace period.");
            f.Game.Input.Keys.Clear();
            f.Tick();
            Assert.IsEmpty(f.Game.Ropes.Ropes);
            Assert.IsFalse(f.Game.Observations[30].Exists);
            StringAssert.Contains(f.State(), "rope=False");
            f.Game.Host.GameTime++; f.Tick();
            Assert.AreEqual(profile.Ability.FullPowerMaxHealth, f.Player.MaxHealth, "The loss clock starts when the last active power source is released.");
            f.Game.Host.GameTime += (int)(profile.Ability.PowerFadeMs * 0.75f); f.Tick();
            Assert.IsTrue(f.Player.MaxHealth > profile.Ability.UnpoweredMaxHealth && f.Player.MaxHealth < profile.Ability.FullPowerMaxHealth);
        }
    }
}
