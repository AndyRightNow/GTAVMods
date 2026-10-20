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

namespace GTAVMods.Tests.PlayerIntegration.ModLifecycle
{
    [TestClass, TestCategory("PlayerIntegration")]
    public class StartupCompatibilityTests
    {
        [TestMethod]
        [DataRow(0)]
        [DataRow(1)]
        [DataRow(2)]
        public void CreationFailurePausesWithoutUsingZeroHandlesAndReleasesEarlierAllocations(int successfulCreations)
        {
            var s = new PlayerSessionFixture(holding: false);
            for (int index = 0; index < successfulCreations; index++) s.WeaponCreations.Enqueue(21 + index);
            s.WeaponCreations.Enqueue(0);
            s.Frame(0, expectFailure: true);
            Assert.HasCount(1, s.Log.Records.Where(record => record.Name == "tick.exception").ToArray());
            Assert.HasCount(successfulCreations, s.CreatedWeapons);
            StringAssert.Contains((string)s.Log.Last("tick.exception").Data["exception"], new[]
            {
                "Could not create the hidden whirling hammer.",
                "Could not create the visible whirling hammer.",
                "The hammer weapon asset could not be created."
            }[successfulCreations]);
            Assert.IsTrue(s.CreatedWeapons.All(handle => !s.Game.Observations[handle].Exists));
            Assert.IsFalse(s.Game.Calls.Any(call => call.Name.StartsWith("entity.", StringComparison.Ordinal) &&
                call.Arguments.Length > 0 && call.Arguments[0] is int && (int)call.Arguments[0] == 0));
            Assert.AreEqual(200, s.Player.MaxHealth);
            int allocations = s.CreatedWeapons.Count;
            s.Frame(0, expectFailure: true);
            Assert.AreEqual(allocations, s.CreatedWeapons.Count);
        }

        [TestMethod]
        [DataRow(0, false, false)]
        [DataRow(10, false, false)]
        [DataRow(10, true, true)]
        public void StartingWithoutALivingCharacterWaitsWithoutAllocatingOrGivingEquipment(int ped, bool exists, bool dead)
        {
            var s = new PlayerSessionFixture(configure: game => {
                game.Host.PlayerPed = ped;
                game.Observations[10].Exists = exists;
                game.Observations[10].Dead = dead;
            });
            s.Frame();
            s.Frame();
            s.Disable();
            Assert.IsEmpty(s.CreatedWeapons);
            Assert.IsFalse(s.Game.Calls.Any(call => call.Name == "weapon.give" || call.Name == "ped.maxHealth"));
            Assert.AreEqual("Ctrl+A+F", s.Log.Last("ability.disabled").Data["reason"]);
            Assert.AreEqual(200, s.Game.Observations[10].MaxHealth);
        }

        [TestMethod]
        [DataRow(true, "2.0.1158.13", false)]
        [DataRow(true, "2.0.1158.17", true)]
        [DataRow(false, null, true)]
        public void CompatibilityNoticeAppearsOnceAndDoesNotPreventUsingTheHammer(bool enhanced, string version, bool warn)
        {
            var s = new PlayerSessionFixture(configure: game => {
                game.Host.IsEnhanced = enhanced;
                game.Host.FileVersion = version == null ? null : new Version(version);
            });
            s.Frame();
            s.Frame();
            Assert.AreEqual(30000, s.Player.MaxHealth);
            Assert.AreEqual(version, s.Log.Last("session.start").Data["shvdneVersion"]);
            Assert.AreEqual("3.9.0.0", s.Log.Last("session.start").Data["apiVersion"]);
            Assert.HasCount(warn ? 1 : 0, s.Log.Records.Where(record => record.Name == "compatibility.baseline").ToArray());
            Assert.HasCount(warn ? 1 : 0, s.Game.Calls.Where(call => call.Name == "host.notify").ToArray());
            s.Frame(0, Keys.Y);
            Assert.AreEqual(WeaponHash.Unarmed, s.Player.CurrentWeapon,
                "Compatibility advice must not block the player's drop control.");
            Assert.IsTrue(s.Game.Entity(20).Exists, "The dropped hammer remains available to recall.");
        }
    }
}
