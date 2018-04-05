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
    class WorthyAbility
    {
        private static float MINIMUM_DISTANCE_BETWEEN_HAMMER_AND_PED_HAND = 1.0f;
        private static int CALLING_FOR_MJONIR_ANIMATION_DURATION = 650;
        private static int HAMMER_HOLDING_HAND_ID = (int)Bone.PH_R_Hand;
        private static float HAMMER_MOVEMENT_LERP_AMOUNT = 0.3f;

        private static WorthyAbility instance;
        private Ped attachedPed;
        private Mjonir hammer;

        private WorthyAbility()
        {
            hammer = Mjonir.Instance;
        }

        public static WorthyAbility Instance
        {
            get
            {
                if (instance == null)
                {
                    instance = new WorthyAbility();
                }

                return instance;
            }
        }

        public void ApplyOn(Ped ped)
        {
            if (attachedPed != null)
            {
                return;
            }

            attachedPed = ped;
        }

        private bool HasHammer()
        {
            return Function.Call<bool>(Hash.HAS_PED_GOT_WEAPON, attachedPed, hammer.WeaponHash, 0);
        }

        public void CallForMjonir()
        {
            string dictName = NativeHelper.GetAnimationDictNameByAction(AnimationActions.CallingForMjonir);
            string animName = NativeHelper.GetAnimationNameByAction(AnimationActions.CallingForMjonir);

            if (attachedPed == null ||
                HasHammer())
            {
                return;
            }

            NativeHelper.PlayPlayerAnimation(
                attachedPed,
                dictName,
                animName,
                AnimationFlags.UpperBodyOnly | AnimationFlags.AllowRotation,
                CALLING_FOR_MJONIR_ANIMATION_DURATION
            );
            Vector3 rightHandBonePos = Function.Call<Vector3>(Hash.GET_PED_BONE_COORDS, attachedPed, HAMMER_HOLDING_HAND_ID, 0.0f, 0.0f, 0.0f);
            Vector3 fromHammerToPedHand = rightHandBonePos - hammer.Position;

            float distanceBetweenHammerToPedHand = Math.Abs(fromHammerToPedHand.Length());
            if (distanceBetweenHammerToPedHand <= MINIMUM_DISTANCE_BETWEEN_HAMMER_AND_PED_HAND)
            {
                Function.Call(Hash.GIVE_WEAPON_OBJECT_TO_PED, hammer.WeaponObject, attachedPed);
                return;
            }

            hammer.MoveToCoordWithPhysics(Vector3.Lerp(hammer.Position, rightHandBonePos, HAMMER_MOVEMENT_LERP_AMOUNT));

        }

        public void ClearCallingForMjonirAnimations()
        {
            string dictName = NativeHelper.GetAnimationDictNameByAction(AnimationActions.CallingForMjonir);
            string animName = NativeHelper.GetAnimationNameByAction(AnimationActions.CallingForMjonir);

            NativeHelper.ClearPlayerAnimation(attachedPed, dictName, animName);
        }

        public void ThrowMjonir()
        {
            if (!HasHammer())
            {
                return;
            }

            hammer.WeaponObject = Function.Call<Rope>(Hash.GET_WEAPON_OBJECT_FROM_PED, attachedPed);
            Function.Call(Hash.REMOVE_WEAPON_FROM_PED, attachedPed, hammer.WeaponHash);
        }
    }
}
