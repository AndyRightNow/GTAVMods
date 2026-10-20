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
    [TestClass]
    public class WorthyAbilityTests
    {
        private static HammerFixture Holding()
        {
            var f = new HammerFixture();
            f.HoldHammer();
            f.PrepareHoverObjects();
            f.Ability.ApplyOn(10);
            return f;
        }

        [TestMethod]
        public void AttachmentInitializesOnceAndRemovalRestoresHealthAndReleasesHoverResources()
        {
            var f = new HammerFixture();
            f.Ability.OnTick(false);
            Assert.IsFalse(f.Ability.IsAttachedToPed);
            Assert.IsFalse(f.Ability.IsHoldingWeapon);
            f.Player.MaxHealth = 250;
            f.HoldHammer(); f.PrepareHoverObjects();
            f.Ability.ApplyOn(10); f.Ability.ApplyOn(99);
            f.Ability.OnTick(false);
            Assert.IsTrue(f.Ability.IsHoldingWeapon);
            Assert.AreEqual(30000, f.Player.MaxHealth);
            Assert.IsFalse(f.Player.CanSufferCriticalHits);
            Assert.AreEqual(1.5f, f.LastNative(Hash.SET_PED_MOVE_RATE_OVERRIDE)[1]);
            f.Player.Health = 5000;
            f.Game.Entities.CreatedBlips.Enqueue(50);
            f.Ability.RemoveAbility();
            Assert.AreEqual(250, f.Player.MaxHealth);
            Assert.AreEqual(250, f.Player.Health);
            Assert.IsTrue(f.Player.CanSufferCriticalHits);
            Assert.IsTrue(f.Player.DiesOnLowHealth);
            Assert.IsFalse(f.Ability.IsAttachedToPed);
            Assert.IsFalse(f.Game.Observations[21].Exists);
            Assert.IsFalse(f.Game.Observations[22].Exists);
            Assert.AreEqual(20, f.Ability.WeaponObject);
            f.Ability.RemoveAbility(); f.Ability.DisposeSounds(); f.Ability.DisposeWeapon();
            Assert.IsFalse(f.Game.Observations[20].Exists);
        }

        [TestMethod]
        public void StoredHammerGrantsHalfPowerAndRecallSelectsItWithoutSpawningLooseHammer()
        {
            var f = Holding();
            f.Player.CurrentWeapon = WeaponHash.Pistol;
            f.Player.CurrentWeaponObject = 0;
            f.Game.Host.GameTime = 1;
            f.Ability.OnTick(false);
            Assert.AreEqual(15100, f.Player.MaxHealth);
            Assert.AreEqual(1.25f, f.LastNative(Hash.SET_PED_MOVE_RATE_OVERRIDE)[1]);
            Assert.IsFalse(f.Player.BulletProof);
            Assert.AreEqual(0, f.Hammer.WeaponObject);
            f.Game.Input.Keys.Add(Keys.H);
            f.Ability.OnTick(false);
            Assert.AreEqual(WeaponHash.Hammer, f.Player.CurrentWeapon);
            Assert.IsTrue(f.Ability.IsHoldingWeapon);
        }

        [TestMethod]
        public void DroppingHammerDecaysPowerOverTenSecondsAndHealthRecoveryUsesCurrentPower()
        {
            var f = Holding();
            f.Game.Host.GameTime = 1; f.Ability.OnTick(false);
            f.Game.Host.GameTime = 201; f.Player.Health = 10;
            f.Ability.OnTick(false);
            Assert.AreEqual(3010, f.Player.Health);
            f.Game.Input.Keys.Add(Keys.Y); f.Game.Entities.CreatedBlips.Enqueue(50); f.ClearTerrainRay();
            f.Ability.OnTick(false);
            Assert.IsFalse(f.Ability.IsHoldingWeapon);
            Assert.AreEqual(Vector3.Zero, f.Hammer.Velocity);
            f.Game.Input.Keys.Clear(); f.ClearTerrainRay();
            f.Ability.OnTick(false);
            f.Game.Host.GameTime = 5201; f.ClearTerrainRay();
            f.Ability.OnTick(false);
            Assert.AreEqual(15100, f.Player.MaxHealth);
            Assert.AreEqual(1.25f, f.LastNative(Hash.SET_PED_MOVE_RATE_OVERRIDE)[1]);
            f.Game.Host.GameTime = 10201; f.ClearTerrainRay();
            f.Ability.OnTick(false);
            Assert.AreEqual(100, f.Player.MaxHealth);
            f.Game.Host.GameTime = 10202; f.ClearTerrainRay();
            f.Ability.OnTick(false);
            var health = f.Player.Health;
            f.Game.Host.GameTime = 10402; f.ClearTerrainRay();
            f.Ability.OnTick(false);
            Assert.AreEqual(health, f.Player.Health);
            Assert.AreEqual(1f, f.LastNative(Hash.SET_PED_MOVE_RATE_OVERRIDE)[1]);
        }

        [TestMethod]
        public void NewUnarmedPlayerSpawnsLooseHammerAndExistingHammerReturnsAfterAttachment()
        {
            var fresh = new HammerFixture();
            fresh.PrepareHoverObjects();
            fresh.WeaponCreations.Enqueue(20);
            fresh.Game.Entities.CreatedBlips.Enqueue(50);
            fresh.Ability.ApplyOn(10);
            fresh.ClearTerrainRay();
            fresh.Ability.OnTick(false);
            Assert.AreEqual(20, fresh.Ability.WeaponObject);
            Assert.AreEqual(100, fresh.Player.MaxHealth);
            var returning = new HammerFixture();
            returning.LooseHammer(new Vector3(0, -20, 0));
            returning.ClearPath();
            returning.Ability.ApplyOn(10);
            Assert.AreEqual(Vector3.UnitY * 100, returning.Hammer.Velocity);
        }

        [TestMethod]
        [DataRow(4f, false)]
        [DataRow(100f, true)]
        [DataRow(150f, false)]
        public void RecallSteersTowardHandAndPlaysApproachSoundOnlyInAudibleBand(float distance, bool audible)
        {
            var f = new HammerFixture();
            f.LooseHammer(new Vector3(0, -distance, 1));
            f.ClearPath(); f.Ability.ApplyOn(10);
            f.ClearPath(); f.ClearPath();
            f.Ability.SummonWeapon();
            Assert.IsTrue(f.Hammer.Velocity.Y > 0);
            Assert.AreEqual(0f, f.Hammer.Velocity.Z, 0.001f);
            Assert.AreEqual(audible, f.Audio.Clips.ContainsKey("hammer-close-to-player.wav"));
            Assert.AreEqual("aim_trans_low", f.LastNative(Hash.TASK_PLAY_ANIM)[2]);
        }

        [TestMethod]
        public void CatchTransfersWeaponPlaysAudioAndTriggersLightningAfterTheEngineWait()
        {
            var f = new HammerFixture();
            f.LooseHammer(new Vector3(0, 0, 1));
            f.ClearPath(); f.Ability.ApplyOn(10);
            f.Game.World.Capsules.Enqueue(new RayHit(true, 10, Vector3.UnitZ));
            f.Game.Host.OnWait = milliseconds => { Assert.AreEqual(1, milliseconds); f.HoldHammer(); };
            f.Ability.SummonWeapon();
            CollectionAssert.AreEqual(new object[] { 20, 10 }, f.LastNative(Hash.GIVE_WEAPON_OBJECT_TO_PED));
            Assert.AreEqual(1, f.Audio.Clips["catch-hammer.wav"].Plays);
            Assert.HasCount(8, f.Game.Native.Calls.Where(call => call.Hash == Hash.FORCE_LIGHTNING_FLASH).ToArray());
            Assert.IsTrue(f.Ability.IsHoldingWeapon);
            StringAssert.Contains(f.Ability.DiagnosticState, "return=False");
            f.PrepareHoverObjects();
            f.Game.Host.GameTime = 1000; f.ClearTerrainRay();
            f.Ability.OnTick(false);
            Assert.IsTrue(f.Game.Native.Calls.Any(call => call.Hash == Hash.START_PARTICLE_FX_NON_LOOPED_ON_PED_BONE));
        }

        [TestMethod]
        public void ThrowAddsLaunchVelocityInCameraDirectionAndHonorsWalkingAnimationFlags()
        {
            var f = Holding();
            f.Player.Walking = true;
            f.Game.Entity(20).Velocity = Vector3.UnitX * 3;
            f.Game.Entities.CreatedBlips.Enqueue(50);
            f.Ability.ThrowWeapon();
            Assert.AreEqual(new Vector3(3, 100, 5), f.Hammer.Velocity);
            Assert.AreEqual(WeaponHash.Unarmed, f.Player.CurrentWeapon);
            Assert.AreEqual((int)(AnimationFlags.UpperBodyOnly | AnimationFlags.Secondary), f.LastNative(Hash.TASK_PLAY_ANIM)[6]);
            f.Ability.ThrowWeapon(); f.Ability.ThrowAndFlyWithWeapon(); f.Ability.ThrowWeapon(new HashSet<int>());
            Assert.HasCount(1, f.Game.Native.Calls.Where(call => call.Hash == Hash.GET_WEAPON_OBJECT_FROM_PED).ToArray());
        }

        [TestMethod]
        public void EmptyTargetThrowUsesFreeThrowWhileTargetedThrowStartsWithoutInitialVelocity()
        {
            var f = Holding();
            f.Game.Entities.CreatedBlips.Enqueue(50);
            f.Ability.ThrowWeapon(new HashSet<int>());
            Assert.AreEqual(new Vector3(0, 100, 5), f.Hammer.Velocity);
            var targeted = Holding();
            targeted.Game.AddEntity(60).Position = new Vector3(0, 10, 0);
            targeted.Game.Entities.CreatedBlips.Enqueue(50);
            targeted.Ability.ThrowWeapon(new HashSet<int> { 60 });
            Assert.AreEqual(Vector3.Zero, targeted.Hammer.Velocity);
            StringAssert.Contains(targeted.Ability.DiagnosticState, "attacking=True");
        }

        [TestMethod]
        public void FlightUsesUpForwardAndSprintInputsAndRestoresHeldWeaponWhenStopped()
        {
            var f = Holding();
            f.Ability.OnTick(false);
            f.Player.InAir = true; f.Game.Input.Keys.Add(Keys.X);
            f.Ability.OnTick(false);
            Assert.AreEqual(0, f.Game.Entity(20).Opacity);
            Assert.AreEqual(255, f.Game.Entity(22).Opacity);
            Assert.AreEqual(Vector3.UnitZ * 50, f.Game.Entity(20).Velocity);
            f.Game.Input.Keys.Clear(); f.Game.Input.Keys.Add(Keys.W); f.Game.Input.Pressed.Add(Control.Sprint);
            f.Game.Entity(20).Velocity = Vector3.Zero;
            f.Ability.OnTick(false);
            Assert.AreEqual(Vector3.UnitY * 350, f.Game.Entity(20).Velocity);
            Assert.AreEqual(255, f.Game.Entity(20).Opacity);
            Assert.AreEqual(0, f.Game.Entity(22).Opacity);
            f.Game.Input.Keys.Clear(); f.Game.Input.Pressed.Clear();
            f.Ability.OnTick(false);
            StringAssert.Contains(f.Ability.DiagnosticState, "flying=False");
            Assert.IsFalse(f.Player.CanRagdoll);
        }

        [TestMethod]
        public void FlightPushesOnlyEntitiesTouchingPlayerOrHeldHammer()
        {
            var f = Holding(); f.Ability.OnTick(false);
            foreach (int id in new[] { 60, 61, 62 }) f.Game.AddEntity(id);
            f.Player.Touching.Add(60); f.Game.Entity(20).Touching.Add(61);
            f.Game.World.Nearby = (position, radius) => new[] { 10, 20, 60, 61, 62 };
            f.Player.InAir = true; f.Game.Input.Pressed.Add(Control.ScriptPadUp);
            f.Ability.OnTick(false);
            CollectionAssert.AreEqual(new[] { 60, 61 }, f.Game.Calls.Where(call => call.Name == "entity.force").Select(call => (int)call.Arguments[0]).ToArray());
        }

        [TestMethod]
        public void ThrowFlightLastsTwoSecondsAndThenStopsAddingVelocity()
        {
            var f = Holding(); f.Ability.OnTick(false);
            f.Ability.ThrowAndFlyWithWeapon();
            var start = f.Game.Host.GameTime;
            f.Game.Host.GameTime = start + 1999; f.Game.Entity(20).Velocity = Vector3.Zero;
            f.Ability.OnTick(false);
            Assert.AreEqual(Vector3.UnitY * 140, f.Game.Entity(20).Velocity);
            f.Game.Host.GameTime = start + 2000; f.Game.Entity(20).Velocity = Vector3.Zero;
            f.Ability.OnTick(false);
            Assert.AreEqual(Vector3.Zero, f.Game.Entity(20).Velocity);
            StringAssert.Contains(f.Ability.DiagnosticState, "throwFlight=False");
        }

        [TestMethod]
        public void AirDashSlamsDownThenDamagesNearbyEntitiesOnLanding()
        {
            var f = Holding(); f.Ability.OnTick(false);
            f.Player.InAir = true; f.Game.Input.Pressed.Add(Control.Attack);
            f.Ability.OnTick(false);
            Assert.AreEqual(new Vector3(0, 0, -200), f.Player.Velocity);
            f.Ability.OnTick(false);
            Assert.AreEqual(new Vector3(0, 0, -200), f.Player.Velocity);
            Assert.IsFalse(f.Game.Calls.Any(call => call.Name == "ped.damage"));
            f.Game.AddEntity(60).Position = Vector3.UnitX; f.Peds.Add(60);
            f.Game.AddEntity(61).Position = Vector3.UnitY;
            f.Game.World.Nearby = (position, radius) => new[] { 10, 20, 60, 61 };
            f.Player.InAir = false; f.Game.Input.Pressed.Clear();
            f.Ability.OnTick(false);
            Assert.AreEqual(100, f.Game.Calls.Single(call => call.Name == "ped.damage").Arguments[1]);
            Assert.HasCount(2, f.Game.Calls.Where(call => call.Name == "entity.force").ToArray());
            StringAssert.Contains(f.Ability.DiagnosticState, "airDash=False");
        }

        [TestMethod]
        public void MeleeDamageRequiresAHitAndIsSuppressedInsideVehicle()
        {
            var f = Holding();
            f.Game.AddEntity(60).DamagedBy.Add(10); f.Peds.Add(60);
            f.Game.AddEntity(61);
            f.Game.World.Nearby = (position, radius) => new[] { 10, 20, 60, 61 };
            f.Player.InMeleeCombat = true; f.Player.InVehicle = true;
            f.Ability.OnTick(false);
            Assert.IsFalse(f.Game.Calls.Any(call => call.Name == "entity.force"));
            f.Player.InVehicle = false;
            f.Ability.OnTick(false);
            var force = f.Game.Calls.Single(call => call.Name == "entity.force");
            CollectionAssert.AreEqual(new object[] { 60, Vector3.UnitY * 250 }, force.Arguments);
        }

        [TestMethod]
        public void GrabSkipsSelfAndPropsAndReleasesOnChordOrDeath()
        {
            var f = Holding();
            f.Game.AddEntity(60); f.Game.AddEntity(61); f.Peds.Add(61);
            f.Game.World.Nearby = (position, radius) => new[] { 10, 60, 61 };
            f.Game.Input.Pressed.Add(Control.ThrowGrenade);
            f.Ability.OnTick(false);
            Assert.AreEqual(10, f.Game.Entity(61).AttachedTo);
            Assert.AreEqual(Bone.IKLeftHand, f.Game.Entity(61).AttachedBone);
            f.Ability.OnTick(false);
            Assert.HasCount(1, f.Game.Calls.Where(call => call.Name == "entity.attach").ToArray());
            f.Game.Input.Pressed.Add(Control.VehicleSubDescend);
            f.Ability.OnTick(false);
            Assert.AreEqual(0, f.Game.Entity(61).AttachedTo);
            f.Game.Input.Pressed.Clear(); f.Game.Input.Pressed.Add(Control.ThrowGrenade);
            f.Ability.OnTick(false);
            f.Game.Entity(61).Dead = true;
            f.Ability.OnTick(false);
            Assert.AreEqual(0, f.Game.Entity(61).AttachedTo);
            StringAssert.Contains(f.Ability.DiagnosticState, "grabbing=False");
        }

        [TestMethod]
        public void FirstTickUpwardFlightDoesNotRequireAnEarlierIdleFrame()
        {
            var f = Holding();
            f.Game.Input.Keys.Add(Keys.X);
            f.Ability.OnTick(false);
            Assert.AreEqual(Vector3.UnitZ * 50, f.Game.Entity(20).Velocity);
            Assert.AreEqual(0, f.Game.Entity(20).Opacity);
            Assert.AreEqual(255, f.Game.Entity(22).Opacity);
        }

        [TestMethod]
        public void RemovalClearsAirDashAndMomentumBeforeReapplyingToAnotherCharacter()
        {
            var f = Holding();
            f.Player.InAir = true; f.Game.Input.Keys.Add(Keys.W); f.Game.Input.Pressed.Add(Control.Attack);
            f.Ability.OnTick(false);
            StringAssert.Contains(f.Ability.DiagnosticState, "airDash=True");
            f.Game.Entities.CreatedBlips.Enqueue(50);
            f.Ability.RemoveAbility();
            f.Game.Input.Keys.Clear(); f.Game.Input.Pressed.Clear();
            var replacement = f.Game.AddEntity(11);
            replacement.Bones[Bone.PHRightHand] = Vector3.UnitZ;
            replacement.Weapons.Add(WeaponHash.Hammer); replacement.CurrentWeapon = WeaponHash.Hammer; replacement.CurrentWeaponObject = 20;
            f.Game.Host.PlayerPed = 11;
            f.Game.AddEntity(31); f.Game.AddEntity(32);
            f.WeaponCreations.Enqueue(31); f.WeaponCreations.Enqueue(32);
            f.Ability.ApplyOn(11); f.ClearTerrainRay();
            f.Ability.OnTick(false);
            Assert.AreEqual(Vector3.Zero, replacement.Velocity);
            StringAssert.Contains(f.Ability.DiagnosticState, "airDash=False");
            Assert.IsFalse(f.Game.Calls.Any(call => call.Name == "ped.damage"));
        }

        [TestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public void FailedHoverObjectCreationCanBeCleanedWithoutCallingIntoZeroHandles(bool firstCreated)
        {
            var f = new HammerFixture();
            f.HoldHammer(); f.Ability.ApplyOn(10);
            if (firstCreated) { f.Game.AddEntity(21); f.WeaponCreations.Enqueue(21); }
            f.WeaponCreations.Enqueue(0);
            var error = Assert.Throws<InvalidOperationException>(() => f.Ability.OnTick(false));
            Assert.AreEqual(firstCreated ? "Could not create the visible whirling hammer." : "Could not create the hidden whirling hammer.", error.Message);
            Assert.IsFalse(f.Game.Calls.Any(call => call.Name.StartsWith("entity.", StringComparison.Ordinal) && (int)call.Arguments[0] == 0));
            f.Player.Weapons.Clear();
            f.Ability.RemoveAbility();
            if (firstCreated) Assert.IsFalse(f.Game.Observations[21].Exists);
            Assert.IsFalse(f.Ability.IsAttachedToPed);
        }

        [TestMethod]
        public void MissingGrabbedAndHoverEntitiesDoNotPreventDetachAndCleanup()
        {
            var f = Holding();
            f.Game.AddEntity(60); f.Peds.Add(60);
            f.Game.Input.Pressed.Add(Control.ThrowGrenade);
            f.Game.World.Nearby = (position, radius) => new[] { 60 };
            f.Ability.OnTick(false);
            f.Game.Observations[60].Exists = false;
            f.Ability.OnTick(false);
            StringAssert.Contains(f.Ability.DiagnosticState, "grabbing=False");
            f.Game.Observations[21].Exists = false;
            f.Game.Observations[22].Exists = false;
            f.Player.Weapons.Clear();
            f.Ability.RemoveAbility();
            Assert.IsFalse(f.Game.Calls.Any(call => call.Name == "entity.detach" && (int)call.Arguments[0] == 60));
        }

        [TestMethod]
        public void SummonWithoutAnAttachedPlayerOrWithOwnedHammerDoesNotQueryWorld()
        {
            var f = new HammerFixture();
            f.Ability.SummonWeapon();
            f.HoldHammer(); f.Ability.ApplyOn(10); f.Ability.SummonWeapon();
            Assert.IsFalse(f.Game.Calls.Any(call => call.Name == "world.capsule"));
        }

        [TestMethod]
        public void HoldingOnlyTickUpdatesMovementWithoutPowerOrHoverObjectInitialization()
        {
            var f = new HammerFixture();
            f.HoldHammer(); f.Ability.ApplyOn(10);
            f.Ability.OnTick(true);
            Assert.AreEqual(200, f.Player.MaxHealth);
            Assert.IsTrue(f.Player.CanRagdoll);
            Assert.IsFalse(f.Game.Native.Calls.Any(call => call.Hash == Hash.CREATE_WEAPON_OBJECT));
            Assert.IsFalse(f.Game.Native.Calls.Any(call => call.Hash == Hash.SET_PED_MOVE_RATE_OVERRIDE));
            Assert.IsTrue(f.Game.Native.Calls.Any(call => call.Hash == Hash.SET_PLAYER_LOCKON_RANGE_OVERRIDE));
        }

        [TestMethod]
        public void TargetCollectionRejectsInvalidHitsCapsAtFifteenAndClearsWhenAimReleased()
        {
            var f = Holding();
            f.Ability.OnTick(false);
            f.Game.Input.Pressed.Add(Control.Aim);
            f.Game.AddEntity(60);
            foreach (var hit in new[] { new RayHit(false, 0, Vector3.Zero), new RayHit(true, 0, Vector3.Zero),
                new RayHit(true, 10, Vector3.Zero), new RayHit(true, 60, Vector3.Zero) })
            {
                f.Game.World.Rays.Enqueue(hit);
                f.Ability.OnTick(false);
            }
            Assert.IsFalse(f.Game.Calls.Any(call => call.Name == "world.marker"));
            for (int id = 61; id <= 76; id++)
            {
                f.Game.AddEntity(id).Position = Vector3.UnitX * id;
                if (id == 61) f.Vehicles.Add(id); else f.Peds.Add(id);
                f.Game.World.Rays.Enqueue(new RayHit(true, id, Vector3.Zero));
                f.Ability.OnTick(false);
            }
            StringAssert.Contains(f.Ability.DiagnosticState, "targets=15");
            Assert.IsFalse(f.Game.Calls.Where(call => call.Name == "world.marker")
                .Any(call => (Vector3)call.Arguments[1] == new Vector3(76, 0, 2)));
            var firstMarker = f.Game.Calls.First(call => call.Name == "world.marker").Arguments;
            Assert.AreEqual(new Vector3(61, 0, 2), firstMarker[1]);
            Assert.AreEqual(System.Drawing.Color.Red, firstMarker[5]);
            f.Game.Input.Pressed.Clear(); f.Game.Input.Released.Add(Control.Aim);
            var before = f.Game.Calls.Count(call => call.Name == "world.marker");
            f.Ability.OnTick(false); f.Ability.OnTick(false);
            Assert.AreEqual(before, f.Game.Calls.Count(call => call.Name == "world.marker"));
            StringAssert.Contains(f.Ability.DiagnosticState, "targets=0");
        }

        [TestMethod]
        public void TargetedThrowReachesTargetWaitsSevenHundredMillisecondsThenReturnsAndCatches()
        {
            var f = Holding();
            f.Ability.OnTick(false);
            f.Game.AddEntity(60).Position = new Vector3(0, 0.4f, 0); f.Peds.Add(60);
            f.Game.Input.Pressed.Add(Control.Aim); f.Game.Input.Keys.Add(Keys.T);
            f.Game.World.Rays.Enqueue(new RayHit(true, 60, Vector3.Zero)); f.ClearTerrainRay();
            f.Game.Entities.CreatedBlips.Enqueue(50);
            f.Ability.OnTick(false);
            StringAssert.Contains(f.Ability.DiagnosticState, "attacking=True");
            f.Game.Input.Pressed.Clear(); f.Game.Input.Keys.Clear();
            f.ClearTerrainRay(); f.Ability.OnTick(false);
            StringAssert.Contains(f.Ability.DiagnosticState, "targets=0");
            f.ClearTerrainRay(); f.Ability.OnTick(false);
            var finished = f.Game.Host.GameTime;
            StringAssert.Contains(f.Ability.DiagnosticState, "attacking=False");
            f.Game.Host.GameTime = finished + 699; f.ClearTerrainRay(); f.Ability.OnTick(false);
            Assert.IsFalse(f.Game.Native.Calls.Any(call => call.Hash == Hash.GIVE_WEAPON_OBJECT_TO_PED));
            f.Game.Host.GameTime = finished + 700;
            f.Game.World.Capsules.Enqueue(new RayHit(true, 10, Vector3.Zero));
            f.Game.Host.OnWait = milliseconds => f.HoldHammer();
            f.ClearTerrainRay(); f.Ability.OnTick(false);
            Assert.IsTrue(f.Ability.IsHoldingWeapon);
            CollectionAssert.AreEqual(new object[] { 20, 10 }, f.LastNative(Hash.GIVE_WEAPON_OBJECT_TO_PED));
        }

        [TestMethod]
        public void AimAndThrowFlightInputStartsFlightWithoutReleasingHammer()
        {
            var f = Holding();
            f.Game.Input.Pressed.Add(Control.Aim); f.Game.Input.Keys.Add(Keys.U);
            f.Game.World.Rays.Enqueue(new RayHit(false, 0, Vector3.Zero));
            f.Ability.OnTick(false);
            StringAssert.Contains(f.Ability.DiagnosticState, "throwFlight=True");
            Assert.IsTrue(f.Ability.IsHoldingWeapon);
            Assert.IsFalse(f.Game.Native.Calls.Any(call => call.Hash == Hash.GET_WEAPON_OBJECT_FROM_PED));
        }

        [TestMethod]
        public void RopeWhirlMaintainsFullPowerUntilReleasedAndSuppressesLooseHammerTrails()
        {
            var f = Holding();
            f.Game.Input.Keys.Add(Keys.Z);
            f.Game.Entities.CreatedBlips.Enqueue(50);
            f.Game.AddEntity(30); f.WeaponCreations.Enqueue(30);
            // Hover helpers must be created before the hand anchor.
            f.Game.Ropes.Creations.Enqueue(70);
            f.Ability.OnTick(false);
            Assert.IsFalse(f.Ability.IsHoldingWeapon);
            StringAssert.Contains(f.Ability.DiagnosticState, "rope=True");
            f.Ability.OnTick(false);
            Assert.IsTrue(f.Hammer.IsMoving);
            Assert.AreEqual(30000, f.Player.MaxHealth);
            Assert.AreEqual(1, f.Audio.Clips["whirling-hammer.wav"].Plays);
            Assert.AreEqual("dodge_generic_centre", f.LastNative(Hash.TASK_PLAY_ANIM)[2]);
            var particlesBefore = f.Game.Native.Calls.Count(call => call.Hash == Hash.START_PARTICLE_FX_NON_LOOPED_AT_COORD);
            f.Game.Host.GameTime = 1; f.Ability.OnTick(false);
            Assert.AreEqual(particlesBefore, f.Game.Native.Calls.Count(call => call.Hash == Hash.START_PARTICLE_FX_NON_LOOPED_AT_COORD));
            f.Game.Input.Keys.Clear();
            f.ClearPath(); f.ClearPath(); f.ClearTerrainRay();
            f.Ability.OnTick(false);
            Assert.IsEmpty(f.Game.Ropes.Ropes);
            Assert.IsFalse(f.Game.Observations[30].Exists);
            StringAssert.Contains(f.Ability.DiagnosticState, "rope=False");
        }

        [TestMethod]
        public void RecallKeyForLooseHammerAndFollowCameraUsePublicInputWorkflow()
        {
            var f = new HammerFixture();
            f.LooseHammer(new Vector3(0, -30, 1));
            f.ClearPath(); f.Ability.ApplyOn(10); f.PrepareHoverObjects();
            f.Game.Input.Keys.Add(Keys.H); f.Game.Input.Keys.Add(Keys.R);
            f.Game.Cameras.Creations.Enqueue(70);
            f.ClearPath(); f.ClearPath(); f.ClearTerrainRay();
            f.Ability.OnTick(false);
            Assert.AreEqual(1, f.Audio.Clips["hammer-close-to-player.wav"].Plays);
            Assert.IsTrue(f.Game.Cameras.Cameras[70].Active);
            Assert.IsTrue(f.Game.Native.Calls.Any(call => call.Hash == Hash.START_PARTICLE_FX_NON_LOOPED_AT_COORD));
            f.Game.Input.Keys.Clear(); f.Game.Cameras.RenderingCamera = 70; f.ClearTerrainRay();
            f.Ability.OnTick(false);
            Assert.AreEqual(0, f.Game.Cameras.RenderingCamera);
            Assert.IsFalse(f.Game.Cameras.Cameras[70].Active);
        }

        [TestMethod]
        public void RemovingStoredWeaponOrMissingPlayerNeverTouchesInvalidPlayerState()
        {
            var stored = Holding();
            stored.Player.CurrentWeapon = WeaponHash.Pistol;
            stored.Ability.RemoveAbility();
            Assert.IsFalse(stored.Player.Weapons.Contains(WeaponHash.Hammer));
            Assert.IsFalse(stored.Game.Native.Calls.Any(call => call.Hash == Hash.GET_WEAPON_OBJECT_FROM_PED));
            var missing = Holding();
            missing.Player.Exists = false;
            missing.Ability.RemoveAbility();
            Assert.IsFalse(missing.Ability.IsAttachedToPed);
            Assert.IsFalse(missing.Game.Calls.Any(call => call.Name == "ped.maxHealth"));
        }

        [TestMethod]
        public void RemoveAbilityReleasesALiveGrabAndIgnoresAnAlreadyDeletedGrab()
        {
            foreach (bool deleted in new[] { false, true })
            {
                var f = Holding();
                f.Game.AddEntity(60); f.Peds.Add(60);
                f.Game.World.Nearby = (position, radius) => new[] { 60 };
                f.Game.Input.Pressed.Add(Control.ThrowGrenade); f.Ability.OnTick(false);
                f.Game.Observations[60].Exists = !deleted;
                f.Player.Weapons.Clear();
                f.Ability.RemoveAbility();
                Assert.AreEqual(!deleted, f.Game.Calls.Any(call => call.Name == "entity.detach" && (int)call.Arguments[0] == 60));
                StringAssert.Contains(f.Ability.DiagnosticState, "grabbing=False");
            }
        }

        [TestMethod]
        public void HalfPowerMeleeDoesNotSpawnLightningAndOtherHeldWeaponSkipsMeleeForce()
        {
            var f = Holding();
            f.Player.CurrentWeapon = WeaponHash.Pistol;
            f.Player.InMeleeCombat = true;
            f.Game.AddEntity(60).DamagedBy.Add(10); f.Peds.Add(60);
            f.Game.World.Nearby = (position, radius) => new[] { 60 };
            f.Ability.OnTick(false);
            Assert.IsFalse(f.Game.Calls.Any(call => call.Name == "entity.force"));
            f.Player.CurrentWeaponObject = 0;
            f.Ability.OnTick(false);
            var force = f.Game.Calls.Single(call => call.Name == "entity.force");
            Assert.AreEqual(Vector3.UnitY * 125, force.Arguments[1]);
            Assert.AreEqual(50, f.Game.Calls.Single(call => call.Name == "ped.damage").Arguments[1]);
            Assert.IsFalse(f.Game.Native.Calls.Any(call => call.Hash == Hash.START_PARTICLE_FX_NON_LOOPED_ON_ENTITY));
        }

        [TestMethod]
        [DataRow(1f, 0f, 0, "+90")]
        [DataRow(-1f, 0f, 1, "-90")]
        [DataRow(0f, -1f, 2, "+180")]
        public void SideAndBackwardThrowsChooseAnAngledAnimationWhileSprintingRunningOrAirborne(float x, float y, int movement, string suffix)
        {
            var f = Holding();
            f.Game.Cameras.GameplayDirection = new Vector3(x, y, 0);
            f.Player.Sprinting = movement == 0;
            f.Player.Running = movement == 1;
            f.Player.InAir = movement == 2;
            f.Game.Entities.CreatedBlips.Enqueue(50);
            f.Ability.ThrowWeapon();
            var animation = f.LastNative(Hash.TASK_PLAY_ANIM);
            StringAssert.EndsWith((string)animation[2], suffix);
            Assert.AreEqual((int)(AnimationFlags.UpperBodyOnly | AnimationFlags.Secondary), animation[6]);
            Assert.AreEqual(new Vector3(x * 100, y * 100, 5), f.Hammer.Velocity);
        }

        [TestMethod]
        public void RepeatedCatchesReuseLightningTimerAndLightningStopsWhenPowerExpires()
        {
            var f = new HammerFixture();
            f.LooseHammer(Vector3.UnitZ);
            f.ClearPath(); f.Ability.ApplyOn(10);
            f.Game.Host.OnWait = milliseconds => f.HoldHammer();
            for (int i = 0; i < 2; i++)
            {
                f.Player.Weapons.Clear();
                f.Game.World.Capsules.Enqueue(new RayHit(true, 10, Vector3.Zero));
                f.Ability.SummonWeapon();
            }
            Assert.HasCount(16, f.Game.Native.Calls.Where(call => call.Hash == Hash.FORCE_LIGHTNING_FLASH).ToArray());
            Assert.AreEqual(2, f.Audio.Clips["catch-hammer.wav"].Plays);
            f.PrepareHoverObjects(); f.ClearTerrainRay(); f.Ability.OnTick(false);
            f.Player.Weapons.Clear(); f.Player.CurrentWeapon = WeaponHash.Unarmed; f.Player.CurrentWeaponObject = 0;
            f.Game.Host.GameTime = 1; f.ClearTerrainRay(); f.Ability.OnTick(false);
            f.Game.Host.GameTime = 1001; f.ClearTerrainRay(); f.Ability.OnTick(false);
            var glowing = f.Game.Native.Calls.Count(call => call.Hash == Hash.START_PARTICLE_FX_NON_LOOPED_ON_PED_BONE);
            Assert.IsTrue(glowing > 2);
            f.Game.Host.GameTime = 10002; f.ClearTerrainRay(); f.Ability.OnTick(false);
            Assert.AreEqual(glowing, f.Game.Native.Calls.Count(call => call.Hash == Hash.START_PARTICLE_FX_NON_LOOPED_ON_PED_BONE));
        }

        [TestMethod]
        public void WhirlKeyWithoutRopeAndGrabWithoutEligiblePedLeaveOwnershipUnchanged()
        {
            var f = new HammerFixture();
            f.LooseHammer(Vector3.Zero); f.ClearPath(); f.Ability.ApplyOn(10); f.PrepareHoverObjects();
            f.Game.Input.Keys.Add(Keys.Z);
            f.Game.Input.Pressed.Add(Control.ThrowGrenade);
            f.ClearTerrainRay(); f.Ability.OnTick(false);
            StringAssert.Contains(f.Ability.DiagnosticState, "rope=False");
            StringAssert.Contains(f.Ability.DiagnosticState, "grabbing=False");
            f.Game.Input.Pressed.Add(Control.VehicleSubDescend);
            f.Game.AddEntity(60); f.Peds.Add(60);
            f.Game.World.Nearby = (position, radius) => new[] { 60 };
            f.ClearTerrainRay(); f.Ability.OnTick(false);
            Assert.AreEqual(0, f.Game.Entity(60).AttachedTo);
        }
    }
}
