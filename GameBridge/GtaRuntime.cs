using System;
using System.Drawing;
using System.Windows.Forms;
using GTA;
using GTA.Math;
using GTA.Native;

namespace GTAVMods.Game
{
    // These leaf bindings preserve SHVDNE semantics. They contain no mod policy.
    // The coverage boundary manifest approves each method individually.
    public static class GtaRuntime
    {
        public static GameServices Create(IGameHost host, IGameInput input, IEntityAccess entities,
            IPedAccess peds, IWeaponAccess weapons, IWorldAccess world, ICameraAccess cameras, IRopeAccess ropes,
            IRawNative native, IArgumentMarshaller marshaller, IRawNativeOutputs outputs)
            => new GameServices(host, input, entities, peds, weapons, world, cameras, ropes,
                new NativeInvoker(native, marshaller), new NativeOutputs(outputs));
    }

    public sealed class GtaHost : IGameHost
    {
        public int GameTime { [GamePrimitive] get => GTA.Game.GameTime; }
        public float LastFrameTime { [GamePrimitive] get => GTA.Game.LastFrameTime; }
        public bool IsPaused { [GamePrimitive] get => GTA.Game.IsPaused; }
        public int PlayerHandle { [GamePrimitive] get => GTA.Game.Player.Handle; }
        public int PlayerPed { [GamePrimitive] get => GameHandles.Of(GTA.Game.Player.Character); }
        public Version FileVersion { [GamePrimitive] get => GTA.Game.FileVersion; }
        public bool IsEnhanced { [GamePrimitive] get => GTA.Game.isEnhanced; }
        [GamePrimitive] public void Yield() => Script.Yield();
        [GamePrimitive] public void Wait(int milliseconds) => Script.Wait(milliseconds);
        [GamePrimitive] public void Notify(string message) => GTA.UI.Notification.PostTicker(message, false);
    }

    public sealed class GtaInput : IGameInput
    {
        [GamePrimitive] public bool IsKeyPressed(Keys key) => GTA.Game.IsKeyPressed(key);
        [GamePrimitive] public bool IsControlPressed(GTA.Control control) => GTA.Game.IsControlPressed(control);
        [GamePrimitive] public bool IsControlJustReleased(GTA.Control control) => GTA.Game.IsControlJustReleased(control);
    }

    public sealed class GtaEntities : IEntityAccess
    {
        private readonly HandleLifetime lifetime;
        public GtaEntities(IEntityLifetimeBindings bindings) { lifetime = new HandleLifetime(bindings, "Entity"); }
        public bool Exists(int entity) => lifetime.Exists(entity);
        [GamePrimitive] public bool IsDead(int entity) => Entity.FromHandle(entity).IsDead;
        [GamePrimitive] public bool IsInAir(int entity) => Entity.FromHandle(entity).IsInAir;
        [GamePrimitive] public Vector3 Position(int entity) => Entity.FromHandle(entity).Position;
        [GamePrimitive] public void SetPosition(int entity, Vector3 position) => Entity.FromHandle(entity).Position = position;
        [GamePrimitive] public Vector3 Velocity(int entity) => Entity.FromHandle(entity).Velocity;
        [GamePrimitive] public void SetVelocity(int entity, Vector3 velocity) => Entity.FromHandle(entity).Velocity = velocity;
        [GamePrimitive] public Vector3 Rotation(int entity) => Entity.FromHandle(entity).Rotation;
        [GamePrimitive] public void SetRotation(int entity, Vector3 rotation) => Entity.FromHandle(entity).Rotation = rotation;
        [GamePrimitive] public Quaternion Quaternion(int entity) => Entity.FromHandle(entity).Quaternion;
        [GamePrimitive] public void SetQuaternion(int entity, Quaternion quaternion) => Entity.FromHandle(entity).Quaternion = quaternion;
        [GamePrimitive] public Vector3 ForwardVector(int entity) => Entity.FromHandle(entity).ForwardVector;
        [GamePrimitive] public float HeightAboveGround(int entity) => Entity.FromHandle(entity).HeightAboveGround;
        [GamePrimitive] public int ModelHash(int entity) => Entity.FromHandle(entity).Model.Hash;
        [GamePrimitive] public int Health(int entity) => Entity.FromHandle(entity).Health;
        [GamePrimitive] public void SetHealth(int entity, int health) => Entity.FromHandle(entity).Health = health;
        [GamePrimitive] public void SetPersistent(int entity, bool persistent) => Entity.FromHandle(entity).IsPersistent = persistent;
        [GamePrimitive] public void SetCollisionEnabled(int entity, bool enabled) => Entity.FromHandle(entity).IsCollisionEnabled = enabled;
        [GamePrimitive] public void SetNoCollision(int entity, int other, bool toggle) => Entity.FromHandle(entity).SetNoCollision(Entity.FromHandle(other), toggle);
        [GamePrimitive] public void SetVisible(int entity, bool visible) => Entity.FromHandle(entity).IsVisible = visible;
        [GamePrimitive] public void SetInvincible(int entity, bool invincible) => Entity.FromHandle(entity).IsInvincible = invincible;
        [GamePrimitive] public void SetBulletProof(int entity, bool value) => Entity.FromHandle(entity).IsBulletProof = value;
        [GamePrimitive] public void SetCollisionProof(int entity, bool value) => Entity.FromHandle(entity).IsCollisionProof = value;
        [GamePrimitive] public void SetExplosionProof(int entity, bool value) => Entity.FromHandle(entity).IsExplosionProof = value;
        [GamePrimitive] public void SetFireProof(int entity, bool value) => Entity.FromHandle(entity).IsFireProof = value;
        [GamePrimitive] public void SetMeleeProof(int entity, bool value) => Entity.FromHandle(entity).IsMeleeProof = value;
        [GamePrimitive] public void SetOpacity(int entity, int opacity) => Entity.FromHandle(entity).Opacity = opacity;
        [GamePrimitive] public void ResetOpacity(int entity) => Entity.FromHandle(entity).ResetOpacity();
        [GamePrimitive] public bool HasBeenDamagedBy(int entity, int other) => Entity.FromHandle(entity).HasBeenDamagedBy(Entity.FromHandle(other));
        [GamePrimitive] public bool IsTouching(int entity, int other) => Entity.FromHandle(entity).IsTouching(Entity.FromHandle(other));
        [GamePrimitive] public void ApplyForce(int entity, Vector3 force) => Entity.FromHandle(entity).ApplyForce(force);
        [GamePrimitive] public void AttachToBone(int entity, int ped, Bone bone) => Entity.FromHandle(entity).AttachTo(((Ped)Entity.FromHandle(ped)).Bones[bone]);
        [GamePrimitive] public void Detach(int entity) => Entity.FromHandle(entity).Detach();
        public void Delete(int entity) => lifetime.Delete(entity);
        [GamePrimitive] public int AddBlip(int entity) => GameHandles.Of(Entity.FromHandle(entity).AddBlip());
        [GamePrimitive] public void SetBlipFriendly(int blip, bool friendly) => new Blip(blip).IsFriendly = friendly;
        [GamePrimitive] public void SetBlipName(int blip, string name) => new Blip(blip).Name = name;
    }

    public sealed class GtaPeds : IPedAccess
    {
        [GamePrimitive] public int MaxHealth(int ped) => ((Ped)Entity.FromHandle(ped)).MaxHealth;
        [GamePrimitive] public void SetMaxHealth(int ped, int health) => ((Ped)Entity.FromHandle(ped)).MaxHealth = health;
        [GamePrimitive] public void SetCanRagdoll(int ped, bool value) => ((Ped)Entity.FromHandle(ped)).CanRagdoll = value;
        [GamePrimitive] public void SetCanSufferCriticalHits(int ped, bool value) => ((Ped)Entity.FromHandle(ped)).CanSufferCriticalHits = value;
        [GamePrimitive] public void SetDiesOnLowHealth(int ped, bool value) => ((Ped)Entity.FromHandle(ped)).DiesOnLowHealth = value;
        [GamePrimitive] public bool IsInVehicle(int ped) => ((Ped)Entity.FromHandle(ped)).IsInVehicle();
        [GamePrimitive] public bool IsInMeleeCombat(int ped) => ((Ped)Entity.FromHandle(ped)).IsInMeleeCombat;
        [GamePrimitive] public bool IsInCombatAgainst(int ped, int other) => ((Ped)Entity.FromHandle(ped)).IsInCombatAgainst(((Ped)Entity.FromHandle(other)));
        [GamePrimitive] public bool IsWalking(int ped) => ((Ped)Entity.FromHandle(ped)).IsWalking;
        [GamePrimitive] public bool IsSprinting(int ped) => ((Ped)Entity.FromHandle(ped)).IsSprinting;
        [GamePrimitive] public bool IsRunning(int ped) => ((Ped)Entity.FromHandle(ped)).IsRunning;
        [GamePrimitive] public bool IsRagdoll(int ped) => ((Ped)Entity.FromHandle(ped)).IsRagdoll;
        [GamePrimitive] public Vector3 BonePosition(int ped, Bone bone) => ((Ped)Entity.FromHandle(ped)).Bones[bone].Position;
        [GamePrimitive] public void ApplyDamage(int ped, int damage) => ((Ped)Entity.FromHandle(ped)).ApplyDamage(damage);
        [GamePrimitive] public void SetIntoVehicle(int ped, int vehicle, VehicleSeat seat) => ((Ped)Entity.FromHandle(ped)).SetIntoVehicle(((Vehicle)Entity.FromHandle(vehicle)), seat);
        [GamePrimitive] public void ClearTasks(int ped) => ((Ped)Entity.FromHandle(ped)).Task.ClearAll();
        [GamePrimitive] public void StartHeliMissionToPosition(int ped, int vehicle, Vector3 position,
            VehicleMissionType mission, float speed, float radius, int heading,
            int maxHeight, int minHeight, float slowDistance, HeliMissionFlags flags)
            => ((Ped)Entity.FromHandle(ped)).Task.StartHeliMission(((Vehicle)Entity.FromHandle(vehicle)), position, mission,
                speed, radius, heading, maxHeight, minHeight, slowDistance, flags);
        [GamePrimitive] public void StartHeliMissionToPed(int ped, int vehicle, int target,
            VehicleMissionType mission, float speed, float radius, int heading,
            int maxHeight, int minHeight, float slowDistance, HeliMissionFlags flags)
            => ((Ped)Entity.FromHandle(ped)).Task.StartHeliMission(((Vehicle)Entity.FromHandle(vehicle)), ((Ped)Entity.FromHandle(target)), mission,
                speed, radius, heading, maxHeight, minHeight, slowDistance, flags);
    }

    public sealed class GtaWeapons : IWeaponAccess
    {
        [GamePrimitive] public bool HasWeapon(int ped, WeaponHash weapon) => ((Ped)Entity.FromHandle(ped)).Weapons.HasWeapon(weapon);
        [GamePrimitive] public WeaponHash Current(int ped) => ((Ped)Entity.FromHandle(ped)).Weapons.Current.Hash;
        [GamePrimitive] public int CurrentObject(int ped) => GameHandles.Of(((Ped)Entity.FromHandle(ped)).Weapons.CurrentWeaponObject);
        [GamePrimitive] public void Give(int ped, WeaponHash weapon, int ammo, bool equipNow, bool loaded) => ((Ped)Entity.FromHandle(ped)).Weapons.Give(weapon, ammo, equipNow, loaded);
        [GamePrimitive] public void Remove(int ped, WeaponHash weapon) => ((Ped)Entity.FromHandle(ped)).Weapons.Remove(weapon);
        [GamePrimitive] public bool Select(int ped, WeaponHash weapon, bool equipNow) => ((Ped)Entity.FromHandle(ped)).Weapons.Select(weapon, equipNow);
        [GamePrimitive] public bool RequestAsset(WeaponHash weapon, int timeoutMs) => new WeaponAsset(weapon).Request(timeoutMs);
    }

    public sealed class GtaWorld : IWorldAccess
    {
        private readonly IWorldQueryBindings queries;
        public GtaWorld(IWorldQueryBindings queries) { this.queries = queries; }
        [GamePrimitive] public int[] NearbyEntities(Vector3 position, float radius) => GameHandles.OfMany(World.GetNearbyEntities(position, radius));
        public RayHit Raycast(Vector3 from, Vector3 to, IntersectFlags flags, int ignoreEntity = 0,
            ShapeTestOptions options = ShapeTestOptions.Default)
        {
            queries.Raycast(from, to, flags, ignoreEntity, options, out bool hit, out int entity, out Vector3 position);
            return new RayHit(hit, entity, position);
        }
        public RayHit Capsule(Vector3 from, Vector3 to, float radius, IntersectFlags flags)
        {
            queries.Capsule(from, to, radius, flags, out bool hit, out int entity, out Vector3 position);
            return new RayHit(hit, entity, position);
        }
        [GamePrimitive] public void DrawMarker(MarkerType type, Vector3 position, Vector3 direction, Vector3 rotation, Vector3 scale, Color color)
            => World.DrawMarker(type, position, direction, rotation, scale, color);
        [GamePrimitive] public bool GetGroundHeight(Vector3 position, out float height) => World.GetGroundHeight(position, out height);
        [GamePrimitive] public bool RequestModel(int modelHash, int timeoutMs) => new Model(modelHash).Request(timeoutMs);
        [GamePrimitive] public void ReleaseModel(int modelHash) => new Model(modelHash).MarkAsNoLongerNeeded();
        [GamePrimitive] public int CreateVehicle(int modelHash, Vector3 position) => GameHandles.Of(World.CreateVehicle(new Model(modelHash), position));
        [GamePrimitive] public int CreateDriver(int vehicle) => GameHandles.Of(((Vehicle)Entity.FromHandle(vehicle)).CreateRandomPedOnSeat(VehicleSeat.Driver));
        [GamePrimitive] public int VehicleDriver(int vehicle) => GameHandles.Of(((Vehicle)Entity.FromHandle(vehicle)).Driver);
    }

    public sealed class GtaCameras : ICameraAccess
    {
        private readonly HandleLifetime lifetime;
        public GtaCameras(ICameraLifetimeBindings bindings) { lifetime = new HandleLifetime(bindings, "Camera"); }
        public Vector3 GameplayPosition { [GamePrimitive] get => GameplayCamera.Position; }
        public Vector3 GameplayDirection { [GamePrimitive] get => GameplayCamera.Direction; }
        public Vector3 GameplayRotation { [GamePrimitive] get => GameplayCamera.Rotation; }
        public int RenderingCamera { [GamePrimitive] get => GameHandles.Of(ScriptCameraDirector.RenderingCam); }
        [GamePrimitive] public int Create(Vector3 position, Vector3 rotation, float fieldOfView)
            => GameHandles.Of(Camera.Create(ScriptedCameraNameHash.DefaultScriptedCamera, position, rotation, fieldOfView));
        [GamePrimitive] public void SetPosition(int camera, Vector3 position) => new Camera(camera).Position = position;
        [GamePrimitive] public void PointAt(int camera, int entity) => new Camera(camera).PointAt(Entity.FromHandle(entity));
        [GamePrimitive] public void SetActive(int camera, bool active) => new Camera(camera).IsActive = active;
        [GamePrimitive] public void StartRendering() => ScriptCameraDirector.StartRendering();
        [GamePrimitive] public void StopRendering() => ScriptCameraDirector.StopRendering();
        public void Delete(int camera) => lifetime.Delete(camera);
        [GamePrimitive] public void Shake(CameraShake shake, float amplitude) => GameplayCamera.Shake(shake, amplitude);
        [GamePrimitive] public void SetHeadingLimits(float minimum, float maximum) => GameplayCamera.SetThirdPersonCameraRelativeHeadingLimitsThisUpdate(minimum, maximum);
        [GamePrimitive] public void SetPitchLimits(float minimum, float maximum) => GameplayCamera.SetThirdPersonCameraRelativePitchLimitsThisUpdate(minimum, maximum);
    }

    public sealed class GtaRopes : IRopeAccess
    {
        private readonly HandleLifetime lifetime;
        public GtaRopes(IRopeLifetimeBindings bindings) { lifetime = new HandleLifetime(bindings, "Rope"); }
        [GamePrimitive] public int Create(RopeType type, Vector3 position, Vector3 rotation, float length, float minLength, bool breakable)
            => GameHandles.Of(World.AddRope(type, position, rotation, length, minLength, breakable));
        [GamePrimitive] public void Connect(int rope, int firstEntity, Vector3 firstPosition, int secondEntity, Vector3 secondPosition, float length)
            => new Rope(rope).Connect(Entity.FromHandle(firstEntity), firstPosition, Entity.FromHandle(secondEntity), secondPosition, length);
        [GamePrimitive] public void ActivatePhysics(int rope) => new Rope(rope).ActivatePhysics();
        public void Delete(int rope) => lifetime.Delete(rope);
    }
}
