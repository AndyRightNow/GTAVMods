using GTA;
using GTA.Native;
using GTA.Math;
using System.Collections.Generic;

namespace Thor
{
    public class Stormbreaker : GodlyWeapon<Stormbreaker>
    {
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
                    weaponObject.Rotation = Vector3.Lerp(
                        weaponObject.Rotation,
                        ADModUtils.Utilities.Math.DirectionToRotation(weaponObject.Velocity.Normalized) + new Vector3(-90.0f, 0.0f, 0.0f),
                        0.5f
                    );
                }
            }
        }
    }
}
