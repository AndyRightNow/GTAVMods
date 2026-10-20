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
        private static HammerFixture Holding(Random random = null)
        {
            var f = new HammerFixture(random);
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
            foreach (string flag in new[] { "flying", "throwFlight", "collecting", "hoverWhirl" })
                StringAssert.Contains(f.Ability.DiagnosticState, flag + "=False");
            f.Player.MaxHealth = 250;
            f.Player.CanRagdoll = false;
            f.Player.DiesOnLowHealth = true;
            f.Player.Weapons.Add(WeaponHash.Parachute);
            f.HoldHammer(); f.PrepareHoverObjects();
            f.Ability.ApplyOn(10); f.Ability.ApplyOn(99);
            Assert.IsTrue(f.Player.CanRagdoll);
            Assert.AreEqual(10, f.Log.Last("ability.attach").Data["ped"]);
            Assert.AreEqual("INFO", f.Log.Last("ability.attach").Level);
            Assert.AreEqual(f.Player.ModelHash, f.Log.Last("ability.attach").Data["model"]);
            Assert.AreEqual(f.Player.Position.ToString(), f.Log.Last("ability.attach").Data["position"]);
            f.Ability.OnTick(false);
            Assert.IsFalse(f.Player.Weapons.Contains(WeaponHash.Parachute));
            Assert.IsTrue(f.Ability.IsHoldingWeapon);
            Assert.AreEqual(30000, f.Player.MaxHealth);
            Assert.IsFalse(f.Player.CanSufferCriticalHits);
            Assert.IsFalse(f.Player.DiesOnLowHealth);
            CollectionAssert.AreEquivalent(new[] { 21, 22 }, f.Game.Calls
                .Where(call => call.Name == "entity.noCollision" && (int)call.Arguments[1] == 10 && (bool)call.Arguments[2])
                .Select(call => (int)call.Arguments[0]).ToArray(),
                "Both hover helpers must avoid colliding with their owner.");
            CollectionAssert.AreEqual(new[] { Vector3.Zero, new Vector3(0, 0, 10) }, f.Game.Native.Calls
                .Where(call => call.Hash == Hash.CREATE_WEAPON_OBJECT)
                .Select(call => new Vector3((float)call.Arguments[2], (float)call.Arguments[3], (float)call.Arguments[4])).ToArray(),
                "The hidden helper starts at the orbit origin and its displayed copy starts at the positive staging altitude.");
            Assert.AreEqual(1.5f, f.LastNative(Hash.SET_PED_MOVE_RATE_OVERRIDE)[1]);
            f.Player.Health = 5000;
            f.Game.Entities.CreatedBlips.Enqueue(50);
            f.Ability.RemoveAbility();
            Assert.AreEqual("INFO", f.Log.Last("ability.remove").Level);
            Assert.AreEqual(10, f.Log.Last("ability.remove").Data["ped"]);
            StringAssert.Contains((string)f.Log.Last("ability.remove").Data["state"], "attached=True");
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
            f.Player.Invincible = f.Player.BulletProof = f.Player.CollisionProof = f.Player.ExplosionProof =
                f.Player.FireProof = f.Player.MeleeProof = f.Player.CanSufferCriticalHits = true;
            f.Player.CurrentWeapon = WeaponHash.Pistol;
            f.Player.CurrentWeaponObject = 0;
            f.Game.Host.GameTime = 1;
            f.Ability.OnTick(false);
            Assert.AreEqual(15100, f.Player.MaxHealth);
            Assert.AreEqual(1.25f, f.LastNative(Hash.SET_PED_MOVE_RATE_OVERRIDE)[1]);
            CollectionAssert.AreEqual(new object[] { f.Game.Host.PlayerHandle, 1.25f }, f.LastNative(Hash.SET_SWIM_MULTIPLIER_FOR_PLAYER));
            Assert.IsFalse(f.Player.BulletProof);
            Assert.IsFalse(f.Player.Invincible || f.Player.CollisionProof || f.Player.ExplosionProof ||
                f.Player.FireProof || f.Player.MeleeProof || f.Player.CanSufferCriticalHits || f.Player.DiesOnLowHealth,
                "Half power permits ordinary damage while preventing critical-hit and low-health instant deaths.");
            CollectionAssert.AreEqual(new object[] { 10, true }, f.LastNative(Hash.SET_ENTITY_CAN_BE_DAMAGED));
            Assert.HasCount(1, f.Game.Native.Calls.Where(call => call.Hash == Hash.SET_SUPER_JUMP_THIS_FRAME).ToArray());
            Assert.AreEqual(0, f.Hammer.WeaponObject);
            f.Game.Input.Keys.Add(Keys.H);
            f.Ability.OnTick(false);
            Assert.AreEqual(WeaponHash.Hammer, f.Player.CurrentWeapon);
            Assert.IsTrue(f.Ability.IsHoldingWeapon);
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
            returning.LooseHammer(new Vector3(0, -3, 0));
            returning.ClearPath();
            returning.Ability.ApplyOn(10);
            Assert.AreEqual(Vector3.UnitY * 35, returning.Hammer.Velocity,
                "Reattachment uses the same gentle final approach as manual recall.");
        }

        [TestMethod]
        [DataRow(3f, false, 35f, true)]
        [DataRow(5f, false, 40f, true)]
        [DataRow(140f, true, 100f, false)]
        [DataRow(150f, false, 100f, false)]
        public void RecallSteersTowardHandAndPlaysApproachSoundOnlyInAudibleBand(float distance, bool audible, float expectedSpeed, bool upright)
        {
            var f = new HammerFixture();
            var hand = new Vector3(17, -23, 11);
            f.Player.Position = hand - Vector3.UnitZ;
            f.Player.Bones[Bone.PHRightHand] = hand;
            var weaponPosition = hand - Vector3.UnitY * distance;
            f.LooseHammer(weaponPosition);
            f.ClearPath(); f.Ability.ApplyOn(10);
            f.Game.AddEntity(60);
            f.Game.World.Capsules.Enqueue(new RayHit(true, 60, weaponPosition));
            f.ClearPath();
            f.Ability.SummonWeapon();
            Assert.AreEqual(Vector3.UnitY * expectedSpeed, f.Hammer.Velocity);
            Assert.IsFalse(f.Game.Native.Calls.Any(call => call.Hash == Hash.GIVE_WEAPON_OBJECT_TO_PED),
                "Touching another ped must not count as contact with the player's hand.");
            CollectionAssert.AreEqual(new object[] { weaponPosition, weaponPosition + Vector3.UnitZ * 0.2f, 0.2f, IntersectFlags.Peds },
                f.Game.Calls.Where(call => call.Name == "world.capsule").ElementAt(1).Arguments);
            Assert.AreEqual(audible, f.Audio.Clips.ContainsKey("hammer-close-to-player.wav"));
            Assert.AreEqual("aim_trans_low", f.LastNative(Hash.TASK_PLAY_ANIM)[2]);
            Assert.AreEqual((int)(AnimationFlags.UpperBodyOnly | AnimationFlags.Secondary), f.LastNative(Hash.TASK_PLAY_ANIM)[6]);
            var summon = f.Log.Last("hammer.summon");
            Assert.AreEqual("INFO", summon.Level); Assert.AreEqual(2000, summon.Interval);
            StringAssert.Contains((string)summon.Data["state"], "holding=False");
            f.ClearPath(); f.ClearPath(); f.Ability.SummonWeapon();
            f.Game.Entity(20).InAir = true;
            for (int frame = 0; frame < 30; frame++) { f.ClearTerrainRay(); f.Hammer.OnTick(); }
            var expectedHead = upright ? Vector3.UnitZ : Vector3.UnitY;
            Assert.IsTrue((f.Game.Entity(20).Quaternion * Vector3.UnitZ - expectedHead).Length() < 0.001f,
                "Recall turns the head upright only within the catching band.");
            if (audible)
            {
                Assert.AreEqual(1, f.Audio.Clips["hammer-close-to-player.wav"].Plays,
                    "A held recall key must not restart the approach clip on every frame.");
                f.Ability.DisposeSounds();
                Assert.AreEqual(1, f.Audio.Clips["hammer-close-to-player.wav"].Disposals);
            }
        }

        [TestMethod]
        public void CatchTransfersWeaponPlaysAudioAndTriggersLightningAfterTheEngineWait()
        {
            var f = new HammerFixture();
            f.LooseHammer(new Vector3(0, -30, 1));
            f.ClearPath(); f.Ability.ApplyOn(10);
            f.ClearPath(); f.ClearPath(); f.Ability.SummonWeapon();
            Assert.IsTrue(f.Audio.Clips["hammer-close-to-player.wav"].IsPlaying);
            f.Game.Entity(20).Position = Vector3.UnitZ;
            f.Game.World.Capsules.Enqueue(new RayHit(true, 10, Vector3.UnitZ));
            f.Game.Host.OnWait = milliseconds => { Assert.AreEqual(1, milliseconds); f.HoldHammer(); };
            f.Ability.SummonWeapon();
            CollectionAssert.AreEqual(new object[] { 20, 10 }, f.LastNative(Hash.GIVE_WEAPON_OBJECT_TO_PED));
            Assert.AreEqual(1, f.Audio.Clips["catch-hammer.wav"].Plays);
            Assert.IsFalse(f.Audio.Clips["hammer-close-to-player.wav"].IsPlaying,
                "The catch must stop the still-playing approach sound.");
            Assert.HasCount(8, f.Game.Native.Calls.Where(call => call.Hash == Hash.FORCE_LIGHTNING_FLASH).ToArray());
            Assert.IsTrue(f.Ability.IsHoldingWeapon);
            var catchAnimation = f.LastNative(Hash.TASK_PLAY_ANIM);
            Assert.AreEqual("melee_damage_back", catchAnimation[2]);
            Assert.AreEqual(250, catchAnimation[5]);
            Assert.AreEqual((int)(AnimationFlags.UpperBodyOnly | AnimationFlags.Secondary), catchAnimation[6]);
            var bones = f.Game.Native.Calls.Where(call => call.Hash == Hash.START_PARTICLE_FX_NON_LOOPED_ON_PED_BONE).ToArray();
            CollectionAssert.AreEquivalent(new[] { (int)Bone.PHRightHand, (int)Bone.SkelLeftForearm, (int)Bone.SkelRightForearm,
                (int)Bone.SkelLeftThigh, (int)Bone.SkelRightThigh }, bones.Select(call => (int)call.Arguments[8]).ToArray());
            Assert.AreEqual(0.8f, bones.Single(call => (int)call.Arguments[8] == (int)Bone.PHRightHand).Arguments[9]);
            Assert.AreEqual(0.5f, f.Game.Native.Calls.Last(call => call.Hash == Hash.START_PARTICLE_FX_NON_LOOPED_ON_ENTITY).Arguments[8]);
            CollectionAssert.AreEqual(new object[] { CameraShake.LargeExplosion, 0.01f },
                f.Game.Calls.Last(call => call.Name == "camera.shake").Arguments);
            CollectionAssert.AreEqual(new object[] { 10, true }, f.LastNative(Hash.DISABLE_PED_PAIN_AUDIO));
            var requested = f.Log.Last("hammer.catchRequested");
            Assert.AreEqual("INFO", requested.Level); Assert.AreEqual(2000, requested.Interval);
            Assert.AreEqual(10, requested.Data["ped"]); Assert.AreEqual(0, requested.Data["gameTime"]);
            var caught = f.Log.Last("hammer.caught");
            Assert.AreEqual("INFO", caught.Level); Assert.AreEqual(10, caught.Data["ped"]);
            Assert.AreEqual(f.Game.Host.GameTime, caught.Data["gameTime"]);
            StringAssert.Contains(f.Ability.DiagnosticState, "return=False");
            f.PrepareHoverObjects();
            f.Game.Host.GameTime = 1000; f.ClearTerrainRay();
            f.Ability.OnTick(false);
            Assert.HasCount(9, f.Game.Native.Calls.Where(call => call.Hash == Hash.START_PARTICLE_FX_NON_LOOPED_ON_PED_BONE).ToArray());
            f.Game.Host.GameTime = 2000; f.ClearTerrainRay(); f.Ability.OnTick(false);
            var hoverFx = f.Game.Native.Calls.Where(call => call.Hash == Hash.START_PARTICLE_FX_NON_LOOPED_ON_ENTITY &&
                (int)call.Arguments[1] == 22).ToArray();
            Assert.HasCount(1, hoverFx);
            Assert.AreEqual(0.5f, hoverFx[0].Arguments[8]);
            f.Player.Weapons.Clear(); f.Player.CurrentWeapon = WeaponHash.Unarmed; f.Player.CurrentWeaponObject = 0;
            f.Game.Host.GameTime = 3000; f.ClearTerrainRay(); f.Ability.OnTick(false);
            Assert.HasCount(1, f.Game.Native.Calls.Where(call => call.Hash == Hash.START_PARTICLE_FX_NON_LOOPED_ON_ENTITY &&
                (int)call.Arguments[1] == 22).ToArray(), "A loose hammer must not light the unused hover helper.");
            Assert.IsFalse(f.Game.Native.Calls.Any(call => call.Hash == Hash.START_PARTICLE_FX_NON_LOOPED_ON_ENTITY &&
                (int)call.Arguments[1] == 0), "The initial catch occurs before hover allocation and cannot target handle zero.");
        }

        [TestMethod]
        public void ThrowAddsLaunchVelocityInCameraDirectionAndHonorsWalkingAnimationFlags()
        {
            var f = Holding();
            f.Player.Walking = true;
            f.Game.Native.Returns(Hash.IS_ENTITY_PLAYING_ANIM, true);
            f.Game.Entity(20).Velocity = Vector3.UnitX * 3;
            f.Game.Entities.CreatedBlips.Enqueue(50);
            f.Ability.ThrowWeapon();
            Assert.AreEqual(new Vector3(3, 100, 5), f.Hammer.Velocity);
            Assert.AreEqual(WeaponHash.Unarmed, f.Player.CurrentWeapon);
            CollectionAssert.AreEqual(new object[] { 10, WeaponHash.Unarmed, true },
                f.Game.Calls.Single(call => call.Name == "weapon.select").Arguments,
                "Release explicitly requests immediate unarmed selection instead of assuming removal equips it.");
            Assert.AreEqual((int)(AnimationFlags.UpperBodyOnly | AnimationFlags.Secondary), f.LastNative(Hash.TASK_PLAY_ANIM)[6]);
            Assert.AreEqual(-1, f.LastNative(Hash.TASK_PLAY_ANIM)[5]);
            Assert.IsTrue(f.Game.Host.GameTime > 0, "Release waits for the configured throw animation wind-up.");
            Assert.AreEqual("INFO", f.Log.Last("hammer.throw").Level);
            StringAssert.Contains((string)f.Log.Last("hammer.throw").Data["state"], "holding=True");
            var release = f.Log.Last("hammer.release");
            Assert.AreEqual("INFO", release.Level); Assert.AreEqual(true, release.Data["initialVelocity"]);
            StringAssert.Contains((string)release.Data["state"], "holding=True");
            f.Game.AddEntity(60).Position = new Vector3(0, 20, 0);
            f.Ability.ThrowWeapon(); f.Ability.ThrowAndFlyWithWeapon(); f.Ability.ThrowWeapon(new HashSet<int> { 60 });
            Assert.HasCount(1, f.Game.Native.Calls.Where(call => call.Hash == Hash.GET_WEAPON_OBJECT_FROM_PED).ToArray());
            Assert.HasCount(1, f.Game.Native.Calls.Where(call => call.Hash == Hash.TASK_PLAY_ANIM).ToArray(),
                "Repeated throw requests without the held hammer must not start more animations.");
            Assert.HasCount(1, f.Game.Calls.Where(call => call.Name == "weapon.select").ToArray());
        }

        [TestMethod]
        public void EmptyTargetThrowUsesFreeThrowWhileTargetedThrowStartsWithoutInitialVelocity()
        {
            var f = Holding();
            f.Game.Entities.CreatedBlips.Enqueue(50);
            f.Ability.ThrowWeapon(new HashSet<int>());
            Assert.AreEqual(new Vector3(0, 100, 5), f.Hammer.Velocity);
            Assert.AreEqual((int)AnimationFlags.None, f.LastNative(Hash.TASK_PLAY_ANIM)[6],
                "A stationary throw may use the full body animation.");
            var targeted = Holding(new FixedRandom(choice: 0));
            targeted.Player.Position = new Vector3(-20, 30, 5);
            targeted.Game.AddEntity(60).Position = new Vector3(-10, 30, 5);
            targeted.Game.Entities.CreatedBlips.Enqueue(50);
            targeted.Ability.ThrowWeapon(new HashSet<int> { 60 });
            Assert.AreEqual(Vector3.Zero, targeted.Hammer.Velocity);
            StringAssert.EndsWith((string)targeted.LastNative(Hash.TASK_PLAY_ANIM)[2], "+90",
                "Targeted animation faces the target relative to the player, independently of the world origin.");
            StringAssert.Contains(targeted.Ability.DiagnosticState, "attacking=True");
            var targets = targeted.Log.Last("hammer.throwTargets");
            Assert.AreEqual("INFO", targets.Level); Assert.AreEqual(1, targets.Data["targets"]);
            StringAssert.Contains((string)targets.Data["state"], "holding=True");
        }

        [TestMethod]
        public void FlightUsesUpForwardAndSprintInputsAndRestoresHeldWeaponWhenStopped()
        {
            var f = Holding();
            f.Game.Input.Keys.Add(Keys.X);
            f.Player.CanRagdoll = false;
            f.Ability.OnTick(false);
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
            f.Ability.OnTick(false);
            Assert.AreEqual(Vector3.UnitY * 350, f.Game.Entity(20).Velocity);
            Assert.AreEqual(255, f.Game.Entity(20).Opacity);
            Assert.AreEqual(0, f.Game.Entity(22).Opacity);
            Assert.AreEqual(orbit, f.Game.Entity(21).Position,
                "Switching from hovering to forward flight must stop the hidden orbit.");
            StringAssert.Contains(f.Ability.DiagnosticState, "hoverWhirl=False");
            f.Game.Input.Keys.Clear(); f.Game.Input.Pressed.Clear();
            f.Ability.OnTick(false);
            StringAssert.Contains(f.Ability.DiagnosticState, "flying=False");
            Assert.IsFalse(f.Player.CanRagdoll);
        }

        [TestMethod]
        public void LeavingFlightCarriesObservedMomentumOnceAndHoveringFollowsTheHand()
        {
            var f = Holding();
            f.Ability.OnTick(false);
            var hand = new Vector3(17, -23, 11);
            f.Player.Bones[Bone.PHRightHand] = hand;
            f.Game.Entity(21).Position = Vector3.UnitX * 0.3f;
            f.Game.Entity(21).Rotation = new Vector3(-90, 0, -90);
            f.Player.InAir = true;
            f.Game.Input.Keys.Add(Keys.X);
            f.Ability.OnTick(false);
            Assert.AreEqual(hand + new Vector3(0.3f, 0, 0.1f), f.Game.Entity(22).Position);
            Assert.AreEqual(new Vector3(-90, 0, -90), f.Game.Entity(22).Rotation);
            var orbitPosition = f.Game.Entity(21).Position;
            Assert.AreEqual(0.3f, orbitPosition.Length(), 0.0001f);
            Assert.AreNotEqual(Vector3.UnitX * 0.3f, orbitPosition, "The hidden helper advances the visible hammer's orbit.");

            f.Player.Velocity = new Vector3(3, -4, 2);
            f.Game.Input.Keys.Clear();
            f.Ability.OnTick(false);
            Assert.AreEqual(new Vector3(6, -8, 4), f.Player.Velocity,
                "On leaving flight, the current observed momentum is added once.");
            Assert.AreEqual(255, f.Game.Entity(20).Opacity);
            Assert.AreEqual(0, f.Game.Entity(22).Opacity);
            Assert.AreEqual(orbitPosition, f.Game.Entity(21).Position, "An idle frame does not continue the hover orbit.");
            f.Ability.OnTick(false);
            Assert.AreEqual(new Vector3(6, -8, 4), f.Player.Velocity, "Further idle frames must not repeatedly add momentum.");

            f.Game.Input.Keys.Add(Keys.W);
            f.Ability.OnTick(false);
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
            f.Ability.OnTick(false);
            Assert.AreEqual(hover ? 0 : 255, f.Game.Entity(20).Opacity);
            Assert.AreEqual(hover ? 255 : 0, f.Game.Entity(22).Opacity);
            StringAssert.Contains(f.Ability.DiagnosticState, "hoverWhirl=" + hover);
        }

        [TestMethod]
        public void FlightPushesOnlyEntitiesTouchingPlayerOrHeldHammer()
        {
            var f = Holding(); f.Ability.OnTick(false);
            foreach (int id in new[] { 60, 61, 62 }) f.Game.AddEntity(id);
            f.Player.Touching.Add(60); f.Game.Entity(20).Touching.Add(61);
            f.Player.Touching.Add(10); f.Player.Touching.Add(20);
            f.Game.World.Nearby = (position, radius) => new[] { 10, 20, 60, 61, 62 };
            f.Player.InAir = true; f.Game.Input.Pressed.Add(Control.ScriptPadUp);
            f.Ability.OnTick(false);
            CollectionAssert.AreEqual(new[] { 60, 61 }, f.Game.Calls.Where(call => call.Name == "entity.force").Select(call => (int)call.Arguments[0]).ToArray());
        }

        [TestMethod]
        public void AirDashSlamsDownThenDamagesNearbyEntitiesOnLanding()
        {
            var f = Holding(); f.Ability.OnTick(false);
            f.Player.Position = new Vector3(17, -23, 11);
            f.Player.InAir = true; f.Game.Input.Pressed.Add(Control.Attack);
            f.Ability.OnTick(false);
            Assert.AreEqual(new Vector3(0, 0, -200), f.Player.Velocity);
            f.Ability.OnTick(false);
            Assert.AreEqual(new Vector3(0, 0, -200), f.Player.Velocity);
            Assert.IsFalse(f.Game.Calls.Any(call => call.Name == "ped.damage"));
            f.Game.AddEntity(60).Position = f.Player.Position + Vector3.UnitX; f.Peds.Add(60);
            f.Game.AddEntity(61).Position = f.Player.Position + Vector3.UnitY;
            f.Game.World.Nearby = (position, radius) => new[] { 10, 20, 60, 61 };
            f.Player.InAir = false; f.Game.Input.Pressed.Clear();
            f.Ability.OnTick(false);
            Assert.AreEqual(100, f.Game.Calls.Single(call => call.Name == "ped.damage").Arguments[1]);
            Assert.HasCount(2, f.Game.Calls.Where(call => call.Name == "entity.force").ToArray());
            CollectionAssert.AreEqual(new object[] { 60, Vector3.UnitX * 250 },
                f.Game.Calls.Single(call => call.Name == "entity.force" && (int)call.Arguments[0] == 60).Arguments);
            CollectionAssert.AreEqual(new object[] { 61, Vector3.UnitY * 250 },
                f.Game.Calls.Single(call => call.Name == "entity.force" && (int)call.Arguments[0] == 61).Arguments);
            CollectionAssert.AreEquivalent(new[] { 60, 61 }, f.Game.Native.Calls
                .Where(call => call.Hash == Hash.START_PARTICLE_FX_NON_LOOPED_ON_ENTITY)
                .Select(call => (int)call.Arguments[1]).ToArray());
            StringAssert.Contains(f.Ability.DiagnosticState, "airDash=False");
            f.Game.Input.Pressed.Add(Control.Attack);
            f.Ability.OnTick(false);
            Assert.HasCount(1, f.Game.Calls.Where(call => call.Name == "ped.damage").ToArray(),
                "Grounded attack input does not start another aerial landing strike.");
        }

        [TestMethod]
        public void MeleeDamageRequiresAHitAndIsSuppressedInsideVehicle()
        {
            var f = Holding();
            f.Game.AddEntity(60).DamagedBy.Add(10); f.Peds.Add(60);
            f.Game.AddEntity(61); f.Peds.Add(61);
            f.Game.World.Nearby = (position, radius) => new[] { 10, 20, 60, 61 };
            f.Player.InMeleeCombat = true; f.Player.InVehicle = true;
            f.Ability.OnTick(false);
            Assert.IsFalse(f.Game.Calls.Any(call => call.Name == "entity.force"));
            Assert.IsFalse(f.Game.Calls.Any(call => call.Name == "ped.damage"),
                "Being in a vehicle must suppress melee damage as well as knockback.");
            f.Player.InVehicle = false;
            f.Ability.OnTick(false);
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
            f.Game.AddEntity(60); f.Game.AddEntity(61); f.Peds.Add(61);
            f.Game.AddEntity(62); f.Peds.Add(62);
            f.Game.World.Nearby = (position, radius) => new[] { 10, 60, 61, 62 };
            f.Game.Input.Pressed.Add(Control.ThrowGrenade);
            f.Ability.OnTick(false);
            Assert.AreEqual(10, f.Game.Entity(61).AttachedTo);
            Assert.AreEqual(Bone.IKLeftHand, f.Game.Entity(61).AttachedBone);
            CollectionAssert.AreEqual(new object[] { 61 }, f.Game.Calls.Single(call => call.Name == "ped.clearTasks").Arguments);
            Assert.AreEqual(0, f.Game.Entity(62).AttachedTo);
            f.Ability.OnTick(false);
            Assert.HasCount(1, f.Game.Calls.Where(call => call.Name == "entity.attach").ToArray());
            f.Game.Input.Pressed.Clear();
            f.Game.Input.Pressed.Add(Control.VehicleSubDescend);
            f.Ability.OnTick(false);
            Assert.AreEqual(10, f.Game.Entity(61).AttachedTo, "The release modifier alone must not drop a grabbed ped.");
            f.Game.Input.Pressed.Add(Control.ThrowGrenade);
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
        public void RemovalClearsAirDashAndMomentumBeforeReapplyingToAnotherCharacter()
        {
            var f = Holding();
            f.Player.InAir = true; f.Game.Input.Keys.Add(Keys.W); f.Game.Input.Pressed.Add(Control.Attack);
            f.Game.AddEntity(60).Position = Vector3.UnitY * 50; f.Peds.Add(60);
            f.Game.Input.Pressed.Add(Control.Aim);
            f.Game.World.Rays.Enqueue(new RayHit(true, 60, Vector3.Zero));
            f.Ability.OnTick(false);
            StringAssert.Contains(f.Ability.DiagnosticState, "airDash=True");
            StringAssert.Contains(f.Ability.DiagnosticState, "targets=1");
            f.Game.Entities.CreatedBlips.Enqueue(50);
            f.Ability.RemoveAbility();
            foreach (string flag in new[] { "flying", "throwFlight", "attacking", "collecting", "hoverWhirl" })
                StringAssert.Contains(f.Ability.DiagnosticState, flag + "=False");
            StringAssert.Contains(f.Ability.DiagnosticState, "targets=0");
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
            f.Player.CanRagdoll = false;
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
            f.Game.Cameras.GameplayPosition = new Vector3(17, -23, 11);
            f.Game.Cameras.GameplayDirection = Vector3.UnitX;
            f.Game.AddEntity(60);
            foreach (var hit in new[] { new RayHit(false, 0, Vector3.Zero), new RayHit(true, 0, Vector3.Zero),
                new RayHit(true, 10, Vector3.Zero), new RayHit(true, 60, Vector3.Zero) })
            {
                f.Game.World.Rays.Enqueue(hit);
                f.Ability.OnTick(false);
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
                f.Ability.OnTick(false);
            }
            StringAssert.Contains(f.Ability.DiagnosticState, "targets=15");
            Assert.IsFalse(f.Game.Calls.Where(call => call.Name == "world.marker")
                .Any(call => (Vector3)call.Arguments[1] == new Vector3(76, 0, 2)));
            var firstMarker = f.Game.Calls.First(call => call.Name == "world.marker").Arguments;
            Assert.AreEqual(new Vector3(61, 0, 2), firstMarker[1]);
            Assert.AreEqual(System.Drawing.Color.Red, firstMarker[5]);
            f.Game.Input.Pressed.Clear();
            f.Ability.OnTick(false);
            StringAssert.Contains(f.Ability.DiagnosticState, "targets=15",
                "An unpressed sample without a release edge does not discard the active selection.");
            f.Game.Input.Released.Add(Control.Aim);
            var before = f.Game.Calls.Count(call => call.Name == "world.marker");
            f.Ability.OnTick(false); f.Ability.OnTick(false);
            Assert.AreEqual(before, f.Game.Calls.Count(call => call.Name == "world.marker"));
            StringAssert.Contains(f.Ability.DiagnosticState, "targets=0");
            StringAssert.Contains(f.Ability.DiagnosticState, "collecting=False");
        }

        [TestMethod]
        public void ManualRecallCancelsTargetAttackAndPickupResetsTheLaterLooseHammer()
        {
            var f = Holding();
            f.Game.World.RaycastObservation = (from, to, flags, ignore) => new RayHit(false, 0, Vector3.Zero);
            f.Game.World.CapsuleObservation = (from, to, radius, flags) => new RayHit(false, 0, Vector3.Zero);
            f.Ability.OnTick(false);
            f.Game.AddEntity(60).Position = new Vector3(0, 30, 0); f.Peds.Add(60);
            f.Game.Input.Pressed.Add(Control.Aim); f.Game.Input.Keys.Add(Keys.T);
            f.Game.World.Rays.Enqueue(new RayHit(true, 60, Vector3.Zero));
            f.Game.Entities.CreatedBlips.Enqueue(50);
            f.Ability.OnTick(false);
            StringAssert.Contains(f.Ability.DiagnosticState, "targets=1");
            StringAssert.Contains(f.Ability.DiagnosticState, "attacking=True");

            f.Game.Input.Pressed.Clear(); f.Game.Input.Keys.Clear(); f.Game.Input.Keys.Add(Keys.H);
            f.Game.Entity(20).Position = new Vector3(0, -3, 1);
            f.Game.Entity(20).InAir = true;
            f.Ability.OnTick(false);
            StringAssert.Contains(f.Ability.DiagnosticState, "targets=0");
            StringAssert.Contains(f.Ability.DiagnosticState, "attacking=False");
            for (int frame = 0; frame < 30; frame++) f.Hammer.OnTick();
            Assert.IsTrue((f.Game.Entity(20).Quaternion * Vector3.UnitZ - Vector3.UnitZ).Length() < 0.001f);

            f.Game.Input.Keys.Clear();
            f.HoldHammer(); // The engine reports an external pickup.
            for (int frame = 0; frame < 30; frame++) f.Ability.OnTick(false);
            Assert.IsTrue((f.Game.Entity(20).Quaternion * Vector3.UnitZ - Vector3.UnitY).Length() < 0.001f,
                "An observed pickup ends the previous near-hand recall orientation.");
            f.Game.Entity(20).Velocity = Vector3.Zero;
            f.Game.Entity(20).HeightAboveGround = 51;
            f.Game.Input.Keys.Add(Keys.Y);
            f.Game.Host.GameTime = 1000; f.Ability.OnTick(false);
            f.Game.Input.Keys.Clear();
            f.Game.Host.GameTime = 2000; f.Ability.OnTick(false);
            Assert.IsTrue(f.Hammer.Velocity.Length() > 0,
                "A later dropped hammer may perform lost-terrain recovery instead of remaining marked as summoned.");
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
            f.Game.Host.GameTime = 1; f.Ability.OnTick(false);
            f.Game.Host.GameTime = 1001;
            f.Ability.OnTick(false);
            Assert.IsTrue(f.Hammer.IsMoving);
            Assert.AreEqual(30000, f.Player.MaxHealth);
            Assert.AreEqual(1, f.Audio.Clips["whirling-hammer.wav"].Plays);
            Assert.AreEqual("dodge_generic_centre", f.LastNative(Hash.TASK_PLAY_ANIM)[2]);
            Assert.AreEqual((int)(AnimationFlags.UpperBodyOnly | AnimationFlags.Secondary), f.LastNative(Hash.TASK_PLAY_ANIM)[6]);
            var particlesBefore = f.Game.Native.Calls.Count(call => call.Hash == Hash.START_PARTICLE_FX_NON_LOOPED_AT_COORD);
            f.Game.Host.GameTime = 12000; f.Ability.OnTick(false);
            Assert.AreEqual(particlesBefore, f.Game.Native.Calls.Count(call => call.Hash == Hash.START_PARTICLE_FX_NON_LOOPED_AT_COORD));
            Assert.AreEqual(30000, f.Player.MaxHealth, "A rope remains a full-power source beyond the ordinary loss grace period.");
            f.Game.Input.Keys.Clear();
            f.ClearPath(); f.ClearPath(); f.ClearTerrainRay();
            f.Ability.OnTick(false);
            Assert.IsEmpty(f.Game.Ropes.Ropes);
            Assert.IsFalse(f.Game.Observations[30].Exists);
            StringAssert.Contains(f.Ability.DiagnosticState, "rope=False");
            f.Game.Host.GameTime = 12001; f.ClearTerrainRay(); f.Ability.OnTick(false);
            Assert.AreEqual(30000, f.Player.MaxHealth, "The loss clock starts when the last active power source is released.");
            f.Game.Host.GameTime = 17001; f.ClearTerrainRay(); f.Ability.OnTick(false);
            Assert.AreEqual(15100, f.Player.MaxHealth);
        }

        [TestMethod]
        public void RecallKeyForLooseHammerAndFollowCameraUsePublicInputWorkflow()
        {
            var f = new HammerFixture();
            f.LooseHammer(new Vector3(0, -30, 1));
            f.ClearPath(); f.Ability.ApplyOn(10); f.PrepareHoverObjects();
            f.Game.AddEntity(60).Position = new Vector3(0.1f, -30, 1); f.Peds.Add(60);
            f.Game.World.Nearby = (position, radius) => new[] { 60 };
            f.Game.Input.Keys.Add(Keys.H); f.Game.Input.Keys.Add(Keys.R);
            f.Game.Cameras.Creations.Enqueue(70);
            f.ClearPath(); f.ClearPath(); f.ClearTerrainRay();
            f.Ability.OnTick(false);
            Assert.AreEqual(1, f.Audio.Clips["hammer-close-to-player.wav"].Plays);
            Assert.IsTrue(f.Game.Cameras.Cameras[70].Active);
            Assert.IsTrue(f.Game.Native.Calls.Any(call => call.Hash == Hash.START_PARTICLE_FX_NON_LOOPED_AT_COORD));
            CollectionAssert.AreEqual(new object[] { 60, Vector3.UnitY * 100 },
                f.Game.Calls.Single(call => call.Name == "entity.force").Arguments);
            CollectionAssert.AreEqual(new object[] { 60, 100 },
                f.Game.Calls.Single(call => call.Name == "ped.damage").Arguments);
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
        [DataRow(false)]
        [DataRow(true)]
        public void RemoveAbilityReleasesALiveGrabAndIgnoresAnAlreadyDeletedGrab(bool deleted)
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
        [DataRow(1f, 1f, 0, "+90")]
        public void SideAndBackwardThrowsChooseAnAngledAnimationWhileSprintingRunningOrAirborne(float x, float y, int movement, string suffix)
        {
            // First unfiltered choice lacks directional variants; the gameplay
            // selection must choose from the angle-capable animations.
            var f = Holding(new FixedRandom(choice: 0));
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
                f.Game.Host.GameTime = i * 500;
                f.Player.Weapons.Clear();
                f.Game.World.Capsules.Enqueue(new RayHit(true, 10, Vector3.Zero));
                f.Ability.SummonWeapon();
            }
            Assert.HasCount(16, f.Game.Native.Calls.Where(call => call.Hash == Hash.FORCE_LIGHTNING_FLASH).ToArray());
            Assert.AreEqual(2, f.Audio.Clips["catch-hammer.wav"].Plays);
            Func<int> boneEffects = () => f.Game.Native.Calls.Count(call => call.Hash == Hash.START_PARTICLE_FX_NON_LOOPED_ON_PED_BONE);
            Assert.AreEqual(10, boneEffects(), "Each catch emits one hand effect and four body effects.");
            f.PrepareHoverObjects(); f.ClearTerrainRay(); f.Ability.OnTick(false);
            f.Player.Weapons.Clear(); f.Player.CurrentWeapon = WeaponHash.Unarmed; f.Player.CurrentWeaponObject = 0;
            f.Game.Host.GameTime = 501; f.ClearTerrainRay(); f.Ability.OnTick(false);
            f.Game.Host.GameTime = 999; f.ClearTerrainRay(); f.Ability.OnTick(false);
            Assert.AreEqual(10, boneEffects(), "The original one-second interval has not elapsed yet.");
            f.Game.Host.GameTime = 1000; f.ClearTerrainRay(); f.Ability.OnTick(false);
            Assert.AreEqual(14, boneEffects(),
                "The second catch must preserve the first catch's timer instead of postponing the next four body effects to 1500 ms.");
            f.ClearTerrainRay(); f.Ability.OnTick(false);
            Assert.AreEqual(14, boneEffects(), "Repeating a frame timestamp must not fire the effect twice.");
            f.Game.Host.GameTime = 2000; f.ClearTerrainRay(); f.Ability.OnTick(false);
            Assert.AreEqual(18, boneEffects(), "One additional interval emits exactly one set of four body effects.");
            f.Game.Host.GameTime = 10502; f.ClearTerrainRay(); f.Ability.OnTick(false);
            Assert.AreEqual(18, boneEffects(), "Expired power stops the periodic body effects.");
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
