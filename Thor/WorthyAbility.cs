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
        private static float THROW_HAMMER_SPEED_MULTIPLIER = 100.0f;
        private static Vector3 THROW_HAMMER_Z_AXIS_PRECISION_COMPENSATION = new Vector3(0.0f, 0.0f, 5.0f);

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

        private bool IsHoldingHammer()
        {
            return HasHammer() && Function.Call<int>(Hash.GET_SELECTED_PED_WEAPON, attachedPed) == hammer.WeaponHash;
        }

        public void CallForMjonir(bool shootUpwardFirst = false, Mjonir.Wait waitFn = null)
        {
            AnimationActions randomCallingAction = Utilities.Random.PickOne(
                new List<AnimationActions>
                {
                    AnimationActions.CallingForMjonir,
                    AnimationActions.CallingForMjonir1,
                    AnimationActions.CallingForMjonir2,
                    AnimationActions.CallingForMjonir3
                }.ToArray()
            );
            string dictName = NativeHelper.GetAnimationDictNameByAction(randomCallingAction);
            string animName = NativeHelper.GetAnimationNameByAction(randomCallingAction);

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

            if (shootUpwardFirst && waitFn != null)
            {
                NativeHelper.SetEntityVelocity(hammer.WeaponObject, new Vector3(0.0f, 0.0f, 1000.0f));
                waitFn(500);
            }

            hammer.MoveToCoordWithPhysics(rightHandBonePos, true);
        }

        public void ClearCallingForMjonirAnimations()
        {
            string dictName = NativeHelper.GetAnimationDictNameByAction(AnimationActions.CallingForMjonir);
            string animName = NativeHelper.GetAnimationNameByAction(AnimationActions.CallingForMjonir);

            NativeHelper.ClearPlayerAnimation(attachedPed, dictName, animName);
        }

        public void ThrowMjonir(Mjonir.Wait waitFn)
        {
            if (!IsHoldingHammer())
            {
                return;
            }
            AnimationActions randomAction = Utilities.Random.PickOne(
                new List<AnimationActions> 
                {
                    AnimationActions.ThrowHammer1,
                    AnimationActions.ThrowHammer2,
                    AnimationActions.ThrowHammer3,
                    AnimationActions.ThrowHammer4,
                    AnimationActions.ThrowHammer5
                }.ToArray()
            );
            string dictName = NativeHelper.GetAnimationDictNameByAction(randomAction);
            string animName = NativeHelper.GetAnimationNameByAction(randomAction);
            NativeHelper.PlayPlayerAnimation(
                attachedPed,
                dictName,
                animName,
                attachedPed.IsWalking || attachedPed.IsSprinting || attachedPed.IsRunning ? 
                    AnimationFlags.UpperBodyOnly | AnimationFlags.AllowRotation :
                    AnimationFlags.None
            );
            waitFn(NativeHelper.GetAnimationWaitTimeByAction(randomAction));
            hammer.WeaponObject = Function.Call<Rope>(Hash.GET_WEAPON_OBJECT_FROM_PED, attachedPed);
            Function.Call(Hash.REMOVE_WEAPON_FROM_PED, attachedPed, hammer.WeaponHash);
            NativeHelper.SetEntityVelocity(hammer.WeaponObject, GameplayCamera.Direction * THROW_HAMMER_SPEED_MULTIPLIER + THROW_HAMMER_Z_AXIS_PRECISION_COMPENSATION);
        }
    }
}
