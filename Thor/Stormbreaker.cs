using GTA;
using GTA.Native;
using GTA.Math;
using System.Collections.Generic;

namespace Thor
{
    public class Stormbreaker : GodlyWeapon<Stormbreaker>
    {
        private Vector3 prevRotation;

        public Stormbreaker() : base(WeaponHash.GolfClub)
        {
            throwActions = new List<AnimationActions>
                {
                    AnimationActions.ThrowTwoHandHammer1,
                    AnimationActions.ThrowTwoHandHammer2,
                }.ToArray();
        }

        public override void OnTick()
        {
            base.OnTick();
            HandleWeaponAvailability();
            HandleWeaponSummonRotation();
        }

        public void SetThrownOutInitialRotation(Vector3 rot)
        {
            prevRotation = rot;
        }

        private void HandleWeaponSummonRotation()
        {
            if (IsMoving && weaponObject.IsInAir)
            {
                if (isBeingSummoned && isCloseToSummoningPed)
                {
                    weaponObject.Rotation = Vector3.Lerp(
                        weaponObject.Rotation,
                        ADModUtils.Utilities.Math.DirectionToRotation(summoningPedForwardDirection),
                        HAMMER_ROTATION_UPWARD_LERP_RATIO
                    );
                }
                else
                {
                    if (weaponObject.HasCollidedWithAnything)
                    {
                        Script.Wait(1);
                        prevRotation = weaponObject.Rotation;
                    }

                    prevRotation = Vector3.Lerp(
                        prevRotation, 
                        prevRotation + new Vector3(0.0f, 90.0f, 0.0f),
                        weaponObject.Velocity.Length() / 300.0f
                    );
                    weaponObject.Rotation = prevRotation;
                }
            }
        }
    }
}
