using System;
using System.Linq;
using System.Windows.Forms;
using GTA.Math;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ModUtilsTest
{
    [TestClass, TestCategory("PlayerIntegration")]
    public class PlayerCameraIntegrationTests
    {
        private sealed class CameraChoices : Random
        {
            public int View, Sign = 1;
            private int basisSamples;
            public override int Next(int maximum)
            {
                if (maximum == 7) { basisSamples = 2; return View; }
                if (basisSamples > 0) { basisSamples--; return 1; }
                return Sign;
            }
            public override int Next(int minimum, int maximum) => Next(maximum);
            public override double NextDouble() => .5;
        }

        private static PlayerSessionFixture Dropped(CameraChoices choices)
        {
            var s = new PlayerSessionFixture(random: choices);
            s.DropHammer(new Vector3(10, 20, 30));
            s.Game.Entity(20).Velocity = Vector3.UnitZ * 10;
            s.Game.Cameras.Creations.Enqueue(80);
            return s;
        }

        [TestMethod]
        [DataRow(0, 0, 5f)]
        [DataRow(1, 1, 70f)]
        public void HoldingFollowCameraTracksTheHammerFromTheSelectedSide(int sign, int view, float distance)
        {
            var choices = new CameraChoices { View = view, Sign = sign };
            var s = Dropped(choices);
            s.Frame(0, Keys.R);
            var offset = new Vector3(1, 1, 0).Normalized * distance * (sign == 0 ? -1 : 1);
            Assert.IsTrue((s.Game.Cameras.Cameras[80].Position - s.Game.Entity(20).Position - offset).Length() < .001f);
            Assert.IsTrue(s.Game.Cameras.Cameras[80].Active);
            CollectionAssert.AreEqual(new object[] { 80, 20 }, s.Game.Calls.Single(call => call.Name == "camera.pointAt").Arguments);
            Assert.HasCount(1, s.Game.Calls.Where(call => call.Name == "camera.startRendering").ToArray(),
                "Holding R must request the activated camera as the rendered view.");
            s.Game.Entity(20).Position += new Vector3(1, 2, 3);
            s.Frame(1999, Keys.R);
            Assert.IsTrue((s.Game.Cameras.Cameras[80].Position - s.Game.Entity(20).Position - offset).Length() < .001f);
            choices.View = 0;
            s.Frame(1, Keys.R);
            Assert.AreEqual(5, s.Game.Cameras.Cameras[80].Position.DistanceTo(s.Game.Entity(20).Position), .001f);
        }

        [TestMethod]
        public void StationaryFollowViewRetainsItsLocationUntilTheTwoSecondResetAcrossClockRollover()
        {
            var s = Dropped(new CameraChoices { View = 3 });
            s.Game.Host.GameTime = int.MaxValue - 1000;
            s.Frame(0, Keys.R);
            var initial = s.Game.Cameras.Cameras[80].Position;
            Assert.IsTrue((initial - new Vector3(10, 20, 80) - new Vector3(1, 1, 0).Normalized * 5).Length() < .001f);
            s.Game.Entity(20).Position += Vector3.One;
            s.Frame(1999, Keys.R);
            Assert.AreEqual(initial, s.Game.Cameras.Cameras[80].Position);
            s.Frame(1, Keys.R);
            Assert.AreEqual(initial + Vector3.One, s.Game.Cameras.Cameras[80].Position);
        }

        [TestMethod]
        public void FailedCameraAllocationLeavesGameplayUsableAndHeldKeyRetriesNextFrame()
        {
            var s = Dropped(new CameraChoices());
            s.Game.Cameras.Creations.Clear();
            s.Game.Cameras.Creations.Enqueue(0);
            s.Game.Cameras.Creations.Enqueue(80);
            s.Frame(0, Keys.R);
            Assert.IsEmpty(s.Game.Cameras.Cameras);
            Assert.IsFalse(s.Log.Records.Any(record => record.Name == "camera.created"));
            s.Frame(0, Keys.R);
            Assert.IsTrue(s.Game.Cameras.Cameras[80].Active);
            Assert.HasCount(1, s.Log.Records.Where(record => record.Name == "camera.created").ToArray());
            s.Reload();
            Assert.IsEmpty(s.Game.Cameras.Cameras);
        }

        [TestMethod]
        public void ReleasingFollowKeyLeavesAnotherScriptsCameraAloneAndStopsOnlyTheOwnedView()
        {
            var s = Dropped(new CameraChoices());
            s.Frame(0, Keys.R);
            s.Game.Cameras.RenderingCamera = 99;
            s.Frame(0);
            Assert.AreEqual(99, s.Game.Cameras.RenderingCamera);
            Assert.IsFalse(s.Game.Calls.Any(call => call.Name == "camera.stopRendering"));
            s.Game.Cameras.RenderingCamera = 80;
            s.Frame(0);
            Assert.AreEqual(0, s.Game.Cameras.RenderingCamera);
            Assert.IsFalse(s.Game.Cameras.Cameras[80].Active);
            s.ObserveHeldHammer();
            s.Frame(0);
            Assert.IsEmpty(s.Game.Cameras.Cameras);
        }
    }
}
