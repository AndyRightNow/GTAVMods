using System;
using System.Linq;
using GTA;
using GTA.Math;
using GTA.Native;

namespace GTAVMods.Game
{
    public interface IAgentWorld
    {
        int GameTime { get; }
        void Pause(bool paused);
        void SetControl(int group, int control, float value);
        object Observe(float radius, int limit);
        object State(StateQuery query);
        string[] StateSections { get; }
        object Invoke(GTA.Native.Hash hash, string result, object[] arguments);
        AimObservation AimAt(int target, int bone, bool shoot, bool requireAimTarget);
    }

    public readonly struct AimObservation
    {
        public AimObservation(bool available, bool? lineOfSight, int? aimTarget, bool attackRequested,
            bool? inCover = null, bool? aimingFromCover = null, bool? weaponReady = null)
        { Available = available; LineOfSight = lineOfSight; AimTarget = aimTarget; AttackRequested = attackRequested;
            InCover = inCover; AimingFromCover = aimingFromCover; WeaponReady = weaponReady; }
        public bool Available { get; }
        public bool? LineOfSight { get; }
        public int? AimTarget { get; }
        public bool AttackRequested { get; }
        public bool? InCover { get; }
        public bool? AimingFromCover { get; }
        public bool? WeaponReady { get; }
    }

    /// <summary>Agent operations use the same observed game boundary as the player abilities.</summary>
    public sealed class AgentWorld : IAgentWorld
    {
        private readonly GameServices game;
        private readonly INativeInvoker native;
        private readonly Func<Blip[]> blips;
        public AgentWorld(GameServices game, INativeInvoker native, Func<Blip[]> blips)
        { this.game = game; this.native = native; this.blips = blips; StatePack = new WorldStatePack(game, native, blips); }
        public WorldStatePack StatePack { get; }
        public string[] StateSections => StatePack.Sections;
        public object State(StateQuery query) => StatePack.Capture(query);
        public int GameTime => game.Host.GameTime;
        public void Pause(bool paused) => native.Call(Hash.SET_GAME_PAUSED, paused);
        public void SetControl(int group, int control, float value) => native.Call(Hash.SET_CONTROL_VALUE_NEXT_FRAME, group, control, value);
        public object Invoke(Hash hash, string result, object[] arguments)
        {
            switch (result)
            {
                case "bool": return native.Call<bool>(hash, arguments);
                case "int": return native.Call<int>(hash, arguments);
                case "uint": return native.Call<uint>(hash, arguments);
                case "float": return native.Call<float>(hash, arguments);
                case "string": return native.Call<string>(hash, arguments);
                case "vector3": return native.Call<Vector3>(hash, arguments);
                default: native.Call(hash, arguments); return null;
            }
        }
        public AimObservation AimAt(int target, int bone, bool shoot, bool requireAimTarget)
        {
            if (!game.Entities.Exists(target) || game.Entities.IsDead(target)) return default;
            Vector3 point = native.Call<Vector3>(Hash.GET_PED_BONE_COORDS, target, bone, 0f, 0f, 0f);
            Vector3 delta = point - game.Cameras.GameplayPosition;
            float yaw = (float)(System.Math.Atan2(-delta.X, delta.Y) * 180 / System.Math.PI);
            float pitch = (float)(System.Math.Atan2(delta.Z, System.Math.Sqrt(delta.X * delta.X + delta.Y * delta.Y)) * 180 / System.Math.PI);
            float difference = (yaw - game.Cameras.GameplayRotation.Z + 540) % 360 - 180;
            native.Call(Hash.SET_GAMEPLAY_CAM_RELATIVE_HEADING, native.Call<float>(Hash.GET_GAMEPLAY_CAM_RELATIVE_HEADING) + difference);
            native.Call(Hash.SET_GAMEPLAY_CAM_RELATIVE_PITCH, native.Call<float>(Hash.GET_GAMEPLAY_CAM_RELATIVE_PITCH) + pitch - game.Cameras.GameplayRotation.X, 1f);
            SetControl(0, (int)Control.Aim, 1);
            bool lineOfSight = native.Call<bool>(Hash.HAS_ENTITY_CLEAR_LOS_TO_ENTITY, game.Host.PlayerPed, target, 17);
            int? aimTarget = game.Outputs.AimTarget(game.Host.PlayerHandle, out int aimed) && game.Entities.Exists(aimed) ? (int?)aimed : null;
            bool attackRequested = shoot && lineOfSight && (!requireAimTarget || aimTarget == target);
            if (attackRequested) SetControl(0, (int)Control.Attack, 1);
            return new AimObservation(true, lineOfSight, aimTarget, attackRequested,
                native.Call<bool>(Hash.IS_PED_IN_COVER, game.Host.PlayerPed, false),
                native.Call<bool>(Hash.IS_PED_AIMING_FROM_COVER, game.Host.PlayerPed),
                native.Call<bool>(Hash.IS_PED_WEAPON_READY_TO_SHOOT, game.Host.PlayerPed));
        }
        public object Observe(float radius, int limit)
        {
            int player = game.Host.PlayerPed;
            bool exists = player != 0 && game.Entities.Exists(player);
            var position = exists ? game.Entities.Position(player) : Vector3.Zero;
            var nearby = game.World.NearbyEntities(position, radius).Where(h => h != player && game.Entities.Exists(h))
                .OrderBy(h => game.Entities.Position(h).DistanceToSquared(position)).ToArray();
            return new {
                gameTime = GameTime, frameSeconds = game.Host.LastFrameTime,
                pauseMenu = game.Host.IsPaused,
                missionActive = native.Call<bool>(Hash.GET_MISSION_FLAG),
                cutsceneActive = native.Call<bool>(Hash.IS_CUTSCENE_ACTIVE),
                screenFaded = native.Call<bool>(Hash.IS_SCREEN_FADED_OUT),
                wantedLevel = native.Call<int>(Hash.GET_PLAYER_WANTED_LEVEL, game.Host.PlayerHandle),
                player = exists ? Describe(player) : null,
                vehicle = exists && game.Peds.IsInVehicle(player) ? native.Call<int>(Hash.GET_VEHICLE_PED_IS_IN, player, false) : 0,
                weapon = exists ? game.Weapons.Current(player).ToString() : null,
                camera = new { position = Vector(game.Cameras.GameplayPosition), direction = Vector(game.Cameras.GameplayDirection), rotation = Vector(game.Cameras.GameplayRotation) },
                nearby = nearby.Take(limit).Select(Describe).ToArray(), nearbyTotal = nearby.Length,
                radius, limit,
                blips = ReadBlips(limit)
            };
        }

        private object[] ReadBlips(int limit)
        {
            return blips().Take(limit).Select(item => {
                int blip = GameHandles.Of(item);
                return (object)new { handle = blip, sprite = native.Call<int>(Hash.GET_BLIP_SPRITE, blip),
                    color = native.Call<int>(Hash.GET_BLIP_COLOUR, blip), position = Vector(native.Call<Vector3>(Hash.GET_BLIP_COORDS, blip)) };
            }).ToArray();
        }

        private object Describe(int handle)
        {
            var position = game.Entities.Position(handle);
            var quaternion = game.Outputs.EntityQuaternion(handle);
            bool visible = game.Outputs.WorldToScreen(position, out float x, out float y);
            return new {
            handle, model = game.Entities.ModelHash(handle), type = native.Call<int>(Hash.GET_ENTITY_TYPE, handle),
            position = Vector(position), velocity = Vector(game.Entities.Velocity(handle)),
            rotation = Vector(game.Entities.Rotation(handle)), health = game.Entities.Health(handle), dead = game.Entities.IsDead(handle),
            quaternion = new { x = quaternion.X, y = quaternion.Y, z = quaternion.Z, w = quaternion.W },
            screen = visible ? new { x, y } : null
            };
        }
        private static object Vector(Vector3 value) => new { x = value.X, y = value.Y, z = value.Z };
    }

    public static class GtaAgentQueries
    {
        [GamePrimitive] public static Blip[] Blips() => World.GetAllBlips();
    }
}
