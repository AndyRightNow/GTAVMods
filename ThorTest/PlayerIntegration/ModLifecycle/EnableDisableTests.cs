using Control = GTA.Control;
using GTA.Math;
using GTA.Native;
using GTA;
using GTAVMods.Tests.Support;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using System;
using static GTAVMods.Tests.Support.PlayerScenarios;

namespace GTAVMods.Tests.PlayerIntegration.ModLifecycle
{
    [TestClass, TestCategory("PlayerIntegration")]
    public class EnableDisableTests
    {
        [TestMethod]
        public void DisablingWhileWhirlingAndGrabbingRestoresThePlayerAndRetriesIndependentFailures()
        {
            var s = Whirling(grab: true);
            s.Game.BeforeCall = call =>
            {
                if (call.Name == "ped.maxHealth" && (int)call.Arguments[1] == 250)
                    throw new InvalidOperationException("restore health failed");
                if (call.Name == "entity.detach" && (int)call.Arguments[0] == 60)
                    throw new InvalidOperationException("release pedestrian failed");
                if (call.Name == "entity.delete" && (int)call.Arguments[0] == 21)
                    throw new InvalidOperationException("release hover hammer failed");
            };
            s.Disable();
            var failure = (string)s.Log.Last("cleanup.exception").Data["exception"];
            foreach (var message in new[] { "restore health failed", "release pedestrian failed", "release hover hammer failed" })
                StringAssert.Contains(failure, message);
            Assert.AreEqual(30000, s.Player.MaxHealth);
            Assert.AreEqual(250, s.Player.Health);
            Assert.AreEqual(10, s.Game.Entity(60).AttachedTo);
            Assert.IsTrue(s.Game.Entity(21).Exists);
            Assert.IsFalse(s.Game.Observations[22].Exists);
            Assert.IsFalse(s.Game.Observations[23].Exists);
            Assert.IsEmpty(s.Game.Ropes.Ropes);
            Assert.IsEmpty(s.Game.Cameras.Cameras);
            Assert.IsFalse(s.Audio.Clips["whirling-hammer.wav"].IsPlaying);

            s.Game.BeforeCall = null;
            s.Chord(Keys.O);
            Assert.AreEqual(250, s.Player.MaxHealth);
            Assert.AreEqual(0, s.Game.Entity(60).AttachedTo);
            Assert.IsFalse(s.Game.Observations[21].Exists);
            Assert.IsFalse(s.Game.Observations[20].Exists);
            Assert.HasCount(2, s.Game.Calls.Where(call => call.Name == "entity.delete" && (int)call.Arguments[0] == 21).ToArray());
            Assert.HasCount(1, s.Game.Calls.Where(call => call.Name == "entity.delete" && (int)call.Arguments[0] == 22).ToArray());
        }

        [TestMethod]
        public void OnlyTheCompleteDisableChordRestoresHealthAndSuppliesOneUnequippedParachute()
        {
            var s = new PlayerSessionFixture();
            s.Frame();
            s.Controller.KeyDown(new KeyEventArgs(Keys.F));
            s.Controller.KeyDown(new KeyEventArgs(Keys.F | Keys.Control));
            s.Chord(Keys.P);
            s.Chord(Keys.O);
            Assert.AreEqual(30000, s.Player.MaxHealth);
            Assert.IsFalse(s.Log.Records.Any(record => record.Name == "ability.disabled"));

            s.Disable();
            s.Disable();
            s.Frame();
            Assert.AreEqual(200, s.Player.MaxHealth);
            CollectionAssert.AreEqual(new object[] { 10, WeaponHash.Parachute, 1, false, false },
                s.Game.Calls.Single(call => call.Name == "weapon.give" && (WeaponHash)call.Arguments[1] == WeaponHash.Parachute).Arguments);
            Assert.HasCount(1, s.Log.Records.Where(record => record.Name == "ability.disabled").ToArray());
            s.Chord(Keys.O);
            s.Frame();
            Assert.AreEqual(100, s.Player.MaxHealth, "Enabling does not confirm that the loose hammer was caught.");
            Assert.HasCount(1, s.Log.Records.Where(record => record.Name == "ability.enabled").ToArray());
        }

        [TestMethod]
        public void DisablingAfterTheGameRemovesTheWhirlRopeStillReleasesItsAnchorAndRestoresThePlayer()
        {
            var s = new PlayerSessionFixture();
            s.Game.Ropes.Creations.Enqueue(70);
            s.Frame(0, Keys.Z);
            Assert.IsTrue(s.Game.Ropes.Ropes.Contains(70));
            int anchor = (int)s.Game.Calls.Last(call => call.Name == "rope.connect").Arguments[1];
            s.Game.Ropes.Ropes.Remove(70);
            s.Game.Calls.Clear();
            s.Disable();
            s.AssertNoErrors();
            Assert.AreEqual(200, s.Player.MaxHealth);
            Assert.IsFalse(s.Game.Observations[anchor].Exists);
            Assert.IsTrue(s.Game.Entity(20).Exists, "Disabling leaves the released hammer available for a later recall.");
            Assert.IsFalse(s.Game.Calls.Any(call => call.Name == "rope.delete"),
                "An already absent rope needs no repeated deletion request.");
        }
    }
}
