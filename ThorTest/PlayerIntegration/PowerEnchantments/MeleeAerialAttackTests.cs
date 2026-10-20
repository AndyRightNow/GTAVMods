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

namespace GTAVMods.Tests.PlayerIntegration.PowerEnchantments
{
    [TestClass, TestCategory("PlayerIntegration")]
    public class MeleeAerialAttackTests
    {
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
        public void AirDashSlamsDownThenDamagesNearbyEntitiesOnLanding()
        {
            var f = new PlayerSessionFixture(); f.Tick();
            f.Player.Position = new Vector3(17, -23, 11);
            f.Player.InAir = true; f.Game.Input.Pressed.Add(Control.Attack);
            f.Tick();
            Assert.AreEqual(new Vector3(0, 0, -200), f.Player.Velocity);
            f.Tick();
            Assert.AreEqual(new Vector3(0, 0, -200), f.Player.Velocity);
            Assert.IsFalse(f.Game.Calls.Any(call => call.Name == "ped.damage"));
            f.Game.AddEntity(60).Position = f.Player.Position + Vector3.UnitX; f.Peds.Add(60);
            f.Game.AddEntity(61).Position = f.Player.Position + Vector3.UnitY;
            f.Game.World.Nearby = (position, radius) => new[] { 0, 10, 20, 60, 61 };
            f.Player.InAir = false; f.Game.Input.Pressed.Clear();
            f.Tick();
            Assert.AreEqual(100, f.Game.Calls.Single(call => call.Name == "ped.damage").Arguments[1]);
            Assert.HasCount(2, f.Game.Calls.Where(call => call.Name == "entity.force").ToArray());
            CollectionAssert.AreEqual(new object[] { 60, Vector3.UnitX * 250 },
                f.Game.Calls.Single(call => call.Name == "entity.force" && (int)call.Arguments[0] == 60).Arguments);
            CollectionAssert.AreEqual(new object[] { 61, Vector3.UnitY * 250 },
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
            var f = new PlayerSessionFixture();
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
            CollectionAssert.AreEqual(new object[] { 60, Vector3.UnitY * 250 }, force.Arguments);
            CollectionAssert.AreEqual(new object[] { 60, 100 },
                f.Game.Calls.Single(call => call.Name == "ped.damage").Arguments,
                "Only the pedestrian reported hit receives full-power melee damage.");
        }

        [TestMethod]
        public void HalfPowerMeleeDoesNotSpawnLightningAndOtherHeldWeaponSkipsMeleeForce()
        {
            var f = new PlayerSessionFixture();
            f.Player.CurrentWeapon = WeaponHash.Pistol;
            f.Player.InMeleeCombat = true;
            f.Game.AddEntity(60).DamagedBy.Add(10); f.Peds.Add(60);
            f.Game.World.Nearby = (position, radius) => new[] { 60 };
            f.Tick();
            Assert.IsFalse(f.Game.Calls.Any(call => call.Name == "entity.force"));
            f.Player.CurrentWeaponObject = 0;
            f.Tick();
            var force = f.Game.Calls.Single(call => call.Name == "entity.force");
            Assert.AreEqual(Vector3.UnitY * 125, force.Arguments[1]);
            Assert.AreEqual(50, f.Game.Calls.Single(call => call.Name == "ped.damage").Arguments[1]);
            Assert.IsFalse(f.Game.Native.Calls.Any(call => call.Hash == Hash.START_PARTICLE_FX_NON_LOOPED_ON_ENTITY));
        }
    }
}
