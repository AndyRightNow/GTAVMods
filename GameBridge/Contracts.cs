using System;
using System.Windows.Forms;
using GTA;
using GTA.Math;
using GTA.Native;

namespace GTAVMods.Game
{
    // Handles are engine identities, not live GTA objects. Zero denotes no object.
    // Each operation below is an engine primitive. Mod policy belongs to callers.
    public interface INativeInvoker
    {
        void Call(Hash hash, params object[] arguments);
        T Call<T>(Hash hash, params object[] arguments);
    }

    public interface IGameHost
    {
        int GameTime { get; }
        float LastFrameTime { get; }
        bool IsPaused { get; }
        int PlayerHandle { get; }
        int PlayerPed { get; }
        Version FileVersion { get; }
        bool IsEnhanced { get; }
        void Yield();
        void Wait(int milliseconds);
        void Notify(string message);
    }

    public interface IGameInput
    {
        bool IsKeyPressed(Keys key);
        bool IsControlPressed(GTA.Control control);
        bool IsControlJustReleased(GTA.Control control);
    }

    public interface IEntityAccess
    {
        bool Exists(int entity);
        bool IsDead(int entity);
        bool IsInAir(int entity);
        Vector3 Position(int entity);
        void SetPosition(int entity, Vector3 position);
        Vector3 Velocity(int entity);
        void SetVelocity(int entity, Vector3 velocity);
        Vector3 Rotation(int entity);
        void SetRotation(int entity, Vector3 rotation);
        Quaternion Quaternion(int entity);
        void SetQuaternion(int entity, Quaternion quaternion);
        Vector3 ForwardVector(int entity);
        float HeightAboveGround(int entity);
        int ModelHash(int entity);
        int Health(int entity);
        void SetHealth(int entity, int health);
        void SetPersistent(int entity, bool persistent);
        void SetCollisionEnabled(int entity, bool enabled);
        void SetNoCollision(int entity, int other, bool toggle);
        void SetVisible(int entity, bool visible);
        void SetInvincible(int entity, bool invincible);
        void SetBulletProof(int entity, bool value);
        void SetCollisionProof(int entity, bool value);
        void SetExplosionProof(int entity, bool value);
        void SetFireProof(int entity, bool value);
        void SetMeleeProof(int entity, bool value);
        void SetOpacity(int entity, int opacity);
        void ResetOpacity(int entity);
        bool HasBeenDamagedBy(int entity, int other);
        bool IsTouching(int entity, int other);
        void ApplyForce(int entity, Vector3 force);
        void AttachToBone(int entity, int ped, Bone bone);
        void Detach(int entity);
        void Delete(int entity);
        int AddBlip(int entity);
        void SetBlipFriendly(int blip, bool friendly);
        void SetBlipName(int blip, string name);
    }

    public interface IPedAccess
    {
        int MaxHealth(int ped);
        void SetMaxHealth(int ped, int health);
        void SetCanRagdoll(int ped, bool value);
        void SetCanSufferCriticalHits(int ped, bool value);
        void SetDiesOnLowHealth(int ped, bool value);
        bool IsInVehicle(int ped);
        bool IsInMeleeCombat(int ped);
        bool IsInCombatAgainst(int ped, int other);
        bool IsWalking(int ped);
        bool IsSprinting(int ped);
        bool IsRunning(int ped);
        bool IsRagdoll(int ped);
        Vector3 BonePosition(int ped, Bone bone);
        void ApplyDamage(int ped, int damage);
        void SetIntoVehicle(int ped, int vehicle, VehicleSeat seat);
        void ClearTasks(int ped);
        void StartHeliMissionToPosition(int ped, int vehicle, Vector3 position,
            VehicleMissionType mission, float speed, float radius, int heading,
            int maxHeight, int minHeight, float slowDistance, HeliMissionFlags flags);
        void StartHeliMissionToPed(int ped, int vehicle, int target,
            VehicleMissionType mission, float speed, float radius, int heading,
            int maxHeight, int minHeight, float slowDistance, HeliMissionFlags flags);
    }

    public interface IWeaponAccess
    {
        bool HasWeapon(int ped, WeaponHash weapon);
        WeaponHash Current(int ped);
        int CurrentObject(int ped);
        void Give(int ped, WeaponHash weapon, int ammo, bool equipNow, bool loaded);
        void Remove(int ped, WeaponHash weapon);
        void Select(int ped, WeaponHash weapon, bool equipNow);
        bool RequestAsset(WeaponHash weapon, int timeoutMs);
    }

    public readonly struct RayHit
    {
        public RayHit(bool didHit, int entity, Vector3 position)
        {
            DidHit = didHit;
            Entity = entity;
            Position = position;
        }
        public bool DidHit { get; }
        public int Entity { get; }
        public Vector3 Position { get; }
    }

    public interface IWorldAccess
    {
        int[] NearbyEntities(Vector3 position, float radius);
        RayHit Raycast(Vector3 from, Vector3 to, IntersectFlags flags, int ignoreEntity = 0);
        RayHit Capsule(Vector3 from, Vector3 to, float radius, IntersectFlags flags);
        void DrawMarker(MarkerType type, Vector3 position, Vector3 direction,
            Vector3 rotation, Vector3 scale, System.Drawing.Color color);
        bool GetGroundHeight(Vector3 position, out float height);
        bool RequestModel(int modelHash, int timeoutMs);
        void ReleaseModel(int modelHash);
        int CreateVehicle(int modelHash, Vector3 position);
        int CreateDriver(int vehicle);
        int VehicleDriver(int vehicle);
    }

    public interface ICameraAccess
    {
        Vector3 GameplayPosition { get; }
        Vector3 GameplayDirection { get; }
        Vector3 GameplayRotation { get; }
        int Create(Vector3 position, Vector3 rotation, float fieldOfView);
        void SetPosition(int camera, Vector3 position);
        void PointAt(int camera, int entity);
        void SetActive(int camera, bool active);
        int RenderingCamera { get; }
        void StartRendering();
        void StopRendering();
        void Delete(int camera);
        void Shake(CameraShake shake, float amplitude);
        void SetHeadingLimits(float minimum, float maximum);
        void SetPitchLimits(float minimum, float maximum);
    }

    public interface IRopeAccess
    {
        int Create(RopeType type, Vector3 position, Vector3 rotation,
            float length, float minLength, bool breakable);
        void Connect(int rope, int firstEntity, Vector3 firstPosition,
            int secondEntity, Vector3 secondPosition, float length);
        void ActivatePhysics(int rope);
        void Delete(int rope);
    }

    public interface INativeOutputs
    {
        bool LastDamageBone(int ped, out int bone);
        Quaternion EntityQuaternion(int entity);
        bool WorldToScreen(Vector3 position, out float x, out float y);
    }

    public sealed class GameServices
    {
        public GameServices(IGameHost host, IGameInput input, IEntityAccess entities,
            IPedAccess peds, IWeaponAccess weapons, IWorldAccess world,
            ICameraAccess cameras, IRopeAccess ropes, INativeInvoker native,
            INativeOutputs outputs)
        {
            Host = host;
            Input = input;
            Entities = entities;
            Peds = peds;
            Weapons = weapons;
            World = world;
            Cameras = cameras;
            Ropes = ropes;
            Native = native;
            Outputs = outputs;
        }
        public IGameHost Host { get; }
        public IGameInput Input { get; }
        public IEntityAccess Entities { get; }
        public IPedAccess Peds { get; }
        public IWeaponAccess Weapons { get; }
        public IWorldAccess World { get; }
        public ICameraAccess Cameras { get; }
        public IRopeAccess Ropes { get; }
        public INativeInvoker Native { get; }
        public INativeOutputs Outputs { get; }
    }
}
