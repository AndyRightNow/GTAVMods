using GTA;
using GTAVMods.Game;
using GTA.Math;
using GTA.Native;
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
        {
            this.game = game;
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
            game.Native.Call(Hash.CLEAR_ENTITY_LAST_DAMAGE_ENTITY, ent);
        }

        public void ClearPlayerAnimation(int ped, string dictName, string animName)
        {
            game.Native.Call(Hash.STOP_ANIM_TASK, ped, dictName, animName, 3);
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
            game.Native.Call(Hash.REQUEST_ANIM_DICT, dictName);
            int started = game.Host.GameTime;
            while (!game.Native.Call<bool>(Hash.HAS_ANIM_DICT_LOADED, dictName))
            {
                if (unchecked(game.Host.GameTime - started) >= 1000)
                {
                    Logger.Throttled("WARN", "animation.timeout." + dictName, 10000, "dictionary", dictName, "animation", animName);
                    return;
                }
                game.Host.Yield();
            }

            game.Native.Call(Hash.TASK_PLAY_ANIM, ped, dictName, animName, 8.0f, 1.0f, duration, (int)flag, -8.0f, 0, 0, 0);
        }

        public bool IsEntityPlayingAnim(int ent, string dictName, string animName)
        {
            return game.Native.Call<bool>(Hash.IS_ENTITY_PLAYING_ANIM, ent, dictName, animName, 3);
        }

        public void SetEntityVelocity(int entity, Vector3 velocity)
        {
            game.Native.Call(Hash.SET_ENTITY_VELOCITY, entity, velocity.X, velocity.Y, velocity.Z);
        }

        public int CreateWeaponObject(WeaponHash weaponHash, int amountCount, Vector3 position, bool showWorldModel = true, float scale = 1.0f)
        {
            Logger.Event("INFO", "asset.weapon.request", "weapon", weaponHash.ToString(), "position", position.ToString(), "scale", scale);
            if (!game.Weapons.RequestAsset(weaponHash, 3000))
                throw new System.InvalidOperationException("Could not load weapon asset " + weaponHash);
            // Supply the complete current native signature; the seventh parameter is scale.
            var prop = game.Native.Call<int>(Hash.CREATE_WEAPON_OBJECT, (int)weaponHash, amountCount,
                position.X, position.Y, position.Z, showWorldModel, scale, false, false, 0);
            Logger.Event(prop == 0 ? "ERROR" : "INFO", "asset.weapon.created", "weapon", weaponHash.ToString(), "handle", prop);
            return prop;
        }

        public void DrawLine(Vector3 start, Vector3 end, Color color)
        {
            game.Native.Call(Hash.DRAW_LINE, start.X, start.Y, start.Z, end.X, end.Y, end.Z, color.R, color.G, color.B, color.A);
        }

        public bool IsPed(int entity)
        {
            return game.Native.Call<bool>(Hash.IS_ENTITY_A_PED, entity);
        }

        public bool IsVehicle(int entity)
        {
            return game.Native.Call<bool>(Hash.IS_ENTITY_A_VEHICLE, entity);
        }

        public void SetPedWeaponVisible(int ped, bool visible)
        {
            game.Native.Call(Hash.SET_PED_CURRENT_WEAPON_VISIBLE, ped, visible, 0, 0, 0);
        }

        private bool BeforePlayingParticleFx(string effectSetName)
        {
            game.Native.Call(Hash.REQUEST_NAMED_PTFX_ASSET, effectSetName);
            if (!game.Native.Call<bool>(Hash.HAS_NAMED_PTFX_ASSET_LOADED, effectSetName)) return false;
            game.Native.Call(Hash.USE_PARTICLE_FX_ASSET, effectSetName);
            return true;
        }

        public void PlayParticleFx(string effectSetName, string effect, int entity, float scale = 1.0f)
        {
            if (!BeforePlayingParticleFx(effectSetName)) return;
            game.Native.Call(Hash.START_PARTICLE_FX_NON_LOOPED_ON_ENTITY, effect, entity, 0.0f, 0.0f, 0.0f, 0.0f, 0.0f, 0.0f, scale, 0, 0, 0);
        }

        public void PlayParticleFx(string effectSetName, string effect, int entity, Vector3 pos, float scale = 1.0f)
        {
            if (!BeforePlayingParticleFx(effectSetName)) return;
            game.Native.Call(Hash.START_PARTICLE_FX_NON_LOOPED_ON_ENTITY, effect, entity, pos.X, pos.Y, pos.Z, 0.0f, 0.0f, 0.0f, scale, 0, 0, 0);
        }

        public void PlayParticleFx(string effectSetName, string effect, int ped, Bone boneId, float scale = 1.0f)
        {
            if (!BeforePlayingParticleFx(effectSetName)) return;
            game.Native.Call(Hash.START_PARTICLE_FX_NON_LOOPED_ON_PED_BONE, effect, ped, 0.0f, 0.0f, 0.0f, 0.0f, 0.0f, 0.0f, (int)boneId, scale, 0, 0, 0);
        }

        public void PlayParticleFx(string effectSetName, string effect, Vector3 pos, Vector3 rot, float scale = 1.0f)
        {
            if (!BeforePlayingParticleFx(effectSetName)) return;
            game.Native.Call(Hash.START_PARTICLE_FX_NON_LOOPED_AT_COORD, effect, pos.X, pos.Y, pos.Z, rot.X, rot.Y, rot.Z, scale, 0, 0, 0);
        }

        public int PlayParticleFxLooped(string effectSetName, string effect, int ped, Bone boneId, float scale = 1.0f)
        {
            if (!BeforePlayingParticleFx(effectSetName)) return 0;
            return game.Native.Call<int>(Hash.START_PARTICLE_FX_LOOPED_ON_PED_BONE, effect, ped, 0.0f, 0.0f, 0.0f, 0.0f, 0.0f, 0.0f, (int)boneId, scale, 0, 0, 0);
        }

        public int PlayParticleFxLooped(string effectSetName, string effect, Vector3 pos, Vector3 rot, float scale = 1.0f)
        {
            if (!BeforePlayingParticleFx(effectSetName)) return 0;
            return game.Native.Call<int>(Hash.START_PARTICLE_FX_LOOPED_AT_COORD, effect, pos.X, pos.Y, pos.Z, rot.X, rot.Y, rot.Z, scale, 0, 0, 0);
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
            game.Native.Call(
                Hash.SET_OBJECT_PHYSICS_PARAMS,
                entity,
                mass,
                gravity,
                dragCoefficient1,
                dragCoefficient2,
                dragCoefficient3,
                rotationDragCoefficient1,
                rotationDragCoefficient2,
                rotationDragCoefficient3,
                0.0f
            );
        }

        public void SetPedToRagdoll(int ped, RagdollType ragdollType, int timeToStayInRagdoll, int timeToStandUp)
        {
            game.Peds.SetCanRagdoll(ped, true);
            game.Native.Call(Hash.SET_PED_TO_RAGDOLL, ped, timeToStayInRagdoll, timeToStandUp, (int)ragdollType, 0, 0, 0);
        }

        public void DrawLines(ref List<Line> lines)
        {
            foreach (var line in lines)
            {
                line.Draw(game.Native);
            }
        }

        public void DrawBox(Vector3 a, Vector3 b, Color col)
        {
            game.Native.Call(Hash.DRAW_BOX, a.X, a.Y, a.Z, b.X, b.Y, b.Z, col.R, col.G, col.B, col.A);
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
