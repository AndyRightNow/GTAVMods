using GTA;
using GTA.Math;
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
        private readonly IParticleAccess particles;

        public ParticleFxLooped(string effectSetName, string effectName, NativeHelper effects, IParticleAccess particles)
        {
            this.effectSetName = effectSetName;
            this.effectName = effectName;
            this.effects = effects;
            this.particles = particles;
        }

        public void Start(int ped, Bone boneId, float scale = 1.0f)
        {
            Stop();
            handle = effects.PlayParticleFxLooped(effectSetName, effectName, ped, boneId, scale);
        }

        public void Start(Vector3 coord, Vector3 rot, float scale = 1.0f)
        {
            Stop();
            handle = effects.PlayParticleFxLooped(effectSetName, effectName, coord, rot, scale);
        }

        public void Stop()
        {
            if (handle == 0) return;
            particles.StopLooped(handle);
            handle = 0;
        }

        public Color Color
        {
            set
            {
                if (handle != 0) particles.SetLoopedColor(handle, value);
            }
        }
    }
}
