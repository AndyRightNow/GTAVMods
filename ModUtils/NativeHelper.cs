using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using CitizenFX.Core;
using CitizenFX.Core.Native;

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
            bool[] animationWithAnglesAndIncompletePlusOrMinusSign,
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

        public static void AttachEntitiesToRope(int ropeHandle, Entity entity1, Vector3 position1, Entity entity2, Vector3 position2, float length)
        {
            Function.Call(Hash.ATTACH_ENTITIES_TO_ROPE, ropeHandle, entity1.Handle, entity2.Handle, position1.X, position1.Y, position1.Z, position2.X, position2.Y, position2.Z, length, 0, 0, 0, 0);
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

        public void ApplyForcesAndDamages(Entity ent, Vector3 direction, float powerLevel = 100.0f)
        {
            if (IsPed(ent) && ent != Game.Player.Character)
            {
                var ped = (Ped)ent;
                SetPedToRagdoll(ped, RagdollType.Normal, 100, 100);
                ped.ApplyDamage((int)(MeleeHitPedDamage * powerLevel / 100.0f));
            }
            ent.ApplyForce(direction * MeleeHitForce * powerLevel / 100.0f);
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

        public static Prop CreateWeaponObject(WeaponHash weaponHash, int amountCount, Vector3 position, bool showWorldModel = true, float heading = 1.0f)
        {
            new WeaponAsset(weaponHash).Request(3000);

            return Function.Call<Prop>(Hash.CREATE_WEAPON_OBJECT, (int)weaponHash, amountCount, position.X, position.Y, position.Z, showWorldModel, heading);
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
            Function.Call(Hash.USE_PARTICLE_FX_ASSET, effectSetName);
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
        public enum VehicleMissionType
        {
            None = 0,
            Cruise = 1,
            Ram = 2,
            Block = 3,
            GoTo = 4,
            Stop = 5,
            Attack = 6,
            Follow = 7,
            Flee = 8,
            Circle = 9,
            Escort = 12,
            GoToRacing = 14,
            FollowRecording = 15,
            PoliceBehaviour = 16,
            Land = 19,
            LandAndWait = 20,
            Crash = 21,
            PullOver = 22,
            HeliProtect = 23
        }

        /// <summary>Gives the helicopter a mission.</summary>
        /// <param name="heli">The helicopter.</param>
        /// <param name="target">The target <see cref="Vehicle"/>.</param>
        /// <param name="missionType">The vehicle mission type.</param>
        /// <param name="cruiseSpeed">The cruise speed for the task.</param>
        /// <param name="targetReachedDist">distance (in meters) at which heli thinks it's arrived. Also used as the hover distance for <see cref="VehicleMissionType.Attack"/> and <see cref="VehicleMissionType.Circle"/></param>
        /// <param name="flightHeight">The Z coordinate the heli tries to maintain (i.e. 30 == 30 meters above sea level).</param>
        /// <param name="minHeightAboveTerrain">The height in meters that the heli will try to stay above terrain (ie 20 == always tries to stay at least 20 meters above ground).</param>
        /// <param name="heliOrientation">The orientation the heli tries to be in (<c>0f</c> to <c>360f</c>). Use <c>-1f</c> if not bothered. <c>-1f</c> Should be used in 99% of the times.</param>
        /// <param name="slowDownDistance">In general, get more control with big number and more dynamic with smaller. Setting to <c>-1</c> means use default tuning(<c>100</c>).</param>
        /// <param name="missionFlags">The heli mission flags for the task.</param>
        public void StartHeliMission(Vehicle heli, Vehicle target, VehicleMissionType missionType, float cruiseSpeed, float targetReachedDist, int flightHeight, int minHeightAboveTerrain, float heliOrientation = -1f, float slowDownDistance = -1f, HeliMissionFlags missionFlags = HeliMissionFlags.None)
        {
            Function.Call(Hash.TASK_HELI_MISSION, _ped.Handle, heli.Handle, target.Handle, 0, 0f, 0f, 0f, (int)missionType, cruiseSpeed, targetReachedDist, heliOrientation, flightHeight, minHeightAboveTerrain, slowDownDistance, (int)missionFlags);
        }

        /// <summary>Gives the helicopter a mission.</summary>
        /// <param name="heli">The helicopter.</param>
        /// <param name="target">The target <see cref="Ped"/>.</param>
        /// <param name="missionType">The vehicle mission type.</param>
        /// <param name="cruiseSpeed">The cruise speed for the task.</param>
        /// <param name="targetReachedDist">distance (in meters) at which heli thinks it's arrived. Also used as the hover distance for <see cref="VehicleMissionType.Attack"/> and <see cref="VehicleMissionType.Circle"/></param>
        /// <param name="flightHeight">The Z coordinate the heli tries to maintain (i.e. 30 == 30 meters above sea level).</param>
        /// <param name="minHeightAboveTerrain">The height in meters that the heli will try to stay above terrain (ie 20 == always tries to stay at least 20 meters above ground).</param>
        /// <param name="heliOrientation">The orientation the heli tries to be in (<c>0f</c> to <c>360f</c>). Use <c>-1f</c> if not bothered. <c>-1f</c> Should be used in 99% of the times.</param>
        /// <param name="slowDownDistance">In general, get more control with big number and more dynamic with smaller. Setting to <c>-1</c> means use default tuning(<c>100</c>).</param>
        /// <param name="missionFlags">The heli mission flags for the task.</param>
        public void StartHeliMission(Vehicle heli, Ped target, VehicleMissionType missionType, float cruiseSpeed, float targetReachedDist, int flightHeight, int minHeightAboveTerrain, float heliOrientation = -1f, float slowDownDistance = -1f, HeliMissionFlags missionFlags = HeliMissionFlags.None)
        {
            Function.Call(Hash.TASK_HELI_MISSION, _ped.Handle, heli.Handle, 0, target.Handle, 0f, 0f, 0f, (int)missionType, cruiseSpeed, targetReachedDist, heliOrientation, flightHeight, minHeightAboveTerrain, slowDownDistance, (int)missionFlags);
        }

        /// <summary>Gives the helicopter a mission.</summary>
        /// <param name="heli">The helicopter.</param>
        /// <param name="target">The target coordinate.</param>
        /// <param name="missionType">The vehicle mission type.</param>
        /// <param name="cruiseSpeed">The cruise speed for the task.</param>
        /// <param name="targetReachedDist">distance (in meters) at which heli thinks it's arrived. Also used as the hover distance for <see cref="VehicleMissionType.Attack"/> and <see cref="VehicleMissionType.Circle"/></param>
        /// <param name="flightHeight">The Z coordinate the heli tries to maintain (i.e. 30 == 30 meters above sea level).</param>
        /// <param name="minHeightAboveTerrain">The height in meters that the heli will try to stay above terrain (ie 20 == always tries to stay at least 20 meters above ground).</param>
        /// <param name="heliOrientation">The orientation the heli tries to be in (<c>0f</c> to <c>360f</c>). Use <c>-1f</c> if not bothered. <c>-1f</c> Should be used in 99% of the times.</param>
        /// <param name="slowDownDistance">In general, get more control with big number and more dynamic with smaller. Setting to <c>-1</c> means use default tuning(<c>100</c>).</param>
        /// <param name="missionFlags">The heli mission flags for the task.</param>
        public void StartHeliMission(Vehicle heli, Vector3 target, VehicleMissionType missionType, float cruiseSpeed, float targetReachedDist, int flightHeight, int minHeightAboveTerrain, float heliOrientation = -1f, float slowDownDistance = -1f, HeliMissionFlags missionFlags = HeliMissionFlags.None)
        {
            Function.Call(Hash.TASK_HELI_MISSION, _ped.Handle, heli.Handle, 0, 0, target.X, target.Y, target.Z, (int)missionType, cruiseSpeed, targetReachedDist, heliOrientation, flightHeight, minHeightAboveTerrain, slowDownDistance, (int)missionFlags);
        }

        public static void DrawBox(Vector3 a, Vector3 b, Color col)
        {
            Function.Call(Hash.DRAW_BOX, a.X, a.Y, a.Z, b.X, b.Y, b.Z, col.R, col.G, col.B, col.A);
        }

        public static IntersectOptions IntersectAllObjects
        {
            get
            {
                return IntersectOptions.Objects | IntersectOptions.Peds1 | IntersectOptions.MissionEntities | IntersectOptions.Map;
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

        [StructLayout(LayoutKind.Explicit, Size = 0x18)]
        internal struct NativeVector3
        {
            [FieldOffset(0x00)]
            internal float X;
            [FieldOffset(0x08)]
            internal float Y;
            [FieldOffset(0x10)]
            internal float Z;

            internal NativeVector3(float x, float y, float z)
            {
                X = x;
                Y = y;
                Z = z;
            }

            public static implicit operator Vector3(NativeVector3 value) => new Vector3(value.X, value.Y, value.Z);
            public static implicit operator NativeVector3(Vector3 value) => new NativeVector3(value.X, value.Y, value.Z);
        }
    }
}
