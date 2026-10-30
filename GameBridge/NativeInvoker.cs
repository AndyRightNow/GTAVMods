using System;
using System.Linq;
using GTA.Math;
using GTA.Native;

namespace GTAVMods.Game
{
    public interface IRawNative
    {
        void Call(Hash hash, InputArgument[] arguments);
        T Call<T>(Hash hash, InputArgument[] arguments);
    }

    public interface IArgumentMarshaller
    {
        InputArgument Convert(object value);
    }

    public sealed class GtaArgumentMarshaller : IArgumentMarshaller
    {
        // Only references reach this host binding: SHVDNE pins strings and reads INativeValue.
        [GamePrimitive] public InputArgument Convert(object value) => new InputArgument(value);
    }

    public sealed class ScalarArgumentMarshaller : IArgumentMarshaller
    {
        private readonly IArgumentMarshaller references;
        public ScalarArgumentMarshaller(IArgumentMarshaller references) { this.references = references; }
        public InputArgument Convert(object value) => NativeScalars.TryConvert(value, out var argument) ? argument : references.Convert(value);
    }

    public static class NativeScalars
    {
        // The object overload deliberately rejects boxed scalars in SHVDNE 3.9.
        // Typed conversions preserve signed integers and the IEEE float bits expected by natives.
        public static bool TryConvert(object value, out InputArgument argument)
        {
            switch (value)
            {
                case bool v: argument = v; break;
                case byte v: argument = v; break;
                case sbyte v: argument = v; break;
                case short v: argument = v; break;
                case ushort v: argument = v; break;
                case int v: argument = v; break;
                case uint v: argument = v; break;
                case long v: argument = v; break;
                case ulong v: argument = v; break;
                case float v: argument = v; break;
                case double v: argument = v; break;
                case Enum v: argument = v; break;
                case IntPtr v: argument = v; break;
                case InputArgument v: argument = v; break;
                default: argument = null; return false;
            }
            return true;
        }
    }

    public sealed class NativeInvoker : INativeInvoker
    {
        private readonly IRawNative native;
        private readonly IArgumentMarshaller marshaller;
        public NativeInvoker(IRawNative native, IArgumentMarshaller marshaller)
        {
            this.native = native;
            this.marshaller = marshaller;
        }
        public void Call(Hash hash, params object[] arguments)
            => native.Call(hash, arguments.Select(marshaller.Convert).ToArray());
        public T Call<T>(Hash hash, params object[] arguments)
            => native.Call<T>(hash, arguments.Select(marshaller.Convert).ToArray());
    }

    public sealed class GtaNative : IRawNative
    {
        [GamePrimitive] public void Call(Hash hash, InputArgument[] arguments) => Function.Call(hash, arguments);
        [GamePrimitive] public T Call<T>(Hash hash, InputArgument[] arguments) => Function.Call<T>(hash, arguments);
    }

    /// <summary>Scalar output transport used only by the measured output adapter.</summary>
    public interface IRawNativeOutputs
    {
        bool AmmoInClip(int ped, uint weapon, out int ammo);
        bool AimTarget(int player, out int entity);
        void StreetNames(float x, float y, float z, out uint street, out uint crossing);
        bool LastDamageBone(int ped, out int bone);
        void EntityQuaternion(int entity, out float x, out float y, out float z, out float w);
        bool WorldToScreen(float worldX, float worldY, float worldZ, out float x, out float y);
    }

    public sealed class NativeOutputs : INativeOutputs
    {
        private readonly IRawNativeOutputs native;
        public NativeOutputs(IRawNativeOutputs native) { this.native = native; }
        public bool AmmoInClip(int ped, uint weapon, out int ammo) => native.AmmoInClip(ped, weapon, out ammo);
        public bool AimTarget(int player, out int entity) => native.AimTarget(player, out entity);
        public void StreetNames(Vector3 position, out uint street, out uint crossing) => native.StreetNames(position.X, position.Y, position.Z, out street, out crossing);
        public Quaternion EntityQuaternion(int entity)
        {
            native.EntityQuaternion(entity, out float x, out float y, out float z, out float w);
            return new Quaternion(x, y, z, w);
        }
        public bool WorldToScreen(Vector3 position, out float x, out float y)
            => native.WorldToScreen(position.X, position.Y, position.Z, out x, out y);
    }

    public sealed class GtaOutputs : IRawNativeOutputs
    {
        [GamePrimitive] public unsafe bool AmmoInClip(int ped, uint weapon, out int ammo)
        {
            int result = 0;
            bool valid = Function.Call<bool>(Hash.GET_AMMO_IN_CLIP, ped, weapon, &result);
            ammo = result; return valid;
        }
        [GamePrimitive] public unsafe bool AimTarget(int player, out int entity)
        {
            int result = 0;
            bool valid = Function.Call<bool>(Hash.GET_ENTITY_PLAYER_IS_FREE_AIMING_AT, player, &result);
            entity = result; return valid;
        }
        [GamePrimitive] public unsafe void StreetNames(float x, float y, float z, out uint street, out uint crossing)
        {
            uint first = 0, second = 0;
            Function.Call(Hash.GET_STREET_NAME_AT_COORD, x, y, z, &first, &second);
            street = first; crossing = second;
        }
        [GamePrimitive] public unsafe bool LastDamageBone(int ped, out int bone)
        {
            int result = 0;
            bool hit = Function.Call<bool>(Hash.GET_PED_LAST_DAMAGE_BONE, ped, &result);
            bone = result;
            return hit;
        }
        [GamePrimitive] public unsafe void EntityQuaternion(int entity, out float x, out float y, out float z, out float w)
        {
            float resultX = 0, resultY = 0, resultZ = 0, resultW = 0;
            Function.Call(Hash.GET_ENTITY_QUATERNION, entity, &resultX, &resultY, &resultZ, &resultW);
            x = resultX;
            y = resultY;
            z = resultZ;
            w = resultW;
        }
        [GamePrimitive] public unsafe bool WorldToScreen(float worldX, float worldY, float worldZ, out float x, out float y)
        {
            float screenX = 0, screenY = 0;
            bool hit = Function.Call<bool>(Hash.GET_SCREEN_COORD_FROM_WORLD_COORD, worldX, worldY, worldZ, &screenX, &screenY);
            x = screenX;
            y = screenY;
            return hit;
        }
    }
}
