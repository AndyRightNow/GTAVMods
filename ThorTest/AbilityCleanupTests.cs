using System;
using System.Linq;
using ADModUtils;
using GTA;
using GTA.Math;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ModUtilsTest
{
    [TestClass]
    public class AbilityCleanupTests
    {
        private static HammerFixture ActiveAbility()
        {
            var f = new HammerFixture();
            f.Player.MaxHealth = 250;
            f.HoldHammer();
            f.PrepareHoverObjects();
            f.Ability.ApplyOn(10);
            f.Game.AddEntity(60);
            f.Peds.Add(60);
            f.Game.World.Nearby = (position, radius) => new[] { 60 };
            f.Game.Input.Pressed.Add(Control.ThrowGrenade);
            f.Player.InAir = true;
            f.Game.Input.Pressed.Add(Control.Attack);
            f.Ability.OnTick(false);
            f.Game.Input.Pressed.Clear();
            f.Player.Health = 5000;
            f.LooseHammer(Vector3.UnitY);
            f.Game.AddEntity(30);
            f.WeaponCreations.Enqueue(30);
            f.Game.Ropes.Creations.Enqueue(70);
            f.Hammer.AttachHammerRopeTo(10, Bone.PHRightHand);
            f.Game.Cameras.Creations.Enqueue(80);
            f.Hammer.RenderWeaponTrackCam();
            f.Game.Cameras.RenderingCamera = 80;
            f.Hammer.Whirl(new ADModUtils.Plane(Vector3.UnitZ, Vector3.Zero, new Random(1)));
            f.Hammer.OnTick();
            f.Game.Calls.Clear();
            return f;
        }

        [TestMethod]
        public void RemovalAttemptsPlayerGrabRopeCameraAndBothHoverResourcesDespiteIndependentFailures()
        {
            var f = ActiveAbility();
            var healthFailure = new InvalidOperationException("restore original max health");
            var grabFailure = new InvalidOperationException("release grabbed ped");
            var hoverFailure = new InvalidOperationException("delete hidden hover hammer");
            f.Game.BeforeCall = call =>
            {
                if (call.Name == "ped.maxHealth" && (int)call.Arguments[1] == 250) throw healthFailure;
                if (call.Name == "entity.detach" && (int)call.Arguments[0] == 60) throw grabFailure;
                if (call.Name == "entity.delete" && (int)call.Arguments[0] == 21) throw hoverFailure;
            };

            var error = Assert.ThrowsExactly<AggregateException>(() => f.Ability.RemoveAbility());

            CollectionAssert.AreEquivalent(new[] { healthFailure, grabFailure, hoverFailure },
                error.Flatten().InnerExceptions.ToArray());
            Assert.IsTrue(f.Ability.IsAttachedToPed);
            Assert.AreEqual(30000, f.Player.MaxHealth);
            Assert.AreEqual(250, f.Player.Health);
            Assert.IsTrue(f.Player.CanSufferCriticalHits);
            Assert.IsTrue(f.Player.DiesOnLowHealth);
            Assert.AreEqual(10, f.Game.Entity(60).AttachedTo);
            Assert.IsTrue(f.Game.Entity(21).Exists);
            Assert.IsFalse(f.Game.Observations[22].Exists);
            Assert.IsFalse(f.Game.Observations[30].Exists);
            Assert.IsEmpty(f.Game.Ropes.Ropes);
            Assert.IsEmpty(f.Game.Cameras.Cameras);
            Assert.IsFalse(f.Audio.Clips["whirling-hammer.wav"].IsPlaying);
            Assert.IsTrue(f.Game.Entity(20).Exists);
            StringAssert.Contains(f.Ability.DiagnosticState, "airDash=True");

            f.Game.BeforeCall = null;
            f.Ability.RemoveAbility();
            f.Ability.RemoveAbility();

            Assert.IsFalse(f.Ability.IsAttachedToPed);
            Assert.AreEqual(250, f.Player.MaxHealth);
            Assert.AreEqual(0, f.Game.Entity(60).AttachedTo);
            Assert.IsFalse(f.Game.Observations[21].Exists);
            StringAssert.Contains(f.Ability.DiagnosticState, "airDash=False");
            StringAssert.Contains(f.Ability.DiagnosticState, "grabbing=False");
            Assert.HasCount(2, f.Game.Calls.Where(call => call.Name == "entity.delete" && (int)call.Arguments[0] == 21).ToArray());
            Assert.HasCount(1, f.Game.Calls.Where(call => call.Name == "entity.delete" && (int)call.Arguments[0] == 22).ToArray());
            Assert.HasCount(1, f.Game.Calls.Where(call => call.Name == "rope.delete").ToArray());
            Assert.HasCount(1, f.Game.Calls.Where(call => call.Name == "camera.delete").ToArray());
        }

        [TestMethod]
        public void FailureReleasingHeldWeaponStillRestoresPlayerAndOtherResourcesThenRetriesWithoutReinitializingHammer()
        {
            var f = ActiveAbility();
            var failure = new InvalidOperationException("remove held hammer");
            f.Game.BeforeCall = call => { if (call.Name == "weapon.remove") throw failure; };

            var error = Assert.ThrowsExactly<AggregateException>(() => f.Ability.RemoveAbility());

            Assert.AreSame(failure, error.Flatten().InnerExceptions.Single());
            Assert.IsTrue(f.Ability.IsAttachedToPed);
            Assert.AreEqual(250, f.Player.MaxHealth);
            Assert.AreEqual(250, f.Player.Health);
            Assert.AreEqual(0, f.Game.Entity(60).AttachedTo);
            Assert.IsFalse(f.Game.Observations[21].Exists);
            Assert.IsFalse(f.Game.Observations[22].Exists);
            Assert.IsEmpty(f.Game.Ropes.Ropes);
            Assert.IsEmpty(f.Game.Cameras.Cameras);
            Assert.IsTrue(f.Player.Weapons.Contains(WeaponHash.Hammer));
            Assert.AreEqual(20, f.Hammer.WeaponObject);

            f.Game.BeforeCall = null;
            f.Ability.RemoveAbility();

            Assert.IsFalse(f.Ability.IsAttachedToPed);
            Assert.IsFalse(f.Player.Weapons.Contains(WeaponHash.Hammer));
            Assert.AreEqual(WeaponHash.Unarmed, f.Player.CurrentWeapon);
            Assert.IsFalse(f.Game.Calls.Any(call => call.Name == "blip.create"));
            Assert.IsFalse(f.Game.Calls.Any(call => call.Name == "entity.delete" && (int)call.Arguments[0] == 20));
        }
    }
}
