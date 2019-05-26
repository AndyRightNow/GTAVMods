using GTA;
using GTA.Math;
using System.Collections.Generic;

namespace CaptainAmerica
{
    public enum AnimationActions
    {
        ShieldBlockFront,
        ThrowShieldAllAngles,
    }

    public enum ParticleEffects
    {

    }

    public static class NativeHelper
    {
        private static float MELEE_HIT_FORCE = 20.0f;
        private static int MELEE_HIT_PED_DAMAGE = 30;
        private static string[] AnimationDictNames = (new List<string>
        {
            "amb@prop_human_seat_chair@male@elbows_on_knees@react_cowering",
        }).ToArray();
        private static string[] AnimationNames = (new List<string>
        {
            "cover_idle",
        }).ToArray();
        private static Dictionary<string, Dictionary<string, int>> AnimationWaitTime = new Dictionary<string, Dictionary<string, int>>()
        {
        };
        private static bool[] AnimationWithAngles = (new List<bool>
        {
        }).ToArray();
        private static bool[] AnimationWithAnglesAndIncompletePlusOrMinusSign = (new List<bool>
        {
        }).ToArray();

        private static string[] ParticleEffectSetNames = (new List<string>
        {
        }).ToArray();
        private static string[] ParticleEffectNames = (new List<string>
        {
        }).ToArray();
        private static ADModUtils.NativeHelper instance = null;

        public static ADModUtils.NativeHelper Instance
        {
            get
            {
                if (instance == null)
                {
                    instance = new ADModUtils.NativeHelper(
                        AnimationDictNames,
                        AnimationNames,
                        AnimationWaitTime,
                        AnimationWithAngles,
                        AnimationWithAnglesAndIncompletePlusOrMinusSign,
                        ParticleEffectSetNames,
                        ParticleEffectNames,
                        MELEE_HIT_PED_DAMAGE,
                        MELEE_HIT_FORCE
                    );
                }

                return instance;
            }
        }
    }
}
