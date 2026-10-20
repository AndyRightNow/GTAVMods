using System;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;
using GTA;
using GTA.Math;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Control = GTA.Control;

namespace ModUtilsTest
{
    [TestClass, TestCategory("PlayerIntegration")]
    public class PlayerDiagnosticTests
    {
        [TestMethod]
        public void PeriodicDiagnosticsTrackActualPlayerStateAndKeepIndependentReportingWindows()
        {
            long timestamp = 0, duration = 2;
            var s = new PlayerSessionFixture(timestamp: () => timestamp += duration, frequency: 1000);
            s.Frame(0);
            int initialStates = s.Log.Records.Count(record => record.Name == "ability.state");
            s.Log.Clock.ElapsedMilliseconds = 249;
            s.Chord(Keys.F);
            s.Frame(0);
            Assert.AreEqual(initialStates, s.Log.Records.Count(record => record.Name == "ability.state"));
            s.Log.Clock.ElapsedMilliseconds = 250;
            s.Frame(0);
            StringAssert.Contains((string)s.Log.Last("ability.state").Data["current"], "disabled=True");
            int changedStates = s.Log.Records.Count(record => record.Name == "ability.state");
            s.Log.Clock.ElapsedMilliseconds = 500;
            duration = 100;
            s.Frame(0);
            Assert.AreEqual(changedStates, s.Log.Records.Count(record => record.Name == "ability.state"));
            Assert.IsFalse(s.Log.Records.Any(record => record.Name == "tick.slow"));
            s.Log.Clock.ElapsedMilliseconds = 4999;
            duration = 101;
            s.Frame(0);
            Assert.AreEqual(101d, Convert.ToDouble(s.Log.Last("tick.slow").Data["durationMs"]));
            int snapshots = s.Log.Records.Count(record => record.Name == "gameplay.snapshot");
            s.Log.Clock.ElapsedMilliseconds = 5000;
            duration = 10;
            s.Frame(0);
            Assert.AreEqual(snapshots + 1, s.Log.Records.Count(record => record.Name == "gameplay.snapshot"));
            Assert.AreEqual("periodic", s.Log.Last("gameplay.snapshot").Data["reason"]);
            int reports = s.Log.Records.Count(record => record.Name == "performance");
            s.Log.Clock.ElapsedMilliseconds = 9999;
            s.Frame(0);
            Assert.AreEqual(reports, s.Log.Records.Count(record => record.Name == "performance"));
            s.Log.Clock.ElapsedMilliseconds = 10000;
            s.Frame(0);
            Assert.AreEqual(reports + 1, s.Log.Records.Count(record => record.Name == "performance"));
            Assert.AreEqual(101d, Convert.ToDouble(s.Log.Last("performance").Data["maxTickMs"]));
            Assert.AreEqual(0, s.Log.Last("performance").Data["droppedLogRecords"]);
            Assert.AreEqual(200, s.Player.MaxHealth);
            s.Controller.Abort();
        }

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
    }
}
