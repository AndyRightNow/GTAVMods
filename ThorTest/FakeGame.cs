using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using GTA;
using GTA.Math;
using GTA.Native;
using GTAVMods.Game;

namespace ModUtilsTest
{
    // Engine observations and immediate primitive assignments only. No motion, AI or mod transitions.
    internal sealed class GameCall
    {
        public GameCall(string name, params object[] arguments) { Name = name; Arguments = arguments; }
        public string Name { get; }
        public object[] Arguments { get; }
    }

    internal sealed class EntityObservation
    {
        public bool Exists { get; set; } = true;
        public bool Dead { get; set; }
        public bool InAir { get; set; }
        public bool Persistent { get; set; }
        public bool CollisionEnabled { get; set; } = true;
        public bool Visible { get; set; } = true;
        public bool Invincible { get; set; }
        public bool BulletProof { get; set; }
        public bool CollisionProof { get; set; }
        public bool ExplosionProof { get; set; }
        public bool FireProof { get; set; }
        public bool MeleeProof { get; set; }
        public Vector3 Position { get; set; }
        public Vector3 Velocity { get; set; }
        public Vector3 Rotation { get; set; }
        public Vector3 Forward { get; set; } = Vector3.UnitY;
        public Quaternion Quaternion { get; set; } = Quaternion.Identity;
        public float HeightAboveGround { get; set; }
        public int ModelHash { get; set; }
        public int Health { get; set; } = 200;
        public int MaxHealth { get; set; } = 200;
        public int Opacity { get; set; } = 255;
        public bool CanRagdoll { get; set; } = true;
        public bool CanSufferCriticalHits { get; set; } = true;
        public bool DiesOnLowHealth { get; set; } = true;
        public bool InVehicle { get; set; }
        public bool InMeleeCombat { get; set; }
        public bool Walking { get; set; }
        public bool Sprinting { get; set; }
        public bool Running { get; set; }
        public bool Ragdoll { get; set; }
        public int Vehicle { get; set; }
        public int Driver { get; set; }
        public int AttachedTo { get; set; }
        public int CurrentWeaponObject { get; set; }
        public Bone AttachedBone { get; set; }
        public WeaponHash CurrentWeapon { get; set; } = WeaponHash.Unarmed;
        public readonly HashSet<WeaponHash> Weapons = new HashSet<WeaponHash>();
        public readonly Dictionary<Bone, Vector3> Bones = new Dictionary<Bone, Vector3>();
        public readonly HashSet<int> DamagedBy = new HashSet<int>(), Touching = new HashSet<int>(), CombatAgainst = new HashSet<int>();
    }

    internal sealed class FakeGame
    {
        public readonly List<GameCall> Calls = new List<GameCall>();
        public readonly Dictionary<int, EntityObservation> Observations = new Dictionary<int, EntityObservation>();
        public FakeHost Host { get; }
        public FakeInput Input { get; } = new FakeInput();
        public FakeEntities Entities { get; }
        public FakePeds Peds { get; }
        public FakeWeapons Weapons { get; }
        public FakeWorld World { get; }
        public FakeCameras Cameras { get; }
        public FakeRopes Ropes { get; }
        public FakeNative Native { get; }
        public FakeOutputs Outputs { get; }
        public GameServices Services { get; }
        private readonly Queue<Action<FakeGame>> nextFrameObservations = new Queue<Action<FakeGame>>();
        // A failed primitive is still recorded, but its immediate assignment has not happened.
        public Action<GameCall> BeforeCall { get; set; }
        public Action<string, int> BeforeObservation { get; set; }
        public FakeGame()
        {
            Host = new FakeHost(this); Entities = new FakeEntities(this); Peds = new FakePeds(this);
            Weapons = new FakeWeapons(this); World = new FakeWorld(this); Cameras = new FakeCameras(this);
            Ropes = new FakeRopes(this); Native = new FakeNative(this); Outputs = new FakeOutputs(this);
            Services = GtaRuntime.Create(Host, Input, Entities, Peds, Weapons, World, Cameras, Ropes,
                Native, Native, Outputs);
        }
        public EntityObservation AddEntity(int handle)
        {
            if (handle == 0) throw new ArgumentException("Zero is not an entity handle.", nameof(handle));
            var entity = new EntityObservation(); Observations.Add(handle, entity); return entity;
        }
        public EntityObservation Entity(int handle)
        {
            if (!Observations.TryGetValue(handle, out var entity)) throw new InvalidOperationException("Unregistered entity: " + handle);
            if (!entity.Exists) throw new InvalidOperationException("Entity was deleted: " + handle);
            return entity;
        }
        public void Record(string name, params object[] arguments)
        {
            var call = new GameCall(name, arguments);
            Calls.Add(call);
            BeforeCall?.Invoke(call);
        }

        // Observation playback is independent of submitted commands. A velocity, force,
        // animation or inventory request does not predict motion or successful completion.
        public void ObserveNextFrame(Action<FakeGame> observation)
            => nextFrameObservations.Enqueue(observation ?? throw new ArgumentNullException(nameof(observation)));

        public void AdvanceFrame(int elapsedMilliseconds)
        {
            if (elapsedMilliseconds < 0) throw new ArgumentOutOfRangeException(nameof(elapsedMilliseconds));
            Host.GameTime = unchecked(Host.GameTime + elapsedMilliseconds);
            Host.LastFrameTime = elapsedMilliseconds / 1000f;
            Input.Released.Clear();
            int count = nextFrameObservations.Count;
            for (int index = 0; index < count; index++) nextFrameObservations.Dequeue()(this);
        }

        public void ApplyWeaponObservation(int ped, WeaponHash weapon, int objectHandle)
        {
            var player = Entity(ped);
            if (weapon == WeaponHash.Unarmed && objectHandle != 0)
                throw new ArgumentException("An unarmed observation cannot have a current weapon object.", nameof(objectHandle));
            if (objectHandle != 0) Entity(objectHandle);
            player.CurrentWeapon = weapon;
            player.CurrentWeaponObject = objectHandle;
            if (weapon != WeaponHash.Unarmed) player.Weapons.Add(weapon);
        }
    }

    internal sealed class FakeHost : IGameHost
    {
        private readonly FakeGame game;
        public FakeHost(FakeGame game) { this.game = game; }
        public int GameTime { get; set; }
        public float LastFrameTime { get; set; } = 0.016f;
        public bool IsPaused { get; set; }
        public int PlayerHandle { get; set; } = 1;
        public int PlayerPed { get; set; } = 10;
        public Version FileVersion { get; set; } = new Version(1, 0, 1158, 16);
        public bool IsEnhanced { get; set; } = true;
        public Action OnYield { get; set; }
        public Action<int> OnWait { get; set; }
        public void Yield() { game.Record("host.yield"); if (OnYield == null) throw new InvalidOperationException("Unscripted yield."); OnYield();  }
        public void Wait(int milliseconds) { game.Record("host.wait", milliseconds); if (OnWait == null) throw new InvalidOperationException("Unscripted wait."); OnWait(milliseconds);  }
        public void Notify(string message) => game.Record("host.notify", message);
    }

    internal sealed class FakeInput : IGameInput
    {
        public readonly HashSet<Keys> Keys = new HashSet<Keys>();
        public readonly HashSet<GTA.Control> Pressed = new HashSet<GTA.Control>(), Released = new HashSet<GTA.Control>();
        public bool IsKeyPressed(Keys key) => Keys.Contains(key);
        public bool IsControlPressed(GTA.Control control) => Pressed.Contains(control);
        public bool IsControlJustReleased(GTA.Control control) => Released.Contains(control);
    }

    internal sealed class FakeEntities : IEntityAccess, IEntityLifetimeBindings
    {
        private readonly FakeGame game;
        private readonly GtaEntities lifetime;
        public readonly Queue<int> CreatedBlips = new Queue<int>();
        public readonly HashSet<int> SilentDeletionRefusals = new HashSet<int>();
        public FakeEntities(FakeGame game) { this.game = game; lifetime = new GtaEntities(this); }
        public bool Exists(int entity) => lifetime.Exists(entity);
        bool IHandleLifetimeBindings.Exists(int entity)
        {
            game.BeforeObservation?.Invoke("entity.exists", entity);
            if (entity == 0) return false;
            if (!game.Observations.TryGetValue(entity, out var observation)) throw new InvalidOperationException("Unregistered entity: " + entity);
            return observation.Exists;
        }
        public bool IsDead(int entity) => game.Entity(entity).Dead;
        public bool IsInAir(int entity) => game.Entity(entity).InAir;
        public Vector3 Position(int entity) { game.BeforeObservation?.Invoke("entity.position", entity); return game.Entity(entity).Position; }
        public Vector3 Velocity(int entity) => game.Entity(entity).Velocity;
        public Vector3 Rotation(int entity) => game.Entity(entity).Rotation;
        public Quaternion Quaternion(int entity) => game.Entity(entity).Quaternion;
        public Vector3 ForwardVector(int entity) => game.Entity(entity).Forward;
        public float HeightAboveGround(int entity) => game.Entity(entity).HeightAboveGround;
        public int ModelHash(int entity) => game.Entity(entity).ModelHash;
        public int Health(int entity) => game.Entity(entity).Health;
        public void SetPosition(int entity, Vector3 value) { game.Record("entity.position", entity, value); game.Entity(entity).Position = value; }
        public void SetVelocity(int entity, Vector3 value) { game.Record("entity.velocity", entity, value); game.Entity(entity).Velocity = value; }
        public void SetRotation(int entity, Vector3 value) { game.Record("entity.rotation", entity, value); game.Entity(entity).Rotation = value; }
        public void SetQuaternion(int entity, Quaternion value) { game.Record("entity.quaternion", entity, value); game.Entity(entity).Quaternion = value; }
        public void SetHealth(int entity, int value) { game.Record("entity.health", entity, value); game.Entity(entity).Health = value; }
        public void SetPersistent(int entity, bool value) { game.Record("entity.persistent", entity, value); game.Entity(entity).Persistent = value; }
        public void SetCollisionEnabled(int entity, bool value) { game.Record("entity.collision", entity, value); game.Entity(entity).CollisionEnabled = value; }
        public void SetNoCollision(int entity, int other, bool toggle) { game.Record("entity.noCollision", entity, other, toggle); game.Entity(entity); game.Entity(other); }
        public void SetVisible(int entity, bool value) { game.Record("entity.visible", entity, value); game.Entity(entity).Visible = value; }
        public void SetInvincible(int entity, bool value) { game.Record("entity.invincible", entity, value); game.Entity(entity).Invincible = value; }
        public void SetBulletProof(int entity, bool value) { game.Record("entity.bulletProof", entity, value); game.Entity(entity).BulletProof = value; }
        public void SetCollisionProof(int entity, bool value) { game.Record("entity.collisionProof", entity, value); game.Entity(entity).CollisionProof = value; }
        public void SetExplosionProof(int entity, bool value) { game.Record("entity.explosionProof", entity, value); game.Entity(entity).ExplosionProof = value; }
        public void SetFireProof(int entity, bool value) { game.Record("entity.fireProof", entity, value); game.Entity(entity).FireProof = value; }
        public void SetMeleeProof(int entity, bool value) { game.Record("entity.meleeProof", entity, value); game.Entity(entity).MeleeProof = value; }
        public void SetOpacity(int entity, int value) { game.Record("entity.opacity", entity, value); game.Entity(entity).Opacity = value; }
        public void ResetOpacity(int entity) { game.Record("entity.resetOpacity", entity); game.Entity(entity).Opacity = 255; }
        public bool HasBeenDamagedBy(int entity, int other) => game.Entity(entity).DamagedBy.Contains(other);
        public bool IsTouching(int entity, int other)
        {
            game.Entity(other);
            return game.Entity(entity).Touching.Contains(other);
        }
        public void ApplyForce(int entity, Vector3 force) { game.Record("entity.force", entity, force); game.Entity(entity); }
        public void AttachToBone(int entity, int ped, Bone bone) { game.Record("entity.attach", entity, ped, bone); game.Entity(ped); var state = game.Entity(entity); state.AttachedTo = ped; state.AttachedBone = bone; }
        public void Detach(int entity) { game.Record("entity.detach", entity); game.Entity(entity).AttachedTo = 0; }
        public void Delete(int entity) => lifetime.Delete(entity);
        void IHandleLifetimeBindings.Delete(int entity)
        {
            game.Record("entity.delete", entity);
            if (!SilentDeletionRefusals.Contains(entity)) game.Entity(entity).Exists = false;
        }
        public int AddBlip(int entity) { game.Entity(entity); int blip = CreatedBlips.Peek(); game.Record("blip.create", entity, blip); CreatedBlips.Dequeue(); return blip; }
        public void SetBlipFriendly(int blip, bool friendly) => game.Record("blip.friendly", blip, friendly);
        public void SetBlipName(int blip, string name) => game.Record("blip.name", blip, name);
    }

    internal sealed class FakePeds : IPedAccess
    {
        private readonly FakeGame game;
        public FakePeds(FakeGame game) { this.game = game; }
        public int MaxHealth(int ped) => game.Entity(ped).MaxHealth;
        public void SetMaxHealth(int ped, int value) { game.Record("ped.maxHealth", ped, value); game.Entity(ped).MaxHealth = value; }
        public void SetCanRagdoll(int ped, bool value) { game.Record("ped.canRagdoll", ped, value); game.Entity(ped).CanRagdoll = value; }
        public void SetCanSufferCriticalHits(int ped, bool value) { game.Record("ped.criticalHits", ped, value); game.Entity(ped).CanSufferCriticalHits = value; }
        public void SetDiesOnLowHealth(int ped, bool value) { game.Record("ped.diesOnLowHealth", ped, value); game.Entity(ped).DiesOnLowHealth = value; }
        public bool IsInVehicle(int ped) => game.Entity(ped).InVehicle;
        public bool IsInMeleeCombat(int ped) => game.Entity(ped).InMeleeCombat;
        public bool IsInCombatAgainst(int ped, int other) => game.Entity(ped).CombatAgainst.Contains(other);
        public bool IsWalking(int ped) => game.Entity(ped).Walking;
        public bool IsSprinting(int ped) => game.Entity(ped).Sprinting;
        public bool IsRunning(int ped) => game.Entity(ped).Running;
        public bool IsRagdoll(int ped) => game.Entity(ped).Ragdoll;
        public Vector3 BonePosition(int ped, Bone bone) => game.Entity(ped).Bones[bone];
        public void ApplyDamage(int ped, int damage) { game.Record("ped.damage", ped, damage); game.Entity(ped); }
        public void SetIntoVehicle(int ped, int vehicle, VehicleSeat seat) { game.Record("ped.intoVehicle", ped, vehicle, seat); game.Entity(ped).Vehicle = vehicle; game.Entity(ped).InVehicle = true; game.Entity(vehicle).Driver = ped; }
        public void ClearTasks(int ped) { game.Record("ped.clearTasks", ped); game.Entity(ped); }
        public void StartHeliMissionToPosition(int ped, int vehicle, Vector3 position, VehicleMissionType mission, float speed, float radius, int heading, int maxHeight, int minHeight, float slowDistance, HeliMissionFlags flags)
        { game.Entity(ped); game.Entity(vehicle); game.Record("ped.missionPosition", ped, vehicle, position, mission, speed, radius, heading, maxHeight, minHeight, slowDistance, flags); }
        public void StartHeliMissionToPed(int ped, int vehicle, int target, VehicleMissionType mission, float speed, float radius, int heading, int maxHeight, int minHeight, float slowDistance, HeliMissionFlags flags)
        { game.Entity(ped); game.Entity(vehicle); game.Entity(target); game.Record("ped.missionPed", ped, vehicle, target, mission, speed, radius, heading, maxHeight, minHeight, slowDistance, flags); }
    }

    internal sealed class FakeWeapons : IWeaponAccess
    {
        private readonly FakeGame game;
        public readonly Dictionary<WeaponHash, bool> AssetResults = new Dictionary<WeaponHash, bool>();
        public FakeWeapons(FakeGame game) { this.game = game; }
        public bool HasWeapon(int ped, WeaponHash weapon) => game.Entity(ped).Weapons.Contains(weapon);
        public WeaponHash Current(int ped) => game.Entity(ped).CurrentWeapon;
        public int CurrentObject(int ped) => game.Entity(ped).CurrentWeaponObject;
        public void Give(int ped, WeaponHash weapon, int ammo, bool equipNow, bool loaded)
        {
            game.Record("weapon.give", ped, weapon, ammo, equipNow, loaded);
            game.Entity(ped).Weapons.Add(weapon);
            // SHVDNE documents Give(equipNow:true) as a switch request, not an
            // immediate equip. The scene must supply the resulting observation.
        }
        public void Remove(int ped, WeaponHash weapon)
        {
            game.Record("weapon.remove", ped, weapon);
            var state = game.Entity(ped);
            state.Weapons.Remove(weapon);
            if (state.CurrentWeapon == weapon) game.ApplyWeaponObservation(ped, WeaponHash.Unarmed, 0);
        }
        public bool Select(int ped, WeaponHash weapon, bool equipNow)
        {
            game.Record("weapon.select", ped, weapon, equipNow);
            var state = game.Entity(ped);
            if (weapon != WeaponHash.Unarmed && !state.Weapons.Contains(weapon)) return false;
            if (equipNow && state.CurrentWeapon != weapon) game.ApplyWeaponObservation(ped, weapon, 0);
            return true;
        }
        public bool RequestAsset(WeaponHash weapon, int timeoutMs) { game.Record("weapon.request", weapon, timeoutMs); return AssetResults[weapon]; }
    }

    internal sealed class FakeWorld : IWorldAccess, IWorldQueryBindings
    {
        private readonly FakeGame game;
        public readonly Queue<RayHit> Rays = new Queue<RayHit>(), Capsules = new Queue<RayHit>();
        public readonly Queue<int> Vehicles = new Queue<int>(), Drivers = new Queue<int>();
        public readonly Dictionary<int, bool> ModelResults = new Dictionary<int, bool>();
        public Func<Vector3, float, int[]> Nearby { get; set; }
        public Func<Vector3, Tuple<bool, float>> Ground { get; set; }
        public Func<Vector3, Vector3, IntersectFlags, int, RayHit> RaycastObservation { get; set; }
        public Func<Vector3, Vector3, float, IntersectFlags, RayHit> CapsuleObservation { get; set; }
        public FakeWorld(FakeGame game) { this.game = game; }
        public int[] NearbyEntities(Vector3 position, float radius) { game.Record("world.nearby", position, radius); if (Nearby == null) throw new InvalidOperationException("Unscripted nearby query."); return Nearby(position, radius); }
        public RayHit Raycast(Vector3 from, Vector3 to, IntersectFlags flags, int ignoreEntity = 0)
            => new GtaWorld(this).Raycast(from, to, flags, ignoreEntity);
        public RayHit Capsule(Vector3 from, Vector3 to, float radius, IntersectFlags flags)
            => new GtaWorld(this).Capsule(from, to, radius, flags);
        void IWorldQueryBindings.Raycast(Vector3 from, Vector3 to, IntersectFlags flags, int ignoreEntity,
            out bool hit, out int entity, out Vector3 position)
        {
            game.Record("world.raycast", from, to, flags, ignoreEntity);
            var observation = Rays.Count != 0 ? Rays.Dequeue() :
                (RaycastObservation ?? throw new InvalidOperationException("Unscripted raycast observation."))(from, to, flags, ignoreEntity);
            hit = observation.DidHit; entity = observation.Entity; position = observation.Position;
        }
        void IWorldQueryBindings.Capsule(Vector3 from, Vector3 to, float radius, IntersectFlags flags,
            out bool hit, out int entity, out Vector3 position)
        {
            game.Record("world.capsule", from, to, radius, flags);
            var observation = Capsules.Count != 0 ? Capsules.Dequeue() :
                (CapsuleObservation ?? throw new InvalidOperationException("Unscripted capsule observation."))(from, to, radius, flags);
            hit = observation.DidHit; entity = observation.Entity; position = observation.Position;
        }
        public void DrawMarker(MarkerType type, Vector3 position, Vector3 direction, Vector3 rotation, Vector3 scale, Color color) => game.Record("world.marker", type, position, direction, rotation, scale, color);
        public bool GetGroundHeight(Vector3 position, out float height) { if (Ground == null) throw new InvalidOperationException("Unscripted ground query."); var result = Ground(position); height = result.Item2; return result.Item1; }
        public bool RequestModel(int modelHash, int timeoutMs) { game.Record("world.requestModel", modelHash, timeoutMs); return ModelResults[modelHash]; }
        public void ReleaseModel(int modelHash) => game.Record("world.releaseModel", modelHash);
        public int CreateVehicle(int modelHash, Vector3 position) { int handle = Vehicles.Peek(); game.Record("world.createVehicle", modelHash, position, handle); Vehicles.Dequeue(); if (handle != 0) game.Entity(handle).Position = position; return handle; }
        public int CreateDriver(int vehicle) { game.Entity(vehicle); int handle = Drivers.Peek(); game.Record("world.createDriver", vehicle, handle); Drivers.Dequeue(); if (handle != 0) { game.Entity(handle).Vehicle = vehicle; game.Entity(vehicle).Driver = handle; } return handle; }
        public int VehicleDriver(int vehicle) => game.Entity(vehicle).Driver;
    }

    internal sealed class CameraObservation
    {
        public Vector3 Position;
        public bool Active;
    }
    internal sealed class FakeCameras : ICameraAccess, ICameraLifetimeBindings
    {
        private readonly FakeGame game;
        private readonly GtaCameras lifetime;
        public FakeCameras(FakeGame game) { this.game = game; lifetime = new GtaCameras(this); }
        public readonly Dictionary<int, CameraObservation> Cameras = new Dictionary<int, CameraObservation>();
        public readonly Queue<int> Creations = new Queue<int>();
        public readonly HashSet<int> SilentDeletionRefusals = new HashSet<int>();
        public Vector3 GameplayPosition { get; set; }
        public Vector3 GameplayDirection { get; set; } = Vector3.UnitY;
        public Vector3 GameplayRotation { get; set; }
        public int RenderingCamera { get; set; }
        public int Create(Vector3 position, Vector3 rotation, float fieldOfView) { int handle = Creations.Peek(); game.Record("camera.create", position, rotation, fieldOfView, handle); Creations.Dequeue(); if (handle != 0) Cameras.Add(handle, new CameraObservation { Position = position }); return handle; }
        public void SetPosition(int camera, Vector3 position) { game.Record("camera.position", camera, position); Cameras[camera].Position = position; }
        public void PointAt(int camera, int entity) { game.Record("camera.pointAt", camera, entity); _ = Cameras[camera]; game.Entity(entity); }
        public void SetActive(int camera, bool active) { game.Record("camera.active", camera, active); Cameras[camera].Active = active; }
        public void StartRendering() => game.Record("camera.startRendering");
        public void StopRendering() { game.Record("camera.stopRendering"); RenderingCamera = 0; }
        public void Delete(int camera) => lifetime.Delete(camera);
        bool IHandleLifetimeBindings.Exists(int camera) => Cameras.ContainsKey(camera);
        void IHandleLifetimeBindings.Delete(int camera)
        {
            game.Record("camera.delete", camera);
            if (!SilentDeletionRefusals.Contains(camera)) Cameras.Remove(camera);
        }
        public void Shake(CameraShake shake, float amplitude) => game.Record("camera.shake", shake, amplitude);
        public void SetHeadingLimits(float minimum, float maximum) => game.Record("camera.headingLimits", minimum, maximum);
        public void SetPitchLimits(float minimum, float maximum) => game.Record("camera.pitchLimits", minimum, maximum);
    }

    internal sealed class FakeRopes : IRopeAccess, IRopeLifetimeBindings
    {
        private readonly FakeGame game;
        private readonly GtaRopes lifetime;
        public FakeRopes(FakeGame game) { this.game = game; lifetime = new GtaRopes(this); }
        public readonly HashSet<int> Ropes = new HashSet<int>();
        public readonly Queue<int> Creations = new Queue<int>();
        public readonly HashSet<int> SilentDeletionRefusals = new HashSet<int>();
        public int Create(RopeType type, Vector3 position, Vector3 rotation, float length, float minLength, bool breakable) { int handle = Creations.Peek(); game.Record("rope.create", type, position, rotation, length, minLength, breakable, handle); Creations.Dequeue(); if (handle != 0 && !Ropes.Add(handle)) throw new InvalidOperationException("Duplicate rope."); return handle; }
        private void Require(int rope) { if (!Ropes.Contains(rope)) throw new InvalidOperationException("Unknown or deleted rope: " + rope); }
        public void Connect(int rope, int firstEntity, Vector3 firstPosition, int secondEntity, Vector3 secondPosition, float length) { game.Record("rope.connect", rope, firstEntity, firstPosition, secondEntity, secondPosition, length); Require(rope); game.Entity(firstEntity); game.Entity(secondEntity); }
        public void ActivatePhysics(int rope) { game.Record("rope.physics", rope); Require(rope); }
        public void Delete(int rope) => lifetime.Delete(rope);
        bool IHandleLifetimeBindings.Exists(int rope) => Ropes.Contains(rope);
        void IHandleLifetimeBindings.Delete(int rope)
        {
            game.Record("rope.delete", rope);
            if (!SilentDeletionRefusals.Contains(rope)) Ropes.Remove(rope);
        }
    }

    internal sealed class FakeNative : INativeInvoker, IRawNative, IArgumentMarshaller
    {
        private readonly FakeGame game;
        public FakeNative(FakeGame game) { this.game = game; }
        public readonly List<(Hash Hash, object[] Arguments)> Calls = new List<(Hash, object[])>();
        public readonly Dictionary<Hash, Func<object[], object>> Queries = new Dictionary<Hash, Func<object[], object>>();
        public void Returns(Hash hash, object result) => Queries[hash] = arguments => result;
        public void Call(Hash hash, params object[] arguments) { Calls.Add((hash, arguments)); game.Record("native." + hash, arguments); }
        public T Call<T>(Hash hash, params object[] arguments) { Call(hash, arguments); if (!Queries.TryGetValue(hash, out var query)) throw new InvalidOperationException("Unscripted native query: " + hash); return (T)query(arguments); }
        // Only SHVDNE's runtime-dependent scalar marshalling is substituted. The
        // shipped invoker still assembles and submits each complete native call.
        private readonly Dictionary<InputArgument, object> arguments = new Dictionary<InputArgument, object>();
        InputArgument IArgumentMarshaller.Convert(object value)
        {
            var token = new InputArgument((ulong)arguments.Count + 1);
            arguments.Add(token, value);
            return token;
        }
        void IRawNative.Call(Hash hash, InputArgument[] values) => Call(hash, values.Select(value => arguments[value]).ToArray());
        T IRawNative.Call<T>(Hash hash, InputArgument[] values) => Call<T>(hash, values.Select(value => arguments[value]).ToArray());
    }

    internal sealed class FakeOutputs : INativeOutputs, IRawNativeOutputs
    {
        private readonly FakeGame game;
        public FakeOutputs(FakeGame game) { this.game = game; }
        public readonly Dictionary<int, Tuple<bool, int>> DamageBones = new Dictionary<int, Tuple<bool, int>>();
        public Func<Vector3, Tuple<bool, float, float>> Projection { get; set; }
        public bool LastDamageBone(int ped, out int bone) { game.Entity(ped); var result = DamageBones[ped]; bone = result.Item2; return result.Item1; }
        public Quaternion EntityQuaternion(int entity) => game.Entity(entity).Quaternion;
        public bool WorldToScreen(Vector3 position, out float x, out float y) { if (Projection == null) throw new InvalidOperationException("Unscripted projection."); var result = Projection(position); x = result.Item2; y = result.Item3; return result.Item1; }
        public void EntityQuaternion(int entity, out float x, out float y, out float z, out float w)
        { var value = EntityQuaternion(entity); x = value.X; y = value.Y; z = value.Z; w = value.W; }
        public bool WorldToScreen(float worldX, float worldY, float worldZ, out float x, out float y)
            => WorldToScreen(new Vector3(worldX, worldY, worldZ), out x, out y);
    }
}
