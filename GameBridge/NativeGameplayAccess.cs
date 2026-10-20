using GTA;
using GTA.Math;
using GTA.Native;
using GTA.UI;

namespace GTAVMods.Game
{
    public sealed class NativeGameplayAccess : IGameplayAccess
    {
        private readonly INativeInvoker native;
        public NativeGameplayAccess(INativeInvoker native) { this.native = native; }

        public bool IsPed(int entity) => native.Call<bool>(Hash.IS_ENTITY_A_PED, entity);
        public bool IsVehicle(int entity) => native.Call<bool>(Hash.IS_ENTITY_A_VEHICLE, entity);
        public Vector3 Position(int entity) => native.Call<Vector3>(Hash.GET_ENTITY_COORDS, entity, false);
        public float Speed(int entity) => native.Call<float>(Hash.GET_ENTITY_SPEED, entity);
        public void SetVelocity(int entity, Vector3 metresPerSecond)
            => native.Call(Hash.SET_ENTITY_VELOCITY, entity, metresPerSecond.X, metresPerSecond.Y, metresPerSecond.Z);
        public void ClearLastDamage(int entity) => native.Call(Hash.CLEAR_ENTITY_LAST_DAMAGE_ENTITY, entity);
        public void SetFreezeWhileWaitingForCollision(int entity, bool freeze)
            => native.Call(Hash.SET_ENTITY_SHOULD_FREEZE_WAITING_ON_COLLISION, entity, freeze);
        public void ActivatePhysics(int entity) => native.Call(Hash.ACTIVATE_PHYSICS, entity);
        public void SetObjectPhysics(int entity, float mass, float gravity, Vector3 dragCoefficients, Vector3 rotationDragCoefficients,
            float collisionMargin = 0f, float maximumAngularSpeed = 0f, float buoyancyFactor = 0f)
            => native.Call(Hash.SET_OBJECT_PHYSICS_PARAMS, entity, mass, gravity,
                dragCoefficients.X, dragCoefficients.Y, dragCoefficients.Z,
                rotationDragCoefficients.X, rotationDragCoefficients.Y, rotationDragCoefficients.Z,
                collisionMargin, maximumAngularSpeed, buoyancyFactor);
        public int CreateWeaponObject(WeaponHash weapon, int ammo, Vector3 position, bool showWorldModel, float scale)
            => native.Call<int>(Hash.CREATE_WEAPON_OBJECT, (int)weapon, ammo,
                position.X, position.Y, position.Z, showWorldModel, scale, 0, false, false);
        public void SetWeaponVisible(int ped, bool visible)
            => native.Call(Hash.SET_PED_CURRENT_WEAPON_VISIBLE, ped, visible, 0, 0, 0);
        public void GiveWeaponObjectToPed(int weaponObject, int ped)
            => native.Call(Hash.GIVE_WEAPON_OBJECT_TO_PED, weaponObject, ped);
        public int DropWeaponObjectFromPed(int ped) => native.Call<int>(Hash.GET_WEAPON_OBJECT_FROM_PED, ped, false);
        public void SetRagdoll(int ped, RagdollType type, int minimumMilliseconds, int maximumMilliseconds)
            => native.Call(Hash.SET_PED_TO_RAGDOLL, ped, minimumMilliseconds, maximumMilliseconds, (int)type, 0, 0, 0);
        public void SetLockOnRange(int player, float metres)
            => native.Call(Hash.SET_PLAYER_LOCKON_RANGE_OVERRIDE, player, metres);
        public void SetMoveRate(int ped, float multiplier) => native.Call(Hash.SET_PED_MOVE_RATE_OVERRIDE, ped, multiplier);
        public void SetSwimRate(int player, float multiplier) => native.Call(Hash.SET_SWIM_MULTIPLIER_FOR_PLAYER, player, multiplier);
        public void SuperJumpThisFrame(int player) => native.Call(Hash.SET_SUPER_JUMP_THIS_FRAME, player);
        public void SetCanBeDamaged(int entity, bool value) => native.Call(Hash.SET_ENTITY_CAN_BE_DAMAGED, entity, value);
        public void DisablePainAudio(int ped, bool disabled) => native.Call(Hash.DISABLE_PED_PAIN_AUDIO, ped, disabled);
        public void ShowHudComponentThisFrame(HudComponent component) => native.Call(Hash.SHOW_HUD_COMPONENT_THIS_FRAME, (int)component);
        public void LightningFlash() => native.Call(Hash.FORCE_LIGHTNING_FLASH);
    }
}
