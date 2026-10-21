using ADModUtils;
using GTA;
using GTAVMods.Game;
using GTA.Math;
using System.Windows.Forms;

namespace Thor
{
    public class MjolnirWorthyAbility : WorthyAbility
    {

        protected bool isHoldingWeaponRope;
        protected ADModUtils.Plane weaponWhirlingPlane;
        protected ADModUtils.Plane weaponHoverWhirlingPlane;
        protected int weaponUsedForHoveringWhirlingOriginal;
        protected int weaponUsedForHoveringWhirlingShown;
        protected bool isHoverWhirling;
        public override string DiagnosticState => base.DiagnosticState + ";rope=" + isHoldingWeaponRope + ";hoverWhirl=" + isHoverWhirling;

        public MjolnirWorthyAbility(GameServices game, NativeHelper effects, System.Random random,
            Mjolnir weapon, IAudioPlaybackFactory audio, IEventLog log, GameplayTuning tuning)
            : base(game, effects, random, weapon, audio, log, tuning)
        {
            isHoldingWeaponRope = false;
            isHoverWhirling = false;
            weaponHoverWhirlingPlane = new ADModUtils.Plane(Vector3.WorldUp, Vector3.Zero, random);
        }

        public override void RemoveAbility()
        {
            Cleanup.Run(
                base.RemoveAbility,
                Weapon.StopEffects,
                () =>
                {
                    if (weaponUsedForHoveringWhirlingOriginal != 0)
                    {
                        if (game.Entities.Exists(weaponUsedForHoveringWhirlingOriginal))
                            game.Entities.Delete(weaponUsedForHoveringWhirlingOriginal);
                        weaponUsedForHoveringWhirlingOriginal = 0;
                    }
                },
                () =>
                {
                    if (weaponUsedForHoveringWhirlingShown != 0)
                    {
                        if (game.Entities.Exists(weaponUsedForHoveringWhirlingShown))
                            game.Entities.Delete(weaponUsedForHoveringWhirlingShown);
                        weaponUsedForHoveringWhirlingShown = 0;
                    }
                });
            isHoldingWeaponRope = false;
            isHoverWhirling = false;
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
            if (isFlying && isHoverWhirling)
            {
                Weapon.Whirl(weaponHoverWhirlingPlane, false, weaponUsedForHoveringWhirlingOriginal);
            }
        }

        protected void HandleWhirlingWeapon()
        {
            if (game.Input.IsKeyPressed(Keys.Z))
            {
                if (isHoldingWeaponRope)
                {
                    var boneCoord = game.Peds.BonePosition(attachedPed, WEAPON_HOLDING_HAND_ID);

                    weaponWhirlingPlane = new ADModUtils.Plane(Vector3.Cross(game.Entities.ForwardVector(attachedPed), Vector3.WorldUp), boneCoord, random);

                    effects.Operations.PlayPlayerAnimation(
                        attachedPed,
                        effects.Operations.GetAnimationDictNameByAction((uint)AnimationActions.WhirlingHammer),
                        effects.Operations.GetAnimationNameByAction((uint)AnimationActions.WhirlingHammer),
                        AnimationFlags.UpperBodyOnly | AnimationFlags.Secondary
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
            if (game.Input.IsKeyPressed(Keys.Z))
            {
                DropAndHoldWeaponRope();
            }
        }

        protected override void PlayThunderFx()
        {
            base.PlayThunderFx();

            if (IsHoldingWeapon)
            {
                effects.PlayThunderFx(weaponUsedForHoveringWhirlingShown, tuning.Ability.HeldLightningScale);
            }
        }

        protected void InitHoverWhirledWeapon()
        {
            if (weaponUsedForHoveringWhirlingOriginal == 0)
            {
                weaponUsedForHoveringWhirlingOriginal = effects.Operations.CreateWeaponObject(Weapon.WeaponHash, 1, Vector3.Zero);
                if (weaponUsedForHoveringWhirlingOriginal == 0)
                    throw new System.InvalidOperationException("Could not create the hidden whirling hammer.");
                game.Entities.SetOpacity(weaponUsedForHoveringWhirlingOriginal, 0);
                game.Entities.SetNoCollision(weaponUsedForHoveringWhirlingOriginal, attachedPed, true);
            }
            if (weaponUsedForHoveringWhirlingShown == 0)
            {
                weaponUsedForHoveringWhirlingShown = effects.Operations.CreateWeaponObject(Weapon.WeaponHash, 1, tuning.Ability.HiddenHoverSpawnPosition);
                if (weaponUsedForHoveringWhirlingShown == 0)
                    throw new System.InvalidOperationException("Could not create the visible whirling hammer.");
                game.Entities.SetOpacity(weaponUsedForHoveringWhirlingShown, 0);
                game.Entities.SetNoCollision(weaponUsedForHoveringWhirlingShown, attachedPed, true);
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
            if (velocityAndUpDot >= tuning.Ability.HoverUpDotMinimum &&
                velocityAndUpDot <= 1.0f)
            {
                isHoverWhirling = true;
                SetHeldWeaponVisible(false);
            }
            else
            {
                isHoverWhirling = false;
                SetHeldWeaponVisible(true);
                Weapon.RotateToDirection(game.Weapons.CurrentObject(attachedPed), velocity.Normalized);
                game.Entities.SetPosition(game.Weapons.CurrentObject(attachedPed), weaponHoldingHandCoord + velocity.Normalized * tuning.Ability.ForwardHammerOffset);
            }
            if (isHoverWhirling)
            {
                game.Entities.SetPosition(weaponUsedForHoveringWhirlingShown, game.Peds.BonePosition(attachedPed, WEAPON_HOLDING_HAND_ID) +
                    (game.Entities.Position(weaponUsedForHoveringWhirlingOriginal) - weaponHoverWhirlingPlane.Center) +
                    tuning.Ability.HoverOffset);
                game.Entities.SetRotation(weaponUsedForHoveringWhirlingShown, game.Entities.Rotation(weaponUsedForHoveringWhirlingOriginal));
            }
        }
        protected override void HandlePreNotFlying()
        {
            SetHeldWeaponVisible(true);
        }

        protected void SetHeldWeaponVisible(bool toggle)
        {
            if (toggle)
            {
                game.Entities.ResetOpacity(game.Weapons.CurrentObject(attachedPed));
            }
            else
            {
                game.Entities.SetOpacity(game.Weapons.CurrentObject(attachedPed), 0);
            }
            if (toggle)
            {
                game.Entities.SetOpacity(weaponUsedForHoveringWhirlingShown, 0);
            }
            else
            {
                game.Entities.ResetOpacity(weaponUsedForHoveringWhirlingShown);
            }
        }

        protected override bool IsRenderWeaponCameraKeyPressed()
        {
            return game.Input.IsKeyPressed(Keys.R);
        }

        protected override bool IsSummonWeaponKeyPressed()
        {
            return game.Input.IsKeyPressed(Keys.H);
        }

        public override void SummonWeapon()
        {
            Weapon.DetachRope();
            isHoldingWeaponRope = false;
            base.SummonWeapon();
        }


        protected void DropAndHoldWeaponRope()
        {
            if (!ThrowWeaponOut(false)) return;
            Weapon.AttachHammerRopeTo(attachedPed, WEAPON_HOLDING_HAND_ID);
            isHoldingWeaponRope = true;
        }
    }
}
