using GTA;
using GTA.Math;
using GTA.UI;
using GTA.Native;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using ADModUtils;

namespace Thor
{
    public class WorthyAbility<T, W>
        where W : GodlyWeapon<W>, new()
        where T : class, new()
    {
        protected static float NULL_FLOAT = -1.0f;
        protected static float FULL_POWER_LEVEL_MAX_RATIO = 100.0f;
        protected static int FULL_POWER_MAX_HEALTH = 30000;
        protected static float HALF_POWER_LEVEL_MAX_RATIO = 50.0f;
        protected static float POWER_LEVEL_PRESENT_MAX_TIME_AFTER_LOSING_WEAPON = 10000.0f;
        protected static float PLAYER_MOVEMENT_MULTIPLIER = 1.5f;
        protected static float MINIMUM_DISTANCE_BETWEEN_WEAPON_AND_PED_HAND = 0.2f;
        protected static float CLOSE_DISTANCE_BETWEEN_WEAPON_AND_PED_HAND_FOR_SOUND = 140.0f;
        protected static float CLOSE_DISTANCE_BETWEEN_WEAPON_AND_PED_HAND_FOR_WEAPON_ROTATION = 5.0f;
        protected static float WEAPON_BEFORE_RETURN_TO_PED_WAIT_TIME = 700.0f;
        protected static int CATCHING_WEAPON_ANIMATION_DURATION = 250;
        protected static Bone WEAPON_HOLDING_HAND_ID = Bone.PHRightHand;
        protected static float THROW_WEAPON_SPEED_MULTIPLIER = 100.0f;
        protected static float AUTO_RETURN_TO_NEW_APPLIED_PED_POSITION_Z_AXIS = 1000.0f;
        protected static float ANIMATION_ANGLE_RANGE_STEP = 45.0f;
        protected static float RAY_CAST_MAX_DISTANCE = 100000.0f;
        protected static float FLY_UPWARD_VELOCITY = 50.0f;
        protected static float FLY_HORIZONTAL_VELOCITY_LEVEL_1 = 70.0f;
        protected static float AIR_DASH_ATTACK_LANDING_VELOCITY = 200.0f;
        protected static int FLY_WITH_THROWN_WEAPON_MAX_TIME = 2000;
        protected static float FLY_SPRINT_VELOCITY_MULTIPLIER = 4.0f;
        protected static float RANGE_TO_LOOK_FOR_CLOSEST_ENTITY = 20.0f;
        protected static int PLAY_THUNDER_FX_INTERVAL_MS = 1000;
        protected static int MAX_TARGET_COUNT = 15;
        protected static Vector3 THROW_WEAPON_Z_AXIS_PRECISION_COMPENSATION = new Vector3(0.0f, 0.0f, 5.0f);
        protected static int HEALTH_RECOVER_TIMER_INTERVAL = 200;

        protected static T instance;
        protected Ped attachedPed;
        protected bool isWeaponAttackingTargets;
        protected float weaponJustFinishedAttackingTargetsTimestamp;
        protected bool isCollectingTargets;
        protected bool hasJustSetEndOfFlyingInitialVelocity;
        protected bool isFlyingWithThrownWeapon;
        protected Vector3 flyWithThrownWeaponDirection;
        protected int flyWithThrownWeaponStartTime;
        protected Vector3 previousPedVelocity;
        protected bool shouldWeaponReturnToPed;
        protected bool isFlying;
        protected HashSet<Entity> targets;
        protected bool isInAirDashAttack;
        protected ADModUtils.Utilities.Timer pedFxTimer;
        protected ADModUtils.Utilities.Timer pedHealthRecoverTimer;
        protected bool hasSummonedThunder;
        protected float lastLostWeaponTime;
        protected float powerLevel;
        protected bool isPreviouslyHoldingWeapon;
        protected NAudio.Wave.WaveOut catchWeaponSoundPlayer;
        protected NAudio.Wave.WaveOut weaponCloseToPlayerSoundPlayer;
        protected string soundFileCatchWeapon;
        protected string soundFileWeaponCloseToPed;
        protected bool isGrabbing = false;
        protected Entity currentlyGrabbedEntity = null;

        protected WorthyAbility()
        {
            isCollectingTargets = false;
            targets = new HashSet<Entity>();
            isWeaponAttackingTargets = false;
            previousPedVelocity = Vector3.Zero;
            isFlying = false;
            hasJustSetEndOfFlyingInitialVelocity = false;
            isFlyingWithThrownWeapon = false;
            flyWithThrownWeaponDirection = Vector3.Zero;
            flyWithThrownWeaponStartTime = 0;
            shouldWeaponReturnToPed = false;
            isInAirDashAttack = false;
            pedFxTimer = null;
            hasSummonedThunder = false;
            catchWeaponSoundPlayer = new NAudio.Wave.WaveOut();
            weaponCloseToPlayerSoundPlayer = new NAudio.Wave.WaveOut();
            weaponJustFinishedAttackingTargetsTimestamp = NULL_FLOAT;
            powerLevel = 0.0f;
            lastLostWeaponTime = NULL_FLOAT;
            isPreviouslyHoldingWeapon = false;
        }

        public W Weapon { get; set; }

        public static T Instance
        {
            get
            {
                if (instance == null)
                {
                    instance = new T();
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

        public virtual void RemoveAbility()
        {
            if (HasWeapon)
            {
                ThrowWeaponOut(false);
            }
            if (attachedPed != null)
            {
                SetInvincible(false);
                attachedPed = null;
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

            if (Weapon != null &&
                Weapon.WeaponObject != null &&
                !HasWeapon)
            {
                weaponJustFinishedAttackingTargetsTimestamp = Game.GameTime;
                Weapon.FindWaysToMoveToCoord(attachedPed.Position, true);
            }
        }

        public virtual void OnTick(bool onlyHandleWeaponHolding)
        {
            if (!onlyHandleWeaponHolding)
            {
                Thunder.Instance.OnTick();
                if (attachedPed == null)
                {
                    return;
                }
                HandlePowerLevel();
                HandleHealthRecovery();
                SetInvincible(true);
                if (pedFxTimer != null && hasSummonedThunder)
                {
                    pedFxTimer.OnTick();
                }
                HandleMovement();
                if (IsHoldingWeapon ||
                    attachedPed.Weapons.CurrentWeaponObject == null)
                {
                    HandleMeleeForces();
                }

                HandlePreOnTick();
            }

            if (IsHoldingWeapon)
            {
                lastLostWeaponTime = NULL_FLOAT;
                HandleFlying();
                Weapon.DestroyWeaponTrackCam();
                Weapon.SetSummonStatus(false);
                World.RenderingCamera = null;
                Function.Call<bool>(Hash.SET_PLAYER_LOCKON_RANGE_OVERRIDE, Game.Player.Handle, 0.0f);
                HandleAirDashAttack();
                CollectTargets();
                isPreviouslyHoldingWeapon = true;
                HandleThrowingWeapon();
                DrawMarkersOnTargets();
                HandlePostHoldingWeaponOnTick();
            }
            else
            {
                if (isPreviouslyHoldingWeapon && lastLostWeaponTime == NULL_FLOAT)
                {
                    lastLostWeaponTime = Game.GameTime;
                }
                isPreviouslyHoldingWeapon = false;
                HandleSummoningWeapon();
                InitWeaponIfNotExist();
                Weapon.ApplyForcesToNearbyEntities();
                ShowWeaponPFX();
                HandleAttackingTargets();
                HandleRenderWeaponCamera();
                HandlePostNotHoldingWeaponOnTick();
            }
            attachedPed.Weapons.Remove(WeaponHash.Parachute);
            Weapon.OnTick();
            HandleGrabbingPed();
        }

        private void HandleRenderWeaponCamera()
        {
            if (IsRenderWeaponCameraKeyPressed() && !HasWeapon)
            {
                Weapon.RenderWeaponTrackCam();
            }
            else
            {
                Weapon.CancelRenderWeaponTrackCam();
            }
        }

        protected virtual bool IsRenderWeaponCameraKeyPressed()
        {
            return false;
        }

        protected virtual void HandlePreOnTick() { }
        protected virtual void HandlePostHoldingWeaponOnTick() { }
        protected virtual void HandlePostNotHoldingWeaponOnTick() { }

        protected void HandleHealthRecovery()
        {
            if (pedHealthRecoverTimer == null)
            {
                pedHealthRecoverTimer = new ADModUtils.Utilities.Timer(HEALTH_RECOVER_TIMER_INTERVAL, RecoverHealth);
            }

            pedHealthRecoverTimer.OnTick();
        }

        protected void RecoverHealth()
        {
            if (powerLevel > 0)
            {
                attachedPed.Health += (int)(attachedPed.MaxHealth * powerLevel / FULL_POWER_LEVEL_MAX_RATIO / 10);
            }
        }

        protected void SummonThunder()
        {
            Function.Call(Hash.FORCE_LIGHTNING_FLASH);
            Function.Call(Hash.FORCE_LIGHTNING_FLASH);
            Function.Call(Hash.FORCE_LIGHTNING_FLASH);
            Function.Call(Hash.FORCE_LIGHTNING_FLASH);
            PlayThunderFx();
            if (pedFxTimer == null)
            {
                pedFxTimer = new ADModUtils.Utilities.Timer(PLAY_THUNDER_FX_INTERVAL_MS, PlayThunderFx);
            }
            Function.Call(Hash.FORCE_LIGHTNING_FLASH);
            Function.Call(Hash.FORCE_LIGHTNING_FLASH);
            Function.Call(Hash.FORCE_LIGHTNING_FLASH);
            Function.Call(Hash.FORCE_LIGHTNING_FLASH);
        }

        protected virtual bool ShouldPossessFullPower()
        {
            return false;
        }

        protected void HandlePowerLevel()
        {
            if (IsHoldingWeapon || ShouldPossessFullPower())
            {
                powerLevel = FULL_POWER_LEVEL_MAX_RATIO;
            }
            else
            {
                if (HasWeapon)
                {
                    powerLevel = HALF_POWER_LEVEL_MAX_RATIO;
                }
                else
                {
                    if (lastLostWeaponTime != NULL_FLOAT)
                    {
                        var timePast = Game.GameTime - lastLostWeaponTime;
                        if (timePast > POWER_LEVEL_PRESENT_MAX_TIME_AFTER_LOSING_WEAPON)
                        {
                            powerLevel = 0.0f;
                            lastLostWeaponTime = NULL_FLOAT;
                        }
                        else
                        {
                            powerLevel = FULL_POWER_LEVEL_MAX_RATIO * (1 - timePast / POWER_LEVEL_PRESENT_MAX_TIME_AFTER_LOSING_WEAPON);
                        }
                    }
                }
            }

            hasSummonedThunder = powerLevel > HALF_POWER_LEVEL_MAX_RATIO;
        }

        protected void HandleMovement()
        {
            var movementMultiplier = 1.0f + (PLAYER_MOVEMENT_MULTIPLIER - 1.0f) * powerLevel / FULL_POWER_LEVEL_MAX_RATIO;
            Function.Call(
                Hash.SET_PED_MOVE_RATE_OVERRIDE,
                attachedPed,
                movementMultiplier
            );
            Function.Call(
                Hash.SET_SWIM_MULTIPLIER_FOR_PLAYER,
                Game.Player,
                movementMultiplier
            );
            if (powerLevel >= HALF_POWER_LEVEL_MAX_RATIO)
            {
                Function.Call(Hash.SET_SUPER_JUMP_THIS_FRAME, Game.Player);
            }
        }

        protected virtual void PlayThunderFx()
        {
            NativeHelper.PlayThunderFx(attachedPed, Bone.SkelLeftForearm);
            NativeHelper.PlayThunderFx(attachedPed, Bone.SkelRightForearm);
            NativeHelper.PlayThunderFx(attachedPed, Bone.SkelLeftThigh);
            NativeHelper.PlayThunderFx(attachedPed, Bone.SkelRightThigh);
            if (IsHoldingWeapon)
            {
                NativeHelper.PlayThunderFx(attachedPed.Weapons.CurrentWeaponObject, 0.5f);
            }
        }

        protected void HandleAirDashAttack()
        {
            if (attachedPed.IsInAir && !isInAirDashAttack)
            {
                if (Game.IsControlPressed(GTA.Control.Attack))
                {
                    isInAirDashAttack = true;
                }
            }

            if (isInAirDashAttack)
            {
                if (!attachedPed.IsInAir)
                {
                    isInAirDashAttack = false;
                    ApplyForcesAndDamagesOnNearbyEntities(true, RANGE_TO_LOOK_FOR_CLOSEST_ENTITY, Vector3.Zero, powerLevel);
                }
                else
                {
                    attachedPed.Velocity = new Vector3(0.0f, 0.0f, -AIR_DASH_ATTACK_LANDING_VELOCITY);
                }
            }
        }

        protected void HandleAttackingTargets()
        {
            if (isWeaponAttackingTargets)
            {
                if (targets.Count > 0)
                {
                    isWeaponAttackingTargets = Weapon.MoveToTargets(ref targets);
                }
                else if (targets.Count == 0)
                {
                    isWeaponAttackingTargets = false;
                    if (weaponJustFinishedAttackingTargetsTimestamp == NULL_FLOAT)
                    {
                        weaponJustFinishedAttackingTargetsTimestamp = Game.GameTime;
                    }
                }
            }

            if (!isWeaponAttackingTargets &&
                weaponJustFinishedAttackingTargetsTimestamp != NULL_FLOAT &&
                Game.GameTime - weaponJustFinishedAttackingTargetsTimestamp >= WEAPON_BEFORE_RETURN_TO_PED_WAIT_TIME)
            {
                shouldWeaponReturnToPed = true;
                weaponJustFinishedAttackingTargetsTimestamp = NULL_FLOAT;
            }

            if (shouldWeaponReturnToPed)
            {
                SummonWeapon();
            }
        }

        protected void InitWeaponIfNotExist()
        {
            if (!HasWeapon)
            {
                Weapon.Init(null);
            }
        }

        protected virtual bool ShouldShowWeaponPFX()
        {
            return true;
        }

        protected void ShowWeaponPFX()
        {
            if (Weapon.IsMoving && ShouldShowWeaponPFX())
            {
                Weapon.ShowParticleFx();
            }
        }

        protected void HandleMeleeForces()
        {
            if (attachedPed.IsInVehicle() ||
                !attachedPed.IsInMeleeCombat)
            {
                return;
            }

            ApplyForcesAndDamagesOnNearbyEntities(false, RANGE_TO_LOOK_FOR_CLOSEST_ENTITY, attachedPed.ForwardVector, powerLevel);
        }

        protected void ShootLightningsToEnemy(Ped ped)
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

        protected void ApplyForcesAndDamagesOnNearbyEntities(bool applyToAll, float range, Vector3 forceDirection, float powerLevel)
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

                if (applyToAll || ent.HasBeenDamagedBy(attachedPed))
                {
                    if (hasSummonedThunder)
                    {
                        NativeHelper.PlayThunderFx(ent);
                    }

                    if (forceDirection.Length() == 0)
                    {
                        var defaultForceDirection = (ent.Position - attachedPed.Position).Normalized;
                        NativeHelper.Instance.ApplyForcesAndDamages(ent, defaultForceDirection, powerLevel);
                    }
                    else
                    {
                        NativeHelper.Instance.ApplyForcesAndDamages(ent, forceDirection, powerLevel);
                    }
                }
            }
        }

        protected void SetInvincible(bool toggle)
        {
            var invincible = toggle && powerLevel > HALF_POWER_LEVEL_MAX_RATIO;


            if (invincible || !toggle)
            {
                attachedPed.CanSufferCriticalHits = !toggle;
                attachedPed.MaxHealth = FULL_POWER_MAX_HEALTH;
                attachedPed.DiesOnLowHealth = !toggle;
            }
            else
            {
                attachedPed.CanSufferCriticalHits = false;
                Function.Call(Hash.SET_ENTITY_CAN_BE_DAMAGED, attachedPed, true);
                attachedPed.MaxHealth = (int)(100 + FULL_POWER_MAX_HEALTH * powerLevel / FULL_POWER_LEVEL_MAX_RATIO);
                attachedPed.IsInvincible = false;
                attachedPed.DiesOnLowHealth = false;
                attachedPed.IsBulletProof = false;
                attachedPed.IsCollisionProof = false;
                attachedPed.IsExplosionProof = false;
                attachedPed.IsFireProof = false;
                attachedPed.IsMeleeProof = false;
            }
        }

        protected virtual void HandlePreInAir() { }
        protected virtual void HandleMidIsFlying(Vector3 velocity, Vector3 weaponHoldingHandCoord) { }
        protected virtual void HandlePreNotFlying() { }

        protected virtual void HandleFlying()
        {
            GameplayCamera.ClampYaw(-180.0f, 180.0f);
            GameplayCamera.ClampPitch(-180.0f, 180.0f);
            var velocity = Vector3.Zero;

            if (!IsHoldingWeapon)
            {
                return;
            }

            if (isFlyingWithThrownWeapon)
            {
                int endTime = flyWithThrownWeaponStartTime + FLY_WITH_THROWN_WEAPON_MAX_TIME;

                if (Game.GameTime < endTime)
                {
                    velocity += flyWithThrownWeaponDirection * FLY_HORIZONTAL_VELOCITY_LEVEL_1 * 2;
                }
                else
                {
                    isFlyingWithThrownWeapon = false;
                }
            }

            if (Game.IsKeyPressed(Keys.X))
            {
                velocity.Z = FLY_UPWARD_VELOCITY;
            }
            if (attachedPed.IsInAir)
            {
                if (Game.IsControlPressed(GTA.Control.ScriptPadUp) ||
                    Game.IsKeyPressed(Keys.W))
                {
                    velocity += GameplayCamera.Direction * FLY_HORIZONTAL_VELOCITY_LEVEL_1;
                }
            }
            
            if (Game.IsControlPressed(GTA.Control.Sprint))
            {
                velocity += Vector3.Multiply(velocity, FLY_SPRINT_VELOCITY_MULTIPLIER);
            }
            if (isFlying)
            {
                hasJustSetEndOfFlyingInitialVelocity = false;
                previousPedVelocity = attachedPed.Velocity;
            }

            HandlePreInAir();
            var weaponHoldingHandCoord = attachedPed.Bones[WEAPON_HOLDING_HAND_ID].Position;
            if (velocity.Length() > 0)
            {
                isFlying = true;
                GameplayCamera.Shake(CameraShake.MediumExplosion, 0.01f);
                SetAttachedPedToRagdoll();
                HandleMidIsFlying(velocity, weaponHoldingHandCoord);
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
                HandlePreNotFlying();
                isFlying = false;
                if (powerLevel == FULL_POWER_LEVEL_MAX_RATIO)
                {
                    attachedPed.CanRagdoll = !attachedPed.IsInAir;
                }
                else
                {
                    attachedPed.CanRagdoll = true;
                }
                if (!isFlying && !hasJustSetEndOfFlyingInitialVelocity)
                {
                    attachedPed.Velocity += previousPedVelocity;
                    hasJustSetEndOfFlyingInitialVelocity = true;
                }
            }
        }

        protected void SetAttachedPedToRagdoll()
        {
            attachedPed.CanRagdoll = true;
            ADModUtils.NativeHelper.SetPedToRagdoll(attachedPed, ADModUtils.RagdollType.WideLegs, 2, 1000);
        }

        protected virtual bool IsSummonWeaponKeyPressed()
        {
            return false;
        }

        protected void HandleSummoningWeapon()
        {
            if (IsSummonWeaponKeyPressed())
            {
                if (HasWeapon)
                {
                    attachedPed.Weapons.Select(Weapon.WeaponHash, true);
                }
                else
                {
                    SummonWeapon();
                }
            }
        }

        protected void HandleGrabbingPed()
        {
            if (currentlyGrabbedEntity != null && (!currentlyGrabbedEntity.Exists() || currentlyGrabbedEntity.IsDead))
            {
                HandleReleaseGrabbedEntity();

                return;
            }

            if (Game.IsControlPressed(GTA.Control.VehicleSubDescend) &&
               Game.IsControlPressed(GTA.Control.ThrowGrenade) && isGrabbing)
            {
                HandleReleaseGrabbedEntity();
                return;
            }

            if (!Game.IsControlPressed(GTA.Control.VehicleSubDescend) && Game.IsControlPressed(GTA.Control.ThrowGrenade) && !isGrabbing)
            {
                var nearbyEnts = World.GetNearbyEntities(Game.Player.Character.Position, 1.5f);

                foreach (var ent in nearbyEnts)
                {
                    if ((ent.EntityType == EntityType.Ped && (Ped)ent != Game.Player.Character)
                        //|| (ent.EntityType == EntityType.Vehicle && !((Vehicle)ent).IsConsideredDestroyed)
                        )
                    {
                        ent.AttachTo(Game.Player.Character.Bones[Bone.IKLeftHand]);

                        if (ent.EntityType == EntityType.Ped)
                        {
                            ((Ped)ent).Task.ClearAll();
                        }

                        currentlyGrabbedEntity = ent;
                        isGrabbing = true;

                        return;
                    }
                }

            }
        }

        private void HandleReleaseGrabbedEntity()
        {
            if (currentlyGrabbedEntity != null)
            {
                currentlyGrabbedEntity.Detach();
                currentlyGrabbedEntity = null;
            }

            isGrabbing = false;
        }

        protected void HandleThrowingWeapon()
        {
            if (Game.IsControlPressed(GTA.Control.Aim))
            {
                Hud.ShowComponentThisFrame(HudComponent.Reticle);

                if (Game.IsKeyPressed(Keys.T))
                {
                    ThrowWeapon(ref targets);
                    isCollectingTargets = false;
                }
                else if (Game.IsKeyPressed(Keys.U))
                {
                    isCollectingTargets = false;
                    ThrowAndFlyWithWeapon();
                }
            }
            else if (Game.IsKeyPressed(Keys.Y))
            {
                DropWeapon();
            }
        }

        protected void CollectTargets()
        {
            if (Game.IsControlPressed(GTA.Control.Aim))
            {
                isCollectingTargets = true;
                var result = World.Raycast(
                    GameplayCamera.Position + GameplayCamera.Direction * 10.0f,
                    GameplayCamera.Position + GameplayCamera.Direction * RAY_CAST_MAX_DISTANCE,
                    ADModUtils.NativeHelper.IntersectAllObjects
                );
                if (targets.Count < MAX_TARGET_COUNT &&
                    result.DidHit &&
                    IsValidHitEntity(result.HitEntity))
                {
                    targets.Add(result.HitEntity);
                }
            }
            else if (Game.IsControlJustReleased(GTA.Control.Aim) && isCollectingTargets)
            {
                isCollectingTargets = false;
                targets.Clear();
            }
        }

        protected bool IsValidHitEntity(Entity entity)
        {
            return entity != null &&
                 entity != attachedPed &&
                 (ADModUtils.NativeHelper.IsPed(entity) ||
                 ADModUtils.NativeHelper.IsVehicle(entity));
        }

        protected void DrawMarkersOnTargets()
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

        protected bool HasWeapon
        {
            get
            {
                return IsAttachedToPed && attachedPed.Weapons.HasWeapon(Weapon.WeaponHash);
            }
        }

        public bool IsHoldingWeapon
        {
            get
            {
                return HasWeapon && attachedPed.Weapons.Current.Hash == Weapon.WeaponHash;
            }
        }

        private void DropWeapon()
        {
            if (!IsHoldingWeapon)
            {
                return;
            }

            ThrowWeaponOut(false);
        }


        public virtual void SummonWeapon()
        {
            isWeaponAttackingTargets = false;
            targets.Clear();
            Vector3 rightHandBonePos = attachedPed.Bones[WEAPON_HOLDING_HAND_ID].Position;
            Vector3 fromWeaponToPedHand = rightHandBonePos - Weapon.Position;
            
            bool isWeaponCloseToPed = false;
            float distanceBetweenWeaponToPedHand = Math.Abs(fromWeaponToPedHand.Length());
            var weaponToPedHandRaycastTest = ShapeTest.StartTestCapsule(
                Weapon.Position, 
                new Vector3(Weapon.Position.X, Weapon.Position.Y, Weapon.Position.Z + MINIMUM_DISTANCE_BETWEEN_WEAPON_AND_PED_HAND), 
                MINIMUM_DISTANCE_BETWEEN_WEAPON_AND_PED_HAND,
                IntersectFlags.Peds
            );
            ShapeTestResult weaponToPedHandRaycastResult;
            weaponToPedHandRaycastTest.GetResult(out weaponToPedHandRaycastResult);
            Entity weaponToPedHandRaycastHitEntity = null;
            weaponToPedHandRaycastResult.TryGetHitEntity(out weaponToPedHandRaycastHitEntity);

            if (weaponToPedHandRaycastHitEntity != null && weaponToPedHandRaycastHitEntity.Equals(attachedPed))
            {
                AnimationActions randomCatchingAction = ADModUtils.Utilities.Random.PickOne(
                    new List<AnimationActions>
                    {
                        AnimationActions.CatchingWeapon1,
                    }.ToArray()
                );
                string catchDictName = NativeHelper.Instance.GetAnimationDictNameByAction((uint)randomCatchingAction);
                string catchAnimName = NativeHelper.Instance.GetAnimationNameByAction((uint)randomCatchingAction);
                Function.Call(Hash.DISABLE_PED_PAIN_AUDIO, attachedPed, true);
                ADModUtils.NativeHelper.PlayPlayerAnimation(
                    attachedPed,
                    catchDictName,
                    catchAnimName,
                    AnimationFlags.UpperBodyOnly | AnimationFlags.Secondary,
                    CATCHING_WEAPON_ANIMATION_DURATION
                );
                NativeHelper.PlayThunderFx(attachedPed, WEAPON_HOLDING_HAND_ID, 0.8f);
                Function.Call(Hash.GIVE_WEAPON_OBJECT_TO_PED, Weapon.WeaponObject, attachedPed);
                PlayCatchWeaponSound();
                weaponCloseToPlayerSoundPlayer.Stop();
                GameplayCamera.Shake(CameraShake.LargeExplosion, 0.01f);
                Script.Wait(1);
                shouldWeaponReturnToPed = false;
                weaponJustFinishedAttackingTargetsTimestamp = NULL_FLOAT;
                SummonThunder();
                return;
            }
            else if (distanceBetweenWeaponToPedHand <= CLOSE_DISTANCE_BETWEEN_WEAPON_AND_PED_HAND_FOR_WEAPON_ROTATION)
            {
                isWeaponCloseToPed = true;
            }
            else if (distanceBetweenWeaponToPedHand <= CLOSE_DISTANCE_BETWEEN_WEAPON_AND_PED_HAND_FOR_SOUND)
            {
                PlayWeaponCloseSound();
            }

            AnimationActions randomCallingAction = ADModUtils.Utilities.Random.PickOne(
                new List<AnimationActions>
                {
                    AnimationActions.CallingForWeapon
                }.ToArray()
            );
            string dictName = NativeHelper.Instance.GetAnimationDictNameByAction((uint)randomCallingAction);
            string animName = NativeHelper.Instance.GetAnimationNameByAction((uint)randomCallingAction);

            if (!IsAttachedToPed ||
                HasWeapon)
            {
                return;
            }

            ADModUtils.NativeHelper.PlayPlayerAnimation(
                attachedPed,
                dictName,
                animName,
                AnimationFlags.UpperBodyOnly | AnimationFlags.Secondary
            );

            Weapon.SetSummonStatus(true, attachedPed, isWeaponCloseToPed);
            Weapon.FindWaysToMoveToCoord(rightHandBonePos, true);
        }

        protected void PlayCatchWeaponSound()
        {
            catchWeaponSoundPlayer.Init(new NAudio.Wave.AudioFileReader(soundFileCatchWeapon));
            catchWeaponSoundPlayer.Volume = 0.3f;
            catchWeaponSoundPlayer.Play();
        }

        protected void PlayWeaponCloseSound()
        {
            if (weaponCloseToPlayerSoundPlayer.PlaybackState == NAudio.Wave.PlaybackState.Playing)
            {
                return;
            }
            weaponCloseToPlayerSoundPlayer.Init(new NAudio.Wave.AudioFileReader(soundFileWeaponCloseToPed));
            weaponCloseToPlayerSoundPlayer.Volume = 0.3f;
            weaponCloseToPlayerSoundPlayer.Play();
        }

        public void ThrowAndFlyWithWeapon()
        {
            if (!IsHoldingWeapon)
            {
                return;
            }
            PlayThrowWeaponAnimation(GameplayCamera.Direction);
            SetAttachedPedToRagdoll();
            isFlyingWithThrownWeapon = true;
            flyWithThrownWeaponDirection = GameplayCamera.Direction;
            flyWithThrownWeaponStartTime = Game.GameTime;
        }

        public void ThrowWeapon(ref HashSet<Entity> targets)
        {
            if (!IsHoldingWeapon)
            {
                return;
            }

            if (targets.Count == 0)
            {
                isWeaponAttackingTargets = false;
                ThrowWeapon();
                return;
            }

            if (targets.Count > 0)
            {
                isWeaponAttackingTargets = true;
                Vector3 firstTargetPosition = targets.ToList().First().Position;
                PlayThrowWeaponAnimation((firstTargetPosition - attachedPed.Position).Normalized);
                ThrowWeaponOut(false);
            }
        }

        public void ThrowWeapon()
        {
            if (!IsHoldingWeapon)
            {
                return;
            }
            PlayThrowWeaponAnimation(GameplayCamera.Direction);
            ThrowWeaponOut();
        }

        protected virtual void ThrowWeaponOut(bool hasInitialVelocity = true)
        {
            if (IsHoldingWeapon)
            {
                Weapon.WeaponObject = Function.Call<Prop>(Hash.GET_WEAPON_OBJECT_FROM_PED, attachedPed);
            }
            attachedPed.Weapons.Remove(Weapon.WeaponHash);
            attachedPed.Weapons.Select(WeaponHash.Unarmed);
            if (hasInitialVelocity)
            {
                var weaponVelocity = GameplayCamera.Direction * THROW_WEAPON_SPEED_MULTIPLIER + THROW_WEAPON_Z_AXIS_PRECISION_COMPENSATION;
                Weapon.WeaponObject.Velocity += weaponVelocity;
            }
        }

        protected void PlayThrowWeaponAnimation(Vector3 directionToTurnTo)
        {
            var animationActionList = Weapon.ThrowActions;
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
                randomAction = ADModUtils.Utilities.Random.PickOneIf(animationActionList, (AnimationActions aa) => NativeHelper.Instance.DoesAnimationActionHaveAngles((uint)aa));
            }

            string dictName = NativeHelper.Instance.GetAnimationDictNameByAction((uint)randomAction);
            string animName = NativeHelper.Instance.GetAnimationNameByAction((uint)randomAction);
            if (!useDefaultAnimation)
            {
                string animationAngle = "180";
                if (angleBetweenPedForwardAndCamDirection >= ANIMATION_ANGLE_RANGE_STEP &&
                    angleBetweenPedForwardAndCamDirection < ANIMATION_ANGLE_RANGE_STEP * 3)
                {
                    animationAngle = "90";
                }

                if (NativeHelper.Instance.DoesAnimationActionHaveAnglesAndIncompletePlusOrMinusSign((uint)randomAction))
                {
                    animName = animName.Replace("_0", "_" + (animationAngle == "90" ? (toLeft ? "" : "-") : "-") + animationAngle);
                }
                else
                {
                    animName = animName.Replace("_0", "_" + (toLeft ? "+" : "-") + animationAngle);
                }
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
