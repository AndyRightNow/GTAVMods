using GTA;
using GTAVMods.Game;
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
    public abstract class WorthyAbility : IThorAbility
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

        protected readonly GameServices game;
        protected readonly NativeHelper effects;
        protected readonly Random random;
        private readonly Thunder thunder;
        protected int attachedPed;
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
        protected HashSet<int> targets;
        protected bool isInAirDashAttack;
        protected ADModUtils.Utilities.Timer pedFxTimer;
        protected ADModUtils.Utilities.Timer pedHealthRecoverTimer;
        protected bool hasSummonedThunder;
        protected float lastLostWeaponTime;
        protected float powerLevel;
        protected bool isPreviouslyHoldingWeapon;
        protected readonly AudioClip catchWeaponSoundPlayer;
        protected readonly AudioClip weaponCloseToPlayerSoundPlayer;
        private int originalMaxHealth;
        protected bool isGrabbing = false;
        protected int currentlyGrabbedEntity = 0;

        protected WorthyAbility(GameServices game, NativeHelper effects, Random random,
            Mjolnir weapon, Thunder thunder, IAudioPlaybackFactory audio, IEventLog log)
        {
            this.game = game;
            this.effects = effects;
            this.random = random;
            this.thunder = thunder;
            Weapon = weapon;
            isCollectingTargets = false;
            targets = new HashSet<int>();
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
            catchWeaponSoundPlayer = new AudioClip(AudioClip.ResourcePath("catch-hammer.wav"), 0.3f, audio, log);
            weaponCloseToPlayerSoundPlayer = new AudioClip(AudioClip.ResourcePath("hammer-close-to-player.wav"), 0.3f, audio, log);
            weaponJustFinishedAttackingTargetsTimestamp = NULL_FLOAT;
            powerLevel = 0.0f;
            lastLostWeaponTime = NULL_FLOAT;
            isPreviouslyHoldingWeapon = false;
        }

        public Mjolnir Weapon { get; }
        public int WeaponObject => Weapon.WeaponObject;
        public void ResetWeapon() => Weapon.WeaponObject = 0;
        public void DisposeWeapon() => Weapon.Dispose();

        public virtual string DiagnosticState => "attached=" + IsAttachedToPed + ";holding=" + (IsAttachedToPed && game.Entities.Exists(attachedPed) && IsHoldingWeapon) +
            ";return=" + shouldWeaponReturnToPed + ";flying=" + isFlying + ";throwFlight=" + isFlyingWithThrownWeapon +
            ";airDash=" + isInAirDashAttack + ";attacking=" + isWeaponAttackingTargets + ";collecting=" + isCollectingTargets +
            ";targets=" + targets.Count + ";grabbing=" + isGrabbing;

        public bool IsAttachedToPed
        {
            get
            {
                return attachedPed != 0;
            }
        }

        public virtual void RemoveAbility()
        {
            Logger.Event("INFO", "ability.remove", "ped", attachedPed, "state", DiagnosticState);
            Cleanup.Run(
                () =>
                {
                    if (attachedPed != 0 && game.Entities.Exists(attachedPed) && HasWeapon)
                        ThrowWeaponOut(false);
                },
                () =>
                {
                    if (attachedPed != 0 && game.Entities.Exists(attachedPed))
                        Cleanup.Run(
                            new Action(() => SetInvincible(false)),
                            new Action(() => game.Peds.SetMaxHealth(attachedPed, originalMaxHealth)),
                            new Action(() => game.Entities.SetHealth(attachedPed,
                                System.Math.Min(game.Entities.Health(attachedPed), originalMaxHealth))));
                },
                catchWeaponSoundPlayer.Stop,
                weaponCloseToPlayerSoundPlayer.Stop,
                Weapon.DestroyWeaponTrackCam,
                () =>
                {
                    if (currentlyGrabbedEntity != 0 && game.Entities.Exists(currentlyGrabbedEntity))
                        game.Entities.Detach(currentlyGrabbedEntity);
                    currentlyGrabbedEntity = 0;
                    isGrabbing = false;
                });
            attachedPed = 0;
            targets.Clear();
            isFlying = false;
            isFlyingWithThrownWeapon = false;
            isInAirDashAttack = false;
            shouldWeaponReturnToPed = false;
            previousPedVelocity = Vector3.Zero;
            hasJustSetEndOfFlyingInitialVelocity = false;
            isPreviouslyHoldingWeapon = false;
            powerLevel = 0;
            lastLostWeaponTime = NULL_FLOAT;
            weaponJustFinishedAttackingTargetsTimestamp = NULL_FLOAT;
            flyWithThrownWeaponDirection = Vector3.Zero;
            flyWithThrownWeaponStartTime = 0;
            isCollectingTargets = false;
            isWeaponAttackingTargets = false;
            pedHealthRecoverTimer = null;
            pedFxTimer = null;

            hasSummonedThunder = false;
        }

        public void DisposeSounds()
        {
            catchWeaponSoundPlayer.Dispose();
            weaponCloseToPlayerSoundPlayer.Dispose();
        }

        public void ApplyOn(int ped)
        {
            if (IsAttachedToPed)
            {
                return;
            }

            attachedPed = ped;
            Logger.Event("INFO", "ability.attach", "ped", ped, "model", game.Entities.ModelHash(ped), "position", game.Entities.Position(ped).ToString());
            originalMaxHealth = game.Peds.MaxHealth(ped);
            game.Peds.SetCanRagdoll(attachedPed, true);

            if (Weapon.WeaponObject != 0 &&
                !HasWeapon)
            {
                weaponJustFinishedAttackingTargetsTimestamp = game.Host.GameTime;
                Weapon.FindWaysToMoveToCoord(game.Entities.Position(attachedPed), true);
            }
        }

        public virtual void OnTick(bool onlyHandleWeaponHolding)
        {
            if (!onlyHandleWeaponHolding)
            {
                if (attachedPed == 0)
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
                    game.Weapons.CurrentObject(attachedPed) == 0)
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
                game.Native.Call(Hash.SET_PLAYER_LOCKON_RANGE_OVERRIDE, game.Host.PlayerHandle, 0.0f);
                HandleAirDashAttack();
                CollectTargets();
                isPreviouslyHoldingWeapon = true;
                HandleThrowingWeapon();
                DrawMarkersOnTargets();
                HandlePostHoldingWeaponOnTick();
            }
            else
            {
                if (isPreviouslyHoldingWeapon)
                {
                    lastLostWeaponTime = game.Host.GameTime;
                }
                isPreviouslyHoldingWeapon = false;
                HandleSummoningWeapon();
                InitWeaponIfNotExist();
                Weapon.ApplyForcesToNearbyEntities();
                ShowWeaponPFX();
                HandleAttackingTargets();

                if (IsRenderWeaponCameraKeyPressed())
                {
                    Weapon.RenderWeaponTrackCam();
                }
                else
                {
                    Weapon.CancelRenderWeaponTrackCam();
                }
                HandlePostNotHoldingWeaponOnTick();
            }
            game.Weapons.Remove(attachedPed, WeaponHash.Parachute);
            Weapon.OnTick();
            HandleGrabbingPed();
        }

        protected abstract bool IsRenderWeaponCameraKeyPressed();

        protected abstract void HandlePreOnTick();
        protected abstract void HandlePostHoldingWeaponOnTick();
        protected abstract void HandlePostNotHoldingWeaponOnTick();

        protected void HandleHealthRecovery()
        {
            if (pedHealthRecoverTimer == null)
            {
                pedHealthRecoverTimer = new ADModUtils.Utilities.Timer(HEALTH_RECOVER_TIMER_INTERVAL, RecoverHealth, () => game.Host.GameTime);
            }

            pedHealthRecoverTimer.OnTick();
        }

        protected void RecoverHealth()
        {
            if (powerLevel > 0)
            {
                game.Entities.SetHealth(attachedPed, game.Entities.Health(attachedPed) + (int)(game.Peds.MaxHealth(attachedPed) * powerLevel / FULL_POWER_LEVEL_MAX_RATIO / 10));
            }
        }

        protected void SummonThunder()
        {
            game.Native.Call(Hash.FORCE_LIGHTNING_FLASH);
            game.Native.Call(Hash.FORCE_LIGHTNING_FLASH);
            game.Native.Call(Hash.FORCE_LIGHTNING_FLASH);
            game.Native.Call(Hash.FORCE_LIGHTNING_FLASH);
            PlayThunderFx();
            if (pedFxTimer == null)
            {
                pedFxTimer = new ADModUtils.Utilities.Timer(PLAY_THUNDER_FX_INTERVAL_MS, PlayThunderFx, () => game.Host.GameTime);
            }
            game.Native.Call(Hash.FORCE_LIGHTNING_FLASH);
            game.Native.Call(Hash.FORCE_LIGHTNING_FLASH);
            game.Native.Call(Hash.FORCE_LIGHTNING_FLASH);
            game.Native.Call(Hash.FORCE_LIGHTNING_FLASH);
        }

        protected abstract bool ShouldPossessFullPower();

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
                        var timePast = game.Host.GameTime - lastLostWeaponTime;
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
            game.Native.Call(
                Hash.SET_PED_MOVE_RATE_OVERRIDE,
                attachedPed,
                movementMultiplier
            );
            game.Native.Call(
                Hash.SET_SWIM_MULTIPLIER_FOR_PLAYER,
                game.Host.PlayerHandle,
                movementMultiplier
            );
            if (powerLevel >= HALF_POWER_LEVEL_MAX_RATIO)
            {
                game.Native.Call(Hash.SET_SUPER_JUMP_THIS_FRAME, game.Host.PlayerHandle);
            }
        }

        protected virtual void PlayThunderFx()
        {
            effects.PlayThunderFx(attachedPed, Bone.SkelLeftForearm);
            effects.PlayThunderFx(attachedPed, Bone.SkelRightForearm);
            effects.PlayThunderFx(attachedPed, Bone.SkelLeftThigh);
            effects.PlayThunderFx(attachedPed, Bone.SkelRightThigh);
            if (IsHoldingWeapon)
            {
                effects.PlayThunderFx(game.Weapons.CurrentObject(attachedPed), 0.5f);
            }
        }

        protected void HandleAirDashAttack()
        {
            if (game.Entities.IsInAir(attachedPed) && !isInAirDashAttack)
            {
                if (game.Input.IsControlPressed(GTA.Control.Attack))
                {
                    isInAirDashAttack = true;
                }
            }

            if (isInAirDashAttack)
            {
                if (!game.Entities.IsInAir(attachedPed))
                {
                    isInAirDashAttack = false;
                    ApplyForcesAndDamagesOnNearbyEntities(true, RANGE_TO_LOOK_FOR_CLOSEST_ENTITY, Vector3.Zero, powerLevel);
                }
                else
                {
                    game.Entities.SetVelocity(attachedPed, new Vector3(0.0f, 0.0f, -AIR_DASH_ATTACK_LANDING_VELOCITY));
                }
            }
        }

        protected void HandleAttackingTargets()
        {
            if (isWeaponAttackingTargets)
            {
                if (targets.Count > 0)
                {
                    isWeaponAttackingTargets = Weapon.MoveToTargets(targets);
                }
                else
                {
                    isWeaponAttackingTargets = false;
                    weaponJustFinishedAttackingTargetsTimestamp = game.Host.GameTime;
                }
            }

            if (!isWeaponAttackingTargets &&
                weaponJustFinishedAttackingTargetsTimestamp != NULL_FLOAT &&
                game.Host.GameTime - weaponJustFinishedAttackingTargetsTimestamp >= WEAPON_BEFORE_RETURN_TO_PED_WAIT_TIME)
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

        protected abstract bool ShouldShowWeaponPFX();

        protected void ShowWeaponPFX()
        {
            if (Weapon.IsMoving && ShouldShowWeaponPFX())
            {
                Weapon.ShowParticleFx();
            }
        }

        protected void HandleMeleeForces()
        {
            if (game.Peds.IsInVehicle(attachedPed) ||
                !game.Peds.IsInMeleeCombat(attachedPed))
            {
                return;
            }

            ApplyForcesAndDamagesOnNearbyEntities(false, RANGE_TO_LOOK_FOR_CLOSEST_ENTITY, game.Entities.ForwardVector(attachedPed), powerLevel);
        }

        protected void ApplyForcesAndDamagesOnNearbyEntities(bool applyToAll, float range, Vector3 forceDirection, float powerLevel)
        {
            int[] closestEntities = game.World.NearbyEntities(game.Entities.Position(attachedPed), range);

            foreach (var ent in closestEntities)
            {
                if (ent == attachedPed ||
                    ent == game.Weapons.CurrentObject(attachedPed))
                {
                    continue;
                }
                if (applyToAll || game.Entities.HasBeenDamagedBy(ent, attachedPed))
                {
                    if (hasSummonedThunder)
                    {
                        effects.PlayThunderFx(ent);
                    }

                    if (forceDirection.Length() == 0)
                    {
                        var defaultForceDirection = (game.Entities.Position(ent) - game.Entities.Position(attachedPed)).Normalized;
                        effects.Operations.ApplyForcesAndDamages(ent, defaultForceDirection, powerLevel);
                    }
                    else
                    {
                        effects.Operations.ApplyForcesAndDamages(ent, forceDirection, powerLevel);
                    }
                }
            }
        }

        protected void SetInvincible(bool toggle)
        {
            var invincible = toggle && powerLevel > HALF_POWER_LEVEL_MAX_RATIO;


            if (invincible || !toggle)
            {
                game.Peds.SetCanSufferCriticalHits(attachedPed, !toggle);
                game.Peds.SetMaxHealth(attachedPed, FULL_POWER_MAX_HEALTH);
                game.Peds.SetDiesOnLowHealth(attachedPed, !toggle);
            }
            else
            {
                game.Peds.SetCanSufferCriticalHits(attachedPed, false);
                game.Native.Call(Hash.SET_ENTITY_CAN_BE_DAMAGED, attachedPed, true);
                game.Peds.SetMaxHealth(attachedPed, (int)(100 + FULL_POWER_MAX_HEALTH * powerLevel / FULL_POWER_LEVEL_MAX_RATIO));
                game.Entities.SetInvincible(attachedPed, false);
                game.Peds.SetDiesOnLowHealth(attachedPed, false);
                game.Entities.SetBulletProof(attachedPed, false);
                game.Entities.SetCollisionProof(attachedPed, false);
                game.Entities.SetExplosionProof(attachedPed, false);
                game.Entities.SetFireProof(attachedPed, false);
                game.Entities.SetMeleeProof(attachedPed, false);
            }
        }

        protected abstract void HandlePreInAir();
        protected abstract void HandleMidIsFlying(Vector3 velocity, Vector3 weaponHoldingHandCoord);
        protected abstract void HandlePreNotFlying();

        protected virtual void HandleFlying()
        {
            game.Cameras.SetHeadingLimits(-180.0f, 180.0f);
            game.Cameras.SetPitchLimits(-180.0f, 180.0f);
            var velocity = Vector3.Zero;

            if (isFlyingWithThrownWeapon)
            {
                int endTime = flyWithThrownWeaponStartTime + FLY_WITH_THROWN_WEAPON_MAX_TIME;

                if (game.Host.GameTime < endTime)
                {
                    velocity += flyWithThrownWeaponDirection * FLY_HORIZONTAL_VELOCITY_LEVEL_1 * 2;
                }
                else
                {
                    isFlyingWithThrownWeapon = false;
                }
            }

            if (game.Input.IsKeyPressed(Keys.X))
            {
                velocity.Z = FLY_UPWARD_VELOCITY;
            }
            if (game.Entities.IsInAir(attachedPed))
            {
                if (game.Input.IsControlPressed(GTA.Control.ScriptPadUp) ||
                    game.Input.IsKeyPressed(Keys.W))
                {
                    velocity += game.Cameras.GameplayDirection * FLY_HORIZONTAL_VELOCITY_LEVEL_1;
                }
            }
            
            if (game.Input.IsControlPressed(GTA.Control.Sprint))
            {
                velocity += Vector3.Multiply(velocity, FLY_SPRINT_VELOCITY_MULTIPLIER);
            }
            if (isFlying)
            {
                hasJustSetEndOfFlyingInitialVelocity = false;
                previousPedVelocity = game.Entities.Velocity(attachedPed);
            }

            HandlePreInAir();
            var weaponHoldingHandCoord = game.Peds.BonePosition(attachedPed, WEAPON_HOLDING_HAND_ID);
            if (velocity.Length() > 0)
            {
                isFlying = true;
                game.Cameras.Shake(CameraShake.MediumExplosion, 0.01f);
                SetAttachedPedToRagdoll();
                HandleMidIsFlying(velocity, weaponHoldingHandCoord);
                game.Entities.SetVelocity(game.Weapons.CurrentObject(attachedPed), game.Entities.Velocity(game.Weapons.CurrentObject(attachedPed)) + velocity);
                game.Native.Call(Hash.DISABLE_PED_PAIN_AUDIO, attachedPed, true);

                var nearbyEntities = game.World.NearbyEntities(game.Entities.Position(attachedPed), 5.0f);
                foreach (var ent in nearbyEntities)
                {
                    if (ent != attachedPed && ent != game.Weapons.CurrentObject(attachedPed) &&
                                (game.Entities.IsTouching(attachedPed, ent) ||
                                game.Entities.IsTouching(game.Weapons.CurrentObject(attachedPed), ent)))
                    {
                        game.Entities.ApplyForce(ent, velocity);
                    }
                }
            }
            else
            {
                HandlePreNotFlying();
                isFlying = false;
                if (powerLevel == FULL_POWER_LEVEL_MAX_RATIO)
                {
                    game.Peds.SetCanRagdoll(attachedPed, !game.Entities.IsInAir(attachedPed));
                }
                else
                {
                    game.Peds.SetCanRagdoll(attachedPed, true);
                }
                if (!hasJustSetEndOfFlyingInitialVelocity)
                {
                    game.Entities.SetVelocity(attachedPed, game.Entities.Velocity(attachedPed) + previousPedVelocity);
                    hasJustSetEndOfFlyingInitialVelocity = true;
                }
            }
        }

        protected void SetAttachedPedToRagdoll()
        {
            game.Peds.SetCanRagdoll(attachedPed, true);
            effects.Operations.SetPedToRagdoll(attachedPed, ADModUtils.RagdollType.WideLegs, 2, 1000);
        }

        protected abstract bool IsSummonWeaponKeyPressed();

        protected void HandleSummoningWeapon()
        {
            if (IsSummonWeaponKeyPressed())
            {
                if (HasWeapon)
                {
                    game.Weapons.Select(attachedPed, Weapon.WeaponHash, true);
                }
                else
                {
                    SummonWeapon();
                }
            }
        }

        protected void HandleGrabbingPed()
        {
            if (currentlyGrabbedEntity != 0 && (!game.Entities.Exists(currentlyGrabbedEntity) || game.Entities.IsDead(currentlyGrabbedEntity)))
            {
                HandleReleaseGrabbedEntity();

                return;
            }

            if (game.Input.IsControlPressed(GTA.Control.VehicleSubDescend) &&
               game.Input.IsControlPressed(GTA.Control.ThrowGrenade) && isGrabbing)
            {
                HandleReleaseGrabbedEntity();
                return;
            }

            if (!game.Input.IsControlPressed(GTA.Control.VehicleSubDescend) && game.Input.IsControlPressed(GTA.Control.ThrowGrenade) && !isGrabbing)
            {
                var nearbyEnts = game.World.NearbyEntities(game.Entities.Position(game.Host.PlayerPed), 1.5f);

                foreach (var ent in nearbyEnts)
                {
                    if (effects.Operations.IsPed(ent) && ent != game.Host.PlayerPed)
                    {
                        game.Entities.AttachToBone(ent, game.Host.PlayerPed, Bone.IKLeftHand);
                        game.Peds.ClearTasks(ent);

                        currentlyGrabbedEntity = ent;
                        isGrabbing = true;

                        return;
                    }
                }

            }
        }

        private void HandleReleaseGrabbedEntity()
        {
            if (game.Entities.Exists(currentlyGrabbedEntity))
                game.Entities.Detach(currentlyGrabbedEntity);
            currentlyGrabbedEntity = 0;

            isGrabbing = false;
        }

        protected void HandleThrowingWeapon()
        {
            if (game.Input.IsControlPressed(GTA.Control.Aim))
            {
                game.Native.Call(Hash.SHOW_HUD_COMPONENT_THIS_FRAME, (int)HudComponent.Reticle);

                if (game.Input.IsKeyPressed(Keys.T))
                {
                    ThrowWeapon(targets);
                    isCollectingTargets = false;
                }
                else if (game.Input.IsKeyPressed(Keys.U))
                {
                    isCollectingTargets = false;
                    ThrowAndFlyWithWeapon();
                }
            }
            else if (game.Input.IsKeyPressed(Keys.Y))
            {
                DropWeapon();
            }
        }

        protected void CollectTargets()
        {
            if (game.Input.IsControlPressed(GTA.Control.Aim))
            {
                isCollectingTargets = true;
                var result = game.World.Raycast(
                    game.Cameras.GameplayPosition + game.Cameras.GameplayDirection * 10.0f,
                    game.Cameras.GameplayPosition + game.Cameras.GameplayDirection * RAY_CAST_MAX_DISTANCE,
                    ADModUtils.NativeHelper.IntersectAllObjects
                );
                if (targets.Count < MAX_TARGET_COUNT &&
                    result.DidHit &&
                    IsValidHitEntity(result.Entity))
                {
                    targets.Add(result.Entity);
                }
            }
            else if (game.Input.IsControlJustReleased(GTA.Control.Aim) && isCollectingTargets)
            {
                isCollectingTargets = false;
                targets.Clear();
            }
        }

        protected bool IsValidHitEntity(int entity)
        {
            return entity != 0 &&
                 entity != attachedPed &&
                 (effects.Operations.IsPed(entity) ||
                 effects.Operations.IsVehicle(entity));
        }

        protected void DrawMarkersOnTargets()
        {
            foreach (var target in targets)
            {
                game.World.DrawMarker(
                    MarkerType.Cone,
                    game.Entities.Position(target) + new Vector3(0.0f, 0.0f, 2.0f),
                    game.Cameras.GameplayDirection,
                    Vector3.Zero,
                    new Vector3(1.0f, 1.0f, 1.0f), Color.Red
                );
            }
        }

        protected bool HasWeapon
        {
            get
            {
                return IsAttachedToPed && game.Weapons.HasWeapon(attachedPed, Weapon.WeaponHash);
            }
        }

        public bool IsHoldingWeapon
        {
            get
            {
                return HasWeapon && game.Weapons.Current(attachedPed) == Weapon.WeaponHash;
            }
        }

        private void DropWeapon()
        {
            ThrowWeaponOut(false);
        }


        public virtual void SummonWeapon()
        {
            if (!IsAttachedToPed || HasWeapon) return;
            Logger.Throttled("INFO", "hammer.summon", 2000, "state", DiagnosticState);
            isWeaponAttackingTargets = false;
            targets.Clear();
            Vector3 rightHandBonePos = game.Peds.BonePosition(attachedPed, WEAPON_HOLDING_HAND_ID);
            Vector3 fromWeaponToPedHand = rightHandBonePos - Weapon.Position;
            
            bool isWeaponCloseToPed = false;
            float distanceBetweenWeaponToPedHand = Math.Abs(fromWeaponToPedHand.Length());
            var weaponToPedHandRaycastResult = game.World.Capsule(
                Weapon.Position, 
                new Vector3(Weapon.Position.X, Weapon.Position.Y, Weapon.Position.Z + MINIMUM_DISTANCE_BETWEEN_WEAPON_AND_PED_HAND), 
                MINIMUM_DISTANCE_BETWEEN_WEAPON_AND_PED_HAND,
                IntersectFlags.Peds
            );
            int weaponToPedHandRaycastHitEntity = weaponToPedHandRaycastResult.Entity;

            if (weaponToPedHandRaycastHitEntity != 0 && weaponToPedHandRaycastHitEntity == attachedPed)
            {
                AnimationActions randomCatchingAction = ADModUtils.Utilities.Random.PickOne(
                    new List<AnimationActions>
                    {
                        AnimationActions.CatchingWeapon1,
                    }.ToArray(), random
                );
                string catchDictName = effects.Operations.GetAnimationDictNameByAction((uint)randomCatchingAction);
                string catchAnimName = effects.Operations.GetAnimationNameByAction((uint)randomCatchingAction);
                game.Native.Call(Hash.DISABLE_PED_PAIN_AUDIO, attachedPed, true);
                effects.Operations.PlayPlayerAnimation(
                    attachedPed,
                    catchDictName,
                    catchAnimName,
                    AnimationFlags.UpperBodyOnly | AnimationFlags.Secondary,
                    CATCHING_WEAPON_ANIMATION_DURATION
                );
                effects.PlayThunderFx(attachedPed, WEAPON_HOLDING_HAND_ID, 0.8f);
                game.Native.Call(Hash.GIVE_WEAPON_OBJECT_TO_PED, Weapon.WeaponObject, attachedPed);
                PlayCatchWeaponSound();
                weaponCloseToPlayerSoundPlayer.Stop();
                game.Cameras.Shake(CameraShake.LargeExplosion, 0.01f);
                game.Host.Wait(1);
                shouldWeaponReturnToPed = false;
                Logger.Event("INFO", "hammer.caught", "ped", attachedPed, "gameTime", game.Host.GameTime);
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
                }.ToArray(), random
            );
            string dictName = effects.Operations.GetAnimationDictNameByAction((uint)randomCallingAction);
            string animName = effects.Operations.GetAnimationNameByAction((uint)randomCallingAction);

            effects.Operations.PlayPlayerAnimation(
                attachedPed,
                dictName,
                animName,
                AnimationFlags.UpperBodyOnly | AnimationFlags.Secondary
            );

            Weapon.SetSummonStatus(true, isWeaponCloseToPed);
            Weapon.FindWaysToMoveToCoord(rightHandBonePos, true);
        }

        protected void PlayCatchWeaponSound()
        {
            catchWeaponSoundPlayer.Play();
        }

        protected void PlayWeaponCloseSound()
        {
            weaponCloseToPlayerSoundPlayer.Play(restart: false);
        }

        public void ThrowAndFlyWithWeapon()
        {
            Logger.Event("INFO", "hammer.throwFlight", "state", DiagnosticState);
            if (!IsHoldingWeapon)
            {
                return;
            }
            PlayThrowWeaponAnimation(game.Cameras.GameplayDirection);
            SetAttachedPedToRagdoll();
            isFlyingWithThrownWeapon = true;
            flyWithThrownWeaponDirection = game.Cameras.GameplayDirection;
            flyWithThrownWeaponStartTime = game.Host.GameTime;
        }

        public void ThrowWeapon(HashSet<int> targets)
        {
            Logger.Event("INFO", "hammer.throwTargets", "targets", targets.Count, "state", DiagnosticState);
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

            isWeaponAttackingTargets = true;
            weaponJustFinishedAttackingTargetsTimestamp = NULL_FLOAT;
            Vector3 firstTargetPosition = game.Entities.Position(targets.First());
            PlayThrowWeaponAnimation((firstTargetPosition - game.Entities.Position(attachedPed)).Normalized);
            ThrowWeaponOut(false);
        }

        public void ThrowWeapon()
        {
            Logger.Event("INFO", "hammer.throw", "state", DiagnosticState);
            if (!IsHoldingWeapon)
            {
                return;
            }
            PlayThrowWeaponAnimation(game.Cameras.GameplayDirection);
            ThrowWeaponOut();
        }

        protected virtual void ThrowWeaponOut(bool hasInitialVelocity = true)
        {
            Logger.Event("INFO", "hammer.release", "initialVelocity", hasInitialVelocity, "state", DiagnosticState);
            if (IsHoldingWeapon)
            {
                Weapon.WeaponObject = game.Native.Call<int>(Hash.GET_WEAPON_OBJECT_FROM_PED, attachedPed);
            }
            game.Weapons.Remove(attachedPed, Weapon.WeaponHash);
            game.Weapons.Select(attachedPed, WeaponHash.Unarmed, true);
            if (hasInitialVelocity)
            {
                var weaponVelocity = game.Cameras.GameplayDirection * THROW_WEAPON_SPEED_MULTIPLIER + THROW_WEAPON_Z_AXIS_PRECISION_COMPENSATION;
                game.Entities.SetVelocity(Weapon.WeaponObject, game.Entities.Velocity(Weapon.WeaponObject) + weaponVelocity);
            }
        }

        protected void PlayThrowWeaponAnimation(Vector3 directionToTurnTo)
        {
            var animationActionList = Weapon.ThrowActions;
            AnimationActions randomAction = ADModUtils.Utilities.Random.PickOne(animationActionList, random);
            float angleBetweenPedForwardAndCamDirection = ADModUtils.Utilities.Math.Angle(
                new Vector2(game.Entities.ForwardVector(attachedPed).X, game.Entities.ForwardVector(attachedPed).Y),
                new Vector2(directionToTurnTo.X, directionToTurnTo.Y)
            );
            bool useDefaultAnimation = Math.Abs(angleBetweenPedForwardAndCamDirection) < ANIMATION_ANGLE_RANGE_STEP;
            if (!useDefaultAnimation)
            {
                randomAction = ADModUtils.Utilities.Random.PickOneIf(animationActionList, (AnimationActions aa) => effects.Operations.DoesAnimationActionHaveAngles((uint)aa), random);
            }

            string dictName = effects.Operations.GetAnimationDictNameByAction((uint)randomAction);
            string animName = effects.GetAnimationNameForAngle(randomAction, angleBetweenPedForwardAndCamDirection);
            effects.Operations.PlayPlayerAnimation(
                attachedPed,
                dictName,
                animName,
                game.Peds.IsWalking(attachedPed) || game.Peds.IsSprinting(attachedPed) || game.Peds.IsRunning(attachedPed) || game.Entities.IsInAir(attachedPed) ?
                    AnimationFlags.UpperBodyOnly | AnimationFlags.Secondary :
                    AnimationFlags.None,
                -1,
                false
            );
            game.Host.Wait(effects.Operations.GetAnimationWaitTimeByDictNameAndAnimName(dictName, animName));
        }
    }
}
