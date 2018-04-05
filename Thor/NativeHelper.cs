using GTA;
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
    }
    
    class NativeHelper
    {
        private static string[] AnimationDictNames = new string[1] { "combat@aim_variations@1h@gang" };
        private static string[] AnimationNames = new string[1] { "aim_variation_d" };

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
            if (!Function.Call<bool>(Hash.IS_ENTITY_PLAYING_ANIM, ped, dictName, animName, 3))
            {
                //Function.Call(Hash.CLEAR_PED_TASKS_IMMEDIATELY, Game.Player.Character);
            }
            Function.Call(Hash.TASK_PLAY_ANIM, ped, dictName, animName, 8.0f, 1.0f, duration, (int)flag, -8.0f, 0, 0, 0);
        }

        public static Tasks TaskInvoker
        {
            get
            {

                return new TaskSequence().AddTask;
            }
        }
    }
}
