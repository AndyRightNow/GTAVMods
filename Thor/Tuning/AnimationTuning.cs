using System.Collections.Generic;

namespace Thor
{
    /// <summary>Animation choices and timing, indexed by AnimationActions. Change before composition/reload.</summary>
    public sealed class AnimationTuning
    {
        public float BlendInSpeed = 8f;
        public float BlendOutSpeed = 1f;
        public float PlaybackRate = -8f;
        public int LoadTimeoutMs = 1000;

        public string[] Dictionaries = (new List<string>
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
        public string[] Names = (new List<string>
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
        // Milliseconds from the selected animation to hammer release. Missing entries release immediately.
        public Dictionary<string, Dictionary<string, int>> ReleaseDelays = new Dictionary<string, Dictionary<string, int>>()
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
        public bool[] HasAngles = (new List<bool>
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
        public bool[] IncompleteSigns = (new List<bool>
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
    }
}
