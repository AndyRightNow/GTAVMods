using GTA;
using GTA.Math;
using System;
using System.Collections.Generic;

namespace Thor
{
    using ThunderBolt = List<ThunderSegment>;

    public class Thunder
    {
        private List<ThunderBolt> thunderBolts;
        private static Thunder instance;

        private Thunder()
        {
            thunderBolts = new List<ThunderBolt>();
        }

        public static Thunder Instance
        {
            get
            {
                if (instance == null)
                {
                    instance = new Thunder();
                }

                return instance;
            }
        }

        public void OnTick()
        {
        }

        public void Shoot(Vector3 from, Vector3 to, int jaggedness = 15, float maxSwayRate = 0.03f, bool hasDamage = true)
        {
            jaggedness = jaggedness == -1 ? 15 : jaggedness;
            maxSwayRate = maxSwayRate == -1.0f ? 0.03f : maxSwayRate;

            if (hasDamage)
            {
                var raycast = World.Raycast(from, to, ADModUtils.NativeHelper.IntersectAllObjects);

                if (raycast.DidHit && raycast.HitEntity != null)
                {
                    var ent = raycast.HitEntity;

                    if (ent != Game.Player.Character)
                    {
                        NativeHelper.Instance.ApplyForcesAndDamages(ent, to - from);
                    }
                }
            }

            ThunderBolt thunderBolt = new ThunderBolt();
            GenerateThunderBolt(ref thunderBolt, from, to, jaggedness, maxSwayRate);
            var i = 0;
            foreach (var line in thunderBolt)
            {
                line.Render();
                i++;
            }
        }

        private void GenerateThunderBolt(ref ThunderBolt thunderBolt, Vector3 start, Vector3 end, int jaggedness, float maxSwayRate, int maxDepthLevel = 3, int currentDepth = 1)
        {
            if (currentDepth > maxDepthLevel)
            {
                return;
            }

            var startToEnd = end - start;
            var direction = (end - start).Normalized;
            var len = startToEnd.Length();
            List<float> stops = new List<float>();
            var rand = ADModUtils.Utilities.Random.SystemRandomInstance;
            float maxSwayValue = len * maxSwayRate;

            for (int i = 0; i < jaggedness; i++)
            {
                var nextStop = Convert.ToSingle(rand.NextDouble());
                stops.Add(nextStop);
            }
            stops.Sort();

            Vector3 prevPoint = start;
            foreach (var stop in stops)
            {
                var randomPerpVec = ADModUtils.Utilities.Math.RandomVectorPerpendicularTo(direction);
                float randomSway = Convert.ToSingle(rand.NextDouble() * maxSwayValue * ADModUtils.Utilities.Random.RandomNegation());

                var curDir = direction * stop * len + randomPerpVec * randomSway;
                var curPoint = start + curDir;
                thunderBolt.Add(new ThunderSegment(prevPoint, curPoint));

                //bool shouldBranchOut = Convert.ToBoolean( rand.Next(0, 2));

                //if (shouldBranchOut)
                //{
                //    ThunderBolt subThunderBolt = new ThunderBolt();
                //    var nextDepthLevel = currentDepth + 1;
                //    GenerateThunderBolt(
                //        ref subThunderBolt, 
                //        curPoint, 
                //        curPoint + curDir.Normalized * len / nextDepthLevel,
                //        jaggedness / nextDepthLevel, 
                //        maxSwayRate / nextDepthLevel, 
                //        maxDepthLevel,
                //        nextDepthLevel
                //    );
                //    if (subThunderBolt.Count > 0)
                //    {
                //        thunderBolt.AddRange(subThunderBolt);
                //    }
                //}

                prevPoint = curPoint;
            }
            thunderBolt.Add(new ThunderSegment(prevPoint, end));
        }
    }
}
