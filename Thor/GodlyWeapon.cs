using GTA;
using GTA.Math;
using GTA.Native;
using GTA.UI;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Thor
{
    public class GodlyWeapon<T> where T : class, new()
    {
        protected static float MOVE_UPWARD_VELOCITY_MULTIPLIER = 500.0f;
        protected static float MOVE_FULL_VELOCITY_MULTIPLIER = 100.0f;
        protected static float MOVE_HALF_VELOCITY_MULTIPLIER = 40.0f;
        protected static float MOVE_CLOSE_TO_STOP_VELOCITY_MULTIPLIER = 35.0f;
        protected static float HALF_TO_STOP_DISTANCE_BEWTEEN_HAMMER_AND_PLAYER = 10.0f;
        protected static float HAMMER_ROTATION_UPWARD_LERP_RATIO = 0.4f;
        protected static float CLOSE_TO_STOP_DISTANCE_BEWTEEN_HAMMER_AND_PLAYER = 3.0f;
        protected static float CLOSE_TO_STOP_DISTANCE_BEWTEEN_HAMMER_AND_PED_TARGET = 0.5f;
        protected static float CLOSE_TO_STOP_DISTANCE_BEWTEEN_HAMMER_AND_VEHICLE_TARGET = 2f;
        protected static float APPLY_FORCE_RADIUS = 1.0f;
        protected static int PLAY_THUNDER_FX_INTERVAL_MS = 1000;
        protected static float WEAPON_MASS = 1000.0f;
        protected static T instance;
        protected Prop weaponObject;
        protected WeaponHash weaponHash;
        protected Vector3 weaponSpawnPos;
        protected ADModUtils.Utilities.Timer weaponFxTimer;
        protected bool isBeingSummoned;
        protected bool isCloseToSummoningPed;
        protected Vector3 summoningPedForwardDirection;
        protected AnimationActions[] throwActions;

        protected GodlyWeapon(WeaponHash weapon)
        {
            weaponHash = weapon;
            weaponSpawnPos = new Vector3(0.0f, 0.0f, 2000.0f);
            isBeingSummoned = false;
        }

        public virtual void OnTick()
        {
            HandleFx();
        }

        public AnimationActions[] ThrowActions
        {
            get
            {
                return throwActions;
            }
        }

        protected void HandleWeaponAvailability()
        {
            if (weaponObject != null &&
                weaponObject.Exists())
            {
                weaponObject.IsPersistent = true;

                if (weaponObject.HeightAboveGround > 1.0f &&
                    weaponObject.Velocity.Length() == 0)
                {
                    weaponObject.Velocity = Vector3.WorldUp;
                    if (weaponObject.Velocity.Length() == 0)
                    {
                        Init(weaponObject.Position + Vector3.WorldUp, true);
                    }
                    else
                    {
                        weaponObject.Velocity = Vector3.Zero;
                    }
                }
            }
        }

        protected void HandleFx()
        {
            if (weaponFxTimer != null)
            {
                weaponFxTimer.OnTick();
            }
        }

        public virtual Vector3 Velocity
        {
            set
            {
                weaponObject.Velocity = value;
            }
            get
            {
                if (weaponObject != null)
                {
                    return weaponObject.Velocity;
                }
                else
                {
                    return Vector3.Zero;
                }
            }
        }

        public void RotateToDirection(Entity weaponObj, Vector3 dir)
        {
            weaponObj.Rotation = ADModUtils.Utilities.Math.DirectionToRotation(dir) + new Vector3(-90.0f, 0.0f, 0.0f);
        }

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

        public WeaponHash WeaponHash
        {
            get
            {
                return weaponHash;
            }
        }

        public Prop WeaponObject
        {
            get
            {
                return weaponObject;
            }
            set
            {
                if (weaponObject != null)
                {
                    weaponObject.Detach();
                    weaponObject.Delete();
                }
                if (value != null)
                {
                    weaponObject = InitializeWeaponObject(value);
                    weaponFxTimer = new ADModUtils.Utilities.Timer(PLAY_THUNDER_FX_INTERVAL_MS,
                    () =>
                    {
                        NativeHelper.PlayThunderFx(weaponObject);
                    });
                }
            }
        }

        public Vector3 Position
        {
            get
            {
                if (weaponObject == null)
                {
                    return Vector3.Zero;
                }

                return Function.Call<Vector3>(Hash.GET_ENTITY_COORDS, weaponObject);
            }
        }

        public void SetSummonStatus(bool isSumoning, Ped summoningPed = null, bool isCloseToSummoningPed = false)
        {
            isBeingSummoned = isSumoning;
            this.isCloseToSummoningPed = isCloseToSummoningPed;

            if (isSumoning && summoningPed != null)
            {
                summoningPedForwardDirection = summoningPed.ForwardVector;
            }
        }

        protected Prop ActivateWeaponPhysics(Prop newWeaponObject)
        {
            Function.Call(Hash.ACTIVATE_PHYSICS, newWeaponObject);
            newWeaponObject.IsCollisionEnabled = true;
            ADModUtils.NativeHelper.SetObjectPhysicsParams(newWeaponObject, WEAPON_MASS);
            return newWeaponObject;
        }

        protected Prop InitializeWeaponObject(Prop newWeaponObject)
        {
            if (newWeaponObject == null)
            {
                return null;
            }

            newWeaponObject = ActivateWeaponPhysics(newWeaponObject);

            Blip weaponBlip = newWeaponObject.AddBlip();

            weaponBlip.IsFriendly = true;
            weaponBlip.Name = "GodlyWeapon";

            return newWeaponObject;
        }

        public void ShowParticleFx()
        {
            if (IsMoving)
            {
                ADModUtils.NativeHelper.PlayParticleFx("scr_familyscenem", "scr_meth_pipe_smoke", weaponObject);
                var fxDirection = -weaponObject.Velocity.Normalized;
                var speed = Function.Call<float>(Hash.GET_ENTITY_SPEED, weaponObject);
                Thunder.Instance.Shoot(weaponObject.Position, weaponObject.Position + fxDirection * speed * 0.08f, -1, -1, false);
            }
        }

        protected virtual bool ShouldApplyForcesToNearbyEntities()
        {
            return true;
        }

        public void ApplyForcesToNearbyEntities()
        {
            if (weaponObject == null ||
                !weaponObject.Exists() ||
                !IsMoving ||
                !ShouldApplyForcesToNearbyEntities())
            {
                return;
            }

            var entities = World.GetNearbyEntities(weaponObject.Position, APPLY_FORCE_RADIUS);

            foreach (var ent in entities)
            {
                if (ent != Game.Player.Character &&
                    ent != weaponObject)
                {
                    ent.ApplyForce(weaponObject.Velocity);
                    if (ADModUtils.NativeHelper.IsPed(ent))
                    {
                        Ped ped = (Ped)ent;
                        ped.ApplyDamage(100);
                    }
                }
            }
        }

        public void DestroyWeaponTrackCam()
        {
            WeaponCamera.Instance.DestroyCamera();
        }

        public void RenderWeaponTrackCam()
        {
            WeaponCamera.Instance.RenderCamera(weaponObject);
        }

        public void CancelRenderWeaponTrackCam()
        {
            WeaponCamera.Instance.CancelRenderedCamera();
        }

        public void Init(Vector3? spawnPos, bool forceReplacingOld = false)
        {
            if (spawnPos == null)
            {
                spawnPos = weaponSpawnPos;
            }
            if (!forceReplacingOld && weaponObject != null)
            {
                return;
            }

            try
            {
                WeaponObject = ADModUtils.NativeHelper.CreateWeaponObject(
                    weaponHash,
                    1,
                    (Vector3)spawnPos
                );
            }
            catch (Exception e)
            {
                Notification.Show("~r~Error occured when initializing GodlyWeapon. Please see the log file for more imformation.");
                ADModUtils.Logger.Log("ERROR", e.ToString());
            }
        }

        public bool MoveToTargets(ref HashSet<Entity> targets)
        {
            if (weaponObject == null)
            {
                return false;
            }
            if (targets.Count == 0)
            {
                weaponObject.Velocity = Vector3.Zero;
                return false;
            }

            var nextTarget = targets.First();

            float distanceBetweenWeaponAndNextTarget = (nextTarget.Position - Position).Length();

            if (nextTarget.Exists() &&
                nextTarget.Position != Vector3.Zero &&
                ((ADModUtils.NativeHelper.IsPed(nextTarget) && distanceBetweenWeaponAndNextTarget <= CLOSE_TO_STOP_DISTANCE_BEWTEEN_HAMMER_AND_PED_TARGET) ||
                (ADModUtils.NativeHelper.IsVehicle(nextTarget) && distanceBetweenWeaponAndNextTarget <= CLOSE_TO_STOP_DISTANCE_BEWTEEN_HAMMER_AND_VEHICLE_TARGET)))
            {
                targets.Remove(nextTarget);
            }
            else
            {
                FindWaysToMoveToCoord(nextTarget.Position, false);
            }

            return true;
        }

        public bool IsMoving
        {
            get
            {
                return weaponObject != null && weaponObject.Exists() && weaponObject.Velocity.Length() > 0;
            }
        }

        public void MoveTowardDirection(Vector3 direction, int startTime, int maxTime)
        {
            if (weaponObject == null)
            {
                return;
            }
            int endTime = startTime + maxTime;

            if (Game.GameTime >= endTime)
            {
                return;
            }

            weaponObject.Velocity = weaponObject.Velocity.Normalized + direction * MOVE_FULL_VELOCITY_MULTIPLIER;
        }

        protected float GetVelocityByDistance(Vector3 newPosition, Vector3 curPosition)
        {
            float distanceBetweenNewPosAndCurPos = (newPosition - curPosition).Length();

            if (distanceBetweenNewPosAndCurPos <= CLOSE_TO_STOP_DISTANCE_BEWTEEN_HAMMER_AND_PLAYER)
            {
                return MOVE_CLOSE_TO_STOP_VELOCITY_MULTIPLIER;
            }
            else if (distanceBetweenNewPosAndCurPos <= HALF_TO_STOP_DISTANCE_BEWTEEN_HAMMER_AND_PLAYER)
            {
                return MOVE_HALF_VELOCITY_MULTIPLIER;
            }
            else
            {
                return MOVE_FULL_VELOCITY_MULTIPLIER;
            }
        }

        public void MoveToCoord(Vector3 newPosition, bool slowDownIfClose, Vector3 blendInVelocity)
        {
            if (weaponObject == null)
            {
                return;
            }
            Vector3 moveDirection = (newPosition - Position).Normalized;
            var velocity = blendInVelocity;
            if (slowDownIfClose)
            {
                velocity += moveDirection * GetVelocityByDistance(newPosition, Position);
            }
            else
            {
                velocity += moveDirection * MOVE_FULL_VELOCITY_MULTIPLIER;
            }

            weaponObject.Velocity = weaponObject.Velocity.Normalized + velocity;
        }

        public void FindWaysToMoveToCoord(Vector3 newPosition, bool slowDownIfClose, bool canShootUpward = true)
        {

            if (ShouldShootUpward(newPosition))
            {
                var velocity = Vector3.Zero;

                if (canShootUpward)
                {
                    velocity = Vector3.Lerp(this.Velocity, Vector3.WorldUp * MOVE_UPWARD_VELOCITY_MULTIPLIER, 0.01f);
                }

                MoveToCoord(newPosition, slowDownIfClose, velocity);
            }
            else
            {
                MoveToCoord(newPosition, slowDownIfClose, Vector3.Zero);
            }
        }

        protected bool ShouldShootUpward(Vector3 newPosition)
        {
            var currentWeaponPos = weaponObject.Position;
            var raycastToTarget = World.Raycast(currentWeaponPos, newPosition, IntersectFlags.Map);

            return raycastToTarget.DidHit;
        }
    }
}
