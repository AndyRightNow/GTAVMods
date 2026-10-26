using Control = GTA.Control;
using GTA.Math;
using GTA.Native;
using GTA;
using GTAVMods.Game;
using GTAVMods.Tests.Support;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using System;
using Thor;

namespace GTAVMods.Tests.PlayerIntegration.PowerEnchantments
{
    [TestClass, TestCategory("PlayerIntegration")]
    public class MeleeAerialAttackTests
    {
        [TestMethod]
        public void MeleeStrikeDamagesOnlyTheStruckPedAndPushesTheStruckVehicle()
        {
            var profile = new GameplayTuning();
            profile.Combat.MeleeForce = 173;
            profile.Combat.MeleePedDamage = 61;
            profile.Combat.MeleeRagdollMinMs = 43;
            profile.Combat.MeleeRagdollMaxMs = 157;
            var s = new PlayerSessionFixture(tuning: profile);
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
            CollectionAssert.AreEqual(new object[] { 60, profile.Combat.MeleePedDamage },
                s.Game.Calls.Last(call => call.Name == "ped.damage").Arguments);
            CollectionAssert.AreEqual(new object[] { 61, Vector3.UnitY * profile.Combat.MeleeForce },
                s.Game.Calls.Last(call => call.Name == "entity.force").Arguments);
            Assert.IsTrue(s.Game.Entity(60).CanRagdoll);
            CollectionAssert.AreEqual(new object[] { 60, profile.Combat.MeleeRagdollMinMs,
                profile.Combat.MeleeRagdollMaxMs, (int)ADModUtils.RagdollType.Normal, 0, 0, 0 },
                s.LastNative(Hash.SET_PED_TO_RAGDOLL));
            Assert.HasCount(1, s.Game.Calls.Where(call => call.Name == "ped.damage").ToArray());
            Assert.IsFalse(s.Game.Calls.Any(call => call.Name == "entity.force" &&
                new[] { 10, 20, 62 }.Contains((int)call.Arguments[0])));
            Assert.AreEqual(200, s.Player.Health, "Melee forces must not damage or automatically heal the player.");
            s.Controller.Abort();
        }

        [TestMethod]
        public void CharacterSwitchDuringMeleeObservationProtectsTheNewPlayerFromPedDamage()
        {
            var profile = new GameplayTuning();
            profile.Combat.MeleePedDamage = 61;
            var s = new PlayerSessionFixture(tuning: profile);
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
            CollectionAssert.AreEqual(new object[] { 60, profile.Combat.MeleePedDamage },
                s.Game.Calls.Single(call => call.Name == "ped.damage").Arguments);
            Assert.AreEqual(250, s.Game.Entity(11).Health);
            Assert.AreEqual(200, s.Game.Entity(10).Health);
            s.Controller.Abort();
        }

        [TestMethod]
        public void AirDashSlamsDownThenDamagesNearbyEntitiesOnLanding()
        {
            var profile = new GameplayTuning();
            profile.Combat.MeleeForce = 173;
            profile.Combat.MeleePedDamage = 61;
            profile.Ability.LandingSpeed = 137;
            profile.Ability.StoredPowerFraction = 0.4f;
            var f = new PlayerSessionFixture(tuning: profile); f.Tick();
            f.Player.Position = new Vector3(17, -23, 11);
            f.Player.InAir = true; f.Game.Input.Pressed.Add(Control.Attack);
            f.Tick();
            Assert.AreEqual(-Vector3.UnitZ * profile.Ability.LandingSpeed, f.Player.Velocity);
            f.Tick();
            Assert.AreEqual(-Vector3.UnitZ * profile.Ability.LandingSpeed, f.Player.Velocity);
            Assert.IsFalse(f.Game.Calls.Any(call => call.Name == "ped.damage"));
            f.Game.AddEntity(60).Position = f.Player.Position + Vector3.UnitX; f.Peds.Add(60);
            f.Game.AddEntity(61).Position = f.Player.Position + Vector3.UnitY;
            f.Game.World.Nearby = (position, radius) => new[] { 0, 10, 20, 60, 61 };
            f.Player.InAir = false; f.Game.Input.Pressed.Clear();
            f.Tick();
            Assert.AreEqual(profile.Combat.MeleePedDamage, f.Game.Calls.Single(call => call.Name == "ped.damage").Arguments[1]);
            Assert.HasCount(2, f.Game.Calls.Where(call => call.Name == "entity.force").ToArray());
            CollectionAssert.AreEqual(new object[] { 60, Vector3.UnitX * profile.Combat.MeleeForce },
                f.Game.Calls.Single(call => call.Name == "entity.force" && (int)call.Arguments[0] == 60).Arguments);
            CollectionAssert.AreEqual(new object[] { 61, Vector3.UnitY * profile.Combat.MeleeForce },
                f.Game.Calls.Single(call => call.Name == "entity.force" && (int)call.Arguments[0] == 61).Arguments);
            CollectionAssert.AreEquivalent(new[] { 60, 61 }, f.Game.Native.Calls
                .Where(call => call.Hash == Hash.START_PARTICLE_FX_NON_LOOPED_ON_ENTITY)
                .Select(call => (int)call.Arguments[1]).ToArray());
            StringAssert.Contains(f.State(), "airDash=False");
            f.Game.Input.Pressed.Add(Control.Attack);
            f.Tick();
            Assert.HasCount(1, f.Game.Calls.Where(call => call.Name == "ped.damage").ToArray(),
                "Grounded attack input does not start another aerial landing strike.");
        }

        [TestMethod]
        public void MeleeDamageRequiresAHitAndIsSuppressedInsideVehicle()
        {
            var profile = new GameplayTuning();
            profile.Combat.MeleeForce = 173;
            profile.Combat.MeleePedDamage = 61;
            profile.Ability.LandingSpeed = 137;
            profile.Ability.StoredPowerFraction = 0.4f;
            var f = new PlayerSessionFixture(tuning: profile);
            f.Game.AddEntity(60).DamagedBy.Add(10); f.Peds.Add(60);
            f.Game.AddEntity(61); f.Peds.Add(61);
            f.Game.World.Nearby = (position, radius) => new[] { 0, 10, 20, 60, 61 };
            f.Player.InMeleeCombat = true; f.Player.InVehicle = true;
            f.Tick();
            Assert.IsFalse(f.Game.Calls.Any(call => call.Name == "entity.force"));
            Assert.IsFalse(f.Game.Calls.Any(call => call.Name == "ped.damage"),
                "Being in a vehicle must suppress melee damage as well as knockback.");
            f.Player.InVehicle = false;
            f.Tick();
            var force = f.Game.Calls.Single(call => call.Name == "entity.force");
            CollectionAssert.AreEqual(new object[] { 60, Vector3.UnitY * profile.Combat.MeleeForce }, force.Arguments);
            CollectionAssert.AreEqual(new object[] { 60, profile.Combat.MeleePedDamage },
                f.Game.Calls.Single(call => call.Name == "ped.damage").Arguments,
                "Only the pedestrian reported hit receives full-power melee damage.");
        }

        [TestMethod]
        public void ReducedPowerMeleeScalesConfiguredForceAndDamageAndOtherHeldWeaponSkipsIt()
        {
            var profile = new GameplayTuning();
            profile.Combat.MeleeForce = 173;
            profile.Combat.MeleePedDamage = 61;
            profile.Ability.LandingSpeed = 137;
            profile.Ability.StoredPowerFraction = 0.4f;
            var f = new PlayerSessionFixture(tuning: profile);
            f.Player.CurrentWeapon = WeaponHash.Pistol;
            f.Player.InMeleeCombat = true;
            f.Game.AddEntity(60).DamagedBy.Add(10); f.Peds.Add(60);
            f.Game.World.Nearby = (position, radius) => new[] { 60 };
            f.Tick();
            Assert.IsFalse(f.Game.Calls.Any(call => call.Name == "entity.force"));
            f.Player.CurrentWeaponObject = 0;
            f.Tick();
            var force = f.Game.Calls.Single(call => call.Name == "entity.force");
            Assert.IsTrue((Vector3.UnitY * (profile.Combat.MeleeForce * profile.Ability.StoredPowerFraction) - (Vector3)force.Arguments[1]).Length() < .001f);
            Assert.AreEqual((int)(profile.Combat.MeleePedDamage * profile.Ability.StoredPowerFraction), f.Game.Calls.Single(call => call.Name == "ped.damage").Arguments[1]);
            Assert.IsFalse(f.Game.Native.Calls.Any(call => call.Hash == Hash.START_PARTICLE_FX_NON_LOOPED_ON_ENTITY));
        }
    }
}
