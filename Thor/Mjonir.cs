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
    class Mjonir
    {
        private static Mjonir instance;
        private Rope weaponObject;
        private float sizeScale;
        private int weaponHash;
        private Vector3 weaponSpawnPos;

        private Mjonir()
        {
            sizeScale = 2.0f;
            weaponHash = (int)GTA.Native.WeaponHash.Hammer;
            weaponSpawnPos = new Vector3(0.0f, 0.0f, 2000.0f);
        }

        public delegate void Wait(int ms);

        public static Mjonir Instance
        {
            get
            {
                if (instance == null)
                {
                    instance = new Mjonir();
                }

                return instance;
            }
        }

        public int WeaponHash
        {
            get
            {
                return weaponHash;
            }
        }

        public Rope WeaponObject
        {
            get
            {
                return weaponObject;
            }
            set
            {
                weaponObject = InitializeWeaponObject(value);
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

        private Rope ActivateWeaponPhysics(Rope newWeaponObject)
        {
            Function.Call(Hash.ACTIVATE_PHYSICS, newWeaponObject);
            Function.Call(
                Hash.SET_OBJECT_PHYSICS_PARAMS,
                newWeaponObject,
                10000000000.0f,
                -1,
                0.0f,
                0.0f,
                0.0f,
                0.0f,
                0.0f,
                0.0f,
                0.0f
            );

            return newWeaponObject;
        }

        private Rope InitializeWeaponObject(Rope newWeaponObject)
        {
            newWeaponObject = ActivateWeaponPhysics(newWeaponObject);

            Blip weaponBlip = Function.Call<Blip>(Hash.ADD_BLIP_FOR_ENTITY, newWeaponObject);
            Function.Call(Hash.SET_BLIP_AS_FRIENDLY, weaponBlip, true);
            Function.Call(Hash.BEGIN_TEXT_COMMAND_SET_BLIP_NAME, "STRING");
            Function.Call(Hash._ADD_TEXT_COMPONENT_STRING, "Mjonir");
            Function.Call(Hash.END_TEXT_COMMAND_SET_BLIP_NAME, weaponBlip);

            return newWeaponObject;
        }

        public void Init(bool asWeaponOfPlayer, Wait waitFunc)
        {
            if (weaponObject != null)
            {
                return;
            }

            try
            {
                Function.Call(Hash.REQUEST_WEAPON_ASSET, weaponHash);
                while (!Function.Call<bool>(Hash.HAS_WEAPON_ASSET_LOADED, weaponHash))
                {
                    waitFunc(0);
                }

                weaponObject = Function.Call<Rope>(
                    Hash.CREATE_WEAPON_OBJECT,
                    weaponHash,
                    1,
                    weaponSpawnPos.X,
                    weaponSpawnPos.Y,
                    weaponSpawnPos.Z,
                    false,
                    sizeScale
                );

                weaponObject = InitializeWeaponObject(weaponObject);

                if (asWeaponOfPlayer)
                {
                    Function.Call(Hash.GIVE_WEAPON_OBJECT_TO_PED, weaponObject, Game.Player.Character);
                }
            }
            catch (Exception e)
            {
                UI.Notify("~r~Error occured when initializing Mjonir. Please see the log file for more imformation.");
                Logger.Log("ERROR", e.Message);
            }
        }

        public void MoveToCoordWithPhysics(Vector3 newPosition)
        {
            Function.Call(Hash.SET_ENTITY_COORDS_NO_OFFSET, weaponObject, newPosition.X, newPosition.Y, newPosition.Z, 0, 0, 0);
            weaponObject = ActivateWeaponPhysics(weaponObject);
        }
    }
}
