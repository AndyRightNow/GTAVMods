using ADModUtils;
using GTA;
using GTA.Math;
using System.Windows.Forms;

namespace Thor
{
    public class MjolnirWorthyAbility : WorthyAbility<MjolnirWorthyAbility, Mjolnir>
    {

        protected bool isHoldingWeaponRope;
        protected ADModUtils.Plane weaponWhirlingPlane;
        protected ADModUtils.Plane weaponHoverWhirlingPlane;
        protected Prop weaponUsedForHoveringWhirlingOriginal;
        protected Prop weaponUsedForHoveringWhirlingShown;
        protected bool isHoverWhirling;

        public MjolnirWorthyAbility() : base()
        {
            isHoldingWeaponRope = false;
            isHoverWhirling = false;
            Weapon = Mjolnir.Instance;
            soundFileCatchWeapon = "./scripts/catch-hammer.wav";
            soundFileWeaponCloseToPed = "./scripts/hammer-close-to-player.wav";
        }

        public override void RemoveAbility()
        {
            base.RemoveAbility();

            if (weaponUsedForHoveringWhirlingOriginal != null)
            {
                weaponUsedForHoveringWhirlingOriginal.Delete();
                weaponUsedForHoveringWhirlingOriginal = null;
            }
            if (weaponUsedForHoveringWhirlingShown != null)
            {
                weaponUsedForHoveringWhirlingShown.Delete();
                weaponUsedForHoveringWhirlingShown = null;
            }
        }

        protected override void HandlePreOnTick()
        {
            InitHoverWhirledWeapon();
        }
        protected override void HandlePostHoldingWeaponOnTick()
        {
            HandleHoverWhirlingWeapon();
            HandleDropAndHoldWeaponRope();
        }
        protected override void HandlePostNotHoldingWeaponOnTick()
        {
            HandleWhirlingWeapon();
        }

        protected override bool ShouldPossessFullPower()
        {
            return isHoldingWeaponRope;
        }

        protected void HandleHoverWhirlingWeapon()
        {
            if (weaponHoverWhirlingPlane == null)
            {
                weaponHoverWhirlingPlane = new ADModUtils.Plane(Vector3.WorldUp, Vector3.Zero);
            }

            if (isFlying && isHoverWhirling)
            {
                Weapon.Whirl(weaponHoverWhirlingPlane, false, weaponUsedForHoveringWhirlingOriginal);
            }
        }

        protected void HandleWhirlingWeapon()
        {
            if (Game.IsKeyPressed(Keys.Z))
            {
                if (isHoldingWeaponRope)
                {
                    var boneCoord = attachedPed.Bones[WEAPON_HOLDING_HAND_ID].Position;

                    weaponWhirlingPlane = new ADModUtils.Plane(Vector3.Cross(attachedPed.ForwardVector, Vector3.WorldUp), boneCoord);

                    ADModUtils.NativeHelper.PlayPlayerAnimation(
                        attachedPed,
                        NativeHelper.Instance.GetAnimationDictNameByAction((uint)AnimationActions.WhirlingHammer),
                        NativeHelper.Instance.GetAnimationNameByAction((uint)AnimationActions.WhirlingHammer),
                        AnimationFlags.UpperBodyOnly | AnimationFlags.AllowRotation
                    );
                    Weapon.Whirl(weaponWhirlingPlane);
                }
            }
            else
            {
                if (isHoldingWeaponRope)
                {
                    SummonWeapon();
                }
            }
        }

        protected void HandleDropAndHoldWeaponRope()
        {
            if (Game.IsKeyPressed(Keys.Z))
            {
                DropAndHoldWeaponRope();
            }
        }

        protected override void PlayThunderFx()
        {
            base.PlayThunderFx();

            if (IsHoldingWeapon && weaponUsedForHoveringWhirlingShown != null)
            {
                NativeHelper.PlayThunderFx(weaponUsedForHoveringWhirlingShown, 0.5f);
            }
        }

        protected void InitHoverWhirledWeapon()
        {
            if (weaponUsedForHoveringWhirlingOriginal == null)
            {
                weaponUsedForHoveringWhirlingOriginal = ADModUtils.NativeHelper.CreateWeaponObject(Weapon.WeaponHash, 1, Vector3.Zero);
                weaponUsedForHoveringWhirlingOriginal.Opacity = 0;
                weaponUsedForHoveringWhirlingOriginal.SetNoCollision(attachedPed, true);
            }
            if (weaponUsedForHoveringWhirlingShown == null)
            {
                weaponUsedForHoveringWhirlingShown = ADModUtils.NativeHelper.CreateWeaponObject(Weapon.WeaponHash, 1, Vector3.Zero + new Vector3(0.0f, 0.0f, 10.0f));
                weaponUsedForHoveringWhirlingShown.Opacity = 0;
                weaponUsedForHoveringWhirlingShown.SetNoCollision(attachedPed, true);
            }
        }

        protected override bool ShouldShowWeaponPFX()
        {
            return !isHoldingWeaponRope;
        }

        protected override void HandlePreInAir()
        {
            SetHeldWeaponVisible(true);
        }
        protected override void HandleMidIsFlying(Vector3 velocity, Vector3 weaponHoldingHandCoord)
        {
            var velocityAndUpDot = Vector3.Dot(velocity.Normalized, Vector3.WorldUp);
            if (velocityAndUpDot >= 0.85f &&
                velocityAndUpDot <= 1.0f)
            {
                isHoverWhirling = true;
                SetHeldWeaponVisible(false);
            }
            else
            {
                isHoverWhirling = false;
                SetHeldWeaponVisible(true);
                Weapon.RotateToDirection(attachedPed.Weapons.CurrentWeaponObject, velocity.Normalized);
                attachedPed.Weapons.CurrentWeaponObject.Position = weaponHoldingHandCoord + velocity.Normalized * 0.3f;
            }
            if (isHoverWhirling)
            {
                weaponUsedForHoveringWhirlingShown.Position =
                    attachedPed.Bones[WEAPON_HOLDING_HAND_ID].Position +
                    (weaponUsedForHoveringWhirlingOriginal.Position - weaponHoverWhirlingPlane.Center) +
                    new Vector3(0, 0, 0.1f);
                weaponUsedForHoveringWhirlingShown.Rotation = weaponUsedForHoveringWhirlingOriginal.Rotation;
            }
        }
        protected override void HandlePreNotFlying()
        {
            SetHeldWeaponVisible(true);
        }

        protected void SetHeldWeaponVisible(bool toggle)
        {
            if (IsHoldingWeapon)
            {
                if (toggle)
                {
                    attachedPed.Weapons.CurrentWeaponObject.ResetOpacity();
                }
                else
                {
                    attachedPed.Weapons.CurrentWeaponObject.Opacity = 0;
                }
            }
            if (weaponUsedForHoveringWhirlingShown != null)
            {
                if (toggle)
                {
                    weaponUsedForHoveringWhirlingShown.Opacity = 0;

                }
                else
                {
                    weaponUsedForHoveringWhirlingShown.ResetOpacity();
                }
            }
        }

        protected override bool IsRenderWeaponCameraKeyPressed()
        {
            return Game.IsKeyPressed(Keys.R);
        }

        protected override bool IsSummonWeaponKeyPressed()
        {
            return Game.IsKeyPressed(Keys.H);
        }

        public override void SummonWeapon(bool shootUpwardFirst = false)
        {
            Weapon.DetachRope();
            isHoldingWeaponRope = false;
            base.SummonWeapon();
        }


        protected void DropAndHoldWeaponRope()
        {
            if (!IsHoldingWeapon)
            {
                return;
            }

            ThrowWeaponOut(false);
            Weapon.AttachHammerRopeTo(attachedPed, WEAPON_HOLDING_HAND_ID);
            isHoldingWeaponRope = true;
        }
    }
}
