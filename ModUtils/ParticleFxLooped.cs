using GTA;
using GTA.Math;
using GTA.Native;
using System.Drawing;
using GTAVMods.Game;

namespace ADModUtils
{
    public class ParticleFxLooped
    {
        private string effectSetName;
        private string effectName;
        private int handle;
        private readonly NativeHelper effects;
        private readonly INativeInvoker native;

        public ParticleFxLooped(string effectSetName, string effectName, NativeHelper effects, INativeInvoker native)
        {
            this.effectSetName = effectSetName;
            this.effectName = effectName;
            this.effects = effects;
            this.native = native;
        }

        public void Start(int ped, Bone boneId, float scale = 1.0f)
        {
            handle = effects.PlayParticleFxLooped(effectSetName, effectName, ped, boneId, scale);
        }

        public void Start(Vector3 coord, Vector3 rot, float scale = 1.0f)
        {
            handle = effects.PlayParticleFxLooped(effectSetName, effectName, coord, rot, scale);
        }

        public void Stop()
        {
            native.Call(Hash.STOP_PARTICLE_FX_LOOPED, handle, 0);
        }

        public Color Color
        {
            set
            {
                native.Call(Hash.SET_PARTICLE_FX_LOOPED_COLOUR, handle, value.R, value.G, value.B, 0);
            }
        }
    }
}
