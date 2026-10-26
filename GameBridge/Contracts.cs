using System;
using System.Windows.Forms;
using GTA;
using GTA.Math;
using GTA.Native;

namespace GTAVMods.Game
{
    // Handles are engine identities, not live GTA objects. Zero denotes no object.
    // Each operation below is an engine primitive. Mod policy belongs to callers.
    /// <summary>Raw transport for boundary adapters and their contract tests.</summary>
    /// <remarks>Production behavior consumes typed capabilities, not this transport.</remarks>
    public interface INativeInvoker
    {
        void Call(Hash hash, params object[] arguments);
        T Call<T>(Hash hash, params object[] arguments);
    }

    /// <summary>Current script-host observations and scheduling commands.</summary>
    /// <remarks>
    /// GameTime is the signed 32-bit game clock in milliseconds and can wrap.
    /// LastFrameTime is elapsed seconds for the previous frame. Yield and Wait may resume after
    /// entities, input, player identity or camera state have changed; callers must re-observe them.
    /// Wait takes milliseconds and is not a guarantee of exact elapsed time.
    /// </remarks>
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

    /// <summary>Current key/control observations for the local game.</summary>
    /// <remarks>These queries do not queue, consume or synthesize future input events.</remarks>
    public interface IGameInput
    {
        bool IsKeyPressed(Keys key);
        bool IsControlPressed(GTA.Control control);
        bool IsControlJustReleased(GTA.Control control);
    }

    /// <summary>Entity observations and immediate engine property commands.</summary>
    /// <remarks>
    /// Handles are borrowed identities, not lifetime leases; validate Exists after a script yield.
    /// Position uses world metres, Velocity uses metres per second and Euler Rotation uses degrees.
    /// Quaternions are the engine's XYZW components, without added normalization.
    /// Force, attach and collision commands do not simulate their physical consequences.
    /// Delete returns only after observing absence. Already absent entities are accepted; refusal or
    /// host failure throws so the caller can retain ownership and retry.
    /// Host exceptions propagate. Invalid handles are not silently replaced with defaults by this boundary.
    /// </remarks>
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
        /// <summary>Deletes if present and confirms absence, without yielding.</summary>
        /// <exception cref="InvalidOperationException">The entity still exists after the deletion request.</exception>
        void Delete(int entity);
        /// <summary>Requests an optional HUD marker; zero means unavailable. Do not configure handle zero.</summary>
        int AddBlip(int entity);
        void SetBlipFriendly(int blip, bool friendly);
        void SetBlipName(int blip, string name);
    }

    /// <summary>Ped property observations, task requests and health commands.</summary>
    /// <remarks>
    /// A task request does not confirm that the ped began or completed it. BonePosition is in world metres.
    /// Heli mission speeds use metres per second, distances/heights use metres and heading uses degrees;
    /// mission flags and other scalar values preserve the pinned SHVDNE API semantics.
    /// </remarks>
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

    /// <summary>Weapon inventory observations, equip requests and asset loading.</summary>
    /// <remarks>
    /// Give adds inventory but equipNow schedules equipping as soon as possible; it need not be immediate.
    /// Select returns whether the ped has the weapon and only equips immediately when equipNow is true.
    /// CurrentObject returns zero when unarmed; a nonzero returned handle still requires Exists checking.
    /// RequestAsset waits for at most the API's requested millisecond timeout and returns its load result.
    /// Neither a successful load nor an equip request guarantees later weapon object creation.
    /// </remarks>
    public interface IWeaponAccess
    {
        bool HasWeapon(int ped, WeaponHash weapon);
        WeaponHash Current(int ped);
        int CurrentObject(int ped);
        void Give(int ped, WeaponHash weapon, int ammo, bool equipNow, bool loaded);
        void Remove(int ped, WeaponHash weapon);
        // Returns whether the ped has the weapon; selection does not grant it.
        bool Select(int ped, WeaponHash weapon, bool equipNow);
        bool RequestAsset(WeaponHash weapon, int timeoutMs);
    }

    /// <summary>A shape-test observation, with world position in metres.</summary>
    /// <remarks>
    /// DidHit can be true with Entity zero when world geometry was hit.
    /// Entity and Position must not be interpreted as a hit when DidHit is false.
    /// A nonzero entity handle is borrowed and may become invalid after the query.
    /// </remarks>
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

    /// <summary>World observations and model/entity creation commands.</summary>
    /// <remarks>
    /// Positions, distances and radii use metres; marker rotations use degrees.
    /// Ground height is meaningful only when GetGroundHeight returns true.
    /// RequestModel takes milliseconds and returns the API's loaded result; ReleaseModel relinquishes
    /// the script's streaming request, not an existing entity. Creation returns zero on failure.
    /// Successful vehicle/driver creations are owned by the caller and must eventually be deleted.
    /// Ray and capsule queries observe the engine; this interface does not predict later collision.
    /// </remarks>
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

    /// <summary>Camera observations, rendering requests and owned camera creation.</summary>
    /// <remarks>
    /// Positions use metres; Euler rotations, heading/pitch bounds and field of view use degrees.
    /// Create returns zero on failure; callers own nonzero created cameras and must delete them.
    /// RenderingCamera is an observation. Start/stop rendering and PointAt issue commands, not rendering
    /// completion promises. GameplayDirection is the host's direction vector, not an Euler angle.
    /// </remarks>
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
        /// <summary>Deletes if present and confirms absence; refusal or host failure throws.</summary>
        /// <remarks>Already absent is success. Does not yield, retry, or stop rendering.</remarks>
        void Delete(int camera);
        void Shake(CameraShake shake, float amplitude);
        void SetHeadingLimits(float minimum, float maximum);
        void SetPitchLimits(float minimum, float maximum);
    }

    /// <summary>Owned rope creation and engine attachment/physics commands.</summary>
    /// <remarks>
    /// Positions and lengths use metres; Euler rotation uses degrees. Create returns zero on failure.
    /// The caller owns each nonzero rope and must delete it. Connect and ActivatePhysics issue requests;
    /// they do not calculate or promise entity movement.
    /// </remarks>
    public interface IRopeAccess
    {
        int Create(RopeType type, Vector3 position, Vector3 rotation,
            float length, float minLength, bool breakable);
        void Connect(int rope, int firstEntity, Vector3 firstPosition,
            int secondEntity, Vector3 secondPosition, float length);
        void ActivatePhysics(int rope);
        /// <summary>Deletes if present and confirms absence; refusal or host failure throws.</summary>
        /// <remarks>Already absent is success. Does not yield or retry.</remarks>
        void Delete(int rope);
    }

    /// <summary>Typed observations from natives that return values through output parameters.</summary>
    /// <remarks>
    /// WorldToScreen takes world metres and returns
    /// normalized screen coordinates; coordinates are meaningful only when true.
    /// EntityQuaternion returns raw XYZW components without normalization.
    /// </remarks>
    public interface INativeOutputs
    {
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
            Gameplay = new NativeGameplayAccess(native);
            Animations = new NativeAnimationAccess(native);
            Particles = new NativeParticleAccess(native);
            Drawing = new NativeDrawingAccess(native);
            ConsoleInput = new NativeConsoleInput(native);
            ConsoleDrawing = new NativeConsoleDrawing(native);
            ConsoleEntities = new NativeConsoleEntities(native);
            ConsoleWorld = new NativeConsoleWorld(native);
            ConsoleNetwork = new NativeConsoleNetwork(native);
            ConsolePlayers = new NativeConsolePlayers(native);
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
        public IGameplayAccess Gameplay { get; }
        public IAnimationAccess Animations { get; }
        public IParticleAccess Particles { get; }
        public IDrawingAccess Drawing { get; }
        public IConsoleInput ConsoleInput { get; }
        public IConsoleDrawing ConsoleDrawing { get; }
        public IConsoleEntities ConsoleEntities { get; }
        public IConsoleWorld ConsoleWorld { get; }
        public IConsoleNetwork ConsoleNetwork { get; }
        public IConsolePlayers ConsolePlayers { get; }
        public INativeOutputs Outputs { get; }
    }
}
