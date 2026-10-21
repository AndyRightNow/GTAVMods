using System.Drawing;
using GTA;
using GTA.Math;
using GTA.UI;

namespace GTAVMods.Game
{
    /// <summary>Typed native gameplay commands and current engine observations.</summary>
    /// <remarks>
    /// Commands acknowledge invocation, not completed movement, animation, physics or ownership transfer.
    /// The caller checks entity lifetime and owns retries; engine exceptions propagate unchanged.
    /// Entity and player handles are distinct identities. Zero denotes no entity.
    /// Positions and ranges use metres; velocity and speed use metres per second.
    /// Multipliers, native physics coefficients and animation flags are forwarded without clamping.
    /// </remarks>
    public interface IGameplayAccess
    {
        bool IsPed(int entity);
        bool IsVehicle(int entity);
        Vector3 Position(int entity);
        float Speed(int entity); // metres per second
        void ClearLastDamage(int entity);
        void SetFreezeWhileWaitingForCollision(int entity, bool freeze);
        void ActivatePhysics(int entity);
        /// <summary>Sets native mass, gravity, linear/angular damping and bound parameters.</summary>
        /// <remarks>
        /// Each damping vector holds constant, velocity and squared-velocity coefficients in XYZ slots;
        /// its components are not spatial axes. The final three values use native semantics.
        /// </remarks>
        void SetObjectPhysics(int entity, float mass, float gravity,
            Vector3 dragCoefficients, Vector3 rotationDragCoefficients,
            float collisionMargin = 0f, float maximumAngularSpeed = 0f, float buoyancyFactor = 0f);
        /// <summary>Creates a weapon object from an already loaded asset.</summary>
        /// <returns>Zero on creation failure; otherwise a handle the caller must release.</returns>
        int CreateWeaponObject(WeaponHash weapon, int ammo, Vector3 position, bool showWorldModel, float scale);
        void GiveWeaponObjectToPed(int weaponObject, int ped);
        /// <summary>Drops the ped's current weapon and returns its object handle.</summary>
        /// <remarks>
        /// This is a state-changing command, not an observation. The unarmed result is engine-dependent.
        /// The caller validates zero/existence and takes ownership of any resulting dropped object.
        /// </remarks>
        int DropWeaponObjectFromPed(int ped);
        /// <summary>Requests ragdoll for the given native duration bounds, in milliseconds.</summary>
        /// <remarks>The engine may decline. Does not enable CanRagdoll or confirm the resulting state.</remarks>
        void SetRagdoll(int ped, RagdollType type, int minimumMilliseconds, int maximumMilliseconds);
        void SetLockOnRange(int player, float metres);
        void SetMoveRate(int ped, float multiplier);
        void SetSwimRate(int player, float multiplier);
        void SuperJumpThisFrame(int player);
        void SetCanBeDamaged(int entity, bool value);
        void DisablePainAudio(int ped, bool disabled);
        void ShowHudComponentThisFrame(HudComponent component);
        void LightningFlash();
    }

    /// <summary>Animation streaming requests, readiness observations and task commands.</summary>
    /// <remarks>
    /// RequestDictionary does not imply readiness; IsDictionaryLoaded reports the engine's current state.
    /// Callers own waiting, timeouts and any retry. Play requires a loaded dictionary and does not confirm
    /// that the task began. Durations use milliseconds; -1 requests indefinite playback.
    /// Native blend speeds, playback rate and flags are forwarded unchanged. Errors propagate unchanged.
    /// </remarks>
    public interface IAnimationAccess
    {
        void RequestDictionary(string dictionary);
        bool IsDictionaryLoaded(string dictionary);
        bool IsPlaying(int entity, string dictionary, string animation);
        void Play(int ped, string dictionary, string animation, AnimationFlags flags,
            int durationMilliseconds, float blendInSpeed, float blendOutSpeed, float playbackRate);
    }

    /// <summary>Particle asset observations and effect commands.</summary>
    /// <remarks>
    /// RequestAsset does not imply readiness. UseAsset selects the loaded asset for the next start call
    /// on the current script thread; callers own the request/check/select/start sequence and any timeout.
    /// Positions and offsets use metres, rotations use degrees, and scale is unitless.
    /// Starts only issue requests. Native errors propagate without retry or suppression.
    /// </remarks>
    public interface IParticleAccess
    {
        void RequestAsset(string asset);
        bool IsAssetLoaded(string asset);
        void UseAsset(string asset);
        void PlayOnEntity(string effect, int entity, Vector3 offset, Vector3 rotationDegrees, float scale);
        void PlayOnPedBone(string effect, int ped, Bone bone, Vector3 offset, Vector3 rotationDegrees, float scale);
        void PlayAt(string effect, Vector3 position, Vector3 rotationDegrees, float scale);
    }

    /// <summary>World-space drawing commands for the current frame.</summary>
    /// <remarks>
    /// Coordinates use metres. Color RGBA bytes are forwarded without normalization.
    /// Commands do not promise visibility, persist geometry, or transfer resource ownership.
    /// </remarks>
    public interface IDrawingAccess
    {
        void DrawLine(Vector3 start, Vector3 end, Color color);
        void DrawBox(Vector3 minimum, Vector3 maximum, Color color);
        void DrawPoly(Vector3 first, Vector3 second, Vector3 third, Color color);
    }
}
