using System;
using System.Collections.Generic;
using ADModUtils;
using GTA.Math;
using GTAVMods.Game;

namespace Thor
{
    public sealed class Thunder
    {
        private readonly GameServices game;
        private readonly NativeHelper effects;
        private readonly Random random;

        public Thunder(GameServices game, NativeHelper effects, Random random)
        {
            this.game = game;
            this.effects = effects;
            this.random = random;
        }

        public void Shoot(Vector3 from, Vector3 to, int jaggedness = 15, float maxSwayRate = 0.03f, bool hasDamage = true)
        {
            jaggedness = jaggedness == -1 ? 15 : jaggedness;
            maxSwayRate = maxSwayRate == -1 ? 0.03f : maxSwayRate;
            if (hasDamage)
            {
                var hit = game.World.Raycast(from, to, ADModUtils.NativeHelper.IntersectAllObjects);
                if (hit.DidHit && hit.Entity != 0 && hit.Entity != game.Host.PlayerPed)
                    effects.Operations.ApplyForcesAndDamages(hit.Entity, to - from);
            }
            Vector3 direction = (to - from).Normalized;
            float length = (to - from).Length();
            var stops = new List<float>();
            for (int i = 0; i < jaggedness; i++) stops.Add((float)random.NextDouble());
            stops.Sort();
            Vector3 previous = from;
            foreach (float stop in stops)
            {
                Vector3 perpendicular = Utilities.Math.RandomVectorPerpendicularTo(direction, random);
                float sway = (float)(random.NextDouble() * length * maxSwayRate * Utilities.Random.RandomNegation(random));
                Vector3 current = from + direction * stop * length + perpendicular * sway;
                new ThunderSegment(previous, current, effects.Operations).Render();
                previous = current;
            }
            new ThunderSegment(previous, to, effects.Operations).Render();
        }
    }
}
