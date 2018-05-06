using GTA;
using GTA.Math;
using GTA.Native;
using System.Drawing;

namespace Thor
{
    public class ParticleFxLooped
    {
        private string effectSetName;
        private string effectName;
        private int handle;

        public ParticleFxLooped(string effectSetName, string effectName)
        {
            this.effectSetName = effectSetName;
            this.effectName = effectName;
        }

        public void Start(Ped ped, Bone boneId, float scale = 1.0f)
        {
            handle = NativeHelper.PlayParticleFxLooped(effectSetName, effectName, ped, boneId, scale);
        }

        public void Start(Vector3 coord, Vector3 rot, float scale = 1.0f)
        {
            handle = NativeHelper.PlayParticleFxLooped(effectSetName, effectName, coord, rot, scale);
        }

        public void Stop()
        {
            Function.Call(Hash.STOP_PARTICLE_FX_LOOPED, handle, 0);
        }

        public Color Color
        {
            set
            {
                Function.Call(Hash.SET_PARTICLE_FX_LOOPED_COLOUR, handle, value.R, value.G, value.B, 0);
            }
        }
    }
}
