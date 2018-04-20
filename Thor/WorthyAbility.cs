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
        private static float AUTO_RETURN_TO_NEW_APPLIED_PED_POSITION_Z_AXIS = 2000.0f;
        private static float ANIMATION_ANGLE_RANGE_STEP = 45.0f;
        private static float RAY_CAST_MAX_DISTANCE = 10000.0f;
        private static float FLY_UPWARD_VELOCITY = 50.0f;
        private static float FLY_HORIZONTAL_VELOCITY_LEVEL_1 = 70.0f;
        private static int FLY_WITH_THROWN_HAMMER_MAX_TIME = 2000;
        private static float FLY_SPRINT_VELOCITY_MULTIPLIER = 4f;
        private static float RANGE_TO_LOOK_FOR_CLOSEST_ENTITY = 20.0f;
        private static float HAMMER_HIT_FORCE = 250.0f;
        private static int MAX_TARGET_COUNT = 15;
        private static Vector3 THROW_HAMMER_Z_AXIS_PRECISION_COMPENSATION = new Vector3(0.0f, 0.0f, 5.0f);

        private static WorthyAbility instance;
        private Ped attachedPed;
        private bool isHammerAttackingTargets;
        private bool isCollectingTargets;
        private bool hasJustSetEndOfFlyingInitialVelocity;
        private bool isFlyingWithThrownHammer;
        private Vector3 flyWithThrownHammerDirection;
        private int flyWithThrownHammerStartTime;
        private Vector3 previousPedVelocity;
        private bool shouldHammerReturnToPed;
        private bool isFlying;
        private HashSet<Entity> targets;

        private WorthyAbility()
        {
            isCollectingTargets = false;
            targets = new HashSet<Entity>();
            Hammer = Mjolnir.Instance;
            isHammerAttackingTargets = false;
            previousPedVelocity = Vector3.Zero;
            isFlying = false;
            hasJustSetEndOfFlyingInitialVelocity = false;
            isFlyingWithThrownHammer = false;
            flyWithThrownHammerDirection = Vector3.Zero;
            flyWithThrownHammerStartTime = 0;
            shouldHammerReturnToPed = false;
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

        public bool IsAttachedToPed
        {
            get
            {
                return attachedPed != null;
            }
        }

        public void RemoveAbility()
        {
            if (HasHammer())
            {
                ThrowHammerOut(false);
            }
            if (attachedPed != null)
            {
                SetInvincible(false);
                attachedPed = null;
            }
        }

        public void ApplyOn(Ped ped)
        {
            if (IsAttachedToPed)
            {
                return;
            }

            attachedPed = ped;
            attachedPed.CanRagdoll = true;


            if (Hammer != null &&
                Hammer.WeaponObject != null &&
                !HasHammer())
            {
                Hammer.MoveToCoord(attachedPed.Position + new Vector3(0.0f, 0.0f, AUTO_RETURN_TO_NEW_APPLIED_PED_POSITION_Z_AXIS), true);
            }
        }

        public Mjolnir Hammer { get; }

        public void OnTick()
        {
            SetInvincible(true);
            HandleMeleeForces();

            if (IsHoldingHammer())
            {
                Function.Call<bool>(Hash.SET_PLAYER_LOCKON_RANGE_OVERRIDE, Game.Player.Handle, 0.0f);
                HandleFlying();
                CollectTargets();
                HandleThrowingMjolnir();
                DrawMarkersOnTargets();
            }
            else
            {
                attachedPed.CanRagdoll = true;
                InitHammerIfNotExist();
                ShowHammerPFX();
                HandleCallingForMjolnir();
                HandleAttackingTargets();
                DrawLineToHammer();
            }
        }

        private void HandleAttackingTargets()
        {
            var previousIsHammerAttackingTargets = isHammerAttackingTargets;
            if (targets.Count > 0 && isHammerAttackingTargets)
            {
                isHammerAttackingTargets = Hammer.MoveToTargets(ref targets);
            }
            else if (targets.Count == 0)
            {
                isHammerAttackingTargets = false;
            }
            if (previousIsHammerAttackingTargets != isHammerAttackingTargets &&
                !isHammerAttackingTargets)
            {
                shouldHammerReturnToPed = true;
            }

            if (shouldHammerReturnToPed)
            {
                CallForMjolnir();
            }
        }

        private void InitHammerIfNotExist()
        {
            if (!HasHammer())
            {
                Hammer.Init(attachedPed);
            }
        }

        private void ShowHammerPFX()
        {
            if (Hammer.IsMoving)
            {
                Hammer.ShowParticleFx();
            }
        }

        private void HandleMeleeForces()
        {
            if (attachedPed.IsInVehicle())
            {
                return;
            }

            Ped[] closestPeds = World.GetNearbyPeds(attachedPed, RANGE_TO_LOOK_FOR_CLOSEST_ENTITY);
            Vehicle[] closestVehicles = World.GetNearbyVehicles(attachedPed, RANGE_TO_LOOK_FOR_CLOSEST_ENTITY);

            foreach (var ped in closestPeds)
            {
                if (ped.HasBeenDamagedBy(attachedPed))
                {
                    NativeHelper.SetPedToRagdoll(ped, RagdollType.Normal, 100, 100);
                    ped.ApplyForce(attachedPed.ForwardVector * HAMMER_HIT_FORCE);
                    Function.Call(Hash.CLEAR_ENTITY_LAST_DAMAGE_ENTITY, ped);
                }
            }

            foreach (var veh in closestVehicles)
            {
                if (veh.HasBeenDamagedBy(attachedPed))
                {
                    veh.ApplyForce(attachedPed.ForwardVector * HAMMER_HIT_FORCE, Vector3.Zero, ForceType.MaxForceRot);
                    Function.Call(Hash.CLEAR_ENTITY_LAST_DAMAGE_ENTITY, veh);
                }
            }
        }

        private void SetInvincible(bool toggle)
        {
            attachedPed.CanSufferCriticalHits = !toggle;
            Function.Call(Hash.SET_ENTITY_CAN_BE_DAMAGED, attachedPed, !toggle);
            attachedPed.IsInvincible = toggle;
            attachedPed.Health = attachedPed.MaxHealth;
            attachedPed.Armor = 100;
            attachedPed.AlwaysDiesOnLowHealth = !toggle;
            attachedPed.IsBulletProof = toggle;
            attachedPed.IsCollisionProof = toggle;
            attachedPed.IsExplosionProof = toggle;
            attachedPed.IsFireProof = toggle;
            attachedPed.IsMeleeProof = toggle;
        }

        private void HandleFlying()
        {
            GameplayCamera.ClampYaw(-180.0f, 180.0f);
            GameplayCamera.ClampPitch(-180.0f, 180.0f);
            var velocity = Vector3.Zero;

            if (isFlyingWithThrownHammer)
            {
                int endTime = flyWithThrownHammerStartTime + FLY_WITH_THROWN_HAMMER_MAX_TIME;

                if (Game.GameTime < endTime)
                {
                    velocity += flyWithThrownHammerDirection * FLY_HORIZONTAL_VELOCITY_LEVEL_1 * 2;
                }
                else
                {
                    isFlyingWithThrownHammer = false;
                }
            }

            if (Game.IsKeyPressed(Keys.J))
            {
                velocity.Z = FLY_UPWARD_VELOCITY;
            }
            if (attachedPed.IsInAir)
            {
                if (Game.IsControlPressed(0, GTA.Control.ScriptPadUp) ||
                    Game.IsKeyPressed(Keys.W))
                {
                    velocity += GameplayCamera.Direction * FLY_HORIZONTAL_VELOCITY_LEVEL_1;
                }
            }
            if (Game.IsControlPressed(0, GTA.Control.Sprint))
            {
                velocity += Vector3.Multiply(velocity, FLY_SPRINT_VELOCITY_MULTIPLIER);
            }
            if (isFlying)
            {
                hasJustSetEndOfFlyingInitialVelocity = false;
                previousPedVelocity = attachedPed.Velocity;
            }

            if (velocity.Length() > 0)
            {
                isFlying = true;
                GameplayCamera.Shake(CameraShake.SkyDiving, 0.1f);
                Function.Call(Hash.DISABLE_PED_PAIN_AUDIO, attachedPed, true);
                SetAttachedPedToRagdoll();
                attachedPed.Weapons.CurrentWeaponObject.Velocity = velocity;
            }
            else
            {
                isFlying = false;
                attachedPed.CanRagdoll = !attachedPed.IsInAir;
                if (!isFlying && !hasJustSetEndOfFlyingInitialVelocity)
                {
                    attachedPed.Velocity = previousPedVelocity;
                    hasJustSetEndOfFlyingInitialVelocity = true;
                }
            }
        }

        private void SetAttachedPedToRagdoll()
        {
            attachedPed.CanRagdoll = true;
            NativeHelper.SetPedToRagdoll(attachedPed, RagdollType.WideLegs, 2, 1000);
        }

        private void DrawLineToHammer()
        {
            if (Hammer.WeaponObject != null && Hammer.WeaponObject.Exists())
            {
                NativeHelper.DrawLine(attachedPed.Position, Hammer.Position, Color.Red);
            }
        }

        private void HandleCallingForMjolnir()
        {
            if (Game.IsKeyPressed(Keys.H))
            {
                CallForMjolnir();
            }
            else if (Game.IsKeyPressed(Keys.B))
            {
                CallForMjolnir(true);
            }
        }

        private void HandleThrowingMjolnir()
        {
            if (Game.IsControlPressed(0, GTA.Control.Aim))
            {
                UI.ShowHudComponentThisFrame(HudComponent.Reticle);

                if (Game.IsKeyPressed(Keys.T))
                {
                    ThrowMjolnir(ref targets);
                    isCollectingTargets = false;
                }
                else if (Game.IsKeyPressed(Keys.U))
                {
                    isCollectingTargets = false;
                    ThrowAndFlyWithMjolnir();
                }
            }
            else if (Game.IsKeyPressed(Keys.Y))
            {
                ThrowHammerOut(false);
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
            return IsAttachedToPed && attachedPed.Weapons.HasWeapon(Hammer.WeaponHash);
        }

        public bool IsHoldingHammer()
        {
            return HasHammer() && attachedPed.Weapons.Current.Hash == Hammer.WeaponHash;
        }

        public void CallForMjolnir(bool shootUpwardFirst = false)
        {
            isHammerAttackingTargets = false;
            targets.Clear();
            Vector3 rightHandBonePos = attachedPed.GetBoneCoord(HAMMER_HOLDING_HAND_ID);
            Vector3 fromHammerToPedHand = rightHandBonePos - Hammer.Position;

            float distanceBetweenHammerToPedHand = Math.Abs(fromHammerToPedHand.Length());
            if (distanceBetweenHammerToPedHand <= MINIMUM_DISTANCE_BETWEEN_HAMMER_AND_PED_HAND)
            {
                Function.Call(Hash.GIVE_WEAPON_OBJECT_TO_PED, Hammer.WeaponObject, attachedPed);
                AnimationActions randomCatchingAction = Utilities.Random.PickOne(
                    new List<AnimationActions>
                    {
                        AnimationActions.CatchingMjolnir1,
                        AnimationActions.CatchingMjolnir2,
                        AnimationActions.CatchingMjolnir3
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
                shouldHammerReturnToPed = false;
                return;
            }

            AnimationActions randomCallingAction = Utilities.Random.PickOne(
                new List<AnimationActions>
                {
                    AnimationActions.CallingForMjolnir
                }.ToArray()
            );
            string dictName = NativeHelper.GetAnimationDictNameByAction(randomCallingAction);
            string animName = NativeHelper.GetAnimationNameByAction(randomCallingAction);

            if (!IsAttachedToPed ||
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
                Hammer.WeaponObject.Velocity = new Vector3(0.0f, 0.0f, 1000.0f);
                Script.Wait(500);
            }

            Hammer.MoveToCoord(rightHandBonePos, true);
        }

        public void ThrowAndFlyWithMjolnir()
        {
            if (!IsHoldingHammer())
            {
                return;
            }
            PlayThrowHammerAnimation(GameplayCamera.Direction);
            SetAttachedPedToRagdoll();
            isFlyingWithThrownHammer = true;
            flyWithThrownHammerDirection = GameplayCamera.Direction;
            flyWithThrownHammerStartTime = Game.GameTime;
        }

        public void ThrowMjolnir(ref HashSet<Entity> targets)
        {
            if (!IsHoldingHammer())
            {
                return;
            }

            if (targets.Count == 0)
            {
                isHammerAttackingTargets = false;
                ThrowMjolnir();
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

        public void ThrowMjolnir()
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
            if (IsHoldingHammer())
            {
                Hammer.WeaponObject = Function.Call<Entity>(Hash.GET_WEAPON_OBJECT_FROM_PED, attachedPed);
            }
            attachedPed.Weapons.Remove(Hammer.WeaponHash);
            if (hasInitialVelocity)
            {
                var hammerVelocity = GameplayCamera.Direction * THROW_HAMMER_SPEED_MULTIPLIER + THROW_HAMMER_Z_AXIS_PRECISION_COMPENSATION;
                Hammer.WeaponObject.Velocity = hammerVelocity;
            }
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
                attachedPed.IsWalking || attachedPed.IsSprinting || attachedPed.IsRunning || attachedPed.IsInAir ?
                    AnimationFlags.UpperBodyOnly | AnimationFlags.AllowRotation :
                    AnimationFlags.None,
                -1,
                false
            );
            Script.Wait(NativeHelper.GetAnimationWaitTimeByDictNameAndAnimName(dictName, animName));
        }
    }
}
