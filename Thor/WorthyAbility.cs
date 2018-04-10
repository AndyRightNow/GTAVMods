using GTA;
using GTA.Math;
using GTA.Native;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using System.Linq;

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
        private static float RAY_CAST_MAX_DISTANCE = 10000.0f;
        private static int MAX_TARGET_COUNT = 15;
        private static Vector3 THROW_HAMMER_Z_AXIS_PRECISION_COMPENSATION = new Vector3(0.0f, 0.0f, 5.0f);

        private static WorthyAbility instance;
        private Ped attachedPed;
        private Mjonir hammer;
        private bool isHammerAttackingTargets;
        private bool isCollectingTargets;
        private HashSet<Entity> targets;

        private WorthyAbility()
        {
            isCollectingTargets = false;
            targets = new HashSet<Entity>();
            hammer = Mjonir.Instance;
            isHammerAttackingTargets = false;
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
            attachedPed.CanSufferCriticalHits = false;
            attachedPed.CanRagdoll = true;
            Function.Call(Hash.SET_ENTITY_CAN_BE_DAMAGED, attachedPed, false);
        }

        public void OnTick()
        {
            if (IsHoldingHammer())
            {
                HandleFlying();
                CollectTargets();
                HandleThrowingMjonir();
            }
            else
            {
                attachedPed.CanRagdoll = true;
                HandleCallingForMjonir();
                if (targets.Count > 0 && isHammerAttackingTargets)
                {
                    isHammerAttackingTargets = hammer.MoveToTargets(ref targets);
                }
            }

            DrawMarkersOnTargets();
            DrawLineToHammer();
        }

        private void HandleFlying()
        {
            if (Game.IsControlPressed(0, GTA.Control.Sprint) &&
                Game.IsControlPressed(0, GTA.Control.Jump))
            {
                attachedPed.CanRagdoll = true;
                UI.ShowSubtitle("here");
                Function.Call(Hash.SET_PED_TO_RAGDOLL, attachedPed, 2, 1000, 3, 0, 0, 0);
                attachedPed.Weapons.CurrentWeaponObject.Velocity = new Vector3(0.0f, 0.0f, 50.0f);
            }
            else
            {
                attachedPed.CanRagdoll = false;
            }
        }

        private void DrawLineToHammer()
        {
            if (hammer.WeaponObject.Exists())
            {
                NativeHelper.DrawLine(attachedPed.Position, hammer.Position, Color.Red);
            }
        }

        private void HandleCallingForMjonir()
        {
            if (Game.IsKeyPressed(Keys.H))
            {
                CallForMjonir();
            }
            else if (Game.IsKeyPressed(Keys.B))
            {
                CallForMjonir(true);
            }
        }

        private void HandleThrowingMjonir()
        {
            if (Game.IsControlPressed(0, GTA.Control.Aim))
            {
                UI.ShowHudComponentThisFrame(HudComponent.Reticle);

                if (Game.IsKeyPressed(Keys.T))
                {
                    ThrowMjonir(ref targets);
                    isCollectingTargets = false;
                }
            }
        }

        private void CollectTargets()
        {
            if (Game.IsControlPressed(0, GTA.Control.Aim))
            {
                isCollectingTargets = true;
                var result = World.Raycast(attachedPed.Position, GameplayCamera.Direction, RAY_CAST_MAX_DISTANCE, IntersectOptions.Everything);
                NativeHelper.DrawLine(attachedPed.Position, attachedPed.Position + GameplayCamera.Direction * RAY_CAST_MAX_DISTANCE, Color.Black);
                if (targets.Count < MAX_TARGET_COUNT &&
                    result.DitHitEntity &&
                    IsValidHitEntity(result.HitEntity))
                {
                    targets.Add(result.HitEntity);
                }
            }
            else if (Game.IsControlJustReleased(0, GTA.Control.Aim) && isCollectingTargets)
            {
                isCollectingTargets = false;
                targets.Clear();
            }
        }

        private bool IsValidHitEntity(Entity entity)
        {
            return entity != null &&
                 entity != attachedPed &&
                 (NativeHelper.IsPed(entity) ||
                 NativeHelper.IsVehicle(entity));
        }

        private void DrawMarkersOnTargets()
        {
            foreach (var target in targets)
            {
                World.DrawMarker(
                    MarkerType.UpsideDownCone,
                    target.Position + new Vector3(0.0f, 0.0f, 2.0f),
                    GameplayCamera.Direction,
                    Vector3.Zero,
                    new Vector3(1.0f, 1.0f, 1.0f), Color.Red
                );
            }
        }

        private bool HasHammer()
        {
            return attachedPed.Weapons.HasWeapon(hammer.WeaponHash);
        }

        public bool IsHoldingHammer()
        {
            return HasHammer() && attachedPed.Weapons.Current.Hash == hammer.WeaponHash;
        }

        public void CallForMjonir(bool shootUpwardFirst = false)
        {
            isHammerAttackingTargets = false;
            targets.Clear();
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
                Script.Wait(1);
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
            Script.Wait(1);

            if (shootUpwardFirst)
            {
                hammer.WeaponObject.Velocity = new Vector3(0.0f, 0.0f, 1000.0f);
                Script.Wait(500);
            }

            hammer.MoveToCoord(rightHandBonePos, true);
        }

        public void ThrowMjonir(ref HashSet<Entity> targets)
        {
            if (!IsHoldingHammer())
            {
                return;
            }

            if (targets.Count == 0)
            {
                isHammerAttackingTargets = false;
                ThrowMjonir();
                return;
            }

            if (targets.Count > 0)
            {
                isHammerAttackingTargets = true;
                Vector3 firstTargetPosition = targets.ToList().First().Position;
                PlayThrowHammerAnimation((firstTargetPosition - attachedPed.Position).Normalized);
                ThrowHammerOut(false);
            }
        }

        public void ThrowMjonir()
        {
            if (!IsHoldingHammer())
            {
                return;
            }
            PlayThrowHammerAnimation(GameplayCamera.Direction);
            ThrowHammerOut();
        }

        private void ThrowHammerOut(bool hasInitialVelocity = true)
        {
            hammer.WeaponObject = Function.Call<Entity>(Hash.GET_WEAPON_OBJECT_FROM_PED, attachedPed);
            attachedPed.Weapons.Remove(hammer.WeaponHash);
            hammer.WeaponObject.Velocity = GameplayCamera.Direction * THROW_HAMMER_SPEED_MULTIPLIER + THROW_HAMMER_Z_AXIS_PRECISION_COMPENSATION;
        }

        private void PlayThrowHammerAnimation(Vector3 directionToTurnTo)
        {
            var animationActionList = new List<AnimationActions>
                {
                    AnimationActions.ThrowHammer1,
                    AnimationActions.ThrowHammer2,
                    AnimationActions.ThrowHammer3,
                    AnimationActions.ThrowHammer4,
                    AnimationActions.ThrowHammer5
                }.ToArray();
            AnimationActions randomAction = Utilities.Random.PickOne(animationActionList);
            float angleBetweenPedForwardAndCamDirection = Utilities.Math.Angle(
                new Vector2(attachedPed.ForwardVector.X, attachedPed.ForwardVector.Y),
                new Vector2(directionToTurnTo.X, directionToTurnTo.Y)
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
            Script.Wait(NativeHelper.GetAnimationWaitTimeByDictNameAndAnimName(dictName, animName));
        }
    }
}
