using GTA;
using GTA.Math;
using GTA.Native;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace CaptainAmerica
{
    class Ability
    {
        private Ped poweredPed;
        private static Ability instance;
        private float PLAYER_MOVEMENT_MULTIPLIER = 1.5f;
        private float THROWN_SHIELD_MASS = 400.0f;
        private WeaponHash shieldHash;
        private Entity thrownShieldObject;
        private Entity carriedShieldOnTheBack;

        private Ability()
        {
            poweredPed = null;
            instance = null;
            shieldHash = (WeaponHash) Function.Call<uint>(Hash.GET_HASH_KEY, "WEAPON_CAPSHIELD");
            thrownShieldObject = null;
            carriedShieldOnTheBack = null;
        }

        public bool IsAttachedToPed
        {
            get
            {
                return poweredPed != null;
            }
        }

        public static Ability Instance
        {
            get
            {
                if (instance == null)
                {
                    instance = new Ability();
                }

                return instance;
            }
        }

        public void ApplyOn(Ped ped)
        {
            if (poweredPed != null)
            {
                return;
            }

            poweredPed = ped;
            thrownShieldObject = ADModUtils.NativeHelper.CreateWeaponObject(shieldHash, 1, Vector3.Zero);
            carriedShieldOnTheBack = ADModUtils.NativeHelper.CreateWeaponObject(shieldHash, 1, Vector3.Zero);
            ActivateShieldPhysics(thrownShieldObject);
        }

        public void RemoveAbility()
        {
            poweredPed = null;
            thrownShieldObject.Delete();
            carriedShieldOnTheBack.Detach();
            carriedShieldOnTheBack.Delete();
            carriedShieldOnTheBack = null;
            thrownShieldObject = null;
        }

        public void OnTick()
        {
            if (poweredPed == null)
            {
                return;
            }

            InitializeShield();
            HandleBasicPhysicalAbility();
            HandleCombatAbility();
            HandleShieldBlock();
            HandleThrowShield();
            HandleCarryShieldOnTheBack();
        }

        void InitializeShield()
        {
            if (!poweredPed.Weapons.HasWeapon(shieldHash))
            {
                poweredPed.Weapons.Give(shieldHash, 1, true, true);
            }

            if (poweredPed.Weapons.Current != null &&
                poweredPed.Weapons.Current.Hash == shieldHash)
            {
                poweredPed.Weapons.Select(shieldHash, true);
            }
        }

        void HandleShieldBlock()
        {
            var action = AnimationActions.ShieldBlockFront;

            string dictName = NativeHelper.Instance.GetAnimationDictNameByAction((uint)action);
            string animName = NativeHelper.Instance.GetAnimationNameByAction((uint)action);

            poweredPed.IsBulletProof = false;
            if (IsHoldingShield() &&
                Game.IsKeyPressed(Keys.T) &&
                !poweredPed.IsSwimming &&
                !poweredPed.IsGettingIntoAVehicle &&
                !poweredPed.IsInVehicle() &&
                !poweredPed.IsRagdoll)
            {
                poweredPed.IsBulletProof = true;
                ADModUtils.NativeHelper.PlayPlayerAnimation(
                    poweredPed,
                    dictName,
                    animName,
                    AnimationFlags.UpperBodyOnly | AnimationFlags.AllowRotation,
                    -1
                );
            }
        }

        void ActivateShieldPhysics(Entity shield)
        {
            Function.Call(Hash.ACTIVATE_PHYSICS, shield);
            shield.HasCollision = true;
            ADModUtils.NativeHelper.SetObjectPhysicsParams(shield, THROWN_SHIELD_MASS);
        }

        void HandleCarryShieldOnTheBack()
        {
            if (!IsHoldingShield())
            {
                if (!carriedShieldOnTheBack.IsAttachedTo(poweredPed))
                {
                    var pedUp = poweredPed.UpVector.Normalized;

                    carriedShieldOnTheBack.AttachTo(
                        poweredPed,
                        poweredPed.GetBoneIndex(Bone.IK_Root),
                        Vector3.Zero + pedUp * 0.29f - poweredPed.RightVector.Normalized * 0.07f - poweredPed.ForwardVector.Normalized * 0.05f,
                        ADModUtils.Utilities.Math.DirectionToRotation(poweredPed.UpVector.Normalized)
                    );
                }

                carriedShieldOnTheBack.IsVisible = true;
            }
            else
            {
                carriedShieldOnTheBack.IsVisible = false;
            }
        }

        bool IsHoldingShield()
        {
            return poweredPed.Weapons.Current != null &&
                poweredPed.Weapons.Current.Hash == shieldHash;
        }

        void HandleThrowShield()
        {
            if (IsHoldingShield() &&
                Game.IsKeyPressed(Keys.H) &&
                !poweredPed.IsSwimming &&
                !poweredPed.IsGettingIntoAVehicle &&
                !poweredPed.IsInVehicle() &&
                !poweredPed.IsRagdoll)
            {

                thrownShieldObject.Position = poweredPed.Position;
                thrownShieldObject.Velocity = poweredPed.ForwardVector.Normalized * 100.0f;
                Script.Wait(300);
            }
        }

        void HandleBasicPhysicalAbility()
        {
            Function.Call(Hash.SET_PED_MOVE_RATE_OVERRIDE, poweredPed, PLAYER_MOVEMENT_MULTIPLIER);
            Function.Call(Hash.SET_SWIM_MULTIPLIER_FOR_PLAYER, Game.Player, PLAYER_MOVEMENT_MULTIPLIER);
            Function.Call(Hash.SET_SUPER_JUMP_THIS_FRAME, Game.Player);
        }

        void HandleCombatAbility()
        {
            if (poweredPed.IsInVehicle() ||
                !poweredPed.IsInMeleeCombat)
            {
                return;
            }
            
            ApplyForcesAndDamagesOnNearbyEntities();
        }

        void ApplyForcesAndDamagesOnNearbyEntities(bool checkDamaged = true, bool checkTouching = false)
        {
            Entity[] closestEntities = World.GetNearbyEntities(poweredPed.Position, 5.0f);
            
            foreach (var ent in closestEntities)
            {
                if (ent == poweredPed ||
                    ent == poweredPed.Weapons.CurrentWeaponObject)
                {
                    continue;
                }

                var forceDirection = (ent.Position - poweredPed.Position).Normalized;

                if (checkDamaged && ent.HasBeenDamagedBy(poweredPed))
                {
                    var isPed = ADModUtils.NativeHelper.IsPed(ent);

                    if (isPed)
                    {
                        var target = (Ped)ent;
                        var lastDamagedBone = ADModUtils.NativeHelper.GetLastDamagedBone(target);
                        forceDirection = (target.GetBoneCoord(lastDamagedBone) - poweredPed.Position).Normalized;
                    }

                    NativeHelper.Instance.ApplyForcesAndDamages(ent, forceDirection);
                }
                else if (checkTouching &&
                    (poweredPed.IsTouching(ent) || poweredPed.Weapons.CurrentWeaponObject.IsTouching(ent)))
                {
                    NativeHelper.Instance.ApplyForcesAndDamages(ent, forceDirection);
                }
            }
        }
    }
}
