using System;
using System.Collections.Generic;
using System.Linq;
using GTA;
using GTA.Math;
using GTA.Native;
using GTAVMods.Game;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Thor;

namespace ModUtilsTest {
    [TestClass]
    public class ThunderTests {
        private sealed class ThunderRandom : Random {
            private readonly Queue<double> samples;
            private readonly int sign;
            internal ThunderRandom(int sign = 1, params double[] samples) {
                this.sign = sign;
                this.samples = new Queue<double>(samples);
            }
            public override double NextDouble() { return samples.Count == 0 ? .5 : samples.Dequeue(); }
            public override int Next(int minimum, int maximum) {
                Assert.AreEqual(0, minimum);
                Assert.AreEqual(2, maximum);
                return sign;
            }
        }

        private static FakeGame CreateWorld() {
            var game = new FakeGame();
            game.AddEntity(10);
            game.AddEntity(20);
            game.Native.Returns(Hash.HAS_NAMED_PTFX_ASSET_LOADED, true);
            game.Native.Returns(Hash.IS_ENTITY_A_PED, true);
            return game;
        }
        private static object[][] Particles(FakeGame game) {
            return game.Native.Calls.Where(call => call.Hash == Hash.START_PARTICLE_FX_NON_LOOPED_AT_COORD)
                .Select(call => call.Arguments).ToArray();
        }
        private static Vector3 Position(object[] particle) {
            return new Vector3((float)particle[1], (float)particle[2], (float)particle[3]);
        }
        private static void AssertVector(Vector3 expected, Vector3 actual) {
            Assert.AreEqual(expected.X, actual.X, .0001);
            Assert.AreEqual(expected.Y, actual.Y, .0001);
            Assert.AreEqual(expected.Z, actual.Z, .0001);
        }

        [TestMethod]
        public void SegmentUsesBothEndpointsAndMidpointWithOppositeOrientationsAndLengthScale() {
            var game = CreateWorld();
            var effects = new Thor.NativeHelper(game.Services);
            var start = new Vector3(1, 2, 3);
            var end = new Vector3(6, 2, 3);
            var segment = new ThunderSegment(start, end, effects.Operations);
            segment.Render();
            var particles = Particles(game);
            Assert.AreEqual(4, particles.Length);
            CollectionAssert.AreEqual(new[] { start, end, new Vector3(3.5f, 2, 3), new Vector3(3.5f, 2, 3) },
                particles.Select(Position).ToArray());
            for (var index = 0; index < particles.Length; index++) {
                var particle = particles[index];
                Assert.AreEqual("muz_railgun", particle[0]);
                Assert.AreEqual(0f, (float)particle[4], .001);
                Assert.AreEqual(0f, (float)particle[5], .001);
                Assert.AreEqual(index % 2 == 0 ? 0f : 180f, (float)particle[6], .001);
                Assert.AreEqual(2f, particle[7]);
                CollectionAssert.AreEqual(new object[] { 0, 0, 0 }, particle.Skip(8).ToArray());
            }
            CollectionAssert.AreEqual(new[] { "core", "core", "core", "core" },
                game.Native.Calls.Where(c => c.Hash == Hash.USE_PARTICLE_FX_ASSET).Select(c => (string)c.Arguments[0]).ToArray());
            StringAssert.Contains(segment.ToString(), "Thor.ThunderSegment");
            StringAssert.Contains(segment.ToString(), "Start: " + start);
            StringAssert.Contains(segment.ToString(), "End: " + end);
            game.Native.Calls.Clear();
            new ThunderSegment(Vector3.Zero, new Vector3(0, 0, 5), effects.Operations).Render();
            particles = Particles(game);
            Assert.AreEqual(-90f, (float)particles[0][5], .001);
            Assert.AreEqual(90f, (float)particles[1][5], .001);
            Assert.AreEqual(90f, (float)particles[0][6], .001);
        }

        [TestMethod]
        [DataRow(false, 20, false)]
        [DataRow(true, 0, false)]
        [DataRow(true, 10, false)]
        [DataRow(true, 20, true)]
        public void DamageRequiresHitEntityOtherThanPlayerAndUsesUnnormalizedStrikeDirection(bool hit, int entity, bool damaged) {
            var game = CreateWorld();
            game.World.Rays.Enqueue(new RayHit(hit, entity, Vector3.Zero));
            var thunder = new Thunder(game.Services, new Thor.NativeHelper(game.Services), new ThunderRandom());
            thunder.Shoot(new Vector3(1, 2, 3), new Vector3(1, 2, 13), 0);
            CollectionAssert.AreEqual(new object[] { new Vector3(1, 2, 3), new Vector3(1, 2, 13), ADModUtils.NativeHelper.IntersectAllObjects, 0 },
                game.Calls.Single(c => c.Name == "world.raycast").Arguments);
            Assert.AreEqual(damaged ? 1 : 0, game.Calls.Count(c => c.Name == "ped.damage"));
            Assert.AreEqual(damaged ? 1 : 0, game.Calls.Count(c => c.Name == "entity.force"));
            if (damaged) {
                CollectionAssert.AreEqual(new object[] { 20, 100 }, game.Calls.Single(c => c.Name == "ped.damage").Arguments);
                CollectionAssert.AreEqual(new object[] { 20, new Vector3(0, 0, 2500) }, game.Calls.Single(c => c.Name == "entity.force").Arguments);
            }
            Assert.AreEqual(4, Particles(game).Length, "A zero-jaggedness bolt still draws its full segment.");
        }

        [TestMethod]
        [DataRow(1)]
        [DataRow(0)]
        public void JaggedStopsAreSortedAndSegmentsJoinContinuouslyWithBoundedPerpendicularSway(int sign) {
            var game = CreateWorld();
            var random = new ThunderRandom(sign, .75, .25, .5, .5, .4, .5, .5, .6);
            var thunder = new Thunder(game.Services, new Thor.NativeHelper(game.Services), random);
            thunder.Shoot(Vector3.Zero, new Vector3(0, 0, 10), 2, .1f, false);
            Assert.IsFalse(game.Calls.Any(c => c.Name == "world.raycast"), "Visual-only bolts never query damage hits.");
            var particles = Particles(game);
            Assert.AreEqual(12, particles.Length);
            var firstJoint = new Vector3((float)Math.Sqrt(.08), (float)Math.Sqrt(.08), 2.5f);
            var secondJoint = new Vector3((float)Math.Sqrt(.18), (float)Math.Sqrt(.18), 7.5f);
            // The same chosen sign is used for both perpendicular basis and sway.
            AssertVector(Vector3.Zero, Position(particles[0]));
            AssertVector(firstJoint, Position(particles[1]));
            AssertVector(firstJoint, Position(particles[4]));
            AssertVector(secondJoint, Position(particles[5]));
            AssertVector(secondJoint, Position(particles[8]));
            AssertVector(new Vector3(0, 0, 10), Position(particles[9]));
            for (var index = 0; index < particles.Length; index += 4) {
                var from = Position(particles[index]);
                var to = Position(particles[index + 1]);
                AssertVector((from + to) / 2, Position(particles[index + 2]));
                AssertVector((from + to) / 2, Position(particles[index + 3]));
                Assert.AreEqual(from.DistanceTo(to) * .4f, (float)particles[index][7], .0001);
            }
        }

        [TestMethod]
        public void SentinelDefaultsProduceFifteenStopsAndSameGeometryAsOptionalDefaults() {
            var normalGame = CreateWorld();
            var sentinelGame = CreateWorld();
            normalGame.World.Rays.Enqueue(new RayHit(false, 0, Vector3.Zero));
            var normal = new Thunder(normalGame.Services, new Thor.NativeHelper(normalGame.Services), new ThunderRandom());
            var sentinel = new Thunder(sentinelGame.Services, new Thor.NativeHelper(sentinelGame.Services), new ThunderRandom());
            normal.Shoot(Vector3.Zero, new Vector3(0, 0, 10));
            sentinel.Shoot(Vector3.Zero, new Vector3(0, 0, 10), -1, -1, false);
            var normalParticles = Particles(normalGame);
            var sentinelParticles = Particles(sentinelGame);
            Assert.AreEqual(64, normalParticles.Length);
            Assert.AreEqual(normalParticles.Length, sentinelParticles.Length);
            for (var index = 0; index < normalParticles.Length; index++)
                CollectionAssert.AreEqual(normalParticles[index], sentinelParticles[index]);
            var joint = Position(normalParticles[1]);
            Assert.AreEqual(.15, new Vector2(joint.X, joint.Y).Length(), .00001);
            Assert.AreEqual(5f, joint.Z);
        }

        [TestMethod]
        public void ThunderParticleOverloadsPreserveDefaultScaleBoneAndCoordinateArguments() {
            var game = CreateWorld();
            var effects = new Thor.NativeHelper(game.Services);
            effects.PlayThunderFx(20);
            CollectionAssert.AreEqual(new object[] { "ent_dst_elec_fire_sp", 20, 0f, 0f, 0f, 0f, 0f, 0f, 1f, 0, 0, 0 },
                game.Native.Calls.Last().Arguments);
            Assert.AreEqual(Hash.START_PARTICLE_FX_NON_LOOPED_ON_ENTITY, game.Native.Calls.Last().Hash);
            effects.PlayThunderFx(20, Bone.PHRightHand, .8f);
            CollectionAssert.AreEqual(new object[] { "ent_dst_elec_fire_sp", 20, 0f, 0f, 0f, 0f, 0f, 0f, (int)Bone.PHRightHand, .8f, 0, 0, 0 },
                game.Native.Calls.Last().Arguments);
            Assert.AreEqual(Hash.START_PARTICLE_FX_NON_LOOPED_ON_PED_BONE, game.Native.Calls.Last().Hash);
            effects.PlayThunderFx(new Vector3(1, 2, 3), 1.5f);
            CollectionAssert.AreEqual(new object[] { "ent_dst_elec_fire_sp", 1f, 2f, 3f, 0f, 0f, 0f, 1.5f, 0, 0, 0 },
                game.Native.Calls.Last().Arguments);
            Assert.AreEqual(Hash.START_PARTICLE_FX_NON_LOOPED_AT_COORD, game.Native.Calls.Last().Hash);
            CollectionAssert.AreEqual(new[] { "core", "core", "core" },
                game.Native.Calls.Where(c => c.Hash == Hash.REQUEST_NAMED_PTFX_ASSET).Select(c => (string)c.Arguments[0]).ToArray());
        }

        [TestMethod]
        [DataRow(AnimationActions.ThrowHammer1, 90f, "small_melee_wpn_short_range_0")]
        [DataRow(AnimationActions.ThrowHammer3, 0f, "small_melee_wpn_short_range_0")]
        [DataRow(AnimationActions.ThrowHammer3, 44.99f, "small_melee_wpn_short_range_0")]
        [DataRow(AnimationActions.ThrowHammer3, -45f, "small_melee_wpn_short_range_+90")]
        [DataRow(AnimationActions.ThrowHammer3, 45f, "small_melee_wpn_short_range_-90")]
        [DataRow(AnimationActions.ThrowHammer4, -134.99f, "small_melee_wpn_long_range_+90")]
        [DataRow(AnimationActions.ThrowHammer4, 134.99f, "small_melee_wpn_long_range_-90")]
        [DataRow(AnimationActions.ThrowHammer4, -135f, "small_melee_wpn_long_range_+180")]
        [DataRow(AnimationActions.ThrowHammer4, 135f, "small_melee_wpn_long_range_-180")]
        [DataRow(AnimationActions.ThrowTwoHandHammer1, -90f, "short_90_attack")]
        [DataRow(AnimationActions.ThrowTwoHandHammer1, 90f, "short_-90_attack")]
        [DataRow(AnimationActions.ThrowTwoHandHammer1, -180f, "short_-180_attack")]
        [DataRow(AnimationActions.ThrowTwoHandHammer2, 0f, "long_0_attack")]
        [DataRow(AnimationActions.ThrowTwoHandHammer2, -90f, "long_90_attack")]
        [DataRow(AnimationActions.ThrowTwoHandHammer2, 90f, "long_-90_attack")]
        [DataRow(AnimationActions.ThrowTwoHandHammer2, 180f, "long_-180_attack")]
        public void AnimationMetadataFormatsSupportedAngleVariantsAndBoundarySectors(AnimationActions action, float angle, string expected) {
            var effects = new Thor.NativeHelper(CreateWorld().Services);
            Assert.AreEqual(expected, effects.GetAnimationNameForAngle(action, angle));
            if (angle == 0) Assert.AreEqual(expected, effects.Operations.GetAnimationNameByAction((uint)action));
        }

        [TestMethod]
        public void AnimationVariantsResolveConfiguredWaitTimesForActualMetadata() {
            var effects = new Thor.NativeHelper(CreateWorld().Services);
            foreach (var scenario in new[] {
                (AnimationActions.ThrowHammer3, -90f, 600), (AnimationActions.ThrowHammer3, 90f, 400),
                (AnimationActions.ThrowHammer5, -180f, 1100), (AnimationActions.ThrowHammer5, 180f, 800),
                (AnimationActions.ThrowTwoHandHammer1, -90f, 800), (AnimationActions.ThrowTwoHandHammer1, -180f, 700),
                (AnimationActions.ThrowTwoHandHammer2, -90f, 1300), (AnimationActions.ThrowTwoHandHammer2, 90f, 1000)
            }) {
                Assert.AreEqual(scenario.Item3, effects.Operations.GetAnimationWaitTimeByDictNameAndAnimName(
                    effects.Operations.GetAnimationDictNameByAction((uint)scenario.Item1),
                    effects.GetAnimationNameForAngle(scenario.Item1, scenario.Item2)));
            }
        }
    }
}
