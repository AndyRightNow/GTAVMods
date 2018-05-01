using GTA;
using GTA.Math;
using GTA.Native;
using System;
using System.Collections.Generic;

namespace Thor
{
    using ThunderBoltInfo = Tuple<Vector3, Vector3, float>;

    public class Thunder
    {
        private List<ThunderBoltInfo> thunderBolts;
        private static Thunder instance;

        private Thunder()
        {
            thunderBolts = new List<ThunderBoltInfo>();
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
            List<ThunderBoltInfo> newThunderBolts = new List<ThunderBoltInfo>();
            foreach (var bolt in thunderBolts)
            {
                var start = bolt.Item1;
                var destination = bolt.Item2;
                NativeHelper.PlayThunderFx(start);
                var nearbyEntities = World.GetNearbyEntities(destination, 1.0f);

                foreach (var ent in nearbyEntities)
                {
                    if (ent == Game.Player.Character ||
                        ent == Game.Player.Character.Weapons.CurrentWeaponObject)
                    {
                        continue;
                    }

                    NativeHelper.ApplyForcesAndDamages(ent, (destination - start).Normalized);
                }
                if (!Utilities.Math.CloseTo(start, destination, 0.5f))
                {
                    newThunderBolts.Add(new ThunderBoltInfo(Vector3.Lerp(start, destination, bolt.Item3), destination, bolt.Item3));
                }
            }
            thunderBolts = newThunderBolts;
        }

        public void Shoot(Vector3 from, Vector3 to, float speed = 0.3f)
        {
            thunderBolts.Add(new ThunderBoltInfo(from, to, speed));
        }
    }
}
