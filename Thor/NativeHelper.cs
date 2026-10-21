using GTA;
using GTAVMods.Game;
using GTA.Math;
using System.Collections.Generic;

namespace Thor
{
    public enum AnimationActions
    {
        CallingForWeapon = 0,
        ThrowHammer1,
        ThrowHammer2,
        ThrowHammer3,
        ThrowHammer4,
        ThrowHammer5,
        CatchingWeapon1,
        GroundAttack1,
        GroundAttack2,
        GroundAttack3,
        GroundAttack4,
        SummonThunder,
        WhirlingHammer,
        ThrowTwoHandHammer1,
        ThrowTwoHandHammer2,
    }

    public enum ParticleEffects
    {
        Thunder
    }

    public sealed class NativeHelper
    {
        private static string[] ParticleEffectSetNames = (new List<string>
        {
            "core"
        }).ToArray();
        private static string[] ParticleEffectNames = (new List<string>
        {
            "ent_dst_elec_fire_sp"
        }).ToArray();
        public NativeHelper(GameServices game, ADModUtils.IEventLog log, GameplayTuning tuning)
        {
            Operations = new ADModUtils.NativeHelper(game,
                tuning.Animation.Dictionaries, tuning.Animation.Names, tuning.Animation.ReleaseDelays,
                tuning.Animation.HasAngles, tuning.Animation.IncompleteSigns,
                ParticleEffectSetNames, ParticleEffectNames, tuning.Combat.MeleePedDamage, tuning.Combat.MeleeForce,
                tuning.Combat.MeleeRagdollMinMs, tuning.Combat.MeleeRagdollMaxMs,
                tuning.Animation.BlendInSpeed, tuning.Animation.BlendOutSpeed, tuning.Animation.PlaybackRate,
                tuning.Animation.LoadTimeoutMs, tuning.Hammer.AssetLoadTimeoutMs, log);
        }

        public ADModUtils.NativeHelper Operations { get; }

        public string GetAnimationNameForAngle(AnimationActions action, float signedAngle)
        {
            uint actionId = (uint)action;
            return AnimationNameForAngle(Operations.GetAnimationNameByAction(actionId), signedAngle,
                Operations.DoesAnimationActionHaveAngles(actionId),
                Operations.DoesAnimationActionHaveAnglesAndIncompletePlusOrMinusSign(actionId));
        }

        public static string AnimationNameForAngle(string name, float signedAngle, bool hasAngles, bool incompleteSign)
        {
            float angle = System.Math.Abs(signedAngle);
            if (!hasAngles || angle < 45)
                return name;
            string angleName = angle < 135 ? "90" : "180";
            bool toLeft = signedAngle < 0;
            string sign = incompleteSign
                ? (angleName == "90" && toLeft ? "" : "-")
                : (toLeft ? "+" : "-");
            return name.Replace("_0", "_" + sign + angleName);
        }

        public void PlayThunderFx(int ent, float scale = 1.0f)
        {
            Operations.PlayParticleFx(Operations.GetParticleSetName((uint)ParticleEffects.Thunder), Operations.GetParticleName((uint)ParticleEffects.Thunder), ent, scale);
        }

        public void PlayThunderFx(int ped, Bone boneId, float scale = 1.0f)
        {
            Operations.PlayParticleFx(Operations.GetParticleSetName((uint)ParticleEffects.Thunder), Operations.GetParticleName((uint)ParticleEffects.Thunder), ped, boneId, scale);
        }

    }
}
