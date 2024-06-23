using CitizenFX.Core;
using CitizenFX.Core.Native;
using System.Collections.Generic;

namespace Thor
{
    public enum AnimationActions
    {
        CallingForWeapon = 0,
        ThrowHammer1,
        ThrowHammer2,
        ThrowHammer3,
        ThrowHammer4,
        ThrowHammer5,
        CatchingWeapon1,
        GroundAttack1,
        GroundAttack2,
        GroundAttack3,
        GroundAttack4,
        SummonThunder,
        WhirlingHammer,
        ThrowTwoHandHammer1,
        ThrowTwoHandHammer2,
    }

    public enum ParticleEffects
    {
        Thunder
    }

    public static class NativeHelper
    {
        private static float MELEE_HIT_FORCE = 250.0f;
        private static int MELEE_HIT_PED_DAMAGE = 100;
        private static string[] AnimationDictNames = (new List<string>
        {
            "weapons@first_person@aim_lt@p_m_zero@projectile@misc@sticky_bomb@aim_trans@lt_to_rng",
            "anim@melee@machete@streamed_core@",
            "anim@melee@machete@streamed_core@",
            "melee@small_wpn@streamed_core_fps",
            "melee@small_wpn@streamed_core_fps",
            "melee@small_wpn@streamed_core",
            "melee@small_wpn@streamed_core",
            "melee@small_wpn@streamed_core_fps",
            "melee@knife@streamed_core",
            "melee@small_wpn@streamed_core",
            "melee@small_wpn@streamed_core",
            "anim@mp_fm_event@intro",
            "melee@small_wpn@streamed_core",
            "melee@large_wpn@streamed_core_fps",
            "melee@large_wpn@streamed_core_fps"
        }).ToArray();
        private static string[] AnimationNames = (new List<string>
        {
            "aim_trans_low",
            "small_melee_wpn_short_range_0",
            "plyr_walking_attack_a",
            "small_melee_wpn_short_range_0",
            "small_melee_wpn_long_range_0",
            "small_melee_wpn_long_range_0",
            "melee_damage_back",
            "ground_attack_on_spot",
            "ground_attack_on_spot",
            "ground_attack_0",
            "ground_attack_on_spot",
            "beast_transform",
            "dodge_generic_centre",
            "short_0_attack",
            "long_0_attack"
        }).ToArray();
        private static Dictionary<string, Dictionary<string, int>> AnimationWaitTime = new Dictionary<string, Dictionary<string, int>>()
        {
            {
                "combat@aim_variations@1h@gang",
                new Dictionary<string, int>() { }
            },
            {
                "anim@melee@machete@streamed_core@",
                new Dictionary<string, int>()
                {
                    {
                        "small_melee_wpn_short_range_0", 800
                    },
                    {
                        "plyr_walking_attack_a", 1000
                    }
                }
            },
            {
                "melee@small_wpn@streamed_core_fps",
                new Dictionary<string, int>()
                {
                    {
                        "small_melee_wpn_short_range_0", 600
                    },
                    {
                        "small_melee_wpn_short_range_+90", 600
                    },
                    {
                        "small_melee_wpn_short_range_+180", 500
                    },
                    {
                        "small_melee_wpn_short_range_-90", 400
                    },
                    {
                        "small_melee_wpn_short_range_-180", 800
                    },
                    {
                        "small_melee_wpn_long_range_0", 1000
                    },
                    {
                        "small_melee_wpn_long_range_+90", 700
                    },
                    {
                        "small_melee_wpn_long_range_+180", 1000
                    },
                    {
                        "small_melee_wpn_long_range_-90", 700
                    },
                    {
                        "small_melee_wpn_long_range_-180", 800
                    }
                }
            },
            {
                "melee@small_wpn@streamed_core",
                new Dictionary<string, int>()
                {
                    {
                        "small_melee_wpn_long_range_0", 1000
                    },
                    {
                        "small_melee_wpn_long_range_+90", 600
                    },
                    {
                        "small_melee_wpn_long_range_+180", 1100
                    },
                    {
                        "small_melee_wpn_long_range_-90", 700
                    },
                    {
                        "small_melee_wpn_long_range_-180", 800
                    }
                }
            },
            {
                "melee@large_wpn@streamed_core_fps",
                new Dictionary<string, int>()
                {
                    {
                        "long_0_attack", 1150
                    },
                    {
                        "long_90_attack", 1300
                    },
                    {
                        "long_-90_attack", 1000
                    },
                    {
                        "long_-180_attack", 1000
                    },
                    {
                        "short_0_attack", 800
                    },
                    {
                        "short_90_attack", 800
                    },
                    {
                        "short_-90_attack", 800
                    },
                    {
                        "short_-180_attack", 700
                    }
                }
            },
            {
                "guard_reactions",
                new Dictionary<string, int>() { }
            },
            {
                "cover@weapon@1h",
                new Dictionary<string, int>() { }
            },
            {
                "combat@fire_variations@1h@gang",
                new Dictionary<string, int>() { }
            }
        };
        private static bool[] AnimationWithAngles = (new List<bool>
        {
            false,
            false,
            false,
            true,
            true,
            true,
            false,
            false,
            false,
            false,
            false,
            false,
            false,
            true,
            true
        }).ToArray();
        private static bool[] AnimationWithAnglesAndIncompletePlusOrMinusSign = (new List<bool>
        {
            false,
            false,
            false,
            false,
            false,
            false,
            false,
            false,
            false,
            false,
            false,
            false,
            false,
            true,
            true
        }).ToArray();

        private static string[] ParticleEffectSetNames = (new List<string>
        {
            "core"
        }).ToArray();
        private static string[] ParticleEffectNames = (new List<string>
        {
            "ent_dst_elec_fire_sp"
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

        public static void PlayThunderFx(Entity ent, float scale = 1.0f)
        {
            ADModUtils.NativeHelper.PlayParticleFx(Instance.GetParticleSetName((uint)ParticleEffects.Thunder), Instance.GetParticleName((uint)ParticleEffects.Thunder), ent, scale);
        }

        public static void PlayThunderFx(Ped ped, Bone boneId, float scale = 1.0f)
        {
            ADModUtils.NativeHelper.PlayParticleFx(Instance.GetParticleSetName((uint)ParticleEffects.Thunder), Instance.GetParticleName((uint)ParticleEffects.Thunder), ped, boneId, scale);
        }

        public static void PlayThunderFx(Vector3 pos, float scale = 1.0f)
        {
            ADModUtils.NativeHelper.PlayParticleFx(Instance.GetParticleSetName((uint)ParticleEffects.Thunder), Instance.GetParticleName((uint)ParticleEffects.Thunder), pos, Vector3.Zero, scale);
        }
    }
}
