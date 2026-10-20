using System;
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
    public class PlayerHammerIntegrationTests
    {
        [TestMethod]
        public void HoldingRecallOnTheFirstUnarmedFrameCreatesAHammerBeforeSteeringIt()
        {
            var s = new PlayerSessionFixture(holding: false);
            s.Frame(0, Keys.H);
            CollectionAssert.AreEqual(new[] { 21, 22, 23 }, s.CreatedWeapons);
            Assert.AreEqual(new Vector3(0, 0, 2000), s.Game.Entity(23).Position);
            Assert.AreEqual(Vector3.Zero, s.Game.Entity(23).Velocity);
            Assert.IsFalse(s.Game.Calls.Any(call => call.Name == "entity.velocity" &&
                (int)call.Arguments[0] == 0));

            s.Game.Entity(23).Position = new Vector3(0, -30, 1);
            s.Frame(0, Keys.H);
            Assert.AreEqual(Vector3.UnitY * 100, s.Game.Entity(23).Velocity);
            Assert.HasCount(3, s.CreatedWeapons);
            s.Reload();
            s.AssertReleased();
        }

        [TestMethod]
        public void PressingWhirlWithoutAHeldHammerLeavesTheLooseHammerUnattached()
        {
            var s = new PlayerSessionFixture(holding: false);
            s.Frame(0, Keys.Z);
            Assert.HasCount(3, s.CreatedWeapons);
            Assert.IsEmpty(s.Game.Ropes.Ropes);
            Assert.IsFalse(s.Game.Calls.Any(call => call.Name == "rope.create" || call.Name == "rope.connect"));
            Assert.IsFalse(s.Audio.Clips.ContainsKey("whirling-hammer.wav"));
            Assert.AreEqual(Vector3.Zero, s.Game.Entity(23).Velocity);
            StringAssert.Contains(s.State(), "rope=False");
            s.Reload();
            s.AssertReleased();
        }

        [TestMethod]
        public void AStoredCatchWaitsForWeaponSelectionWithoutRequestingAnotherTransfer()
        {
            var s = new PlayerSessionFixture();
            s.DropHammer(Vector3.UnitZ);
            s.Game.World.Capsules.Enqueue(new RayHit(true, 10, Vector3.UnitZ));
            s.Frame(0, Keys.H);
            Assert.HasCount(1, s.Game.Native.Calls.Where(call => call.Hash == Hash.GIVE_WEAPON_OBJECT_TO_PED).ToArray());
            StringAssert.Contains(s.State(), "return=True");

            s.Game.ApplyWeaponObservation(10, WeaponHash.Pistol, 0);
            s.Player.Weapons.Add(WeaponHash.Hammer);
            s.Game.Calls.Clear();
            s.Frame(0);
            Assert.AreEqual(WeaponHash.Pistol, s.Player.CurrentWeapon);
            Assert.IsFalse(s.Game.Calls.Any(call => call.Name == "entity.velocity" &&
                (int)call.Arguments[0] == 20));
            Assert.HasCount(1, s.Game.Native.Calls.Where(call => call.Hash == Hash.GIVE_WEAPON_OBJECT_TO_PED).ToArray());
            StringAssert.Contains(s.State(), "return=True");

            s.Frame(0, Keys.H);
            Assert.IsTrue(s.Game.Calls.Any(call => call.Name == "weapon.select" &&
                (WeaponHash)call.Arguments[1] == WeaponHash.Hammer));
            s.ObserveHeldHammer();
            s.Frame(0);
            StringAssert.Contains(s.State(), "return=False");
            Assert.AreEqual(WeaponHash.Hammer, s.Player.CurrentWeapon);
        }

        [TestMethod]
        [DataRow(3f, 35f, false, true)]
        [DataRow(3.01f, 40f, false, true)]
        [DataRow(5f, 40f, false, true)]
        [DataRow(10f, 40f, true, false)]
        [DataRow(10.01f, 100f, true, false)]
        [DataRow(140f, 100f, true, false)]
        [DataRow(140.01f, 100f, false, false)]
        public void HoldingRecallSteersTowardTheHandAndUsesTheCorrectApproachBand(float distance, float speed, bool audible, bool upright)
        {
            var s = new PlayerSessionFixture();
            var hand = new Vector3(17, -23, 11);
            s.Player.Bones[Bone.PHRightHand] = hand;
            s.DropHammer(hand - Vector3.UnitY * distance);
            s.Game.Entity(20).InAir = true;
            s.Game.AddEntity(60);
            s.Game.World.Capsules.Enqueue(new RayHit(true, 60, hand));
            s.Frame(0, Keys.H);
            Assert.AreEqual(Vector3.UnitY * speed, s.Game.Entity(20).Velocity);
            Assert.IsFalse(s.Game.Native.Calls.Any(call => call.Hash == Hash.GIVE_WEAPON_OBJECT_TO_PED),
                "Contact with another pedestrian does not complete the player's catch.");
            Assert.AreEqual(audible, s.Audio.Clips.ContainsKey("hammer-close-to-player.wav"));
            Assert.AreEqual("aim_trans_low", s.LastNative(Hash.TASK_PLAY_ANIM)[2]);
            for (int frame = 0; frame < 30; frame++) s.Frame(0, Keys.H);
            Assert.IsTrue((s.Game.Entity(20).Quaternion * Vector3.UnitZ -
                (upright ? Vector3.UnitZ : Vector3.UnitY)).Length() < 0.001f);
            if (audible) Assert.AreEqual(1, s.Audio.Clips["hammer-close-to-player.wav"].Plays);
            s.Reload();
            s.AssertReleased();
        }

        [TestMethod]
        [DataRow(false, false, false)]
        [DataRow(true, false, false)]
        [DataRow(false, true, false)]
        [DataRow(false, false, true)]
        public void RecallUsesTheNavigationHelperOnlyWhenTheNearbyObstructionEnclosesTheHammer(bool x, bool y, bool z)
        {
            var s = new PlayerSessionFixture();
            var center = new Vector3(12, -8, 30);
            s.Player.Bones[Bone.PHRightHand] = center + Vector3.UnitY * 30;
            s.DropHammer(center);
            s.Game.World.CapsuleObservation = (from, to, radius, flags) =>
            {
                if (flags == IntersectFlags.Peds) return new RayHit(false, 0, Vector3.Zero);
                if (radius == 0.1f) return new RayHit(true, 0, center + Vector3.UnitY * 20);
                var axis = to - from;
                return new RayHit(axis.X != 0 ? x : axis.Y != 0 ? y : z, 0, Vector3.Zero);
            };
            s.Frame(0, Keys.H);
            Assert.AreEqual(x || y || z, s.Game.Calls.Any(call => call.Name == "ped.missionPosition"));
            Assert.AreEqual(x || y || z ? Vector3.Zero : Vector3.UnitY * 100, s.Game.Entity(20).Velocity);
            var probes = s.Game.Calls.Where(call => call.Name == "world.capsule" && (float)call.Arguments[2] == 10).ToArray();
            Assert.HasCount(3, probes);
            for (int i = 0; i < 3; i++)
            {
                var axis = new[] { Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ }[i];
                CollectionAssert.AreEqual(new object[] { center - axis * 10, center + axis * 10, 10f, IntersectFlags.Map }, probes[i].Arguments);
            }
            s.Game.Entity(40).Position = center + new Vector3(4, 2, 0);
            s.Game.Entity(40).Velocity = new Vector3(1, 2, 3);
            s.Frame(0, Keys.H);
            if (x || y || z) Assert.AreEqual(new Vector3(3, 3, 3), s.Game.Entity(20).Velocity);
            s.Game.World.CapsuleObservation = (from, to, radius, flags) => new RayHit(false, 0, Vector3.Zero);
            s.Frame(0, Keys.H);
            Assert.AreEqual(Vector3.UnitY * 100, s.Game.Entity(20).Velocity);
        }

        [TestMethod]
        public void AThrownHammerDamagesBystandersButPreservesItsOwnerAndNavigationHelpers()
        {
            var s = new PlayerSessionFixture();
            s.Frame(0);
            s.Throw();
            s.Game.AddEntity(60);
            s.Game.AddEntity(61);
            // A null entry in SHVDNE's nearby-entity array crosses the handle boundary as zero.
            s.Game.World.Nearby = (position, radius) => new[] { 0, 10, 20, 40, 41, 60, 61 };
            s.Game.Entity(20).Velocity = Vector3.UnitY * 100;
            s.Game.Calls.Clear();
            s.Game.Native.Calls.Clear();
            s.Frame(0);
            CollectionAssert.AreEqual(new[] { 60, 61 }, s.Game.Calls.Where(call => call.Name == "entity.force")
                .Select(call => (int)call.Arguments[0]).ToArray());
            CollectionAssert.AreEqual(new object[] { 60, 100 }, s.Game.Calls.Single(call => call.Name == "ped.damage").Arguments);
            Assert.AreEqual("scr_meth_pipe_smoke", s.Game.Native.Calls.Single(call =>
                call.Hash == Hash.START_PARTICLE_FX_NON_LOOPED_ON_ENTITY).Arguments[0]);
            var trail = s.Game.Native.Calls.Where(call => call.Hash == Hash.START_PARTICLE_FX_NON_LOOPED_AT_COORD)
                .Select(call => new Vector3((float)call.Arguments[1], (float)call.Arguments[2], (float)call.Arguments[3])).ToArray();
            Assert.HasCount(64, trail);
            Assert.AreEqual(-8f, trail.Min(point => point.Y), 0.0001f);
            Assert.AreEqual(0f, trail.Max(point => point.Y), 0.0001f);
            Assert.IsTrue(trail.All(point => Math.Sqrt(point.X * point.X + point.Z * point.Z) <= .24001));
            Assert.AreEqual(Vector3.Zero, trail[0]);
            Assert.AreEqual(Vector3.UnitY * -8, trail[61]);
            var particles = s.Game.Native.Calls.Where(call => call.Hash == Hash.START_PARTICLE_FX_NON_LOOPED_AT_COORD).ToArray();
            for (int index = 0; index < trail.Length; index += 4)
            {
                var midpoint = (trail[index] + trail[index + 1]) / 2;
                Assert.IsTrue((trail[index + 2] - midpoint).Length() < .0001f);
                Assert.IsTrue((trail[index + 3] - midpoint).Length() < .0001f);
                Assert.AreEqual(trail[index].DistanceTo(trail[index + 1]) * .4f, (float)particles[index].Arguments[7], .0001f);
                if (index > 0) Assert.AreEqual(trail[index - 3], trail[index]);
            }
        }

        [TestMethod]
        public void CatchFeedbackStopsApproachAudioAndRepeatedCatchesKeepTheOriginalLightningSchedule()
        {
            var s = new PlayerSessionFixture();
            s.DropHammer(new Vector3(0, -30, 1));
            s.Frame(0, Keys.H);
            Assert.IsTrue(s.Audio.Clips["hammer-close-to-player.wav"].IsPlaying);
            s.Game.Host.OnWait = duration =>
            {
                Assert.AreEqual(1, duration);
                s.ObserveHeldHammer();
            };
            for (int catchNumber = 0; catchNumber < 2; catchNumber++)
            {
                if (catchNumber > 0) s.Frame(500, Keys.Y);
                s.Game.Entity(20).Position = Vector3.UnitZ;
                s.Game.World.Capsules.Enqueue(new RayHit(true, 10, Vector3.UnitZ));
                s.Frame(0, Keys.H);
                Assert.AreEqual(WeaponHash.Hammer, s.Player.CurrentWeapon);
            }
            Assert.AreEqual(2, s.Audio.Clips["catch-hammer.wav"].Plays);
            Assert.IsFalse(s.Audio.Clips["hammer-close-to-player.wav"].IsPlaying);
            Assert.HasCount(16, s.Game.Native.Calls.Where(call => call.Hash == Hash.FORCE_LIGHTNING_FLASH).ToArray());
            CollectionAssert.AreEqual(new object[] { 20, 10 }, s.LastNative(Hash.GIVE_WEAPON_OBJECT_TO_PED));
            var animation = s.LastNative(Hash.TASK_PLAY_ANIM);
            Assert.AreEqual("melee_damage_back", animation[2]);
            Assert.AreEqual(250, animation[5]);
            Assert.AreEqual((int)(AnimationFlags.UpperBodyOnly | AnimationFlags.Secondary), animation[6]);
            Func<int> bodyPulses = () => s.Game.Native.Calls.Count(call => call.Hash == Hash.START_PARTICLE_FX_NON_LOOPED_ON_PED_BONE);
            Assert.AreEqual(10, bodyPulses(), "Each catch emits one hand effect plus four body effects.");
            s.Frame(499);
            Assert.AreEqual(10, bodyPulses());
            s.Frame(1);
            Assert.AreEqual(14, bodyPulses(), "The second catch must not postpone the original one-second timer.");
            s.Frame(0);
            Assert.AreEqual(14, bodyPulses());
            s.Frame(0, Keys.Y);
            s.Frame(0);
            s.Frame(10001);
            Assert.AreEqual(14, bodyPulses(), "Expired power must stop the periodic body effects.");
        }

        [TestMethod]
        [DataRow(false, .5f, true)]
        [DataRow(false, .501f, false)]
        [DataRow(true, 2f, true)]
        [DataRow(true, 2.001f, false)]
        public void AimedThrowKeepsChasingUntilTheObservedTargetIsWithinContactRange(bool vehicle, float distance, bool reached)
        {
            var s = new PlayerSessionFixture();
            s.Frame(0);
            s.Game.AddEntity(60).Position = new Vector3(10, 0, 1);
            if (vehicle) { s.Peds.Remove(60); s.Vehicles.Add(60); }
            s.Game.World.Rays.Enqueue(new RayHit(true, 60, Vector3.Zero));
            s.Frame(0, Keys.T, aim: true);
            Assert.AreEqual(Vector3.Zero, s.Game.Entity(20).Velocity);
            s.Game.Entity(20).Position = s.Game.Entity(60).Position - Vector3.UnitY * distance;
            s.Frame(0);
            Assert.AreEqual(reached, s.Log.Records.Any(record => record.Name == "hammer.targetReached"));
            StringAssert.Contains(s.State(), "targets=" + (reached ? "0" : "1"));
            Assert.AreEqual(reached ? 0 : 100, s.Game.Entity(20).Velocity.Length(), .001f);
        }

        [TestMethod]
        public void ADeletedTargetIsForgottenAndManualRecallCancelsTheRemainingAttack()
        {
            var s = new PlayerSessionFixture();
            s.Frame(0);
            foreach (int handle in new[] { 60, 61 })
            {
                s.Game.AddEntity(handle).Position = Vector3.UnitY * handle;
                s.Peds.Add(handle);
                s.Game.World.Rays.Enqueue(new RayHit(true, handle, Vector3.Zero));
                s.Frame(0, aim: true);
            }
            s.Frame(0, Keys.T, aim: true);
            s.Game.Observations[60].Exists = false;
            s.Frame(0);
            StringAssert.Contains(s.State(), "targets=1");
            s.Frame(0, Keys.H);
            StringAssert.Contains(s.State(), "targets=0");
            StringAssert.Contains(s.State(), "attacking=False");
        }

        [TestMethod]
        public void LostTerrainRecoveryStaysBoundedAndRecallResumesDirectedTravel()
        {
            var s = new PlayerSessionFixture(random: new FixedRandom(0, .25));
            s.DropHammer(Vector3.Zero);
            foreach (float height in new[] { 25f, 50f, 100f })
            {
                s.Game.Entity(20).HeightAboveGround = height;
                s.Frame(1000);
                Assert.AreEqual(Vector3.Zero, s.Game.Entity(20).Velocity);
            }
            s.Game.Entity(20).HeightAboveGround = 151;
            s.Frame(0);
            s.Frame(1000);
            for (int frame = 0; frame < 200; frame++)
            {
                s.Frame(1);
                Assert.IsTrue(s.Game.Entity(20).Velocity.Length() <= 30.001f);
            }
            Assert.AreEqual(7.5f, s.Game.Entity(20).Velocity.Length(), .001f);
            s.Player.Bones[Bone.PHRightHand] = Vector3.UnitY * 20;
            s.Frame(0, Keys.H);
            Assert.AreEqual(Vector3.UnitY * 100, s.Game.Entity(20).Velocity);
            Assert.IsTrue(s.Game.Entity(20).Persistent);
        }

        [TestMethod]
        public void StationaryDroppedHammerEmitsOneLightningPulsePerSecondAndDeletionStopsOldPulses()
        {
            var s = new PlayerSessionFixture();
            s.DropHammer(new Vector3(12, -8, 30));
            s.Game.World.RaycastObservation = (from, to, flags, ignore) => new RayHit(true, 0, Vector3.Zero);
            s.Game.Native.Calls.Clear();
            foreach (int time in new[] { 0, 999, 1000, 1000, 1999, 2000 })
            {
                s.Game.Host.GameTime = time;
                s.Frame(0);
            }
            CollectionAssert.AreEqual(new[] { 20, 20 }, s.Game.Native.Calls.Where(call =>
                call.Hash == Hash.START_PARTICLE_FX_NON_LOOPED_ON_ENTITY).Select(call => (int)call.Arguments[1]).ToArray());
            s.Game.Observations[20].Exists = false;
            s.Game.Entities.CreatedBlips.Enqueue(2001);
            s.Frame(0);
            s.Frame(1000);
            Assert.IsFalse(s.Game.Native.Calls.Any(call => call.Hash == Hash.START_PARTICLE_FX_NON_LOOPED_ON_ENTITY &&
                (int)call.Arguments[1] == 0));
            Assert.HasCount(2, s.Game.Native.Calls.Where(call => call.Hash == Hash.START_PARTICLE_FX_NON_LOOPED_ON_ENTITY &&
                (int)call.Arguments[1] == 20).ToArray());
        }
    }
}
