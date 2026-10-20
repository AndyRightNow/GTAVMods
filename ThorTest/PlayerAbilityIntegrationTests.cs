using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using GTA;
using GTA.Math;
using GTA.Native;
using GTAVMods.Game;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Control = GTA.Control;

namespace ModUtilsTest
{
    [TestClass, TestCategory("PlayerIntegration")]
    public class PlayerAbilityIntegrationTests
    {
        private static PlayerSessionFixture Holding(Random random = null) => new PlayerSessionFixture(random: random);

        [TestMethod]
        public void StoredHammerGrantsHalfPowerAndRecallSelectsItWithoutSpawningLooseHammer()
        {
            var f = Holding();
            f.Player.Invincible = f.Player.BulletProof = f.Player.CollisionProof = f.Player.ExplosionProof =
                f.Player.FireProof = f.Player.MeleeProof = f.Player.CanSufferCriticalHits = true;
            f.Player.CurrentWeapon = WeaponHash.Pistol;
            f.Player.CurrentWeaponObject = 0;
            f.Game.Host.GameTime = 1;
            f.Tick();
            Assert.AreEqual(15100, f.Player.MaxHealth);
            Assert.AreEqual(1.25f, f.LastNative(Hash.SET_PED_MOVE_RATE_OVERRIDE)[1]);
            CollectionAssert.AreEqual(new object[] { f.Game.Host.PlayerHandle, 1.25f }, f.LastNative(Hash.SET_SWIM_MULTIPLIER_FOR_PLAYER));
            Assert.IsFalse(f.Player.BulletProof);
            Assert.IsFalse(f.Player.Invincible || f.Player.CollisionProof || f.Player.ExplosionProof ||
                f.Player.FireProof || f.Player.MeleeProof || f.Player.CanSufferCriticalHits || f.Player.DiesOnLowHealth,
                "Half power permits ordinary damage while preventing critical-hit and low-health instant deaths.");
            CollectionAssert.AreEqual(new object[] { 10, true }, f.LastNative(Hash.SET_ENTITY_CAN_BE_DAMAGED));
            Assert.HasCount(1, f.Game.Native.Calls.Where(call => call.Hash == Hash.SET_SUPER_JUMP_THIS_FRAME).ToArray());
            Assert.AreEqual(0, f.CreatedWeapons.Count(handle => f.Game.Observations[handle].Opacity == 255));
            f.Game.Input.Keys.Add(Keys.H);
            f.Tick();
            Assert.AreEqual(WeaponHash.Hammer, f.Player.CurrentWeapon);
        }


        [TestMethod]
        public void FlightUsesUpForwardAndSprintInputsAndRestoresHeldWeaponWhenStopped()
        {
            var f = Holding();
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
            var f = Holding();
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
            var f = Holding();
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
            var f = Holding(); f.Tick();
            foreach (int id in new[] { 60, 61, 62 }) f.Game.AddEntity(id);
            f.Player.Touching.Add(60); f.Game.Entity(20).Touching.Add(61);
            f.Player.Touching.Add(10); f.Player.Touching.Add(20);
            f.Game.World.Nearby = (position, radius) => new[] { 0, 10, 20, 60, 61, 62 };
            f.Player.InAir = true; f.Game.Input.Pressed.Add(Control.ScriptPadUp);
            f.Tick();
            CollectionAssert.AreEqual(new[] { 60, 61 }, f.Game.Calls.Where(call => call.Name == "entity.force").Select(call => (int)call.Arguments[0]).ToArray());
        }


        [TestMethod]
        public void AirDashSlamsDownThenDamagesNearbyEntitiesOnLanding()
        {
            var f = Holding(); f.Tick();
            f.Player.Position = new Vector3(17, -23, 11);
            f.Player.InAir = true; f.Game.Input.Pressed.Add(Control.Attack);
            f.Tick();
            Assert.AreEqual(new Vector3(0, 0, -200), f.Player.Velocity);
            f.Tick();
            Assert.AreEqual(new Vector3(0, 0, -200), f.Player.Velocity);
            Assert.IsFalse(f.Game.Calls.Any(call => call.Name == "ped.damage"));
            f.Game.AddEntity(60).Position = f.Player.Position + Vector3.UnitX; f.Peds.Add(60);
            f.Game.AddEntity(61).Position = f.Player.Position + Vector3.UnitY;
            f.Game.World.Nearby = (position, radius) => new[] { 0, 10, 20, 60, 61 };
            f.Player.InAir = false; f.Game.Input.Pressed.Clear();
            f.Tick();
            Assert.AreEqual(100, f.Game.Calls.Single(call => call.Name == "ped.damage").Arguments[1]);
            Assert.HasCount(2, f.Game.Calls.Where(call => call.Name == "entity.force").ToArray());
            CollectionAssert.AreEqual(new object[] { 60, Vector3.UnitX * 250 },
                f.Game.Calls.Single(call => call.Name == "entity.force" && (int)call.Arguments[0] == 60).Arguments);
            CollectionAssert.AreEqual(new object[] { 61, Vector3.UnitY * 250 },
                f.Game.Calls.Single(call => call.Name == "entity.force" && (int)call.Arguments[0] == 61).Arguments);
            CollectionAssert.AreEquivalent(new[] { 60, 61 }, f.Game.Native.Calls
                .Where(call => call.Hash == Hash.START_PARTICLE_FX_NON_LOOPED_ON_ENTITY)
                .Select(call => (int)call.Arguments[1]).ToArray());
            StringAssert.Contains(f.State(), "airDash=False");
            f.Game.Input.Pressed.Add(Control.Attack);
            f.Tick();
            Assert.HasCount(1, f.Game.Calls.Where(call => call.Name == "ped.damage").ToArray(),
                "Grounded attack input does not start another aerial landing strike.");
        }


        [TestMethod]
        public void MeleeDamageRequiresAHitAndIsSuppressedInsideVehicle()
        {
            var f = Holding();
            f.Game.AddEntity(60).DamagedBy.Add(10); f.Peds.Add(60);
            f.Game.AddEntity(61); f.Peds.Add(61);
            f.Game.World.Nearby = (position, radius) => new[] { 0, 10, 20, 60, 61 };
            f.Player.InMeleeCombat = true; f.Player.InVehicle = true;
            f.Tick();
            Assert.IsFalse(f.Game.Calls.Any(call => call.Name == "entity.force"));
            Assert.IsFalse(f.Game.Calls.Any(call => call.Name == "ped.damage"),
                "Being in a vehicle must suppress melee damage as well as knockback.");
            f.Player.InVehicle = false;
            f.Tick();
            var force = f.Game.Calls.Single(call => call.Name == "entity.force");
            CollectionAssert.AreEqual(new object[] { 60, Vector3.UnitY * 250 }, force.Arguments);
            CollectionAssert.AreEqual(new object[] { 60, 100 },
                f.Game.Calls.Single(call => call.Name == "ped.damage").Arguments,
                "Only the pedestrian reported hit receives full-power melee damage.");
        }


        [TestMethod]
        public void GrabSkipsSelfAndPropsAndReleasesOnChordOrDeath()
        {
            var f = Holding();
            f.Peds.Remove(60);
            f.Game.AddEntity(60); f.Game.AddEntity(61); f.Peds.Add(61);
            f.Game.AddEntity(62); f.Peds.Add(62);
            f.Game.World.Nearby = (position, radius) => new[] { 0, 10, 60, 61, 62 };
            f.Game.Input.Pressed.Add(Control.ThrowGrenade);
            f.Tick();
            Assert.AreEqual(10, f.Game.Entity(61).AttachedTo);
            Assert.AreEqual(Bone.IKLeftHand, f.Game.Entity(61).AttachedBone);
            Assert.IsFalse(f.Game.Native.Calls.Any(call => call.Hash == Hash.IS_ENTITY_A_PED &&
                (int)call.Arguments[0] == 0), "Missing nearby entries must not be treated as possible grab targets.");
            CollectionAssert.AreEqual(new object[] { 61 }, f.Game.Calls.Single(call => call.Name == "ped.clearTasks").Arguments);
            Assert.AreEqual(0, f.Game.Entity(62).AttachedTo);
            f.Tick();
            Assert.HasCount(1, f.Game.Calls.Where(call => call.Name == "entity.attach").ToArray());
            f.Game.Input.Pressed.Clear();
            f.Game.Input.Pressed.Add(Control.VehicleSubDescend);
            f.Tick();
            Assert.AreEqual(10, f.Game.Entity(61).AttachedTo, "The release modifier alone must not drop a grabbed ped.");
            f.Game.Input.Pressed.Add(Control.ThrowGrenade);
            f.Tick();
            Assert.AreEqual(0, f.Game.Entity(61).AttachedTo);
            f.Game.Input.Pressed.Clear(); f.Game.Input.Pressed.Add(Control.ThrowGrenade);
            f.Tick();
            f.Game.Entity(61).Dead = true;
            f.Tick();
            Assert.AreEqual(0, f.Game.Entity(61).AttachedTo);
            StringAssert.Contains(f.State(), "grabbing=False");
        }


        [TestMethod]
        public void MissingGrabbedAndHoverEntitiesDoNotPreventDetachAndCleanup()
        {
            var f = Holding();
            f.Game.AddEntity(60); f.Peds.Add(60);
            f.Game.Input.Pressed.Add(Control.ThrowGrenade);
            f.Game.World.Nearby = (position, radius) => new[] { 60 };
            f.Tick();
            f.Game.Observations[60].Exists = false;
            f.Tick();
            StringAssert.Contains(f.State(), "grabbing=False");
            f.Game.Observations[21].Exists = false;
            f.Game.Observations[22].Exists = false;
            f.Player.Weapons.Clear();
            int beforeDisable = f.Game.Calls.Count;
            f.Disable();
            Assert.IsFalse(f.Game.Calls.Any(call => call.Name == "entity.detach" && (int)call.Arguments[0] == 60));
            Assert.AreEqual(200, f.Player.MaxHealth);
            StringAssert.Contains(f.State(), "attached=False");
            Assert.IsFalse(f.Game.Calls.Skip(beforeDisable).Any(call =>
                call.Name.StartsWith("entity.", StringComparison.Ordinal) && call.Arguments.Length > 0 &&
                call.Arguments[0] is int && new[] { 21, 22 }.Contains((int)call.Arguments[0])));
            f.AssertNoErrors();
        }


        [TestMethod]
        public void TargetCollectionRejectsInvalidHitsCapsAtFifteenAndClearsWhenAimReleased()
        {
            var f = Holding();
            f.Tick();
            f.Game.Input.Pressed.Add(Control.Aim);
            f.Game.Cameras.GameplayPosition = new Vector3(17, -23, 11);
            f.Game.Cameras.GameplayDirection = Vector3.UnitX;
            f.Game.AddEntity(60); f.Peds.Remove(60);
            foreach (var hit in new[] { new RayHit(false, 0, Vector3.Zero), new RayHit(true, 0, Vector3.Zero),
                new RayHit(true, 10, Vector3.Zero), new RayHit(true, 60, Vector3.Zero) })
            {
                f.Game.World.Rays.Enqueue(hit);
                f.Tick();
            }
            Assert.IsFalse(f.Game.Calls.Any(call => call.Name == "world.marker"));
            CollectionAssert.AreEqual(new object[] { new Vector3(27, -23, 11), new Vector3(100017, -23, 11),
                IntersectFlags.Objects | IntersectFlags.Peds | IntersectFlags.Vehicles | IntersectFlags.Map, 0 },
                f.Game.Calls.First(call => call.Name == "world.raycast").Arguments);
            Assert.IsTrue(f.Game.Native.Calls.Any(call => call.Hash == Hash.SHOW_HUD_COMPONENT_THIS_FRAME));
            for (int id = 61; id <= 76; id++)
            {
                f.Game.AddEntity(id).Position = Vector3.UnitX * id;
                if (id == 61) f.Vehicles.Add(id); else f.Peds.Add(id);
                f.Game.World.Rays.Enqueue(new RayHit(true, id, Vector3.Zero));
                f.Tick();
            }
            StringAssert.Contains(f.State(), "targets=15");
            Assert.IsFalse(f.Game.Calls.Where(call => call.Name == "world.marker")
                .Any(call => (Vector3)call.Arguments[1] == new Vector3(76, 0, 2)));
            var firstMarker = f.Game.Calls.First(call => call.Name == "world.marker").Arguments;
            Assert.AreEqual(new Vector3(61, 0, 2), firstMarker[1]);
            Assert.AreEqual(System.Drawing.Color.Red, firstMarker[5]);
            f.Game.Input.Pressed.Clear();
            f.Tick();
            StringAssert.Contains(f.State(), "targets=15",
                "An unpressed sample without a release edge does not discard the active selection.");
            f.Game.Input.Released.Add(Control.Aim);
            var before = f.Game.Calls.Count(call => call.Name == "world.marker");
            f.Tick(); f.Tick();
            Assert.AreEqual(before, f.Game.Calls.Count(call => call.Name == "world.marker"));
            StringAssert.Contains(f.State(), "targets=0");
            StringAssert.Contains(f.State(), "collecting=False");
        }


        [TestMethod]
        public void RopeWhirlMaintainsFullPowerUntilReleasedAndSuppressesLooseHammerTrails()
        {
            var f = Holding();
            f.Game.Input.Keys.Add(Keys.Z);
            f.Game.Entities.CreatedBlips.Enqueue(50);
            f.WeaponCreations.Enqueue(21); f.WeaponCreations.Enqueue(22); f.WeaponCreations.Enqueue(30);
            // Hover helpers must be created before the hand anchor.
            f.Game.Ropes.Creations.Enqueue(70);
            f.Tick();
            Assert.IsFalse((f.Player.CurrentWeapon == WeaponHash.Hammer));
            StringAssert.Contains(f.State(), "rope=True");
            f.Game.Host.GameTime = 1; f.Tick();
            f.Game.Host.GameTime = 1001;
            f.Tick();
            Assert.IsTrue((f.Game.Entity(20).Velocity.Length() > 0));
            Assert.AreEqual(30000, f.Player.MaxHealth);
            Assert.AreEqual(1, f.Audio.Clips["whirling-hammer.wav"].Plays);
            Assert.AreEqual("dodge_generic_centre", f.LastNative(Hash.TASK_PLAY_ANIM)[2]);
            Assert.AreEqual((int)(AnimationFlags.UpperBodyOnly | AnimationFlags.Secondary), f.LastNative(Hash.TASK_PLAY_ANIM)[6]);
            var particlesBefore = f.Game.Native.Calls.Count(call => call.Hash == Hash.START_PARTICLE_FX_NON_LOOPED_AT_COORD);
            f.Game.Host.GameTime = 12000; f.Tick();
            Assert.AreEqual(particlesBefore, f.Game.Native.Calls.Count(call => call.Hash == Hash.START_PARTICLE_FX_NON_LOOPED_AT_COORD));
            Assert.AreEqual(30000, f.Player.MaxHealth, "A rope remains a full-power source beyond the ordinary loss grace period.");
            f.Game.Input.Keys.Clear();
            f.Tick();
            Assert.IsEmpty(f.Game.Ropes.Ropes);
            Assert.IsFalse(f.Game.Observations[30].Exists);
            StringAssert.Contains(f.State(), "rope=False");
            f.Game.Host.GameTime = 12001; f.Tick();
            Assert.AreEqual(30000, f.Player.MaxHealth, "The loss clock starts when the last active power source is released.");
            f.Game.Host.GameTime = 17001; f.Tick();
            Assert.AreEqual(15100, f.Player.MaxHealth);
        }


        [TestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public void DisablingReleasesALiveGrabAndIgnoresAnAlreadyDeletedPed(bool deleted)
        {
            var f = Holding();
            f.Game.AddEntity(60); f.Peds.Add(60);
            f.Game.World.Nearby = (position, radius) => new[] { 60 };
            f.Game.Input.Pressed.Add(Control.ThrowGrenade); f.Tick();
            f.Game.Observations[60].Exists = !deleted;
            f.Player.Weapons.Clear();

            f.Disable();

            Assert.AreEqual(!deleted, f.Game.Calls.Any(call => call.Name == "entity.detach" && (int)call.Arguments[0] == 60));
            StringAssert.Contains(f.State(), "grabbing=False");
        }


        [TestMethod]
        public void HalfPowerMeleeDoesNotSpawnLightningAndOtherHeldWeaponSkipsMeleeForce()
        {
            var f = Holding();
            f.Player.CurrentWeapon = WeaponHash.Pistol;
            f.Player.InMeleeCombat = true;
            f.Game.AddEntity(60).DamagedBy.Add(10); f.Peds.Add(60);
            f.Game.World.Nearby = (position, radius) => new[] { 60 };
            f.Tick();
            Assert.IsFalse(f.Game.Calls.Any(call => call.Name == "entity.force"));
            f.Player.CurrentWeaponObject = 0;
            f.Tick();
            var force = f.Game.Calls.Single(call => call.Name == "entity.force");
            Assert.AreEqual(Vector3.UnitY * 125, force.Arguments[1]);
            Assert.AreEqual(50, f.Game.Calls.Single(call => call.Name == "ped.damage").Arguments[1]);
            Assert.IsFalse(f.Game.Native.Calls.Any(call => call.Hash == Hash.START_PARTICLE_FX_NON_LOOPED_ON_ENTITY));
        }

        [TestMethod]
        [DataRow(0)]
        [DataRow(1)]
        [DataRow(2)]
        [DataRow(3)]
        public void ThrowingWhileMovingUsesUpperBodyAnimationAndAddsToTheObservedHammerMomentum(int movement)
        {
            var s = Holding();
            s.Player.Walking = movement == 0;
            s.Player.Sprinting = movement == 1;
            s.Player.Running = movement == 2;
            s.Player.InAir = movement == 3;
            s.Game.Cameras.GameplayDirection = Vector3.UnitX;
            s.Game.Entity(20).Velocity = new Vector3(3, 4, 5);
            s.Throw();
            Assert.AreEqual(new Vector3(103, 4, 10), s.Game.Entity(20).Velocity);
            Assert.AreEqual(WeaponHash.Unarmed, s.Player.CurrentWeapon);
            var animation = s.LastNative(Hash.TASK_PLAY_ANIM);
            Assert.AreEqual((int)(AnimationFlags.UpperBodyOnly | AnimationFlags.Secondary), animation[6]);
            StringAssert.EndsWith((string)animation[2], "+90");
            int animations = s.Game.Native.Calls.Count(call => call.Hash == Hash.TASK_PLAY_ANIM);
            s.Throw();
            Assert.HasCount(animations, s.Game.Native.Calls.Where(call => call.Hash == Hash.TASK_PLAY_ANIM).ToArray(),
                "Continuing to press throw after release cannot restart a held-weapon animation.");
        }

        [TestMethod]
        public void ChangingCharactersDuringAnAerialAttackRestoresTheOldPlayerAndClearsTargetsAndMomentum()
        {
            var s = Holding();
            s.Player.InAir = true;
            s.Game.Input.Keys.Add(Keys.W);
            s.Game.Input.Pressed.Add(Control.Attack);
            s.Game.Input.Pressed.Add(Control.Aim);
            s.Game.AddEntity(60).Position = Vector3.UnitY * 50;
            s.Game.World.Rays.Enqueue(new RayHit(true, 60, Vector3.Zero));
            s.Tick();
            StringAssert.Contains(s.State(), "airDash=True");
            StringAssert.Contains(s.State(), "targets=1");
            s.Game.Host.PlayerPed = 11;
            s.Game.AddEntity(30);
            s.Game.ApplyWeaponObservation(11, WeaponHash.Hammer, 30);
            s.Frame(0);
            Assert.AreEqual(200, s.Game.Entity(10).MaxHealth);
            Assert.AreEqual(30000, s.Game.Entity(11).MaxHealth);
            Assert.AreEqual(Vector3.Zero, s.Game.Entity(11).Velocity);
            foreach (string flag in new[] { "airDash", "flying", "throwFlight", "attacking", "collecting", "hoverWhirl" })
                StringAssert.Contains(s.State(), flag + "=False");
            StringAssert.Contains(s.State(), "targets=0");
            Assert.IsFalse(s.Game.Calls.Any(call => call.Name == "ped.damage"));
        }

    }
}
