using System.Drawing;
using GTA;
using GTA.Math;
using GTA.Native;

namespace GTAVMods.Game
{
    public sealed class NativeAnimationAccess : IAnimationAccess
    {
        private readonly INativeInvoker native;
        public NativeAnimationAccess(INativeInvoker native) { this.native = native; }
        public void RequestDictionary(string dictionary) => native.Call(Hash.REQUEST_ANIM_DICT, dictionary);
        public bool IsDictionaryLoaded(string dictionary) => native.Call<bool>(Hash.HAS_ANIM_DICT_LOADED, dictionary);
        public bool IsPlaying(int entity, string dictionary, string animation)
            => native.Call<bool>(Hash.IS_ENTITY_PLAYING_ANIM, entity, dictionary, animation, 3);
        public void Play(int ped, string dictionary, string animation, AnimationFlags flags,
            int durationMilliseconds, float blendInSpeed, float blendOutSpeed, float playbackRate)
            => native.Call(Hash.TASK_PLAY_ANIM, ped, dictionary, animation, blendInSpeed, blendOutSpeed,
                durationMilliseconds, (int)flags, playbackRate, 0, 0, 0);
    }

    public sealed class NativeParticleAccess : IParticleAccess
    {
        private readonly INativeInvoker native;
        public NativeParticleAccess(INativeInvoker native) { this.native = native; }
        public void RequestAsset(string asset) => native.Call(Hash.REQUEST_NAMED_PTFX_ASSET, asset);
        public bool IsAssetLoaded(string asset) => native.Call<bool>(Hash.HAS_NAMED_PTFX_ASSET_LOADED, asset);
        public void UseAsset(string asset) => native.Call(Hash.USE_PARTICLE_FX_ASSET, asset);
        public void PlayOnEntity(string effect, int entity, Vector3 offset, Vector3 rotationDegrees, float scale)
            => native.Call(Hash.START_PARTICLE_FX_NON_LOOPED_ON_ENTITY, effect, entity,
                offset.X, offset.Y, offset.Z, rotationDegrees.X, rotationDegrees.Y, rotationDegrees.Z, scale, 0, 0, 0);
        public void PlayOnPedBone(string effect, int ped, Bone bone, Vector3 offset, Vector3 rotationDegrees, float scale)
            => native.Call(Hash.START_PARTICLE_FX_NON_LOOPED_ON_PED_BONE, effect, ped,
                offset.X, offset.Y, offset.Z, rotationDegrees.X, rotationDegrees.Y, rotationDegrees.Z, (int)bone, scale, 0, 0, 0);
        public void PlayAt(string effect, Vector3 position, Vector3 rotationDegrees, float scale)
            => native.Call(Hash.START_PARTICLE_FX_NON_LOOPED_AT_COORD, effect,
                position.X, position.Y, position.Z, rotationDegrees.X, rotationDegrees.Y, rotationDegrees.Z, scale, 0, 0, 0);
    }

    public sealed class NativeDrawingAccess : IDrawingAccess
    {
        private readonly INativeInvoker native;
        public NativeDrawingAccess(INativeInvoker native) { this.native = native; }
        public void DrawLine(Vector3 start, Vector3 end, Color color)
            => native.Call(Hash.DRAW_LINE, start.X, start.Y, start.Z, end.X, end.Y, end.Z, color.R, color.G, color.B, color.A);
        public void DrawBox(Vector3 minimum, Vector3 maximum, Color color)
            => native.Call(Hash.DRAW_BOX, minimum.X, minimum.Y, minimum.Z, maximum.X, maximum.Y, maximum.Z, color.R, color.G, color.B, color.A);
        public void DrawPoly(Vector3 first, Vector3 second, Vector3 third, Color color)
            => native.Call(Hash.DRAW_POLY, first.X, first.Y, first.Z, second.X, second.Y, second.Z, third.X, third.Y, third.Z, color.R, color.G, color.B, color.A);
    }
}
