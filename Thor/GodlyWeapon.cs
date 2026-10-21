using System;
using System.Collections.Generic;
using System.Linq;
using ADModUtils;
using GTA;
using GTA.Math;
using GTAVMods.Game;

namespace Thor
{
    public abstract class GodlyWeapon
    {
        protected readonly GameplayTuning tuning;
        protected readonly GameServices game;
        protected readonly NativeHelper effects;
        protected readonly Random random;
        protected readonly IEventLog log;
        private readonly PathFinder.PathFinder pathfinder;
        private readonly WeaponCamera camera;
        private readonly Thunder thunder;
        protected int weaponObject;
        private readonly HashSet<int> retiringWeapons = new HashSet<int>();
        private readonly WeaponHash weaponHash;
        private Utilities.Timer weaponFxTimer;
        protected bool isBeingSummoned, isCloseToSummoningPed;
        private bool previouslyObstructed, idleOutOfRange;
        private Vector3 idleVelocity;
        private int idleInterval;
        private int idleVelocityStartedAt;
        private float previousHeight;
        protected AnimationActions[] throwActions;

        protected GodlyWeapon(WeaponHash weapon, GameServices game, NativeHelper effects,
            Random random, PathFinder.PathFinder pathfinder, WeaponCamera camera, Thunder thunder, IEventLog log, GameplayTuning tuning)
        {
            weaponHash = weapon;
            this.game = game;
            this.effects = effects;
            this.random = random;
            this.pathfinder = pathfinder;
            this.camera = camera;
            this.thunder = thunder;
            this.log = log;
            this.tuning = tuning;
            idleInterval = tuning.Hammer.IdleIntervalMs;
        }

        public virtual void OnTick()
        {
            if (weaponObject != 0 && !game.Entities.Exists(weaponObject)) WeaponObject = 0;
            pathfinder.OnTick(weaponObject);
            weaponFxTimer?.OnTick();
            if (idleOutOfRange && !isBeingSummoned)
            {
                if (unchecked((uint)(game.Host.GameTime - idleVelocityStartedAt)) >= (uint)idleInterval)
                {
                    idleVelocityStartedAt = game.Host.GameTime;
                    idleInterval = (int)(Utilities.Random.NextFloat(random, false) * tuning.Hammer.IdleIntervalJitterMs) + tuning.Hammer.IdleIntervalMs;
                    idleVelocity = new Vector3(Utilities.Random.NextFloat(random), Utilities.Random.NextFloat(random),
                        Utilities.Random.NextFloat(random)).Normalized * Utilities.Random.NextFloat(random, false) * tuning.Hammer.IdleMaxSpeed;
                }
                game.Entities.SetVelocity(weaponObject, Vector3.Lerp(game.Entities.Velocity(weaponObject),
                    idleVelocity, tuning.Hammer.IdleVelocityBlend * Utilities.Random.NextFloat(random, false)));
            }
        }

        public AnimationActions[] ThrowActions => throwActions;
        public WeaponHash WeaponHash => weaponHash;

        protected void HandleWeaponAvailability()
        {
            if (weaponObject == 0 || !game.Entities.Exists(weaponObject)) return;
            game.Entities.SetPersistent(weaponObject, true);
            game.Gameplay.SetFreezeWhileWaitingForCollision(weaponObject, false);
            if (game.Entities.HeightAboveGround(weaponObject) - previousHeight > tuning.Hammer.RecoveryHeightJump)
            {
                if (!isBeingSummoned && !idleOutOfRange)
                {
                    idleOutOfRange = true;
                    idleVelocityStartedAt = game.Host.GameTime;
                }
            }
            else previousHeight = game.Entities.HeightAboveGround(weaponObject);
            if (game.World.Raycast(game.Entities.Position(weaponObject), Vector3.UnitZ * tuning.Hammer.TerrainProbeEndZ, IntersectFlags.Map).DidHit)
                idleOutOfRange = false;
        }

        public void RotateToDirection(int weapon, Vector3 direction)
            => game.Entities.SetRotation(weapon, Utilities.Math.DirectionToRotation(direction) + new Vector3(-90, 0, 0));

        public int WeaponObject
        {
            get => weaponObject;
            set
            {
                bool changed = weaponObject != value;
                if (!changed && retiringWeapons.Count == 0) return;
                if (changed)
                {
                    if (weaponObject != 0) retiringWeapons.Add(weaponObject);
                    // Own the incoming handle before any fallible game configuration.
                    weaponObject = value;
                    retiringWeapons.Remove(value);
                    weaponFxTimer = null;
                    idleOutOfRange = false;
                    idleVelocity = Vector3.Zero;
                    idleInterval = tuning.Hammer.IdleIntervalMs;
                    previousHeight = 0;
                    previouslyObstructed = false;
                    isBeingSummoned = false;
                    isCloseToSummoningPed = false;
                }
                Cleanup.Run(ReleaseRetiringWeapons, () =>
                {
                    if (changed && value != 0) ConfigureWeapon(value);
                });
            }
        }

        private void ReleaseRetiringWeapons()
        {
            Cleanup.Run(retiringWeapons.Select(handle => new Action(() => ReleaseRetiringWeapon(handle))).ToArray());
        }

        private void ReleaseRetiringWeapon(int handle)
        {
            if (game.Entities.Exists(handle))
                Cleanup.Run(
                    () => game.Entities.Detach(handle),
                    () =>
                    {
                        game.Entities.Delete(handle);
                        retiringWeapons.Remove(handle);
                    });
            else retiringWeapons.Remove(handle);
        }

        private void ConfigureWeapon(int value)
        {
            game.Gameplay.ActivatePhysics(value);
            game.Entities.SetCollisionEnabled(value, true);
            effects.Operations.SetObjectPhysicsParams(value, tuning.Hammer.Mass);
            int blip = game.Entities.AddBlip(value);
            if (blip != 0)
            {
                game.Entities.SetBlipFriendly(blip, true);
                game.Entities.SetBlipName(blip, "GodlyWeapon");
            }
            pathfinder.SetNoCollision(value);
            weaponFxTimer = new Utilities.Timer(tuning.Hammer.LightningIntervalMs, () => effects.PlayThunderFx(weaponObject), () => game.Host.GameTime);
        }

        public Vector3 Position => weaponObject == 0 ? Vector3.Zero :
            game.Gameplay.Position(weaponObject);

        public void SetSummonStatus(bool summoning, bool closeToPed = false)
        {
            isBeingSummoned = summoning;
            isCloseToSummoningPed = closeToPed;
        }

        public void ShowParticleFx()
        {
            effects.Operations.PlayParticleFx("scr_familyscenem", "scr_meth_pipe_smoke", weaponObject);
            Vector3 direction = -game.Entities.Velocity(weaponObject).Normalized;
            float speed = game.Gameplay.Speed(weaponObject);
            Vector3 position = game.Entities.Position(weaponObject);
            thunder.RenderTrail(position, position + direction * speed * tuning.Hammer.TrailDurationSeconds);
        }

        protected abstract bool ShouldApplyForcesToNearbyEntities();

        public void ApplyForcesToNearbyEntities()
        {
            if (weaponObject == 0 || !game.Entities.Exists(weaponObject) || !IsMoving || !ShouldApplyForcesToNearbyEntities()) return;
            foreach (int entity in game.World.NearbyEntities(game.Entities.Position(weaponObject), tuning.Hammer.ImpactRadius))
            {
                if (entity == 0 || entity == game.Host.PlayerPed || entity == weaponObject || pathfinder.IsRelatedEntity(entity)) continue;
                game.Entities.ApplyForce(entity, game.Entities.Velocity(weaponObject));
                if (effects.Operations.IsPed(entity)) game.Peds.ApplyDamage(entity, tuning.Hammer.ImpactDamage);
            }
        }

        public void DestroyWeaponTrackCam() => camera.DestroyCamera();
        public void RenderWeaponTrackCam() => camera.RenderCamera(weaponObject);
        public void CancelRenderWeaponTrackCam() => camera.CancelRenderedCamera();

        public void Init()
        {
            if (weaponObject != 0) return;
            try
            {
                WeaponObject = effects.Operations.CreateWeaponObject(weaponHash, 1, tuning.Hammer.SpawnPosition);
                if (WeaponObject == 0) throw new InvalidOperationException("The hammer weapon asset could not be created.");
            }
            catch (Exception error)
            {
                game.Host.Notify("~r~Error occured when initializing GodlyWeapon. Please see the log file for more imformation.");
                log.Event("ERROR", "hammer.createFailed", "exception", error.ToString());
                throw;
            }
        }

        public void MoveToTargets(HashSet<int> targets)
        {
            int target = targets.First();
            if (!game.Entities.Exists(target))
            {
                targets.Remove(target);
                return;
            }
            float distance = (game.Entities.Position(target) - Position).Length();
            if (game.Entities.Position(target) != Vector3.Zero &&
                ((effects.Operations.IsPed(target) && distance <= tuning.Hammer.PedTargetReachDistance) ||
                (effects.Operations.IsVehicle(target) && distance <= tuning.Hammer.VehicleTargetReachDistance)))
            {
                targets.Remove(target);
                log.Event("INFO", "hammer.targetReached", "target", target, "remaining", targets.Count);
            }
            else FindWaysToMoveToCoord(game.Entities.Position(target), false);
        }

        public bool IsMoving => weaponObject != 0 && game.Entities.Exists(weaponObject) && game.Entities.Velocity(weaponObject).Length() > 0;

        private float SpeedForDistance(float distance)
        {
            if (distance <= tuning.Hammer.NearRecallDistance) return tuning.Hammer.NearRecallSpeed;
            if (distance <= tuning.Hammer.MediumRecallDistance) return tuning.Hammer.MediumRecallSpeed;
            return tuning.Hammer.CruiseSpeed;
        }

        public void FindWaysToMoveToCoord(Vector3 position, bool slowDownIfClose)
        {
            if (weaponObject == 0) return;
            bool blocked = HasObstaclesInBetween(position);
            if (blocked != previouslyObstructed)
                log.Throttled("INFO", "hammer.pathObstruction", 500, "blocked", blocked, "target", position.ToString());
            if (blocked && !IsInOpenAir())
            {
                if (!previouslyObstructed) pathfinder.UpdateStartPosition(game.Entities.Position(weaponObject));
                previouslyObstructed = blocked;
                pathfinder.UpdateCurrentTarget(position);
                game.Entities.SetVelocity(weaponObject, pathfinder.GetTargetVelocity(game.Entities.Position(weaponObject)));
                return;
            }
            previouslyObstructed = blocked;
            Vector3 difference = position - Position;
            float speed = slowDownIfClose ? SpeedForDistance(difference.Length()) : tuning.Hammer.CruiseSpeed;
            game.Entities.SetVelocity(weaponObject, difference.Normalized * speed);
        }

        private bool HasObstaclesInBetween(Vector3 position)
        {
            Vector3 current = game.Entities.Position(weaponObject);
            var hit = game.World.Capsule(current, position, tuning.Hammer.ObstacleProbeRadius, IntersectFlags.Map);
            return hit.DidHit && current.DistanceTo(hit.Position) <= tuning.Hammer.ObstacleDistance;
        }

        private bool IsInOpenAir()
        {
            Vector3 current = game.Entities.Position(weaponObject);
            float radius = tuning.Hammer.OpenAirProbeRadius;
            var x = game.World.Capsule(current - Vector3.UnitX * radius, current + Vector3.UnitX * radius, radius, IntersectFlags.Map);
            var y = game.World.Capsule(current - Vector3.UnitY * radius, current + Vector3.UnitY * radius, radius, IntersectFlags.Map);
            var z = game.World.Capsule(current - Vector3.UnitZ * radius, current + Vector3.UnitZ * radius, radius, IntersectFlags.Map);
            return !x.DidHit && !y.DidHit && !z.DidHit;
        }
    }
}
