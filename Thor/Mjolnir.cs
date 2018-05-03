using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using GTA;
using GTA.Native;
using GTA.Math;
using System.Windows.Forms;
using System.Threading;

namespace Thor
{
    class Mjolnir
    {
        private static float MOVE_FULL_VELOCITY_MULTIPLIER = 150.0f;
        private static float MOVE_HALF_VELOCITY_MULTIPLIER = 80.0f;
        private static float MOVE_CLOSE_TO_STOP_VELOCITY_MULTIPLIER = 60.0f;
        private static float HALF_TO_STOP_DISTANCE_BEWTEEN_HAMMER_AND_PLAYER = 10.0f;
        private static float CLOSE_TO_STOP_DISTANCE_BEWTEEN_HAMMER_AND_PLAYER = 3.0f;
        private static float CLOSE_TO_STOP_DISTANCE_BEWTEEN_HAMMER_AND_PED_TARGET = 0.5f;
        private static float CLOSE_TO_STOP_DISTANCE_BEWTEEN_HAMMER_AND_VEHICLE_TARGET = 2f;
        private static float APPLY_FORCE_RADIUS = 1.0f;
        private static int PLAY_THUNDER_FX_INTERVAL_MS = 1000;
        private static float WEAPON_MASS = 100000000000.0f;
        private static Mjolnir instance;
        private Entity weaponObject;
        private WeaponHash weaponHash;
        private Vector3 weaponSpawnPos;
        private Utilities.Timer hammerFxTimer;

        private Mjolnir()
        {
            weaponHash = WeaponHash.Hammer;
            weaponSpawnPos = new Vector3(0.0f, 0.0f, 2000.0f);
        }

        public void OnTick()
        {
            if (hammerFxTimer != null)
            {
                hammerFxTimer.OnTick();
            }

            if (weaponObject != null && 
                weaponObject.Exists() &&
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

        public static Mjolnir Instance
        {
            get
            {
                if (instance == null)
                {
                    instance = new Mjolnir();
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

        public Entity WeaponObject
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
                    hammerFxTimer = new Utilities.Timer(PLAY_THUNDER_FX_INTERVAL_MS,
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

        private Entity ActivateWeaponPhysics(Entity newWeaponObject)
        {
            Function.Call(Hash.ACTIVATE_PHYSICS, newWeaponObject);
            NativeHelper.SetObjectPhysicsParams(newWeaponObject, WEAPON_MASS);
            return newWeaponObject;
        }

        private Entity InitializeWeaponObject(Entity newWeaponObject)
        {
            if (newWeaponObject == null)
            {
                return null;
            }

            newWeaponObject = ActivateWeaponPhysics(newWeaponObject);

            Blip weaponBlip = newWeaponObject.AddBlip();
            weaponBlip.IsFriendly = true;
            weaponBlip.Name = "Mjolnir";

            return newWeaponObject;
        }

        public void ShowParticleFx()
        {
            NativeHelper.PlayThunderFx(weaponObject);
            if (IsMoving)
            {
                NativeHelper.PlayParticleFx("scr_familyscenem", "scr_meth_pipe_smoke", weaponObject);
            }
        }

        public void ApplyForcesToNearbyEntities()
        {
            if (weaponObject == null ||
                !weaponObject.Exists() ||
                !IsMoving)
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
                    if (Function.Call<bool>(Hash.IS_ENTITY_A_PED, ent))
                    {
                        Ped ped = (Ped) ent;
                        ped.ApplyDamage(100);
                    }
                }
            }
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
                WeaponObject = NativeHelper.CreateWeaponObject(
                    weaponHash,
                    1,
                    (Vector3) spawnPos
                );
            }
            catch (Exception e)
            {
                UI.Notify("~r~Error occured when initializing Mjolnir. Please see the log file for more imformation.");
                Logger.Log("ERROR", e.ToString());
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

            float distanceBetweenHammerAndNextTarget = (nextTarget.Position - Position).Length();

            if ((NativeHelper.IsPed(nextTarget) && distanceBetweenHammerAndNextTarget <= CLOSE_TO_STOP_DISTANCE_BEWTEEN_HAMMER_AND_PED_TARGET) ||
                (NativeHelper.IsVehicle(nextTarget) && distanceBetweenHammerAndNextTarget <= CLOSE_TO_STOP_DISTANCE_BEWTEEN_HAMMER_AND_VEHICLE_TARGET))
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

            weaponObject.Velocity = direction * MOVE_FULL_VELOCITY_MULTIPLIER;
        }

        private float GetVelocityByDistance(Vector3 newPosition, Vector3 curPosition)
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

            weaponObject.Velocity = velocity;
        }

        public void FindWaysToMoveToCoord(Vector3 newPosition, bool slowDownIfClose, bool canShootUpward = true)
        {
            var currentWeaponPos = weaponObject.Position;
            var raycastToTarget = World.Raycast(currentWeaponPos, newPosition, IntersectOptions.Map);

            if (raycastToTarget.DitHitAnything)
            {
                var velocity = Vector3.Zero;

                //var raycastDistance = 100.0f;
                //var raycastMoveStep = 1.0f;
                //var closeCompareDelta = 5.0f;
                //var raycastUp = World.Raycast(currentWeaponPos, new Vector3(0.0f, 0.0f, 100.0f), IntersectOptions.Map);
                //if (raycastUp.DitHitAnything)
                //{
                //    var topLeftPos = currentWeaponPos + new Vector3(-raycastDistance, raycastDistance, 0.0f);
                //    var topRightPos = currentWeaponPos + new Vector3(raycastDistance, raycastDistance, 0.0f);
                //    var bottomLeftPos = currentWeaponPos + new Vector3(-raycastDistance, -raycastDistance, 0.0f);
                //    var bottomRightPos = currentWeaponPos + new Vector3(raycastDistance, -raycastDistance, 0.0f);
                //    var foundDirection = false;
                //    var currentRaycastTarget = topLeftPos;
                //    // Top left to top right
                //    while (!Utilities.Math.CloseTo(currentRaycastTarget.X, topRightPos.X, closeCompareDelta))
                //    {
                //        Script.Wait(1);
                //        currentRaycastTarget.X += raycastMoveStep;
                //        var raycast = World.Raycast(currentWeaponPos, currentRaycastTarget, IntersectOptions.Map);
                //        if (!raycast.DitHitAnything)
                //        {
                //            foundDirection = true;
                //        }
                //    }

                //    // Top right to bottom right
                //    if (!foundDirection)
                //    {
                //        while (!Utilities.Math.CloseTo(currentRaycastTarget.Y, bottomRightPos.Y, closeCompareDelta))
                //        {
                //            Script.Wait(1);
                //            currentRaycastTarget.Y -= raycastMoveStep;
                //            var raycast = World.Raycast(currentWeaponPos, currentRaycastTarget, IntersectOptions.Map);
                //            if (!raycast.DitHitAnything)
                //            {
                //                foundDirection = true;
                //            }
                //        }
                //    }

                //    // Bottom right to bottom left
                //    if (!foundDirection)
                //    {
                //        while (!Utilities.Math.CloseTo(currentRaycastTarget.X, bottomLeftPos.X, closeCompareDelta))
                //        {
                //            Script.Wait(1);
                //            currentRaycastTarget.X -= raycastMoveStep;
                //            var raycast = World.Raycast(currentWeaponPos, currentRaycastTarget, IntersectOptions.Map);
                //            if (!raycast.DitHitAnything)
                //            {
                //                foundDirection = true;
                //            }
                //        }
                //    }

                //    // Bottom left to top left
                //    if (!foundDirection)
                //    {
                //        while (!Utilities.Math.CloseTo(currentRaycastTarget.Y, topLeftPos.Y, closeCompareDelta))
                //        {
                //            Script.Wait(1);
                //            currentRaycastTarget.Y += raycastMoveStep;
                //            var raycast = World.Raycast(currentWeaponPos, currentRaycastTarget, IntersectOptions.Map);
                //            if (!raycast.DitHitAnything)
                //            {
                //                foundDirection = true;
                //            }
                //        }
                //    }

                //    if (foundDirection)
                //    {
                //        velocity += (currentRaycastTarget - currentWeaponPos).Normalized * GetVelocityByDistance(newPosition, currentWeaponPos);
                //    }
                //}

                if (canShootUpward)
                {
                    velocity += Vector3.WorldUp * GetVelocityByDistance(newPosition, currentWeaponPos);
                }

                MoveToCoord(newPosition, slowDownIfClose, velocity);
            }
            else
            {
                MoveToCoord(newPosition, slowDownIfClose, Vector3.Zero);
            }
        }
    }
}
