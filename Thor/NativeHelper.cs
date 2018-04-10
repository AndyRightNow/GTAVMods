using GTA;
using GTA.Math;
using GTA.Native;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Drawing;

namespace Thor
{
    public enum AnimationActions
    {
        CallingForMjonir = 0,
        ThrowHammer1 = 1,
        ThrowHammer2 = 2,
        ThrowHammer3 = 3,
        ThrowHammer4 = 4,
        ThrowHammer5 = 5,
        CatchingMjonir1,
        CatchingMjonir2,
        CatchingMjonir3
    }

    public static class NativeHelper
    {
        private static string[] AnimationDictNames = (new List<string>
        {
            "combat@aim_variations@1h@gang",
            "anim@melee@machete@streamed_core@",
            "anim@melee@machete@streamed_core@",
            "melee@small_wpn@streamed_core_fps",
            "melee@small_wpn@streamed_core_fps",
            "melee@small_wpn@streamed_core",
            "guard_reactions",
            "cover@weapon@1h",
            "combat@fire_variations@1h@gang"
        }).ToArray();
        private static string[] AnimationNames = (new List<string>
        {
            "aim_variation_d",
            "small_melee_wpn_short_range_0",
            "plyr_walking_attack_a",
            "small_melee_wpn_short_range_0",
            "small_melee_wpn_long_range_0",
            "small_melee_wpn_long_range_0",
            "1hand_right_trans",
            "outro_hi_r_corner_short",
            "fire_variation_e"
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
            false
        }).ToArray();

        public static int GetAnimationWaitTimeByDictNameAndAnimName(string dictName, string animName)
        {
            if (AnimationWaitTime.ContainsKey(dictName) &&
                AnimationWaitTime[dictName].ContainsKey(animName))
            {
                return AnimationWaitTime[dictName][animName];
            }

            return 0;

        }

        public static string GetAnimationDictNameByAction(AnimationActions action)
        {
            return AnimationDictNames[(int)action];
        }

        public static string GetAnimationNameByAction(AnimationActions action)
        {
            return AnimationNames[(int)action];
        }

        public static bool DoesAnimationActionHaveAngles(AnimationActions action)
        {
            return AnimationWithAngles[(int)action];
        }

        public static void ClearPlayerAnimation(Ped ped, string dictName, string animName)
        {
            Function.Call(Hash.STOP_ANIM_TASK, ped, dictName, animName, 3);
        }

        public static void PlayPlayerAnimation(Ped ped, string dictName, string animName, AnimationFlags flag, int duration = -1, bool checkIsPlaying = true)
        {
            if (checkIsPlaying && Function.Call<bool>(Hash.IS_ENTITY_PLAYING_ANIM, ped, dictName, animName, 3))
            {
                return;
            }
            Function.Call(Hash.REQUEST_ANIM_DICT, dictName);
            if (!Function.Call<bool>(Hash.HAS_ANIM_DICT_LOADED, dictName))
            {
                Function.Call(Hash.REQUEST_ANIM_DICT, dictName);
            }
            Function.Call(Hash.TASK_PLAY_ANIM, ped, dictName, animName, 8.0f, 1.0f, duration, (int)flag, -8.0f, 0, 0, 0);
        }

        public static void SetEntityVelocity(InputArgument entity, Vector3 velocity)
        {
            Function.Call(Hash.SET_ENTITY_VELOCITY, entity, velocity.X, velocity.Y, velocity.Z);
        }

        public static Entity CreateWeaponObject(WeaponHash weaponHash, int amountCount, Vector3 position, bool showWorldModel = true, float heading = 1.0f)
        {
            new WeaponAsset((WeaponHash)weaponHash).Request(3000);

            return Function.Call<Entity>(Hash.CREATE_WEAPON_OBJECT, (int)weaponHash, amountCount, position.X, position.Y, position.Z, showWorldModel, heading);
        }

        public static void DrawLine(Vector3 start, Vector3 end, Color color)
        {
            Function.Call(Hash.DRAW_LINE, start.X, start.Y, start.Z, end.X, end.Y, end.Z, color.R, color.G, color.B, color.A);
        }

        public static bool IsPed(Entity entity)
        {
            return Function.Call<bool>(Hash.IS_ENTITY_A_PED, entity);
        }

        public static bool IsVehicle(Entity entity)
        {
            return Function.Call<bool>(Hash.IS_ENTITY_A_VEHICLE, entity);
        }
    }
}
