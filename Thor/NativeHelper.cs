using GTA;
using GTA.Math;
using GTA.Native;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

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
        CallingForMjonir1,
        CallingForMjonir2,
        CallingForMjonir3
    }
    
    class NativeHelper
    {
        private static string[] AnimationDictNames = (new List<string> 
        {
            "combat@aim_variations@1h@gang",
            "anim@melee@machete@streamed_core@",
            "anim@melee@machete@streamed_core@",
            "melee@small_wpn@streamed_core_fps",
            "melee@small_wpn@streamed_core_fps",
            "melee@small_wpn@streamed_core",
            "combat@fire_variations@1h@gang",
            "guard_reactions",
            "cover@weapon@1h"
        }).ToArray<string>();
        private static string[] AnimationNames = (new List<string>
        {
            "aim_variation_d",
            "small_melee_wpn_short_range_0",
            "plyr_walking_attack_a",
            "small_melee_wpn_short_range_0",
            "small_melee_wpn_long_range_0",
            "small_melee_wpn_long_range_0",
            "fire_variation_e",
            "1hand_right_trans",
            "outro_hi_r_corner_short"
        }).ToArray<string>();
        private static int[] AnimationWaitTime = (new List<int>
        {
            0,
            800,
            900,
            600,
            900,
            900,
            0,
            0,
            0
        }).ToArray<int>();

        public static int GetAnimationWaitTimeByAction(AnimationActions action)
        {
            return AnimationWaitTime[(int)action];
        }

        public static string GetAnimationDictNameByAction(AnimationActions action)
        {
            return AnimationDictNames[(int)action];
        }

        public static string GetAnimationNameByAction(AnimationActions action)
        {
            return AnimationNames[(int)action];
        }

        public static void ClearPlayerAnimation(Ped ped, string dictName, string animName)
        {
            Function.Call(Hash.STOP_ANIM_TASK, ped, dictName, animName, 3);
        }

        public static void PlayPlayerAnimation(Ped ped, string dictName, string animName, AnimationFlags flag, int duration = -1)
        {
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

        public static Rope CreateWeaponObject(WeaponHash weaponHash, int amountCount, Vector3 position, bool showWorldModel = true, float heading = 1.0f)
        {
            Function.Call(Hash.REQUEST_WEAPON_ASSET, (int)weaponHash);
            if (!Function.Call<bool>(Hash.HAS_WEAPON_ASSET_LOADED, (int)weaponHash))
            {
                Function.Call(Hash.REQUEST_WEAPON_ASSET, (int)weaponHash);
            }
            
            return Function.Call<Rope>(Hash.CREATE_WEAPON_OBJECT, (int)weaponHash, amountCount, position.X, position.Y, position.Z, showWorldModel, heading);
        }
    }
}
