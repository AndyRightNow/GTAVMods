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
        // SHVDNE's object constructor resolves its native runtime even for scalar values.
        [GamePrimitive] public InputArgument Convert(object value) => new InputArgument(value);
    }

    public sealed class NativeInvoker : INativeInvoker
    {
        private readonly IRawNative native;
        private readonly IArgumentMarshaller marshaller;
        public NativeInvoker(IRawNative native) : this(native, new GtaArgumentMarshaller()) { }
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
        bool LastDamageBone(int ped, out int bone);
        void EntityQuaternion(int entity, out float x, out float y, out float z, out float w);
        bool WorldToScreen(float worldX, float worldY, float worldZ, out float x, out float y);
    }

    public sealed class NativeOutputs : INativeOutputs
    {
        private readonly IRawNativeOutputs native;
        public NativeOutputs(IRawNativeOutputs native) { this.native = native; }
        public bool LastDamageBone(int ped, out int bone) => native.LastDamageBone(ped, out bone);
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
