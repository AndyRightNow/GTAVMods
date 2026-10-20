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
            float meleeHitForce
        )
            : this(game, animationDictNames, animationNames, animationWaitTime, animationWithAngles,
                animationWithAnglesAndIncompletePlusOrMinusSign, particleEffectSetNames, particleEffectNames,
                meleeHitPedDamage, meleeHitForce, Logger.Default)
        {
        }

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
        }

        public int GetAnimationWaitTimeByDictNameAndAnimName(string dictName, string animName)
        {
            if (AnimationWaitTime.ContainsKey(dictName) &&
                AnimationWaitTime[dictName].ContainsKey(animName))
            {
                return AnimationWaitTime[dictName][animName];
            }

            return 0;

        }

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
                SetPedToRagdoll(ped, RagdollType.Normal, 100, 100);
                game.Peds.ApplyDamage(ped, (int)(MeleeHitPedDamage * powerLevel / 100.0f));
            }
            game.Entities.ApplyForce(ent, direction * MeleeHitForce * powerLevel / 100.0f);
            game.Gameplay.ClearLastDamage(ent);
        }

        public void ClearPlayerAnimation(int ped, string dictName, string animName)
        {
            game.Animations.Stop(ped, dictName, animName, 3f);
        }

        public Bone GetLastDamagedBone(int ped)
        {
            return game.Outputs.LastDamageBone(ped, out int bone) ? (Bone)bone : Bone.IKRoot;
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
                if (unchecked(game.Host.GameTime - started) >= 1000)
                {
                    log.Throttled("WARN", "animation.timeout." + dictName, 10000, "dictionary", dictName, "animation", animName);
                    return;
                }
                game.Host.Yield();
            }

            game.Animations.Play(ped, dictName, animName, flag, duration, 8f, 1f, -8f);
        }

        public bool IsEntityPlayingAnim(int ent, string dictName, string animName)
        {
            return game.Animations.IsPlaying(ent, dictName, animName);
        }

        public void SetEntityVelocity(int entity, Vector3 velocity)
        {
            game.Gameplay.SetVelocity(entity, velocity);
        }

        public int CreateWeaponObject(WeaponHash weaponHash, int amountCount, Vector3 position, bool showWorldModel = true, float scale = 1.0f)
        {
            log.Event("INFO", "asset.weapon.request", "weapon", weaponHash.ToString(), "position", position.ToString(), "scale", scale);
            if (!game.Weapons.RequestAsset(weaponHash, 3000))
                throw new System.InvalidOperationException("Could not load weapon asset " + weaponHash);
            var prop = game.Gameplay.CreateWeaponObject(weaponHash, amountCount, position, showWorldModel, scale);
            log.Event(prop == 0 ? "ERROR" : "INFO", "asset.weapon.created", "weapon", weaponHash.ToString(), "handle", prop);
            return prop;
        }

        public void DrawLine(Vector3 start, Vector3 end, Color color)
        {
            game.Drawing.DrawLine(start, end, color);
        }

        public bool IsPed(int entity)
        {
            return game.Gameplay.IsPed(entity);
        }

        public bool IsVehicle(int entity)
        {
            return game.Gameplay.IsVehicle(entity);
        }

        public void SetPedWeaponVisible(int ped, bool visible)
        {
            game.Gameplay.SetWeaponVisible(ped, visible);
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

        public void PlayParticleFx(string effectSetName, string effect, int entity, Vector3 pos, float scale = 1.0f)
        {
            if (!BeforePlayingParticleFx(effectSetName)) return;
            game.Particles.PlayOnEntity(effect, entity, pos, Vector3.Zero, scale);
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

        public int PlayParticleFxLooped(string effectSetName, string effect, int ped, Bone boneId, float scale = 1.0f)
        {
            if (!BeforePlayingParticleFx(effectSetName)) return 0;
            return game.Particles.StartLoopedOnPedBone(effect, ped, boneId, Vector3.Zero, Vector3.Zero, scale);
        }

        public int PlayParticleFxLooped(string effectSetName, string effect, Vector3 pos, Vector3 rot, float scale = 1.0f)
        {
            if (!BeforePlayingParticleFx(effectSetName)) return 0;
            return game.Particles.StartLoopedAt(effect, pos, rot, scale);
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

        public void DrawLines(ref List<Line> lines)
        {
            foreach (var line in lines)
            {
                line.Draw(game.Drawing);
            }
        }

        public void DrawBox(Vector3 a, Vector3 b, Color col)
        {
            game.Drawing.DrawBox(a, b, col);
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
}
