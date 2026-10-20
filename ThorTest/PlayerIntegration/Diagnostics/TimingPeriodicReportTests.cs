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
    public class TimingPeriodicReportTests
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
        public void SessionPerformanceUsesTheHostCounterFrequencyWhenReportingAPlayerFrame()
        {
            var timestamps = new Queue<long>(new[] { 1000000L, 1000050L });
            var s = new PlayerSessionFixture(timestamp: () => timestamps.Dequeue(), frequency: 2000);
            s.Frame();
            Assert.AreEqual(30000, s.Player.MaxHealth);
            Assert.AreEqual(25d, Convert.ToDouble(s.Log.Last("performance").Data["averageTickMs"]));
            Assert.AreEqual(25d, Convert.ToDouble(s.Log.Last("performance").Data["maxTickMs"]));
        }
    }
}
