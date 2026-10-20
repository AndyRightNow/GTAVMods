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

namespace GTAVMods.Tests.PlayerIntegration.ModLifecycle
{
    [TestClass, TestCategory("PlayerIntegration")]
    public class CharacterChangeDeathTests
    {
        [TestMethod]
        [DataRow(1729)]
        [DataRow(8675309)]
        public void SeededLifecycleSequencesRestoreEveryFormerPlayerAndNeverAllocateWhileInactive(int seed)
        {
            var s = new PlayerSessionFixture(seed, holding: false);
            var events = new Random(seed);
            bool enabled = true;
            s.Frame();
            int mainHammer = s.CreatedWeapons.Single(handle => s.Game.Entity(handle).Opacity == 255);
            for (int step = 0; step < 48; step++)
            {
                int oldPlayer = s.Game.Host.PlayerPed;
                switch (events.Next(6))
                {
                    case 0: s.Chord(Keys.F); enabled = false; break;
                    case 1: s.Chord(Keys.O); enabled = true; break;
                    case 2: s.Game.Entity(oldPlayer).Dead = true; break;
                    case 3: s.Game.Entity(oldPlayer).Dead = false; break;
                    case 4: s.Game.Host.PlayerPed = oldPlayer == 10 ? 11 : 10; break;
                }
                int before = s.CreatedWeapons.Count;
                int player = s.Game.Host.PlayerPed;
                bool active = enabled && !s.Game.Entity(player).Dead;
                s.Frame(new[] { 1, 16, 33, 250 }[step % 4]);
                Assert.AreEqual(active, s.State().Contains("attached=True"), "seed " + seed + ", step " + step);
                Assert.AreEqual(active ? 3 : 1, s.CreatedWeapons.Count(handle => s.Game.Observations[handle].Exists),
                    "Only the main hammer and the active pair of hover helpers may remain owned.");
                if (!active) Assert.AreEqual(before, s.CreatedWeapons.Count);
                foreach (int inactivePlayer in new[] { 10, 11 }.Where(handle => !active || handle != player))
                    Assert.AreEqual(inactivePlayer == 10 ? 200 : 250, s.Game.Entity(inactivePlayer).MaxHealth);
                Assert.IsTrue(s.Game.Entity(mainHammer).Exists);
            }
            s.Controller.Abort();
            s.Controller.Abort();
            s.AssertNoErrors();
            s.AssertReleased();
            foreach (int handle in s.CreatedWeapons.Concat(new[] { 40, 41 }))
                Assert.HasCount(1, s.Game.Calls.Where(call => call.Name == "entity.delete" && (int)call.Arguments[0] == handle).ToArray());
        }

        [TestMethod]
        public void ChangingCharactersDuringAnAerialAttackRestoresTheOldPlayerAndClearsTargetsAndMomentum()
        {
            var s = new PlayerSessionFixture();
            s.Player.InAir = true;
            s.Game.Input.Keys.Add(Keys.W);
            s.Game.Input.Pressed.Add(Control.Attack);
            s.Game.Input.Pressed.Add(Control.Aim);
            s.Game.AddEntity(60).Position = Vector3.UnitY * 50;
            s.Game.World.Rays.Enqueue(new RayHit(true, 60, Vector3.Zero));
            s.Tick();
            StringAssert.Contains(s.State(), "airDash=True");
            StringAssert.Contains(s.State(), "targets=1");
            s.Game.Host.PlayerPed = 11;
            s.Game.AddEntity(30);
            s.Game.ApplyWeaponObservation(11, WeaponHash.Hammer, 30);
            s.Frame(0);
            Assert.AreEqual(200, s.Game.Entity(10).MaxHealth);
            Assert.AreEqual(30000, s.Game.Entity(11).MaxHealth);
            Assert.AreEqual(Vector3.Zero, s.Game.Entity(11).Velocity);
            foreach (string flag in new[] { "airDash", "flying", "throwFlight", "attacking", "collecting", "hoverWhirl" })
                StringAssert.Contains(s.State(), flag + "=False");
            StringAssert.Contains(s.State(), "targets=0");
            Assert.IsFalse(s.Game.Calls.Any(call => call.Name == "ped.damage"));
        }

        [TestMethod]
        public void RemovingThePlayerEntityReleasesHoverObjectsWithoutRestoringADeletedCharacter()
        {
            var s = new PlayerSessionFixture();
            s.Frame(0);
            Assert.AreEqual(30000, s.Player.MaxHealth);
            s.Game.Observations[10].Exists = false;
            s.Game.Calls.Clear();
            s.Frame(0);

            Assert.IsFalse(s.Game.Observations[21].Exists);
            Assert.IsFalse(s.Game.Observations[22].Exists);
            Assert.IsFalse(s.Game.Calls.Any(call =>
                new[] { "entity.health", "ped.maxHealth", "entity.invincible" }.Contains(call.Name) &&
                (int)call.Arguments[0] == 10));
            Assert.IsTrue(s.Game.Observations[20].Exists,
                "The game-owned held object must not be acquired from a deleted character.");
            StringAssert.Contains(s.State(), "attached=False");
            Assert.IsFalse(s.Log.Records.Any(record => record.Name == "cleanup.exception"));
        }

        [TestMethod]
        public void ChangingCharactersAndDyingRestoreThePreviousCharactersHealth()
        {
            var s = new PlayerSessionFixture();
            s.Frame();
            Assert.AreEqual(30000, s.Player.MaxHealth);
            s.Game.Host.PlayerPed = 11;
            s.Frame();
            Assert.AreEqual(200, s.Game.Entity(10).MaxHealth);
            Assert.AreEqual(100, s.Game.Entity(11).MaxHealth, "The new character has not picked up the hammer.");
            Assert.IsFalse(s.Game.Observations[21].Exists);
            Assert.IsFalse(s.Game.Observations[22].Exists);
            s.Game.Entity(11).Dead = true;
            s.Frame();
            Assert.AreEqual(250, s.Game.Entity(11).MaxHealth);
            int allocated = s.CreatedWeapons.Count;
            s.Frame();
            Assert.AreEqual(allocated, s.CreatedWeapons.Count);
            s.Game.Entity(11).Dead = false;
            s.Frame();
            Assert.AreEqual(100, s.Player.MaxHealth);
        }
    }
}
