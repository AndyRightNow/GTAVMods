using GTA.Math;
using GTAVMods.Tests.Support;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Linq;
using System.Windows.Forms;
using System;
using Thor;

namespace GTAVMods.Tests.PlayerIntegration.HammerActions
{
    [TestClass, TestCategory("PlayerIntegration")]
    public class FollowCameraTests
    {
        private sealed class CameraChoices : Random
        {
            public int View, ViewCount, Sign = 1;
            private int basisSamples;
            public override int Next(int maximum)
            {
                if (maximum == ViewCount) { basisSamples = 2; return View; }
                if (basisSamples > 0) { basisSamples--; return 1; }
                return Sign;
            }
            public override int Next(int minimum, int maximum) => Next(maximum);
            public override double NextDouble() => .5;
        }

        private static PlayerSessionFixture Dropped(CameraChoices choices, GameplayTuning tuning = null)
        {
            tuning = tuning ?? new GameplayTuning();
            tuning.Camera.Views = new[] { MjolnirCameraType.SideFollow,
                MjolnirCameraType.FarSideFollow, MjolnirCameraType.StationaryViewFollow };
            choices.ViewCount = tuning.Camera.Views.Length;
            var s = new PlayerSessionFixture(random: choices, tuning: tuning);
            s.DropHammer(new Vector3(10, 20, 30));
            s.Game.Entity(20).Velocity = Vector3.UnitZ * 10;
            s.Game.Cameras.Creations.Enqueue(80);
            return s;
        }

        [TestMethod]
        [DataRow(0, 0)]
        [DataRow(1, 1)]
        public void HoldingFollowCameraTracksTheHammerAtTheConfiguredViewingDistance(int sign, int view)
        {
            var profile = new GameplayTuning();
            profile.Camera.NearDistance = 13;
            profile.Camera.FarDistance = 41;
            profile.Camera.ViewDurationMs = 713;
            profile.Camera.FieldOfView = 63;
            var choices = new CameraChoices { View = view, Sign = sign };
            var s = Dropped(choices, profile);
            s.Frame(0, Keys.R);
            var offset = s.Game.Cameras.Cameras[80].Position - s.Game.Entity(20).Position;
            Assert.AreEqual(view == 0 ? profile.Camera.NearDistance : profile.Camera.FarDistance, offset.Length(), .001f);
            Assert.AreEqual(0, Vector3.Dot(offset, s.Game.Entity(20).Velocity), .001f,
                "A side view must lie perpendicular to the hammer's travel.");
            Assert.AreEqual(sign == 0 ? -1 : 1, Math.Sign(offset.X));
            Assert.AreEqual(profile.Camera.FieldOfView, s.Game.Calls.Single(call => call.Name == "camera.create").Arguments[2]);
            Assert.IsTrue(s.Game.Cameras.Cameras[80].Active);
            CollectionAssert.AreEqual(new object[] { 80, 20 }, s.Game.Calls.Single(call => call.Name == "camera.pointAt").Arguments);
            Assert.HasCount(1, s.Game.Calls.Where(call => call.Name == "camera.startRendering").ToArray(),
                "Holding R must request the activated camera as the rendered view.");
            s.Game.Entity(20).Position += new Vector3(1, 2, 3);
            s.Frame(profile.Camera.ViewDurationMs - 1, Keys.R);
            Assert.IsTrue((s.Game.Cameras.Cameras[80].Position - s.Game.Entity(20).Position - offset).Length() < .001f);
            choices.View = view == 0 ? 1 : 0;
            s.Frame(1, Keys.R);
            Assert.AreEqual(view == 0 ? profile.Camera.FarDistance : profile.Camera.NearDistance,
                s.Game.Cameras.Cameras[80].Position.DistanceTo(s.Game.Entity(20).Position), .001f);
        }

        [TestMethod]
        public void StationaryFollowViewRetainsItsLocationUntilTheConfiguredResetAcrossClockRollover()
        {
            var profile = new GameplayTuning();
            profile.Camera.ViewDurationMs = 937;
            profile.Camera.StationaryLeadDistance = 31;
            profile.Camera.NearDistance = 9;
            var s = Dropped(new CameraChoices { View = 2 }, profile);
            s.Game.Host.GameTime = int.MaxValue - 500;
            s.Frame(0, Keys.R);
            var initial = s.Game.Cameras.Cameras[80].Position;
            var offset = initial - s.Game.Entity(20).Position;
            Assert.AreEqual(profile.Camera.StationaryLeadDistance, Vector3.Dot(offset, Vector3.UnitZ), .001f);
            Assert.AreEqual(profile.Camera.NearDistance, new Vector3(offset.X, offset.Y, 0).Length(), .001f);
            s.Game.Entity(20).Position += Vector3.One;
            s.Frame(profile.Camera.ViewDurationMs - 1, Keys.R);
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
