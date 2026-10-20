using System;
using System.Globalization;
using System.Linq;
using GTA;
using GTA.Math;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Thor;

namespace ModUtilsTest
{
    [TestClass]
    public class GameplayDiagnosticsTests
    {
        [TestMethod]
        public void StateSnapshotAndPerformanceIntervalsHaveIndependentBoundaries()
        {
            var game = new FakeGame();
            game.AddEntity(10);
            var log = new RecordingLog();
            var ability = new AbilityProbe();
            long now = 0;
            var diagnostics = new GameplayDiagnostics(game.Services, log, () => now, () => 7);
            diagnostics.RecordTick(ability, false, 2);
            now = 249; ability.DiagnosticState = "flying"; diagnostics.RecordTick(ability, false, 4);
            Assert.HasCount(1, log.Records.Where(record => record.Name == "ability.state").ToArray());
            now = 250; diagnostics.RecordTick(ability, false, 6);
            Assert.AreEqual("disabled=False;flying", log.Last("ability.state").Data["current"]);
            now = 500; diagnostics.RecordTick(ability, false, 100);
            Assert.HasCount(2, log.Records.Where(record => record.Name == "ability.state").ToArray());
            now = 4999; diagnostics.RecordTick(ability, false, 101);
            Assert.AreEqual(5000, log.Last("tick.slow").Interval);
            Assert.HasCount(1, log.Records.Where(record => record.Name == "gameplay.snapshot").ToArray());
            now = 5000; diagnostics.RecordTick(ability, true, 10);
            Assert.HasCount(2, log.Records.Where(record => record.Name == "gameplay.snapshot").ToArray());
            now = 9999; diagnostics.RecordTick(ability, true, 12);
            Assert.HasCount(1, log.Records.Where(record => record.Name == "performance").ToArray());
            now = 10000; diagnostics.RecordTick(ability, true, 14);
            var performance = log.Last("performance");
            Assert.AreEqual(7L, performance.Data["ticks"]);
            Assert.AreEqual(247.0 / 7, (double)performance.Data["averageTickMs"], 0.00001);
            Assert.AreEqual(101.0, performance.Data["maxTickMs"]);
            Assert.AreEqual(7L, performance.Data["droppedLogRecords"]);
        }

        [TestMethod]
        public void SnapshotSelectsHeldOrLooseHammerAndFormatsCoordinatesInvariantly()
        {
            var game = new FakeGame();
            var ped = game.AddEntity(10);
            ped.Position = new Vector3(1, 2, 3); ped.ModelHash = 123;
            ped.CurrentWeapon = WeaponHash.Hammer; ped.CurrentWeaponObject = 20;
            game.AddEntity(20).Position = new Vector3(1, 5, 7);
            game.AddEntity(30).Position = new Vector3(1, 2, 13);
            game.Input.Pressed.Add(Control.Aim);
            var ability = new AbilityProbe { IsHoldingWeapon = true, WeaponObject = 30 };
            var log = new RecordingLog();
            var diagnostics = new GameplayDiagnostics(game.Services, log, () => 0, () => 0);
            var previous = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
                diagnostics.Snapshot(ability, "held");
                Assert.AreEqual(5f, log.Last("gameplay.snapshot").Data["distance"]);
                StringAssert.Contains((string)log.Last("gameplay.snapshot").Data["player"], "position=(1.00,2.00,3.00)");
            }
            finally { CultureInfo.CurrentCulture = previous; }
            Assert.AreEqual(true, log.Last("gameplay.snapshot").Data["aim"]);
            ability.IsHoldingWeapon = false;
            diagnostics.Snapshot(ability, "loose");
            Assert.AreEqual(10f, log.Last("gameplay.snapshot").Data["distance"]);
            game.Observations[30].Exists = false;
            diagnostics.Snapshot(ability, "deleted");
            Assert.AreEqual("missing", log.Last("gameplay.snapshot").Data["hammer"]);
            Assert.AreEqual(-1f, log.Last("gameplay.snapshot").Data["distance"]);
            ability.WeaponObject = 0;
            diagnostics.Snapshot(ability, "absent");
            Assert.AreEqual("missing", log.Last("gameplay.snapshot").Data["hammer"]);
            Assert.AreEqual(-1f, log.Last("gameplay.snapshot").Data["distance"]);
            Assert.AreEqual("Hammer", log.Last("gameplay.snapshot").Data["selectedWeapon"]);
            game.Observations[10].Exists = false;
            diagnostics.Snapshot(ability, "deleted-player");
            Assert.AreEqual("missing", log.Last("gameplay.snapshot").Data["player"]);
            Assert.AreEqual(0, log.Last("gameplay.snapshot").Data["health"]);
            Assert.AreEqual("none", log.Last("gameplay.snapshot").Data["selectedWeapon"]);
            game.Host.PlayerPed = 0;
            diagnostics.Snapshot(ability, "no-player");
            var last = log.Last("gameplay.snapshot");
            Assert.AreEqual(0, last.Data["health"]);
            Assert.AreEqual("none", last.Data["selectedWeapon"]);
            Assert.AreEqual("missing", last.Data["player"]);
        }
    }
}
