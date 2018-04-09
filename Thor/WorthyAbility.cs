using GTA;
using GTA.Math;
using GTA.Native;
using System;
using System.Collections.Generic;

namespace Thor
{
    class WorthyAbility
    {
        private static float MINIMUM_DISTANCE_BETWEEN_HAMMER_AND_PED_HAND = 5.0f;
        private static int CALLING_FOR_MJONIR_ANIMATION_DURATION = 650;
        private static int CATCHING_MJONIR_ANIMATION_DURATION = 250;
        private static Bone HAMMER_HOLDING_HAND_ID = Bone.PH_R_Hand;
        private static float THROW_HAMMER_SPEED_MULTIPLIER = 100.0f;
        private static float ANIMATION_ANGLE_RANGE_STEP = 45.0f;
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
            return attachedPed.Weapons.HasWeapon(hammer.WeaponHash);
        }

        private bool IsHoldingHammer()
        {
            return HasHammer() && attachedPed.Weapons.Current.Hash == hammer.WeaponHash;
        }

        public void CallForMjonir(bool shootUpwardFirst = false)
        {
            Vector3 rightHandBonePos = attachedPed.GetBoneCoord(HAMMER_HOLDING_HAND_ID);
            Vector3 fromHammerToPedHand = rightHandBonePos - hammer.Position;

            float distanceBetweenHammerToPedHand = Math.Abs(fromHammerToPedHand.Length());
            if (distanceBetweenHammerToPedHand <= MINIMUM_DISTANCE_BETWEEN_HAMMER_AND_PED_HAND)
            {
                Function.Call(Hash.GIVE_WEAPON_OBJECT_TO_PED, hammer.WeaponObject, attachedPed);
                AnimationActions randomCatchingAction = Utilities.Random.PickOne(
                    new List<AnimationActions>
                    {
                        AnimationActions.CatchingMjonir1,
                        AnimationActions.CatchingMjonir2,
                        AnimationActions.CatchingMjonir3
                    }.ToArray()
                );
                string catchDictName = NativeHelper.GetAnimationDictNameByAction(randomCatchingAction);
                string catchAnimName = NativeHelper.GetAnimationNameByAction(randomCatchingAction);
                NativeHelper.PlayPlayerAnimation(
                    attachedPed,
                    catchDictName,
                    catchAnimName,
                    AnimationFlags.UpperBodyOnly | AnimationFlags.AllowRotation,
                    CATCHING_MJONIR_ANIMATION_DURATION
                );
                Script.Wait(50);
                return;
            }

            AnimationActions randomCallingAction = Utilities.Random.PickOne(
                new List<AnimationActions>
                {
                    AnimationActions.CallingForMjonir
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
            Script.Wait(50);

            if (shootUpwardFirst)
            {
                hammer.WeaponObject.Velocity = new Vector3(0.0f, 0.0f, 1000.0f);
                Script.Wait(500);
            }

            hammer.MoveToCoordWithPhysics(rightHandBonePos, true);
        }

        public void ThrowMjonir()
        {
            if (!IsHoldingHammer())
            {
                return;
            }
            var animationActionList = new List<AnimationActions>
                {
                    AnimationActions.ThrowHammer1,
                    AnimationActions.ThrowHammer2,
                    AnimationActions.ThrowHammer3,
                    AnimationActions.ThrowHammer4,
                    AnimationActions.ThrowHammer5
                }.ToArray();
            AnimationActions randomAction = Utilities.Random.PickOne(animationActionList);
            float angleBetweenPedForwardAndCamDirection = Vector2.SignedAngle(
                new Vector2(attachedPed.ForwardVector.X, attachedPed.ForwardVector.Y),
                new Vector2(GameplayCamera.Direction.X, GameplayCamera.Direction.Y)
            );
            bool toLeft = angleBetweenPedForwardAndCamDirection < 0;
            angleBetweenPedForwardAndCamDirection = Math.Abs(angleBetweenPedForwardAndCamDirection);

            bool useDefaultAnimation = angleBetweenPedForwardAndCamDirection < ANIMATION_ANGLE_RANGE_STEP &&
                angleBetweenPedForwardAndCamDirection >= 0;
            if (!useDefaultAnimation)
            {
                randomAction = Utilities.Random.PickOneIf(animationActionList, NativeHelper.DoesAnimationActionHaveAngles);
            }

            string dictName = NativeHelper.GetAnimationDictNameByAction(randomAction);
            string animName = NativeHelper.GetAnimationNameByAction(randomAction);
            if (!useDefaultAnimation)
            {
                string animationAngle = "180";
                if (angleBetweenPedForwardAndCamDirection >= ANIMATION_ANGLE_RANGE_STEP &&
                    angleBetweenPedForwardAndCamDirection < ANIMATION_ANGLE_RANGE_STEP * 3)
                {
                    animationAngle = "90";
                }
                
                animName = animName.Replace("_0", "_" + (toLeft ? "+" : "-") + animationAngle);
            }
            UI.ShowSubtitle(String.Format("{0}: {1}", dictName, animName));
            NativeHelper.PlayPlayerAnimation(
                attachedPed,
                dictName,
                animName,
                attachedPed.IsWalking || attachedPed.IsSprinting || attachedPed.IsRunning ?
                    AnimationFlags.UpperBodyOnly | AnimationFlags.AllowRotation :
                    AnimationFlags.None,
                -1,
                false
            );
            Script.Wait(NativeHelper.GetAnimationWaitTimeByAction(randomAction));
            hammer.WeaponObject = Function.Call<Entity>(Hash.GET_WEAPON_OBJECT_FROM_PED, attachedPed);
            attachedPed.Weapons.Remove(hammer.WeaponHash);
            hammer.WeaponObject.Velocity = GameplayCamera.Direction * THROW_HAMMER_SPEED_MULTIPLIER + THROW_HAMMER_Z_AXIS_PRECISION_COMPENSATION;
        }
    }
}
