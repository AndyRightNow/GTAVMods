﻿using GTA;
using GTA.Math;
using GTA.Native;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using System.Linq;
using System.IO;
using System.Media;

namespace Thor
{
    class WorthyAbility
    {
        private static float NULL_FLOAT = -1.0f;
        private static float PLAYER_MOVEMENT_MULTIPLIER = 1.5f;
        private static float MINIMUM_DISTANCE_BETWEEN_HAMMER_AND_PED_HAND = 0.6f;
        private static float CLOSE_DISTANCE_BETWEEN_HAMMER_AND_PED_HAND_FOR_SOUND = 140.0f;
        private static float CLOSE_DISTANCE_BETWEEN_HAMMER_AND_PED_HAND_FOR_HAMMER_ROTATION = 5.0f;
        private static float HAMMER_BEFORE_RETURN_TO_PED_WAIT_TIME = 700.0f;
        private static int CATCHING_MJONIR_ANIMATION_DURATION = 250;
        private static Bone HAMMER_HOLDING_HAND_ID = Bone.PH_R_Hand;
        private static float THROW_HAMMER_SPEED_MULTIPLIER = 100.0f;
        private static float AUTO_RETURN_TO_NEW_APPLIED_PED_POSITION_Z_AXIS = 1000.0f;
        private static float ANIMATION_ANGLE_RANGE_STEP = 45.0f;
        private static float RAY_CAST_MAX_DISTANCE = 100000.0f;
        private static float FLY_UPWARD_VELOCITY = 50.0f;
        private static float FLY_HORIZONTAL_VELOCITY_LEVEL_1 = 70.0f;
        private static float AIR_DASH_ATTACK_LANDING_VELOCITY = 200.0f;
        private static int FLY_WITH_THROWN_HAMMER_MAX_TIME = 2000;
        private static float FLY_SPRINT_VELOCITY_MULTIPLIER = 4.0f;
        private static float RANGE_TO_LOOK_FOR_CLOSEST_ENTITY = 20.0f;
        private static int PLAY_THUNDER_FX_INTERVAL_MS = 1000;
        private static int MAX_TARGET_COUNT = 15;
        private static string SOUND_FILE_HAMMER_CLOSE_TO_PLAYER = "./scripts/hammer-close-to-player.wav";
        private static string SOUND_FILE_CATCH_HAMMER = "./scripts/catch-hammer.wav";
        private static Vector3 THROW_HAMMER_Z_AXIS_PRECISION_COMPENSATION = new Vector3(0.0f, 0.0f, 5.0f);

        private static WorthyAbility instance;
        private Ped attachedPed;
        private bool isHammerAttackingTargets;
        private float hammerJustFinishedAttackingTargetsTimestamp;
        private bool isCollectingTargets;
        private bool hasJustSetEndOfFlyingInitialVelocity;
        private bool isFlyingWithThrownHammer;
        private Vector3 flyWithThrownHammerDirection;
        private int flyWithThrownHammerStartTime;
        private Vector3 previousPedVelocity;
        private bool shouldHammerReturnToPed;
        private bool isFlying;
        private HashSet<Entity> targets;
        private bool isInAirDashAttack;
        private ADModUtils.Utilities.Timer pedFxTimer;
        private bool hasSummonedThunder;
        private bool isHoldingHammerRope;
        private ADModUtils.Plane hammerWhirlingPlane;
        private ADModUtils.Plane hammerHoverWhirlingPlane;
        private Entity hammerUsedForHoveringWhirlingOriginal;
        private Entity hammerUsedForHoveringWhirlingShown;
        private bool isHoverWhirling;
        private NAudio.Wave.WaveOut catchHammerSoundPlayer;
        private NAudio.Wave.WaveOut hammerCloseToPlayerSoundPlayer;

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
            isInAirDashAttack = false;
            pedFxTimer = null;
            hasSummonedThunder = false;
            isHoldingHammerRope = false;
            isHoverWhirling = false;
            catchHammerSoundPlayer = new NAudio.Wave.WaveOut();
            hammerCloseToPlayerSoundPlayer = new NAudio.Wave.WaveOut();
            hammerJustFinishedAttackingTargetsTimestamp = NULL_FLOAT;
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
            if (HasHammer)
            {
                ThrowHammerOut(false);
            }
            if (attachedPed != null)
            {
                SetInvincible(false);
                attachedPed = null;
            }
            if (hammerUsedForHoveringWhirlingOriginal != null)
            {
                hammerUsedForHoveringWhirlingOriginal.Delete();
                hammerUsedForHoveringWhirlingOriginal = null;
            }
            if (hammerUsedForHoveringWhirlingShown != null)
            {
                hammerUsedForHoveringWhirlingShown.Delete();
                hammerUsedForHoveringWhirlingShown = null;
            }

            if (pedFxTimer != null)
            {
                pedFxTimer = null;
            }

            hasSummonedThunder = false;
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
                !HasHammer)
            {
                hammerJustFinishedAttackingTargetsTimestamp = Game.GameTime;
                Hammer.FindWaysToMoveToCoord(attachedPed.Position + new Vector3(0.0f, 0.0f, AUTO_RETURN_TO_NEW_APPLIED_PED_POSITION_Z_AXIS), true);
            }
        }

        public Mjolnir Hammer { get; }

        public void OnTick()
        {
            Thunder.Instance.OnTick();
            if (attachedPed == null)
            {
                return;
            }
            InitHoverWhirledHammer();

            SetInvincible(true);
            HandleTimeScaleChange();
            if (pedFxTimer != null)
            {
                pedFxTimer.OnTick();
            }
            HandleMovement();
            HandleLightningAutoAttack(3.0f);
            if (IsHoldingHammer ||
                attachedPed.Weapons.CurrentWeaponObject == null)
            {
                HandleMeleeForces();
            }

            if (Game.IsKeyPressed(Keys.X))
            {
                SummonThunder();
            }
            if (IsHoldingHammer)
            {
                HandleHoverWhirlingHammer();
                HandleFlying();
                Hammer.DestroyHammerTrackCam();
                Hammer.SetSummonStatus(false);
                World.RenderingCamera = null;
                Function.Call<bool>(Hash.SET_PLAYER_LOCKON_RANGE_OVERRIDE, Game.Player.Handle, 0.0f);
                HandleAirDashAttack();
                CollectTargets();
                HandleThrowingMjolnir();
                DrawMarkersOnTargets();
                HandleDropAndHoldHammerRope();
            }
            else
            {

                HandleSummoningMjolnir();
                HandleWhirlingHammer();
                InitHammerIfNotExist();
                Hammer.ApplyForcesToNearbyEntities();
                ShowHammerPFX();
                HandleAttackingTargets();

                if (Game.IsKeyPressed(Keys.R))
                {
                    Hammer.RenderHammerTrackCam();
                }
                else
                {
                    Hammer.CancelRenderHammerTrackCam();
                }
            }
            Hammer.OnTick();
        }

        private void HandleHoverWhirlingHammer()
        {
            if (hammerHoverWhirlingPlane == null)
            {
                hammerHoverWhirlingPlane = new ADModUtils.Plane(Vector3.WorldUp, Vector3.Zero);
            }

            if (isFlying && isHoverWhirling)
            {
                Hammer.Whirl(hammerHoverWhirlingPlane, false, hammerUsedForHoveringWhirlingOriginal);
            }
        }

        private void HandleWhirlingHammer()
        {
            if (Game.IsKeyPressed(Keys.Z))
            {
                if (isHoldingHammerRope)
                {
                    var boneCoord = attachedPed.GetBoneCoord(HAMMER_HOLDING_HAND_ID);

                    hammerWhirlingPlane = new ADModUtils.Plane(Vector3.Cross(attachedPed.ForwardVector, Vector3.WorldUp), boneCoord);

                    ADModUtils.NativeHelper.PlayPlayerAnimation(
                        attachedPed,
                        NativeHelper.Instance.GetAnimationDictNameByAction((uint) AnimationActions.WhirlingHammer),
                        NativeHelper.Instance.GetAnimationNameByAction((uint) AnimationActions.WhirlingHammer),
                        AnimationFlags.UpperBodyOnly | AnimationFlags.AllowRotation
                    );
                    Hammer.Whirl(hammerWhirlingPlane);
                }
            }
            else
            {
                if (isHoldingHammerRope)
                {
                    SummonMjolnir();
                }
            }
        }

        private void HandleDropAndHoldHammerRope()
        {
            if (Game.IsKeyPressed(Keys.Z))
            {
                DropAndHoldHammerRope();
            }
        }

        private void HandleMovement()
        {
            Function.Call(Hash.SET_PED_MOVE_RATE_OVERRIDE, attachedPed, PLAYER_MOVEMENT_MULTIPLIER);
            Function.Call(Hash.SET_SWIM_MULTIPLIER_FOR_PLAYER, Game.Player, PLAYER_MOVEMENT_MULTIPLIER);
            if (hasSummonedThunder)
            {
                Function.Call(Hash.SET_SUPER_JUMP_THIS_FRAME, Game.Player);
            }
        }

        private void SummonThunder()
        {
            if (hasSummonedThunder)
            {
                return;
            }
            hasSummonedThunder = true;

            var prevWeather = World.Weather;
            World.Weather = Weather.ThunderStorm;
            Function.Call(Hash._CREATE_LIGHTNING_THUNDER);
            Function.Call(Hash._CREATE_LIGHTNING_THUNDER);
            Function.Call(Hash._CREATE_LIGHTNING_THUNDER);
            Function.Call(Hash._CREATE_LIGHTNING_THUNDER);
            ADModUtils.NativeHelper.PlayPlayerAnimation(
                attachedPed,
                NativeHelper.Instance.GetAnimationDictNameByAction((uint) AnimationActions.SummonThunder),
                NativeHelper.Instance.GetAnimationNameByAction((uint) AnimationActions.SummonThunder),
                AnimationFlags.None);
            Script.Wait(2000);
            Thunder.Instance.Shoot(attachedPed.Position + new Vector3(0.0f, 0.0f, 1000.0f), attachedPed.Position);
            PlayThunderFx();
            pedFxTimer = new ADModUtils.Utilities.Timer(PLAY_THUNDER_FX_INTERVAL_MS, PlayThunderFx);
            ApplyForcesAndDamagesOnNearbyEntities(true, RANGE_TO_LOOK_FOR_CLOSEST_ENTITY, Vector3.Zero);
            World.Weather = prevWeather;
            Function.Call(Hash._CREATE_LIGHTNING_THUNDER);
            Function.Call(Hash._CREATE_LIGHTNING_THUNDER);
            Function.Call(Hash._CREATE_LIGHTNING_THUNDER);
            Function.Call(Hash._CREATE_LIGHTNING_THUNDER);
        }

        private void PlayThunderFx()
        {
            NativeHelper.PlayThunderFx(attachedPed, Bone.SKEL_L_Forearm);
            NativeHelper.PlayThunderFx(attachedPed, Bone.SKEL_R_Forearm);
            NativeHelper.PlayThunderFx(attachedPed, Bone.SKEL_L_Thigh);
            NativeHelper.PlayThunderFx(attachedPed, Bone.SKEL_R_Thigh);
            if (IsHoldingHammer)
            {
                NativeHelper.PlayThunderFx(attachedPed.Weapons.CurrentWeaponObject, 0.5f);

                if (hammerUsedForHoveringWhirlingShown != null)
                {
                    NativeHelper.PlayThunderFx(hammerUsedForHoveringWhirlingShown, 0.5f);
                }
            }
        }

        private void HandleAirDashAttack()
        {
            if (attachedPed.IsInAir && !isInAirDashAttack)
            {
                if (Game.IsControlPressed(0, GTA.Control.Attack))
                {
                    isInAirDashAttack = true;
                }
            }

            if (isInAirDashAttack)
            {
                if (!attachedPed.IsInAir)
                {
                    isInAirDashAttack = false;
                    ApplyForcesAndDamagesOnNearbyEntities(true, RANGE_TO_LOOK_FOR_CLOSEST_ENTITY, Vector3.Zero);
                }
                else
                {
                    attachedPed.Velocity = new Vector3(0.0f, 0.0f, -AIR_DASH_ATTACK_LANDING_VELOCITY);
                }
            }
        }

        private void HandleTimeScaleChange()
        {
        }

        private void HandleAttackingTargets()
        {
            if (isHammerAttackingTargets)
            {
                if (targets.Count > 0)
                {
                    isHammerAttackingTargets = Hammer.MoveToTargets(ref targets);
                }
                else if (targets.Count == 0)
                {
                    isHammerAttackingTargets = false;
                    if (hammerJustFinishedAttackingTargetsTimestamp == NULL_FLOAT)
                    {
                        hammerJustFinishedAttackingTargetsTimestamp = Game.GameTime;
                    }
                }
            }

            if (!isHammerAttackingTargets &&
                hammerJustFinishedAttackingTargetsTimestamp != NULL_FLOAT &&
                Game.GameTime - hammerJustFinishedAttackingTargetsTimestamp >= HAMMER_BEFORE_RETURN_TO_PED_WAIT_TIME)
            {
                shouldHammerReturnToPed = true;
                hammerJustFinishedAttackingTargetsTimestamp = NULL_FLOAT;
            }

            if (shouldHammerReturnToPed)
            {
                SummonMjolnir();
            }
        }

        private void InitHammerIfNotExist()
        {
            if (!HasHammer)
            {
                Hammer.Init(null);
            }
        }

        private void InitHoverWhirledHammer()
        {
            if (hammerUsedForHoveringWhirlingOriginal == null)
            {
                hammerUsedForHoveringWhirlingOriginal = ADModUtils.NativeHelper.CreateWeaponObject(WeaponHash.Hammer, 1, Vector3.Zero);
                hammerUsedForHoveringWhirlingOriginal.Alpha = 0;
                hammerUsedForHoveringWhirlingOriginal.SetNoCollision(attachedPed, true);
            }
            if (hammerUsedForHoveringWhirlingShown == null)
            {
                hammerUsedForHoveringWhirlingShown = ADModUtils.NativeHelper.CreateWeaponObject(WeaponHash.Hammer, 1, Vector3.Zero + new Vector3(0.0f, 0.0f, 10.0f));
                hammerUsedForHoveringWhirlingShown.Alpha = 0;
                hammerUsedForHoveringWhirlingShown.SetNoCollision(attachedPed, true);
            }
        }

        private void ShowHammerPFX()
        {
            if (Hammer.IsMoving && !isHoldingHammerRope)
            {
                Hammer.ShowParticleFx();
            }
        }

        private void HandleMeleeForces()
        {
            if (attachedPed.IsInVehicle() ||
                !attachedPed.IsInMeleeCombat)
            {
                return;
            }

            ApplyForcesAndDamagesOnNearbyEntities(false, RANGE_TO_LOOK_FOR_CLOSEST_ENTITY, attachedPed.ForwardVector);
        }

        private void ShootLightningsToEnemy(Ped ped)
        {
            if (!hasSummonedThunder)
            {
                return;
            }

            if (ped.IsInCombatAgainst(attachedPed))
            {
                Thunder.Instance.Shoot(attachedPed.Position, ped.Position);
            }
        }

        private void HandleLightningAutoAttack(float range)
        {
            if (!hasSummonedThunder || attachedPed.IsInVehicle())
            {
                return;
            }

            Ped[] nearbyPeds = World.GetNearbyPeds(attachedPed.Position, range);

            foreach (var ped in nearbyPeds)
            {
                ShootLightningsToEnemy(ped);
            }
        }

        private void ApplyForcesAndDamagesOnNearbyEntities(bool applyToAll, float range, Vector3 forceDirection)
        {
            Entity[] closestEntities = World.GetNearbyEntities(attachedPed.Position, range);

            foreach (var ent in closestEntities)
            {
                if (ent == attachedPed ||
                    ent == attachedPed.Weapons.CurrentWeaponObject)
                {
                    continue;
                }
                var isPed = ADModUtils.NativeHelper.IsPed(ent);

                if (isPed)
                {
                    ShootLightningsToEnemy((Ped)ent);
                }

                if (applyToAll || ent.HasBeenDamagedBy(attachedPed))
                {
                    if (hasSummonedThunder)
                    {
                        NativeHelper.PlayThunderFx(ent);
                    }

                    if (forceDirection.Length() == 0)
                    {
                        var defaultForceDirection = (ent.Position - attachedPed.Position).Normalized;
                        NativeHelper.Instance.ApplyForcesAndDamages(ent, defaultForceDirection);
                    }
                    else
                    {
                        NativeHelper.Instance.ApplyForcesAndDamages(ent, forceDirection);
                    }
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

            if (!IsHoldingHammer)
            {
                return;
            }

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

            SetHeldHammerVisible(true);
            var hammerHoldingHandCoord = attachedPed.GetBoneCoord(HAMMER_HOLDING_HAND_ID);
            if (velocity.Length() > 0)
            {
                isFlying = true;
                GameplayCamera.Shake(CameraShake.MediumExplosion, 0.01f);
                SetAttachedPedToRagdoll();
                var velocityAndUpDot = Vector3.Dot(velocity.Normalized, Vector3.WorldUp);
                if (velocityAndUpDot >= 0.85f &&
                    velocityAndUpDot <= 1.0f)
                {
                    isHoverWhirling = true;
                    SetHeldHammerVisible(false);
                }
                else
                {
                    isHoverWhirling = false;
                    SetHeldHammerVisible(true);
                    Hammer.RotateToDirection(attachedPed.Weapons.CurrentWeaponObject, velocity.Normalized);
                    attachedPed.Weapons.CurrentWeaponObject.Position = hammerHoldingHandCoord + velocity.Normalized * 0.3f;
                }
                if (isHoverWhirling)
                {
                    hammerUsedForHoveringWhirlingShown.Position = 
                        attachedPed.GetBoneCoord(HAMMER_HOLDING_HAND_ID) + 
                        (hammerUsedForHoveringWhirlingOriginal.Position - hammerHoverWhirlingPlane.Center) + 
                        new Vector3(0, 0, 0.1f);
                    hammerUsedForHoveringWhirlingShown.Rotation = hammerUsedForHoveringWhirlingOriginal.Rotation;
                }
                attachedPed.Weapons.CurrentWeaponObject.Velocity += velocity;
                Function.Call(Hash.DISABLE_PED_PAIN_AUDIO, attachedPed, true);

                var nearbyEntities = World.GetNearbyEntities(attachedPed.Position, 5.0f);
                foreach (var ent in nearbyEntities)
                {
                    if (ent != attachedPed && ent != attachedPed.Weapons.CurrentWeaponObject &&
                                (attachedPed.IsTouching(ent) ||
                                attachedPed.Weapons.CurrentWeaponObject.IsTouching(ent)))
                    {
                        ent.ApplyForce(velocity);
                    }
                }
            }
            else
            {
                SetHeldHammerVisible(true);
                isFlying = false;
                attachedPed.CanRagdoll = !attachedPed.IsInAir;
                if (!isFlying && !hasJustSetEndOfFlyingInitialVelocity)
                {
                    attachedPed.Velocity += previousPedVelocity;
                    hasJustSetEndOfFlyingInitialVelocity = true;
                }
            }
        }

        private void SetHeldHammerVisible(bool toggle)
        {
            if (IsHoldingHammer)
            {
                if (toggle)
                {
                    attachedPed.Weapons.CurrentWeaponObject.ResetAlpha();
                }
                else
                {
                    attachedPed.Weapons.CurrentWeaponObject.Alpha = 0;
                }
            }
            if (hammerUsedForHoveringWhirlingShown != null)
            {
                if (toggle)
                {
                    hammerUsedForHoveringWhirlingShown.Alpha = 0;

                }
                else
                {
                    hammerUsedForHoveringWhirlingShown.ResetAlpha();
                }
            }
        }

        private void SetAttachedPedToRagdoll()
        {
            attachedPed.CanRagdoll = true;
            ADModUtils.NativeHelper.SetPedToRagdoll(attachedPed, ADModUtils.RagdollType.WideLegs, 2, 1000);
        }

        private void HandleSummoningMjolnir()
        {
            if (Game.IsKeyPressed(Keys.H))
            {
                SummonMjolnir();
            }
            else if (Game.IsKeyPressed(Keys.B))
            {
                SummonMjolnir(true);
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
                var result = World.Raycast(
                    GameplayCamera.Position + GameplayCamera.Direction * 10.0f,
                    GameplayCamera.Position + GameplayCamera.Direction * RAY_CAST_MAX_DISTANCE,
                    ADModUtils.NativeHelper.IntersectAllObjects
                );
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
                 (ADModUtils.NativeHelper.IsPed(entity) ||
                 ADModUtils.NativeHelper.IsVehicle(entity));
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

        private bool HasHammer
        {
            get
            {
                return IsAttachedToPed && attachedPed.Weapons.HasWeapon(Hammer.WeaponHash);
            }
        }

        public bool IsHoldingHammer
        {
            get
            {
                return HasHammer && attachedPed.Weapons.Current.Hash == Hammer.WeaponHash;
            }
        }


        public void SummonMjolnir(bool shootUpwardFirst = false)
        {
            Hammer.DetachRope();
            isHoldingHammerRope = false;
            isHammerAttackingTargets = false;
            targets.Clear();
            Vector3 rightHandBonePos = attachedPed.GetBoneCoord(HAMMER_HOLDING_HAND_ID);
            Vector3 fromHammerToPedHand = rightHandBonePos - Hammer.Position;

            bool isHammerCloseToPed = false;
            float distanceBetweenHammerToPedHand = Math.Abs(fromHammerToPedHand.Length());
            if (distanceBetweenHammerToPedHand <= MINIMUM_DISTANCE_BETWEEN_HAMMER_AND_PED_HAND)
            {
                AnimationActions randomCatchingAction = ADModUtils.Utilities.Random.PickOne(
                    new List<AnimationActions>
                    {
                        AnimationActions.CatchingMjolnir1,
                    }.ToArray()
                );
                string catchDictName = NativeHelper.Instance.GetAnimationDictNameByAction((uint) randomCatchingAction);
                string catchAnimName = NativeHelper.Instance.GetAnimationNameByAction((uint) randomCatchingAction);
                Function.Call(Hash.DISABLE_PED_PAIN_AUDIO, attachedPed, true);
                ADModUtils.NativeHelper.PlayPlayerAnimation(
                    attachedPed,
                    catchDictName,
                    catchAnimName,
                    AnimationFlags.UpperBodyOnly | AnimationFlags.AllowRotation,
                    CATCHING_MJONIR_ANIMATION_DURATION
                );
                NativeHelper.PlayThunderFx(attachedPed, HAMMER_HOLDING_HAND_ID, 0.8f);
                Function.Call(Hash.GIVE_WEAPON_OBJECT_TO_PED, Hammer.WeaponObject, attachedPed);
                PlayCatchHammerSound();
                hammerCloseToPlayerSoundPlayer.Stop();
                GameplayCamera.Shake(CameraShake.LargeExplosion, 0.01f);
                Script.Wait(1);
                shouldHammerReturnToPed = false;
                hammerJustFinishedAttackingTargetsTimestamp = NULL_FLOAT;
                return;
            }
            else if (distanceBetweenHammerToPedHand <= CLOSE_DISTANCE_BETWEEN_HAMMER_AND_PED_HAND_FOR_HAMMER_ROTATION)
            {
                isHammerCloseToPed = true;
            }
            else if (distanceBetweenHammerToPedHand <= CLOSE_DISTANCE_BETWEEN_HAMMER_AND_PED_HAND_FOR_SOUND)
            {
                PlayHammerCloseSound();
            }

            AnimationActions randomCallingAction = ADModUtils.Utilities.Random.PickOne(
                new List<AnimationActions>
                {
                    AnimationActions.CallingForMjolnir
                }.ToArray()
            );
            string dictName = NativeHelper.Instance.GetAnimationDictNameByAction((uint) randomCallingAction);
            string animName = NativeHelper.Instance.GetAnimationNameByAction((uint) randomCallingAction);

            if (!IsAttachedToPed ||
                HasHammer)
            {
                return;
            }

            ADModUtils.NativeHelper.PlayPlayerAnimation(
                attachedPed,
                dictName,
                animName,
                AnimationFlags.UpperBodyOnly | AnimationFlags.AllowRotation
            );

            if (shootUpwardFirst)
            {
                Hammer.WeaponObject.Velocity += new Vector3(0.0f, 0.0f, 1000.0f);
                Script.Wait(500);
            }

            Hammer.SetSummonStatus(true, attachedPed, isHammerCloseToPed);
            Hammer.FindWaysToMoveToCoord(rightHandBonePos, true);
        }

        private void PlayCatchHammerSound()
        {
            catchHammerSoundPlayer.Init(new NAudio.Wave.AudioFileReader(SOUND_FILE_CATCH_HAMMER));
            catchHammerSoundPlayer.Volume = 0.3f;
            catchHammerSoundPlayer.Play();
        }

        private void PlayHammerCloseSound()
        {
            if (hammerCloseToPlayerSoundPlayer.PlaybackState == NAudio.Wave.PlaybackState.Playing)
            {
                return;
            }
            hammerCloseToPlayerSoundPlayer.Init(new NAudio.Wave.AudioFileReader(SOUND_FILE_HAMMER_CLOSE_TO_PLAYER));
            hammerCloseToPlayerSoundPlayer.Volume = 0.3f;
            hammerCloseToPlayerSoundPlayer.Play();
        }

        public void ThrowAndFlyWithMjolnir()
        {
            if (!IsHoldingHammer)
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
            if (!IsHoldingHammer)
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
            if (!IsHoldingHammer)
            {
                return;
            }
            PlayThrowHammerAnimation(GameplayCamera.Direction);
            ThrowHammerOut();
        }

        private void ThrowHammerOut(bool hasInitialVelocity = true)
        {
            if (IsHoldingHammer)
            {
                Hammer.WeaponObject = Function.Call<Entity>(Hash.GET_WEAPON_OBJECT_FROM_PED, attachedPed);
            }
            attachedPed.Weapons.Remove(Hammer.WeaponHash);
            attachedPed.Weapons.Select(WeaponHash.Unarmed);
            if (hasInitialVelocity)
            {
                var hammerVelocity = GameplayCamera.Direction * THROW_HAMMER_SPEED_MULTIPLIER + THROW_HAMMER_Z_AXIS_PRECISION_COMPENSATION;
                Hammer.WeaponObject.Velocity += hammerVelocity;
            }
        }

        private void DropAndHoldHammerRope()
        {
            if (!IsHoldingHammer)
            {
                return;
            }

            ThrowHammerOut(false);
            Hammer.AttachHammerRopeTo(attachedPed, HAMMER_HOLDING_HAND_ID);
            isHoldingHammerRope = true;
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
            AnimationActions randomAction = ADModUtils.Utilities.Random.PickOne(animationActionList);
            float angleBetweenPedForwardAndCamDirection = ADModUtils.Utilities.Math.Angle(
                new Vector2(attachedPed.ForwardVector.X, attachedPed.ForwardVector.Y),
                new Vector2(directionToTurnTo.X, directionToTurnTo.Y)
            );
            bool toLeft = angleBetweenPedForwardAndCamDirection < 0;
            angleBetweenPedForwardAndCamDirection = Math.Abs(angleBetweenPedForwardAndCamDirection);

            bool useDefaultAnimation = angleBetweenPedForwardAndCamDirection < ANIMATION_ANGLE_RANGE_STEP &&
                angleBetweenPedForwardAndCamDirection >= 0;
            if (!useDefaultAnimation)
            {
                randomAction = ADModUtils.Utilities.Random.PickOneIf(animationActionList, (AnimationActions aa) => NativeHelper.Instance.DoesAnimationActionHaveAngles((uint) aa));
            }

            string dictName = NativeHelper.Instance.GetAnimationDictNameByAction((uint) randomAction);
            string animName = NativeHelper.Instance.GetAnimationNameByAction((uint) randomAction);
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
            ADModUtils.NativeHelper.PlayPlayerAnimation(
                attachedPed,
                dictName,
                animName,
                attachedPed.IsWalking || attachedPed.IsSprinting || attachedPed.IsRunning || attachedPed.IsInAir ?
                    AnimationFlags.UpperBodyOnly | AnimationFlags.AllowRotation :
                    AnimationFlags.None,
                -1,
                false
            );
            Script.Wait(NativeHelper.Instance.GetAnimationWaitTimeByDictNameAndAnimName(dictName, animName));
        }
    }
}
