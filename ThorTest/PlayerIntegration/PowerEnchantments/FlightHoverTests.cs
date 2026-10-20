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

namespace GTAVMods.Tests.PlayerIntegration.PowerEnchantments
{
    [TestClass, TestCategory("PlayerIntegration")]
    public class FlightHoverTests
    {
        [TestMethod]
        [DataRow(1000)]
        [DataRow(int.MaxValue - 1024)]
        public void ThrowFlightExpiresAtTwoSecondsIncludingSignedClockWrap(int startTime)
        {
            var s = new PlayerSessionFixture();
            s.Game.Host.GameTime = startTime;
            s.Frame(0, Keys.U, aim: true);
            StringAssert.Contains(s.State(), "holding=True");
            StringAssert.Contains(s.State(), "collecting=False");
            Assert.IsFalse(s.Game.Native.Calls.Any(call => call.Hash == Hash.GET_WEAPON_OBJECT_FROM_PED));
            Assert.IsTrue(s.Game.Native.Calls.Any(call => call.Hash == Hash.TASK_PLAY_ANIM));
            Assert.IsTrue(s.Game.Native.Calls.Any(call => call.Hash == Hash.SET_PED_TO_RAGDOLL),
                "Aim+U enters ragdoll before the first flight frame without releasing the held hammer.");
            Assert.AreEqual("INFO", s.Log.Last("hammer.throwFlight").Level);
            StringAssert.Contains((string)s.Log.Last("hammer.throwFlight").Data["state"], "holding=True");
            int started = s.Game.Host.GameTime;
            foreach (int elapsed in new[] { 0, 1, 1999, 2000, 2001 })
            {
                s.Game.Entity(20).Velocity = Vector3.Zero; // Independent engine sample, no motion simulation.
                s.Game.Host.GameTime = unchecked(started + elapsed);
                s.Frame(0);
                Assert.AreEqual(elapsed < 2000, s.State().Contains("throwFlight=True"), "elapsed " + elapsed);
                Assert.AreEqual(elapsed < 2000 ? new Vector3(0, 140, 0) : Vector3.Zero, s.Game.Entity(20).Velocity);
            }
            s.Controller.Abort();
            s.AssertNoErrors();
            s.AssertReleased();
        }

        [TestMethod]
        public void FlightUsesUpForwardAndSprintInputsAndRestoresHeldWeaponWhenStopped()
        {
            var f = new PlayerSessionFixture();
            f.Game.Input.Keys.Add(Keys.X);
            f.Player.CanRagdoll = false;
            f.Tick();
            Assert.AreEqual(0, f.Game.Entity(20).Opacity);
            Assert.AreEqual(255, f.Game.Entity(22).Opacity);
            Assert.AreEqual(Vector3.UnitZ * 50, f.Game.Entity(20).Velocity);
            Assert.IsTrue(f.Player.CanRagdoll);
            CollectionAssert.AreEqual(new object[] { 10, 2, 1000, (int)ADModUtils.RagdollType.WideLegs, 0, 0, 0 },
                f.LastNative(Hash.SET_PED_TO_RAGDOLL));
            CollectionAssert.AreEqual(new object[] { 10, true }, f.LastNative(Hash.DISABLE_PED_PAIN_AUDIO));
            CollectionAssert.AreEqual(new object[] { CameraShake.MediumExplosion, 0.01f },
                f.Game.Calls.Last(call => call.Name == "camera.shake").Arguments);
            var orbit = f.Game.Entity(21).Position;
            f.Player.InAir = true;
            f.Game.Input.Keys.Clear(); f.Game.Input.Keys.Add(Keys.W); f.Game.Input.Pressed.Add(Control.Sprint);
            f.Game.Entity(20).Velocity = Vector3.Zero;
            f.Tick();
            Assert.AreEqual(Vector3.UnitY * 350, f.Game.Entity(20).Velocity);
            Assert.AreEqual(255, f.Game.Entity(20).Opacity);
            Assert.AreEqual(0, f.Game.Entity(22).Opacity);
            Assert.AreEqual(orbit, f.Game.Entity(21).Position,
                "Switching from hovering to forward flight must stop the hidden orbit.");
            StringAssert.Contains(f.State(), "hoverWhirl=False");
            f.Game.Input.Keys.Clear(); f.Game.Input.Pressed.Clear();
            f.Tick();
            StringAssert.Contains(f.State(), "flying=False");
            Assert.IsFalse(f.Player.CanRagdoll);
        }

        [TestMethod]
        public void LeavingFlightCarriesObservedMomentumOnceAndHoveringFollowsTheHand()
        {
            var f = new PlayerSessionFixture();
            f.Tick();
            var hand = new Vector3(17, -23, 11);
            f.Player.Bones[Bone.PHRightHand] = hand;
            f.Game.Entity(21).Position = Vector3.UnitX * 0.3f;
            f.Game.Entity(21).Rotation = new Vector3(-90, 0, -90);
            f.Player.InAir = true;
            f.Game.Input.Keys.Add(Keys.X);
            f.Tick();
            Assert.AreEqual(hand + new Vector3(0.3f, 0, 0.1f), f.Game.Entity(22).Position);
            Assert.AreEqual(new Vector3(-90, 0, -90), f.Game.Entity(22).Rotation);
            var orbitPosition = f.Game.Entity(21).Position;
            Assert.AreEqual(0.3f, orbitPosition.Length(), 0.0001f);
            Assert.AreNotEqual(Vector3.UnitX * 0.3f, orbitPosition, "The hidden helper advances the visible hammer's orbit.");

            f.Player.Velocity = new Vector3(3, -4, 2);
            f.Game.Input.Keys.Clear();
            f.Tick();
            Assert.AreEqual(new Vector3(6, -8, 4), f.Player.Velocity,
                "On leaving flight, the current observed momentum is added once.");
            Assert.AreEqual(255, f.Game.Entity(20).Opacity);
            Assert.AreEqual(0, f.Game.Entity(22).Opacity);
            Assert.AreEqual(orbitPosition, f.Game.Entity(21).Position, "An idle frame does not continue the hover orbit.");
            f.Tick();
            Assert.AreEqual(new Vector3(6, -8, 4), f.Player.Velocity, "Further idle frames must not repeatedly add momentum.");

            f.Game.Input.Keys.Add(Keys.W);
            f.Tick();
            Assert.AreEqual(hand + Vector3.UnitY * 0.3f, f.Game.Entity(20).Position);
            Assert.IsTrue((f.Game.Entity(20).Rotation - new Vector3(-90, 0, 0)).Length() < 0.001f);
            CollectionAssert.AreEqual(new object[] { -180f, 180f },
                f.Game.Calls.Last(call => call.Name == "camera.headingLimits").Arguments);
            CollectionAssert.AreEqual(new object[] { -180f, 180f },
                f.Game.Calls.Last(call => call.Name == "camera.pitchLimits").Arguments);
        }

        [TestMethod]
        [DataRow(0.85f, true)]
        [DataRow(0.849f, false)]
        public void FlightUsesHoverPresentationOnlyWithinTheUpwardCone(float upward, bool hover)
        {
            var f = new PlayerSessionFixture();
            f.Player.InAir = true;
            f.Game.Cameras.GameplayDirection = new Vector3((float)Math.Sqrt(1 - upward * upward), 0, upward);
            f.Game.Input.Keys.Add(Keys.W);
            f.Tick();
            Assert.AreEqual(hover ? 0 : 255, f.Game.Entity(20).Opacity);
            Assert.AreEqual(hover ? 255 : 0, f.Game.Entity(22).Opacity);
            StringAssert.Contains(f.State(), "hoverWhirl=" + hover);
        }

        [TestMethod]
        public void FlightPushesOnlyEntitiesTouchingPlayerOrHeldHammer()
        {
            var f = new PlayerSessionFixture(); f.Tick();
            foreach (int id in new[] { 60, 61, 62 }) f.Game.AddEntity(id);
            f.Player.Touching.Add(60); f.Game.Entity(20).Touching.Add(61);
            f.Player.Touching.Add(10); f.Player.Touching.Add(20);
            f.Game.World.Nearby = (position, radius) => new[] { 0, 10, 20, 60, 61, 62 };
            f.Player.InAir = true; f.Game.Input.Pressed.Add(Control.ScriptPadUp);
            f.Tick();
            CollectionAssert.AreEqual(new[] { 60, 61 }, f.Game.Calls.Where(call => call.Name == "entity.force").Select(call => (int)call.Arguments[0]).ToArray());
        }
    }
}
