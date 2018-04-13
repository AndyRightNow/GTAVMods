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
        private static float MOVE_FULL_VELOCITY_MULTIPLIER = 200.0f;
        private static float MOVE_HALF_VELOCITY_MULTIPLIER = 90.0f;
        private static float MOVE_CLOSE_TO_STOP_VELOCITY_MULTIPLIER = 60.0f;
        private static float HALF_TO_STOP_DISTANCE_BEWTEEN_HAMMER_AND_PLAYER = 10.0f;
        private static float CLOSE_TO_STOP_DISTANCE_BEWTEEN_HAMMER_AND_PLAYER = 3.0f;
        private static float CLOSE_TO_STOP_DISTANCE_BEWTEEN_HAMMER_AND_PED_TARGET = 0.3f;
        private static float CLOSE_TO_STOP_DISTANCE_BEWTEEN_HAMMER_AND_VEHICLE_TARGET = 3f;
        private static float WEAPON_MASS = 100000000000.0f;
        private static Mjolnir instance;
        private Entity weaponObject;
        private WeaponHash weaponHash;
        private Vector3 weaponSpawnPos;
        
        private Mjolnir()
        {
            weaponHash = WeaponHash.Hammer;
            weaponSpawnPos = new Vector3(0.0f, 0.0f, 2000.0f);
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
                weaponObject.Detach();
                weaponObject.Delete();
                if (value != null)
                {
                    weaponObject = InitializeWeaponObject(value);
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
            newWeaponObject = ActivateWeaponPhysics(newWeaponObject);

            Blip weaponBlip = newWeaponObject.AddBlip();
            weaponBlip.IsFriendly = true;
            weaponBlip.Name = "Mjolnir";

            return newWeaponObject;
        }

        public void ShowParticleFx()
        {
            NativeHelper.PlayParticleFx("scr_familyscenem", "scr_meth_pipe_smoke", weaponObject);
        }

        public void Init(bool asWeaponOfPlayer)
        {
            if (weaponObject != null)
            {
                return;
            }

            try
            {
                weaponObject = NativeHelper.CreateWeaponObject(
                    weaponHash,
                    1,
                    weaponSpawnPos
                );

                weaponObject = InitializeWeaponObject(weaponObject);

                if (asWeaponOfPlayer)
                {
                    Function.Call(Hash.GIVE_WEAPON_OBJECT_TO_PED, weaponObject, Game.Player.Character);
                }
            }
            catch (Exception e)
            {
                UI.Notify("~r~Error occured when initializing Mjolnir. Please see the log file for more imformation.");
                Logger.Log("ERROR", e.ToString());
            }
        }

        public bool MoveToTargets(ref HashSet<Entity> targets)
        {
            if (targets.Count == 0)
            {
                weaponObject.Velocity = Vector3.Zero;
                return false;
            }

            var nextTarget = targets.First();

            Vector3 moveDirection = (nextTarget.Position - Position).Normalized;
            float distanceBetweenHammerAndNextTarget = (nextTarget.Position - Position).Length();
            
            if (NativeHelper.IsPed(nextTarget) && distanceBetweenHammerAndNextTarget <= CLOSE_TO_STOP_DISTANCE_BEWTEEN_HAMMER_AND_PED_TARGET ||
                NativeHelper.IsVehicle(nextTarget) && distanceBetweenHammerAndNextTarget <= CLOSE_TO_STOP_DISTANCE_BEWTEEN_HAMMER_AND_VEHICLE_TARGET)
            {
                targets.Remove(nextTarget);
            }
            else
            {
                weaponObject.Velocity = moveDirection * MOVE_HALF_VELOCITY_MULTIPLIER;
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
            int endTime = startTime + maxTime;

            if (Game.GameTime >= endTime)
            {
                return;
            }

            weaponObject.Velocity = direction * MOVE_FULL_VELOCITY_MULTIPLIER;
        }

        public void MoveToCoord(Vector3 newPosition, bool slowDownIfClose)
        {
            Vector3 moveDirection = (newPosition - Position).Normalized;
            if (slowDownIfClose)
            {
                float distanceBetweenNewPosAndCurPos = (newPosition - Position).Length();

                if (distanceBetweenNewPosAndCurPos <= CLOSE_TO_STOP_DISTANCE_BEWTEEN_HAMMER_AND_PLAYER)
                {
                    weaponObject.Velocity = moveDirection * MOVE_CLOSE_TO_STOP_VELOCITY_MULTIPLIER;
                }
                else if (distanceBetweenNewPosAndCurPos <= HALF_TO_STOP_DISTANCE_BEWTEEN_HAMMER_AND_PLAYER)
                {
                    weaponObject.Velocity = moveDirection * MOVE_HALF_VELOCITY_MULTIPLIER;
                }
                else
                {
                    weaponObject.Velocity = moveDirection * MOVE_FULL_VELOCITY_MULTIPLIER;
                }
            }
            else
            {
                weaponObject.Velocity = moveDirection * MOVE_FULL_VELOCITY_MULTIPLIER;
            }
        }
    }
}
