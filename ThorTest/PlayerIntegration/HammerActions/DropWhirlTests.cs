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
    public class DropWhirlTests
    {
        [TestMethod]
        public void DisablingWhirlStopsItsEffectsAndEnablingRetainsTheDroppedHammer()
        {
            const int frameMs = 16;
            var s = new PlayerSessionFixture();
            s.Frame(frameMs);
            int fullHealth = s.Player.MaxHealth;
            s.Game.Ropes.Creations.Enqueue(70);
            s.Frame(frameMs, Keys.Z);
            Assert.IsTrue(s.Game.Ropes.Ropes.Contains(70));
            s.Frame(frameMs, Keys.Z);
            Assert.AreEqual(fullHealth, s.Game.Entity(10).MaxHealth);
            Assert.IsTrue(s.Audio.Clips["whirling-hammer.wav"].IsPlaying);
            s.Chord(Keys.F);
            Assert.IsEmpty(s.Game.Ropes.Ropes);
            Assert.IsFalse(s.Audio.Clips["whirling-hammer.wav"].IsPlaying);
            Assert.AreEqual(200, s.Game.Entity(10).MaxHealth);
            Assert.IsTrue(s.Game.Entity(20).Exists);
            var creations = s.CreatedWeapons.Count;
            s.Frame(frameMs);
            Assert.AreEqual(creations, s.CreatedWeapons.Count);
            s.Chord(Keys.O);
            s.Frame(frameMs);
            Assert.IsTrue(s.Game.Entity(20).Exists, "A normal disable/enable must preserve the existing loose hammer.");
            Assert.IsFalse(s.Game.Calls.Any(call => call.Name == "entity.delete" && (int)call.Arguments[0] == 20));
            s.Controller.Abort();
            s.AssertNoErrors();
            s.AssertReleased();
        }

        [TestMethod]
        [DataRow(Keys.T, false)]
        [DataRow(Keys.T, true)]
        [DataRow(Keys.Y, false)]
        [DataRow(Keys.Z, false)]
        public void FailedDropLeavesHeldHammerOwnedAndRetryableWithoutStartingThrowOrRope(Keys action, bool targeted)
        {
            var profile = new GameplayTuning();
            profile.Ability.ThrowSpeed = 79;
            profile.Ability.ThrowVelocityOffset = new Vector3(0.3f, -0.7f, 1.1f);
            var s = new PlayerSessionFixture(tuning: profile);
            s.Frame();
            s.Frame(16, Keys.Y);
            s.Game.ObserveNextFrame(game => game.ApplyWeaponObservation(10, WeaponHash.Hammer, 20));
            s.Frame();
            if (targeted)
            {
                s.Game.AddEntity(60).Position = new Vector3(0, 10, 0);
                s.Game.World.Rays.Enqueue(new RayHit(true, 60, Vector3.Zero));
            }
            s.Game.Native.Returns(Hash.GET_WEAPON_OBJECT_FROM_PED, 0);
            int firstCall = s.Game.Calls.Count;
            int allocations = s.CreatedWeapons.Count;
            s.Frame(16, action, aim: action == Keys.T);
            var attempt = s.Game.Calls.Skip(firstCall).ToArray();
            Assert.AreEqual(WeaponHash.Hammer, s.Game.Entity(10).CurrentWeapon);
            Assert.AreEqual(20, s.Game.Entity(10).CurrentWeaponObject);
            Assert.AreEqual(Vector3.Zero, s.Game.Entity(20).Velocity);
            Assert.AreEqual(allocations, s.CreatedWeapons.Count);
            Assert.IsEmpty(s.Game.Ropes.Ropes);
            Assert.IsFalse(attempt.Any(call => call.Name == "weapon.remove" && (WeaponHash)call.Arguments[1] == WeaponHash.Hammer));
            Assert.IsFalse(attempt.Any(call => call.Name == "weapon.select"));
            Assert.IsFalse(attempt.Any(call => call.Name == "entity.delete"));
            StringAssert.Contains(s.State(), "holding=True");
            StringAssert.Contains(s.State(), "attacking=False");
            StringAssert.Contains(s.State(), "rope=False");

            s.Game.Native.Queries[Hash.GET_WEAPON_OBJECT_FROM_PED] = args => s.Game.Entity((int)args[0]).CurrentWeaponObject;
            if (action == Keys.Z) s.Game.Ropes.Creations.Enqueue(70);
            s.Frame(16, action, aim: action == Keys.T);
            Assert.AreEqual(WeaponHash.Unarmed, s.Game.Entity(10).CurrentWeapon);
            Assert.IsTrue(s.Game.Entity(20).Exists);
            Assert.AreEqual(targeted, s.State().Contains("attacking=True"));
            Assert.AreEqual(action == Keys.Z, s.State().Contains("rope=True"));
            Assert.AreEqual(action == Keys.T && !targeted ? Vector3.UnitY * profile.Ability.ThrowSpeed + profile.Ability.ThrowVelocityOffset : Vector3.Zero,
                s.Game.Entity(20).Velocity);
            s.Controller.Abort();
            s.AssertNoErrors();
            s.AssertReleased();
        }

        [TestMethod]
        public void PressingWhirlWithoutAHeldHammerLeavesTheLooseHammerUnattached()
        {
            var s = new PlayerSessionFixture(holding: false);
            s.Frame(0, Keys.Z);
            Assert.HasCount(3, s.CreatedWeapons);
            Assert.IsEmpty(s.Game.Ropes.Ropes);
            Assert.IsFalse(s.Game.Calls.Any(call => call.Name == "rope.create" || call.Name == "rope.connect"));
            Assert.IsFalse(s.Audio.Clips.ContainsKey("whirling-hammer.wav"));
            Assert.AreEqual(Vector3.Zero, s.Game.Entity(23).Velocity);
            StringAssert.Contains(s.State(), "rope=False");
            s.Reload();
            s.AssertReleased();
        }
    }
}
