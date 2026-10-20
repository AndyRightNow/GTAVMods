using GTA;
using GTA.Math;

namespace GTAVMods.Game
{
    /// <summary>Raw shape-test observations, without collision prediction or hit normalization.</summary>
    /// <remarks>Used by GtaWorld to assemble measured RayHit results. Position is in world metres.</remarks>
    public interface IWorldQueryBindings
    {
        void Raycast(Vector3 from, Vector3 to, IntersectFlags flags, int ignoreEntity,
            out bool didHit, out int entity, out Vector3 position);
        void Capsule(Vector3 from, Vector3 to, float radius, IntersectFlags flags,
            out bool didHit, out int entity, out Vector3 position);
    }

    public sealed class GtaWorldQueries : IWorldQueryBindings
    {
        [GamePrimitive]
        public void Raycast(Vector3 from, Vector3 to, IntersectFlags flags, int ignoreEntity,
            out bool didHit, out int entity, out Vector3 position)
        {
            var result = World.Raycast(from, to, flags, Entity.FromHandle(ignoreEntity));
            didHit = result.DidHit;
            entity = GameHandles.Of(result.HitEntity);
            position = result.HitPosition;
        }

        [GamePrimitive]
        public void Capsule(Vector3 from, Vector3 to, float radius, IntersectFlags flags,
            out bool didHit, out int entity, out Vector3 position)
        {
            var test = ShapeTest.StartTestCapsule(from, to, radius, flags);
            test.GetResult(out ShapeTestResult result);
            result.TryGetHitEntity(out Entity hitEntity);
            didHit = result.DidHit;
            entity = GameHandles.Of(hitEntity);
            position = result.HitPosition;
        }
    }
}
