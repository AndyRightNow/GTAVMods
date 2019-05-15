using GTA;
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
        public NativeHelper(
            string[] animationDictNames,
            string[] animationNames,
            AnimationWaitTimeDict animationWaitTime,
            bool[] animationWithAngles,
            string[] particleEffectSetNames,
            string[] particleEffectNames,
            int meleeHitPedDamage,
            float meleeHitForce
        )
        {
            AnimationDictNames = animationDictNames;
            AnimationNames = animationNames;
            AnimationWaitTime = animationWaitTime;
            AnimationWithAngles = animationWithAngles;
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

        public void ApplyForcesAndDamages(Entity ent, Vector3 direction)
        {
            if (IsPed(ent) && ent != Game.Player.Character)
            {
                var ped = (Ped)ent;
                SetPedToRagdoll(ped, RagdollType.Normal, 100, 100);
                ped.ApplyDamage(MeleeHitPedDamage);
            }
            ent.ApplyForce(direction * MeleeHitForce);
            Function.Call(Hash.CLEAR_ENTITY_LAST_DAMAGE_ENTITY, ent);
        }

        public static void ClearPlayerAnimation(Ped ped, string dictName, string animName)
        {
            Function.Call(Hash.STOP_ANIM_TASK, ped, dictName, animName, 3);
        }

        public static Bone GetLastDamagedBone(Ped ped)
        {
            int outBone;

            unsafe
            {
                if (Function.Call<bool>(Hash.GET_PED_LAST_DAMAGE_BONE, ped.Handle, &outBone))
                {
                    return (Bone) outBone;
                }
            }

            return Bone.IK_Root;
        }

        public static void PlayPlayerAnimation(Ped ped, string dictName, string animName, AnimationFlags flag, int duration = -1, bool checkIsPlaying = true)
        {
            if (checkIsPlaying && IsEntityPlayingAnim(ped, dictName, animName))
            {
                return;
            }
            Function.Call(Hash.REQUEST_ANIM_DICT, dictName);
            if (!Function.Call<bool>(Hash.HAS_ANIM_DICT_LOADED, dictName))
            {
                Function.Call(Hash.REQUEST_ANIM_DICT, dictName);
            }

            Function.Call(Hash.TASK_PLAY_ANIM, ped, dictName, animName, 8.0f, 1.0f, duration, (int)flag, -8.0f, 0, 0, 0);
        }

        public static bool IsEntityPlayingAnim(Entity ent, string dictName, string animName)
        {
            Function.Call(Hash.REQUEST_ANIM_DICT, dictName);
            if (!Function.Call<bool>(Hash.HAS_ANIM_DICT_LOADED, dictName))
            {
                Function.Call(Hash.REQUEST_ANIM_DICT, dictName);
            }
            return Function.Call<bool>(Hash.IS_ENTITY_PLAYING_ANIM, ent, dictName, animName, 3);
        }

        public static void SetEntityVelocity(InputArgument entity, Vector3 velocity)
        {
            Function.Call(Hash.SET_ENTITY_VELOCITY, entity, velocity.X, velocity.Y, velocity.Z);
        }

        public static Entity CreateWeaponObject(WeaponHash weaponHash, int amountCount, Vector3 position, bool showWorldModel = true, float heading = 1.0f)
        {
            new WeaponAsset(weaponHash).Request(3000);

            return Function.Call<Entity>(Hash.CREATE_WEAPON_OBJECT, (int)weaponHash, amountCount, position.X, position.Y, position.Z, showWorldModel, heading);
        }

        public static void DrawLine(Vector3 start, Vector3 end, Color color)
        {
            Function.Call(Hash.DRAW_LINE, start.X, start.Y, start.Z, end.X, end.Y, end.Z, color.R, color.G, color.B, color.A);
        }

        public static bool IsPed(Entity entity)
        {
            return Function.Call<bool>(Hash.IS_ENTITY_A_PED, entity);
        }

        public static bool IsVehicle(Entity entity)
        {
            return Function.Call<bool>(Hash.IS_ENTITY_A_VEHICLE, entity);
        }

        public static void SetPedWeaponVisible(Ped ped, bool visible)
        {
            Function.Call(Hash.SET_PED_CURRENT_WEAPON_VISIBLE, ped, visible, 0, 0, 0);
        }

        private static void BeforePlayingParticleFx(string effectSetName)
        {
            Function.Call(Hash.REQUEST_NAMED_PTFX_ASSET, effectSetName);
            Function.Call(Hash._SET_PTFX_ASSET_NEXT_CALL, effectSetName);
        }

        public static void PlayParticleFx(string effectSetName, string effect, Entity entity, float scale = 1.0f)
        {
            BeforePlayingParticleFx(effectSetName);
            Function.Call(Hash.START_PARTICLE_FX_NON_LOOPED_ON_ENTITY, effect, entity, 0.0f, 0.0f, 0.0f, 0.0f, 0.0f, 0.0f, scale, 0, 0, 0);
        }

        public static void PlayParticleFx(string effectSetName, string effect, Entity entity, Vector3 pos, float scale = 1.0f)
        {
            BeforePlayingParticleFx(effectSetName);
            Function.Call(Hash.START_PARTICLE_FX_NON_LOOPED_ON_ENTITY, effect, entity, pos.X, pos.Y, pos.Z, 0.0f, 0.0f, 0.0f, scale, 0, 0, 0);
        }

        public static void PlayParticleFx(string effectSetName, string effect, Ped ped, Bone boneId, float scale = 1.0f)
        {
            BeforePlayingParticleFx(effectSetName);
            Function.Call(Hash.START_PARTICLE_FX_NON_LOOPED_ON_PED_BONE, effect, ped, 0.0f, 0.0f, 0.0f, 0.0f, 0.0f, 0.0f, (int)boneId, scale, 0, 0, 0);
        }

        public static void PlayParticleFx(string effectSetName, string effect, Vector3 pos, Vector3 rot, float scale = 1.0f)
        {
            BeforePlayingParticleFx(effectSetName);
            Function.Call(Hash.START_PARTICLE_FX_NON_LOOPED_AT_COORD, effect, pos.X, pos.Y, pos.Z, rot.X, rot.Y, rot.Z, scale, 0, 0, 0);
        }

        public static int PlayParticleFxLooped(string effectSetName, string effect, Ped ped, Bone boneId, float scale = 1.0f)
        {
            BeforePlayingParticleFx(effectSetName);
            return Function.Call<int>(Hash.START_PARTICLE_FX_LOOPED_ON_PED_BONE, effect, ped, 0.0f, 0.0f, 0.0f, 0.0f, 0.0f, 0.0f, (int)boneId, scale, 0, 0, 0);
        }

        public static int PlayParticleFxLooped(string effectSetName, string effect, Vector3 pos, Vector3 rot, float scale = 1.0f)
        {
            BeforePlayingParticleFx(effectSetName);
            return Function.Call<int>(Hash.START_PARTICLE_FX_LOOPED_AT_COORD, effect, pos.X, pos.Y, pos.Z, rot.X, rot.Y, rot.Z, scale, 0, 0, 0);
        }

        public static void SetObjectPhysicsParams(
            Entity entity,
            float mass,
            float gravity = -1,
            float dragCoefficient1 = 0.0f,
            float dragCoefficient2 = 0.0f,
            float dragCoefficient3 = 0.0f,
            float rotationDragCoefficient1 = 0.0f,
            float rotationDragCoefficient2 = 0.0f,
            float rotationDragCoefficient3 = 0.0f)
        {
            Function.Call(
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

        public static void SetPedToRagdoll(Ped ped, RagdollType ragdollType, int timeToStayInRagdoll, int timeToStandUp)
        {
            ped.CanRagdoll = true;
            Function.Call(Hash.SET_PED_TO_RAGDOLL, ped, timeToStayInRagdoll, timeToStandUp, (int)ragdollType, 0, 0, 0);
        }

        public static void DrawLines(ref List<Line> lines)
        {
            foreach (var line in lines)
            {
                line.Draw();
            }
        }

        public static void DrawBox(Vector3 a, Vector3 b, Color col)
        {
            Function.Call(Hash.DRAW_BOX, a.X, a.Y, a.Z, b.X, b.Y, b.Z, col.R, col.G, col.B, col.A);
        }

        public static IntersectOptions IntersectAllObjects
        {
            get
            {
                return (IntersectOptions)(2 | 4 | 8 | 16);
            }
        }

        public string[] AnimationDictNames { get; private set; }
        public string[] AnimationNames { get; private set; }
        public AnimationWaitTimeDict AnimationWaitTime { get; private set; }
        public bool[] AnimationWithAngles { get; private set; }
        public string[] ParticleEffectSetNames { get; private set; }
        public string[] ParticleEffectNames { get; private set; }
        public float MeleeHitForce { get; private set; }
        public int MeleeHitPedDamage { get; private set; }
    }
}
