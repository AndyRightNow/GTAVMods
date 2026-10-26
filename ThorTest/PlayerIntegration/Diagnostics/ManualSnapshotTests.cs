using Control = GTA.Control;
using GTA.Math;
using GTA;
using GTAVMods.Tests.Support;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using System;

namespace GTAVMods.Tests.PlayerIntegration.Diagnostics
{
    [TestClass, TestCategory("PlayerIntegration")]
    public class ManualSnapshotTests
    {
        [TestMethod]
        public void F9ShowsHeldThenDroppedHammerWithInvariantCoordinatesAndTheCurrentControls()
        {
            var s = new PlayerSessionFixture();
            s.Frame(0);
            s.Player.Position = new Vector3(1, 2, 3);
            s.Player.ModelHash = 123;
            s.Player.Velocity = new Vector3(-4, 5, -6);
            s.Game.Entity(20).Position = new Vector3(1, 5, 7);
            s.Game.Input.Pressed.Add(Control.Aim);
            s.Game.Input.Pressed.Add(Control.Sprint);
            s.Game.Host.IsPaused = true;
            var original = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
                s.Controller.KeyDown(new KeyEventArgs(Keys.F9));
            }
            finally { CultureInfo.CurrentCulture = original; }
            var held = s.Log.Last("gameplay.snapshot");
            Assert.AreEqual(5d, Convert.ToDouble(held.Data["distance"]));
            Assert.AreEqual("handle=10;model=123;position=(1.00,2.00,3.00);velocity=(-4.00,5.00,-6.00);dead=False", held.Data["player"]);
            Assert.AreEqual("Hammer", held.Data["selectedWeapon"]);
            Assert.AreEqual(true, held.Data["paused"]);
            Assert.AreEqual(true, held.Data["aim"]);
            Assert.AreEqual(false, held.Data["jump"]);
            Assert.AreEqual(true, held.Data["sprint"]);
            s.Game.Host.IsPaused = false;
            s.Frame(0, Keys.Y);
            s.Game.Entity(20).Position = new Vector3(1, 2, 13);
            s.Controller.KeyDown(new KeyEventArgs(Keys.F9));
            Assert.AreEqual(10d, Convert.ToDouble(s.Log.Last("gameplay.snapshot").Data["distance"]));
            Assert.AreEqual("Unarmed", s.Log.Last("gameplay.snapshot").Data["selectedWeapon"]);
            s.Game.Observations[20].Exists = false;
            s.Controller.KeyDown(new KeyEventArgs(Keys.F9));
            Assert.AreEqual("missing", s.Log.Last("gameplay.snapshot").Data["hammer"]);
            Assert.AreEqual(-1, s.Log.Last("gameplay.snapshot").Data["distance"]);
            s.Controller.Abort();
        }

        [TestMethod]
        public void ManualSnapshotsReportPlayerStateAndRapidRepeatedKeysDoNotFloodTheLog()
        {
            var s = new PlayerSessionFixture();
            s.Frame();
            s.Controller.KeyDown(new KeyEventArgs(Keys.F9 | Keys.Shift));
            s.Controller.KeyDown(new KeyEventArgs(Keys.F9 | Keys.Shift));
            Assert.HasCount(1, s.Log.Records.Where(record => record.Name == "input.F9").ToArray());
            Assert.AreEqual(true, s.Log.Last("input.F9").Data["shift"]);
            Assert.AreEqual(false, s.Log.Last("input.F9").Data["control"]);
            Assert.AreEqual("manual-F9", s.Log.Last("gameplay.snapshot").Data["reason"]);
            Assert.AreEqual(200, s.Log.Last("gameplay.snapshot").Data["health"],
                "F9 reports current health, not the new maximum health capacity.");
            s.Log.Clock.ElapsedMilliseconds += 250;
            s.Controller.KeyDown(new KeyEventArgs(Keys.F9));
            Assert.HasCount(2, s.Log.Records.Where(record => record.Name == "input.F9").ToArray());
        }
    }
}
