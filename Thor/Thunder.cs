using System;
using System.Collections.Generic;
using ADModUtils;
using GTA.Math;

namespace Thor
{
    public sealed class Thunder
    {
        private readonly NativeHelper effects;
        private readonly Random random;
        private readonly EffectsTuning tuning;

        public Thunder(NativeHelper effects, Random random, EffectsTuning tuning)
        {
            this.effects = effects;
            this.random = random;
            this.tuning = tuning;
        }

        public void RenderTrail(Vector3 from, Vector3 to)
        {
            Vector3 direction = (to - from).Normalized;
            float length = (to - from).Length();
            var stops = new List<float>();
            for (int i = 0; i < tuning.TrailInteriorPoints; i++) stops.Add((float)random.NextDouble());
            stops.Sort();
            Vector3 previous = from;
            foreach (float stop in stops)
            {
                Vector3 perpendicular = Utilities.Math.RandomVectorPerpendicularTo(direction, random);
                float sway = (float)(random.NextDouble() * length * tuning.TrailLateralSwayRatio * Utilities.Random.RandomNegation(random));
                Vector3 current = from + direction * stop * length + perpendicular * sway;
                new ThunderSegment(previous, current, effects.Operations, tuning).Render();
                previous = current;
            }
            new ThunderSegment(previous, to, effects.Operations, tuning).Render();
        }
    }
}
