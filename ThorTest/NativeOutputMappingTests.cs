using System;
using GTA;
using GTA.Math;
using GTAVMods.Game;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ModUtilsTest
{
    [TestClass]
    public class NativeOutputMappingTests
    {
        private sealed class OutputBindings : IRawNativeOutputs
        {
            public int Ped, Entity;
            public Vector3 ProjectedPosition;
            public bool HasBone, OnScreen;
            public int Bone;
            public Exception Failure;
            public bool LastDamageBone(int ped, out int bone)
            { Ped = ped; bone = Bone; return HasBone; }
            public void EntityQuaternion(int entity, out float x, out float y, out float z, out float w)
            {
                Entity = entity;
                if (Failure != null) throw Failure;
                x = 0.2f; y = -0.3f; z = 0.4f; w = 1.5f;
            }
            public bool WorldToScreen(float worldX, float worldY, float worldZ, out float x, out float y)
            { ProjectedPosition = new Vector3(worldX, worldY, worldZ); x = 0.125f; y = 0.75f; return OnScreen; }
        }

        private sealed class WorldBindings : IWorldQueryBindings
        {
            public Vector3 From, To, Position;
            public IntersectFlags Flags;
            public int IgnoredEntity, Entity;
            public float Radius;
            public bool Hit;
            public void Raycast(Vector3 from, Vector3 to, IntersectFlags flags, int ignoreEntity,
                out bool didHit, out int entity, out Vector3 position)
            {
                From = from; To = to; Flags = flags; IgnoredEntity = ignoreEntity;
                didHit = Hit; entity = Entity; position = Position;
            }
            public void Capsule(Vector3 from, Vector3 to, float radius, IntersectFlags flags,
                out bool didHit, out int entity, out Vector3 position)
            {
                From = from; To = to; Radius = radius; Flags = flags;
                didHit = Hit; entity = Entity; position = Position;
            }
        }

        [TestMethod]
        public void OutputAdapterPreservesRawComponentsValidityAndEntityIdentity()
        {
            var raw = new OutputBindings { HasBone = true, Bone = 31086, OnScreen = true };
            var outputs = new NativeOutputs(raw);
            Assert.IsTrue(outputs.LastDamageBone(23, out int bone));
            Assert.AreEqual(23, raw.Ped);
            Assert.AreEqual(31086, bone);
            raw.HasBone = false; raw.Bone = 0;
            Assert.IsFalse(outputs.LastDamageBone(24, out bone));
            Assert.AreEqual(24, raw.Ped);
            Assert.AreEqual(0, bone);

            var quaternion = outputs.EntityQuaternion(31);
            Assert.AreEqual(31, raw.Entity);
            Assert.AreEqual(0.2f, quaternion.X);
            Assert.AreEqual(-0.3f, quaternion.Y);
            Assert.AreEqual(0.4f, quaternion.Z);
            Assert.AreEqual(1.5f, quaternion.W, "The adapter must not normalize the engine observation.");

            Assert.IsTrue(outputs.WorldToScreen(new Vector3(-7.5f, 8.25f, 0.125f), out float x, out float y));
            Assert.AreEqual(new Vector3(-7.5f, 8.25f, 0.125f), raw.ProjectedPosition);
            Assert.AreEqual(0.125f, x); Assert.AreEqual(0.75f, y);
            raw.OnScreen = false;
            Assert.IsFalse(outputs.WorldToScreen(new Vector3(1, 2, 3), out x, out y));
            Assert.AreEqual(new Vector3(1, 2, 3), raw.ProjectedPosition);
            Assert.AreEqual(0.125f, x); Assert.AreEqual(0.75f, y);

            raw.Failure = new InvalidOperationException("Host query failed.");
            Assert.AreSame(raw.Failure, Assert.ThrowsExactly<InvalidOperationException>(() => outputs.EntityQuaternion(32)));
            Assert.AreEqual(32, raw.Entity);
        }

        [TestMethod]
        public void WorldAdapterPreservesEntityTerrainAndMissObservationsWithoutInventingCollisions()
        {
            var raw = new WorldBindings { Hit = true, Entity = 41, Position = new Vector3(-2, 4, 9) };
            var world = new GtaWorld(raw);
            var entityHit = world.Raycast(new Vector3(1, 3, 5), new Vector3(2, 4, 6), IntersectFlags.Peds, 23);
            Assert.AreEqual(new Vector3(1, 3, 5), raw.From);
            Assert.AreEqual(new Vector3(2, 4, 6), raw.To);
            Assert.AreEqual(IntersectFlags.Peds, raw.Flags); Assert.AreEqual(23, raw.IgnoredEntity);
            Assert.IsTrue(entityHit.DidHit); Assert.AreEqual(41, entityHit.Entity);
            Assert.AreEqual(new Vector3(-2, 4, 9), entityHit.Position);

            raw.Entity = 0; raw.Position = new Vector3(7, -8, 2);
            var terrainHit = world.Capsule(new Vector3(11, 12, 13), new Vector3(21, 22, 23), 0.75f, IntersectFlags.Map);
            Assert.AreEqual(new Vector3(11, 12, 13), raw.From);
            Assert.AreEqual(new Vector3(21, 22, 23), raw.To);
            Assert.AreEqual(0.75f, raw.Radius); Assert.AreEqual(IntersectFlags.Map, raw.Flags);
            Assert.IsTrue(terrainHit.DidHit, "World geometry is a hit even though it has no entity handle.");
            Assert.AreEqual(0, terrainHit.Entity);
            Assert.AreEqual(new Vector3(7, -8, 2), terrainHit.Position);

            raw.Hit = false;
            var miss = world.Raycast(Vector3.Zero, Vector3.UnitX, IntersectFlags.Map);
            Assert.IsFalse(miss.DidHit);
            Assert.AreEqual(0, miss.Entity);
            Assert.AreEqual(new Vector3(7, -8, 2), miss.Position, "Miss payload is preserved but must not be interpreted as a hit.");
            Assert.AreEqual(0, raw.IgnoredEntity);
        }
    }
}
