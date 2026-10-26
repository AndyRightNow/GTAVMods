using GTA;
using GTAVMods.Game;
using GTA.Math;
using System.Collections.Generic;
using System.Drawing;

namespace ADModUtils
{
    using AnimationWaitTimeDict = Dictionary<string, Dictionary<string, int>>;

    public enum RagdollType
    {
        Normal = 0,
        StiffLegs = 1,
        NarrowLegs = 2,
        WideLegs = 3,
    }

    public class NativeHelper
    {
        private readonly GameServices game;
        private readonly IEventLog log;
        private readonly int meleeRagdollMinMs;
        private readonly int meleeRagdollMaxMs;
        private readonly float animationBlendInSpeed;
        private readonly float animationBlendOutSpeed;
        private readonly float animationPlaybackRate;
        private readonly int animationLoadTimeoutMs;
        private readonly int weaponAssetLoadTimeoutMs;

        public NativeHelper(
            GameServices game,
            string[] animationDictNames,
            string[] animationNames,
            AnimationWaitTimeDict animationWaitTime,
            bool[] animationWithAngles,
            bool[] animationWithAnglesAndIncompletePlusOrMinusSign,
            string[] particleEffectSetNames,
            string[] particleEffectNames,
            int meleeHitPedDamage,
            float meleeHitForce,
            int meleeRagdollMinMs,
            int meleeRagdollMaxMs,
            float animationBlendInSpeed,
            float animationBlendOutSpeed,
            float animationPlaybackRate,
            int animationLoadTimeoutMs,
            int weaponAssetLoadTimeoutMs,
            IEventLog log)
        {
            this.game = game;
            this.log = log;
            AnimationDictNames = animationDictNames;
            AnimationNames = animationNames;
            AnimationWaitTime = animationWaitTime;
            AnimationWithAngles = animationWithAngles;
            AnimationWithAnglesAndIncompletePlusOrMinusSign = animationWithAnglesAndIncompletePlusOrMinusSign;
            ParticleEffectSetNames = particleEffectSetNames;
            ParticleEffectNames = particleEffectNames;
            MeleeHitPedDamage = meleeHitPedDamage;
            MeleeHitForce = meleeHitForce;
            this.meleeRagdollMinMs = meleeRagdollMinMs;
            this.meleeRagdollMaxMs = meleeRagdollMaxMs;
            this.animationBlendInSpeed = animationBlendInSpeed;
            this.animationBlendOutSpeed = animationBlendOutSpeed;
            this.animationPlaybackRate = animationPlaybackRate;
            this.animationLoadTimeoutMs = animationLoadTimeoutMs;
            this.weaponAssetLoadTimeoutMs = weaponAssetLoadTimeoutMs;
        }

        public int GetAnimationWaitTimeByDictNameAndAnimName(string dictName, string animName)
            => AnimationTiming.GetDelay(AnimationWaitTime, dictName, animName);

        public string GetAnimationDictNameByAction(uint action)
        {
            return AnimationDictNames[(int)action];
        }

        public string GetAnimationNameByAction(uint action)
        {
            return AnimationNames[(int)action];
        }

        public string GetParticleSetName(uint fx)
        {
            return ParticleEffectSetNames[(int)fx];
        }

        public string GetParticleName(uint fx)
        {
            return ParticleEffectNames[(int)fx];
        }

        public bool DoesAnimationActionHaveAngles(uint action)
        {
            return AnimationWithAngles[(int)action];
        }

        public bool DoesAnimationActionHaveAnglesAndIncompletePlusOrMinusSign(uint action)
        {
            return AnimationWithAnglesAndIncompletePlusOrMinusSign[(int)action];
        }

        public void ApplyForcesAndDamages(int ent, Vector3 direction, float powerLevel = 100.0f)
        {
            if (IsPed(ent) && ent != game.Host.PlayerPed)
            {
                var ped = ent;
                SetPedToRagdoll(ped, RagdollType.Normal, meleeRagdollMinMs, meleeRagdollMaxMs);
                game.Peds.ApplyDamage(ped, (int)(MeleeHitPedDamage * powerLevel / 100.0f));
            }
            game.Entities.ApplyForce(ent, direction * MeleeHitForce * powerLevel / 100.0f);
            game.Gameplay.ClearLastDamage(ent);
        }

        public void PlayPlayerAnimation(int ped, string dictName, string animName, AnimationFlags flag, int duration = -1, bool checkIsPlaying = true)
        {
            if (checkIsPlaying && IsEntityPlayingAnim(ped, dictName, animName))
            {
                return;
            }
            game.Animations.RequestDictionary(dictName);
            int started = game.Host.GameTime;
            while (!game.Animations.IsDictionaryLoaded(dictName))
            {
                if (unchecked(game.Host.GameTime - started) >= animationLoadTimeoutMs)
                {
                    log.Throttled("WARN", "animation.timeout." + dictName, 10000, "dictionary", dictName, "animation", animName);
                    return;
                }
                game.Host.Yield();
            }

            game.Animations.Play(ped, dictName, animName, flag, duration,
                animationBlendInSpeed, animationBlendOutSpeed, animationPlaybackRate);
        }

        public bool IsEntityPlayingAnim(int ent, string dictName, string animName)
        {
            return game.Animations.IsPlaying(ent, dictName, animName);
        }

        public int CreateWeaponObject(WeaponHash weaponHash, int amountCount, Vector3 position, bool showWorldModel = true, float scale = 1.0f)
        {
            log.Event("INFO", "asset.weapon.request", "weapon", weaponHash.ToString(), "position", position.ToString(), "scale", scale);
            if (!game.Weapons.RequestAsset(weaponHash, weaponAssetLoadTimeoutMs))
                throw new System.InvalidOperationException("Could not load weapon asset " + weaponHash);
            var prop = game.Gameplay.CreateWeaponObject(weaponHash, amountCount, position, showWorldModel, scale);
            log.Event(prop == 0 ? "ERROR" : "INFO", "asset.weapon.created", "weapon", weaponHash.ToString(), "handle", prop);
            return prop;
        }

        public bool IsPed(int entity)
        {
            return game.Gameplay.IsPed(entity);
        }

        public bool IsVehicle(int entity)
        {
            return game.Gameplay.IsVehicle(entity);
        }

        private bool BeforePlayingParticleFx(string effectSetName)
        {
            game.Particles.RequestAsset(effectSetName);
            if (!game.Particles.IsAssetLoaded(effectSetName)) return false;
            game.Particles.UseAsset(effectSetName);
            return true;
        }

        public void PlayParticleFx(string effectSetName, string effect, int entity, float scale = 1.0f)
        {
            if (!BeforePlayingParticleFx(effectSetName)) return;
            game.Particles.PlayOnEntity(effect, entity, Vector3.Zero, Vector3.Zero, scale);
        }

        public void PlayParticleFx(string effectSetName, string effect, int ped, Bone boneId, float scale = 1.0f)
        {
            if (!BeforePlayingParticleFx(effectSetName)) return;
            game.Particles.PlayOnPedBone(effect, ped, boneId, Vector3.Zero, Vector3.Zero, scale);
        }

        public void PlayParticleFx(string effectSetName, string effect, Vector3 pos, Vector3 rot, float scale = 1.0f)
        {
            if (!BeforePlayingParticleFx(effectSetName)) return;
            game.Particles.PlayAt(effect, pos, rot, scale);
        }

        public void SetObjectPhysicsParams(
            int entity,
            float mass,
            float gravity = -1,
            float dragCoefficient1 = 0.0f,
            float dragCoefficient2 = 0.0f,
            float dragCoefficient3 = 0.0f,
            float rotationDragCoefficient1 = 0.0f,
            float rotationDragCoefficient2 = 0.0f,
            float rotationDragCoefficient3 = 0.0f)
        {
            game.Gameplay.SetObjectPhysics(entity, mass, gravity,
                new Vector3(dragCoefficient1, dragCoefficient2, dragCoefficient3),
                new Vector3(rotationDragCoefficient1, rotationDragCoefficient2, rotationDragCoefficient3));
        }

        public void SetPedToRagdoll(int ped, RagdollType ragdollType, int timeToStayInRagdoll, int timeToStandUp)
        {
            game.Peds.SetCanRagdoll(ped, true);
            game.Gameplay.SetRagdoll(ped, (GTA.RagdollType)ragdollType, timeToStayInRagdoll, timeToStandUp);
        }

        public static IntersectFlags IntersectAllObjects
        {
            get
            {
                return IntersectFlags.Objects | IntersectFlags.Peds | IntersectFlags.Vehicles | IntersectFlags.Map;
            }
        }

        public string[] AnimationDictNames { get; private set; }
        public string[] AnimationNames { get; private set; }
        public AnimationWaitTimeDict AnimationWaitTime { get; private set; }
        public bool[] AnimationWithAngles { get; private set; }
        public bool[] AnimationWithAnglesAndIncompletePlusOrMinusSign { get; private set; }
        public string[] ParticleEffectSetNames { get; private set; }
        public string[] ParticleEffectNames { get; private set; }
        public float MeleeHitForce { get; private set; }
        public int MeleeHitPedDamage { get; private set; }
    }

    public static class AnimationTiming
    {
        public static int GetDelay(Dictionary<string, Dictionary<string, int>> timings, string dictionary, string animation)
        {
            return timings.TryGetValue(dictionary, out var clips) && clips.TryGetValue(animation, out int milliseconds)
                ? milliseconds : 0;
        }
    }
}
