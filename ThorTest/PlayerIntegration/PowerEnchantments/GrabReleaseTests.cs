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
    public class GrabReleaseTests
    {
        [TestMethod]
        public void GrabSkipsSelfAndPropsAndReleasesOnChordOrDeath()
        {
            var profile = new GameplayTuning();
            profile.Ability.GrabRange = 4.3f;
            var f = new PlayerSessionFixture(tuning: profile);
            f.Peds.Remove(60);
            f.Game.AddEntity(60); f.Game.AddEntity(61); f.Peds.Add(61);
            f.Game.AddEntity(62); f.Peds.Add(62);
            f.Game.World.Nearby = (position, radius) => new[] { 0, 10, 60, 61, 62 };
            f.Game.Input.Pressed.Add(Control.ThrowGrenade);
            f.Tick();
            CollectionAssert.AreEqual(new object[] { f.Player.Position, profile.Ability.GrabRange },
                f.Game.Calls.Single(call => call.Name == "world.nearby").Arguments);
            Assert.AreEqual(10, f.Game.Entity(61).AttachedTo);
            Assert.AreEqual(Bone.IKLeftHand, f.Game.Entity(61).AttachedBone);
            Assert.IsFalse(f.Game.Native.Calls.Any(call => call.Hash == Hash.IS_ENTITY_A_PED &&
                (int)call.Arguments[0] == 0), "Missing nearby entries must not be treated as possible grab targets.");
            CollectionAssert.AreEqual(new object[] { 61 }, f.Game.Calls.Single(call => call.Name == "ped.clearTasks").Arguments);
            Assert.AreEqual(0, f.Game.Entity(62).AttachedTo);
            f.Tick();
            Assert.HasCount(1, f.Game.Calls.Where(call => call.Name == "entity.attach").ToArray());
            f.Game.Input.Pressed.Clear();
            f.Game.Input.Pressed.Add(Control.VehicleSubDescend);
            f.Tick();
            Assert.AreEqual(10, f.Game.Entity(61).AttachedTo, "The release modifier alone must not drop a grabbed ped.");
            f.Game.Input.Pressed.Add(Control.ThrowGrenade);
            f.Tick();
            Assert.AreEqual(0, f.Game.Entity(61).AttachedTo);
            f.Game.Input.Pressed.Clear(); f.Game.Input.Pressed.Add(Control.ThrowGrenade);
            f.Tick();
            f.Game.Entity(61).Dead = true;
            f.Tick();
            Assert.AreEqual(0, f.Game.Entity(61).AttachedTo);
            StringAssert.Contains(f.State(), "grabbing=False");
        }

        [TestMethod]
        public void MissingGrabbedAndHoverEntitiesDoNotPreventDetachAndCleanup()
        {
            var f = new PlayerSessionFixture();
            f.Game.AddEntity(60); f.Peds.Add(60);
            f.Game.Input.Pressed.Add(Control.ThrowGrenade);
            f.Game.World.Nearby = (position, radius) => new[] { 60 };
            f.Tick();
            f.Game.Observations[60].Exists = false;
            f.Tick();
            StringAssert.Contains(f.State(), "grabbing=False");
            f.Game.Observations[21].Exists = false;
            f.Game.Observations[22].Exists = false;
            f.Player.Weapons.Clear();
            int beforeDisable = f.Game.Calls.Count;
            f.Disable();
            Assert.IsFalse(f.Game.Calls.Any(call => call.Name == "entity.detach" && (int)call.Arguments[0] == 60));
            Assert.AreEqual(200, f.Player.MaxHealth);
            StringAssert.Contains(f.State(), "attached=False");
            Assert.IsFalse(f.Game.Calls.Skip(beforeDisable).Any(call =>
                call.Name.StartsWith("entity.", StringComparison.Ordinal) && call.Arguments.Length > 0 &&
                call.Arguments[0] is int && new[] { 21, 22 }.Contains((int)call.Arguments[0])));
            f.AssertNoErrors();
        }

        [TestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public void DisablingReleasesALiveGrabAndIgnoresAnAlreadyDeletedPed(bool deleted)
        {
            var f = new PlayerSessionFixture();
            f.Game.AddEntity(60); f.Peds.Add(60);
            f.Game.World.Nearby = (position, radius) => new[] { 60 };
            f.Game.Input.Pressed.Add(Control.ThrowGrenade); f.Tick();
            f.Game.Observations[60].Exists = !deleted;
            f.Player.Weapons.Clear();

            f.Disable();

            Assert.AreEqual(!deleted, f.Game.Calls.Any(call => call.Name == "entity.detach" && (int)call.Arguments[0] == 60));
            StringAssert.Contains(f.State(), "grabbing=False");
        }
    }
}
