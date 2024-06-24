using ADModUtils;
using GTA;
using GTA.Math;
using GTA.Native;
using GTA.UI;
using System;
using System.Collections.Generic;
using System.Linq;
using Thor.PathFinder;

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
        protected static float MAX_SHOOT_UPWARD_DIST = 4.0f;
        protected static int PLAY_THUNDER_FX_INTERVAL_MS = 1000;
        protected static float WEAPON_MASS = 50000.0f;
        protected static T instance;
        protected Prop weaponObject;
        protected WeaponHash weaponHash;
        protected Vector3 weaponSpawnPos;
        protected ADModUtils.Utilities.Timer weaponFxTimer;
        protected bool isBeingSummoned;
        protected bool prevHasObstaclesInBetween = false;
        protected bool isCloseToSummoningPed;
        protected Vector3 summoningPedForwardDirection;
        protected bool isInOutOfRangeIdleState = false;
        protected Vector3 outOfRangeIdleRandomVelocity = Vector3.Zero;
        protected int outOfRangeIdleRandomVelocityChangeInterval = 1000;
        protected AnimationActions[] throwActions;

        protected GodlyWeapon(WeaponHash weapon)
        {
            weaponHash = weapon;
            weaponSpawnPos = new Vector3(0.0f, 0.0f, 2000.0f);
            isBeingSummoned = false;
        }

        public virtual void OnTick()
        {
            PathFinder.PathFinder.Instance.OnTick(weaponObject);
            HandleFx();

            if (isInOutOfRangeIdleState && !isBeingSummoned)
            {
                if (Game.GameTime % outOfRangeIdleRandomVelocityChangeInterval == 0)
                {
                    outOfRangeIdleRandomVelocityChangeInterval = (int)ADModUtils.Utilities.Random.NextFloat(false) * 1000 + 1000;

                    outOfRangeIdleRandomVelocity = new Vector3(
                            ADModUtils.Utilities.Random.NextFloat(),
                            ADModUtils.Utilities.Random.NextFloat(),
                            ADModUtils.Utilities.Random.NextFloat()
                        ).Normalized * ADModUtils.Utilities.Random.NextFloat(false) * 30.0f;
                }

                weaponObject.Velocity = Vector3.Lerp(
                    weaponObject.Velocity,
                    outOfRangeIdleRandomVelocity,
                    0.5f * ADModUtils.Utilities.Random.NextFloat(false)
                    );
            }
        }

        public AnimationActions[] ThrowActions
        {
            get
            {
                return throwActions;
            }
        }

        protected float prevHeightAboveGround = 0.0f;

        protected void HandleWeaponAvailability()
        {
            if (weaponObject != null &&
                weaponObject.Exists())
            {
                weaponObject.IsPersistent = true;
                Function.Call(Hash.SET_ENTITY_SHOULD_FREEZE_WAITING_ON_COLLISION, weaponObject, false);

                if (weaponObject.HeightAboveGround - prevHeightAboveGround > 50.0f)
                {

                    if (!isBeingSummoned)
                    {
                        isInOutOfRangeIdleState = true;
                    }
                }
                else
                {
                    prevHeightAboveGround = weaponObject.HeightAboveGround;
                }

                var raycastDownwardResult = World.Raycast(weaponObject.Position, Vector3.UnitZ * -1000.0f, IntersectFlags.Map);

                if (raycastDownwardResult.DidHit)
                {
                    isInOutOfRangeIdleState = false;
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
                    PathFinder.PathFinder.Instance.SetNoCollision(weaponObject);
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
                    ent != weaponObject &&
                    !PathFinder.PathFinder.Instance.IsRelatedEntity(ent))
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

        protected void MoveToCoord(Vector3 newPosition, bool slowDownIfClose, Vector3 blendInVelocity)
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

            weaponObject.Velocity = velocity;
        }

        public void FindWaysToMoveToCoord(Vector3 newPosition, bool slowDownIfClose)
        {
            bool isHasObstaclesInBetweenReady = false;
            bool curHasObstaclesInBetween = HasObstaclesInBetween(newPosition, out isHasObstaclesInBetweenReady);

            Logger.LogConsole("[Info]", "curHasObstaclesInBetween", "" + curHasObstaclesInBetween, "isHasObstaclesInBetweenReady", "" + isHasObstaclesInBetweenReady);

            var isIsInOpenAirReady = true;
            var isInOpenAir = false;
            // IsInOpenAir(out isIsInOpenAirReady);

            Logger.LogConsole("[Info]", "isInOpenAir", "" + isInOpenAir, "isIsInOpenAirReady", "" + isIsInOpenAirReady);

            if ((!isHasObstaclesInBetweenReady || curHasObstaclesInBetween) && (!isIsInOpenAirReady || !isInOpenAir))
            {
                if (PathFinder.PathFinder.Instance.Position is null)
                {
                    Logger.LogConsole("[Warning]", "PathFinder position is null, exiting method");
                    return;
                }

                if (isHasObstaclesInBetweenReady && !prevHasObstaclesInBetween && curHasObstaclesInBetween)
                {
                    Logger.LogConsole("[Info]", "Obstacle status changed, updating start position");
                    PathFinder.PathFinder.Instance.UpdateStartPosition(weaponObject.Position);
                }

                prevHasObstaclesInBetween = curHasObstaclesInBetween;

                PathFinder.PathFinder.Instance.UpdateCurrentTarget(newPosition);

                weaponObject.Velocity = PathFinder.PathFinder.Instance.GetTargetVelocity(weaponObject.Position);
                return;
            }

            prevHasObstaclesInBetween = curHasObstaclesInBetween;

            MoveToCoord(newPosition, slowDownIfClose, Vector3.Zero);
            Logger.LogConsole("[Info]", "Moving directly to coordinates without pathfinder");
        }





        private static float MAX_SHAPE_TEST_TIMEOUT = 2000;
        private ShapeTestHandle CurrentHasObstaclesInBetweenTestHandle;
        private bool IsCurrentHasObstaclesInBetweenTestOngoing;
        private float LastHasObstaclesInBetweenTestTime = 0;
        protected bool HasObstaclesInBetween(Vector3 newPosition, out bool isReady)
        {
            var currentWeaponPos = weaponObject.Position;
            isReady = true;

            if (Game.GameTime - LastHasObstaclesInBetweenTestTime >= MAX_SHAPE_TEST_TIMEOUT)
            {
                Logger.LogConsole("[Info]", "HasObstaclesInBetween Passed Timeout, Reset");
                IsCurrentHasObstaclesInBetweenTestOngoing = false;
            }

            if (!IsCurrentHasObstaclesInBetweenTestOngoing)
            {
                IsCurrentHasObstaclesInBetweenTestOngoing = true;
                LastHasObstaclesInBetweenTestTime = Game.GameTime;
                CurrentHasObstaclesInBetweenTestHandle = ShapeTest.StartTestCapsule(currentWeaponPos, newPosition, 0.5f, IntersectFlags.Map, null, ShapeTestOptions.IgnoreGlass);

                if (CurrentHasObstaclesInBetweenTestHandle.IsRequestFailed)
                {

                    Logger.LogConsole("[Info]", "HasObstaclesInBetween Request Failed");
                    isReady = false;
                    IsCurrentHasObstaclesInBetweenTestOngoing = false;
                    return false;
                }
            }

            ShapeTestResult raycastResult;
            var raycastStatus = CurrentHasObstaclesInBetweenTestHandle.GetResult(out raycastResult);

            if (raycastStatus != ShapeTestStatus.Ready)
            {
                Logger.LogConsole("[Info]", "HasObstaclesInBetween ShapeTestStatus NOT READY");
                isReady = false;
                return false;
            }
            IsCurrentHasObstaclesInBetweenTestOngoing = false;

            return raycastResult.DidHit && World.GetDistance(currentWeaponPos, raycastResult.HitPosition) <= 100.0f;
        }


        private static float OPEN_AIR_TEST_RADIUS = 10.0f;
        private ShapeTestHandle CurrentIsInOpenAirTestCapsuleXHandle;
        private ShapeTestHandle CurrentIsInOpenAirTestCapsuleYHandle;
        private ShapeTestHandle CurrentIsInOpenAirTestCapsuleZHandle;
        private bool IsCurrentIsInOpenAirTestCapsuleOngoing;
        private float LastIsInOpenAirTestTime = 0;
        protected bool IsInOpenAir(out bool isReady)
        {
            var currentWeaponPos = weaponObject.Position;
            isReady = true;

            if (Game.GameTime - LastIsInOpenAirTestTime >= MAX_SHAPE_TEST_TIMEOUT)
            {
                IsCurrentIsInOpenAirTestCapsuleOngoing = false;
            }

            if (!IsCurrentIsInOpenAirTestCapsuleOngoing)
            {
                IsCurrentIsInOpenAirTestCapsuleOngoing = true;
                LastIsInOpenAirTestTime = Game.GameTime;
                CurrentIsInOpenAirTestCapsuleXHandle = ShapeTest.StartTestCapsule(currentWeaponPos - Vector3.UnitX * OPEN_AIR_TEST_RADIUS, currentWeaponPos + Vector3.UnitX * OPEN_AIR_TEST_RADIUS, OPEN_AIR_TEST_RADIUS, IntersectFlags.Map, null, ShapeTestOptions.IgnoreGlass);

                if (CurrentIsInOpenAirTestCapsuleXHandle.IsRequestFailed)
                {
                    Logger.LogConsole("[Info]", "IsInOpenAir Request Failed");
                    IsCurrentIsInOpenAirTestCapsuleOngoing = false;
                    isReady = false;
                    return false;
                }

                CurrentIsInOpenAirTestCapsuleYHandle = ShapeTest.StartTestCapsule(currentWeaponPos - Vector3.UnitY * OPEN_AIR_TEST_RADIUS, currentWeaponPos + Vector3.UnitY * OPEN_AIR_TEST_RADIUS, OPEN_AIR_TEST_RADIUS, IntersectFlags.Map, null, ShapeTestOptions.IgnoreGlass);

                if (CurrentIsInOpenAirTestCapsuleYHandle.IsRequestFailed)
                {
                    Logger.LogConsole("[Info]", "IsInOpenAir Request Failed");
                    IsCurrentIsInOpenAirTestCapsuleOngoing = false;
                    isReady = false;
                    return false;
                }

                CurrentIsInOpenAirTestCapsuleZHandle = ShapeTest.StartTestCapsule(currentWeaponPos - Vector3.UnitZ * OPEN_AIR_TEST_RADIUS, currentWeaponPos + Vector3.UnitZ * OPEN_AIR_TEST_RADIUS, OPEN_AIR_TEST_RADIUS, IntersectFlags.Map, null, ShapeTestOptions.IgnoreGlass);

                if (CurrentIsInOpenAirTestCapsuleZHandle.IsRequestFailed)
                {
                    Logger.LogConsole("[Info]", "IsInOpenAir Request Failed");
                    IsCurrentIsInOpenAirTestCapsuleOngoing = false;
                    isReady = false;
                    return false;
                }
            }

            ShapeTestResult raycastXResult;
            ShapeTestResult raycastYResult;
            ShapeTestResult raycastZResult;

            var raycastXResultStatus = CurrentIsInOpenAirTestCapsuleXHandle.GetResult(out raycastXResult);


            if (raycastXResultStatus != ShapeTestStatus.Ready)
            {
                Logger.LogConsole("[Info]", "IsInOpenAir ShapeTestStatus NOT READY");
                isReady = false;
                return false;
            }

            var raycastYResultStatus = CurrentIsInOpenAirTestCapsuleYHandle.GetResult(out raycastYResult);
            if (raycastXResultStatus != ShapeTestStatus.Ready)
            {
                Logger.LogConsole("[Info]", "IsInOpenAir ShapeTestStatus NOT READY");
                isReady = false;
                return false;
            }

            var raycastZResultStatus = CurrentIsInOpenAirTestCapsuleZHandle.GetResult(out raycastZResult);
            if (raycastXResultStatus != ShapeTestStatus.Ready)
            {
                Logger.LogConsole("[Info]", "IsInOpenAir ShapeTestStatus NOT READY");
                isReady = false;
                return false;
            }

            IsCurrentIsInOpenAirTestCapsuleOngoing = false;

            return !raycastXResult.DidHit && !raycastYResult.DidHit && !raycastZResult.DidHit;
        }
    }
}
