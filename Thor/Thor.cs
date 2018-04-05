using GTA;
using GTA.Native;
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Reflection;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using System.ComponentModel;
using GTA.Math;

namespace Thor
{
    public class Thor : Script
    {

        public Thor()
        {
            Tick += OnTick;
            Interval = 0;
        }

        void OnTick(Object sender, EventArgs e)
        {
            GTA.GameplayCamera.ClampPitch(-90.0f, 90.0f);
            Vector3 forwardVector = Function.Call<Vector3>(Hash.GET_ENTITY_FORWARD_VECTOR, Game.Player.Character);
            UI.ShowSubtitle(String.Format("Camera direction vector {0}, pitch {1}", GTA.GameplayCamera.Direction, GTA.GameplayCamera.RelativePitch));

            bool isJumpPressed = false;
            bool isJumpRelease = false;
            bool isUpPressed = false;
            bool isUpReleased = false;


            if (Game.IsControlPressed(0, GTA.Control.Jump))
            {
                isJumpPressed = true;
                isJumpRelease = false;
            }
            else if (Game.IsControlJustReleased(0, GTA.Control.Jump))
            {
                isJumpRelease = true;
                isJumpPressed = false;
            }
            if (Game.IsControlPressed(0, GTA.Control.ScriptPadUp))
            {
                isUpPressed = true;
                isUpReleased = false;
            }
            else if (Game.IsControlJustReleased(0, GTA.Control.ScriptPadUp))
            {

                isUpPressed = false;
                isUpReleased = true;
            }

            if (isJumpPressed)
            {
                GTA.GameplayCamera.StopShaking();
                Function.Call(Hash.SET_PED_CAN_RAGDOLL, Game.Player.Character, true);
                Function.Call(Hash.SET_PED_TO_RAGDOLL, Game.Player.Character, 1, 1, 2, false, false, false);

                Entity currentWeapon = Function.Call<Entity>(Hash.GET_CURRENT_PED_WEAPON_ENTITY_INDEX, Game.Player.Character);
                Vector3 weaponPos = Game.Player.Character.Position + new Vector3(10.0f, 0.0f, 0.0f);
                Function.Call(Hash.REQUEST_WEAPON_ASSET, (int)WeaponHash.Hammer);
                while (!Function.Call<bool>(Hash.HAS_WEAPON_ASSET_LOADED, (int)WeaponHash.Hammer))
                {
                    Wait(0);
                }
                var weaponObject = Function.Call<Rope>(Hash.CREATE_WEAPON_OBJECT, (int)WeaponHash.Hammer, 1, weaponPos.X, weaponPos.Y, weaponPos.Z, true, 6.0f);
                if (weaponObject != null)
                {
                    Function.Call(Hash.ACTIVATE_PHYSICS, weaponObject);
                    Function.Call(Hash.GIVE_WEAPON_OBJECT_TO_PED, weaponObject, Game.Player.Character);
                }

                if (currentWeapon != null)
                {
                    float forwardMultiplier = 1.0f;
                    Vector3 upVelocity = new Vector3(0.0f, 0.0f, 50.0f);
                    if (isUpPressed)
                    {
                        forwardMultiplier *= 100.0f;
                        if (GameplayCamera.RelativePitch > 0)
                        {
                            upVelocity = Vector3.Zero;
                        }
                    }
                    Vector3 velocity = GTA.GameplayCamera.Direction * forwardMultiplier + upVelocity;
                    Function.Call<GTA.Math.Vector3>(Hash.SET_ENTITY_VELOCITY, currentWeapon, velocity.X, velocity.Y, velocity.Z);

                }

            }
        }
    }
}
