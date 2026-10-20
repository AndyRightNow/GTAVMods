using System;
using System.Collections.Generic;
using System.Linq;
using ADModUtils;
using GTA;
using GTA.Math;
using GTA.Native;
using GTAVMods.Game;

namespace Thor
{
    public abstract class GodlyWeapon
    {
        protected const float HAMMER_ROTATION_UPWARD_LERP_RATIO = 0.4f;
        protected const float WEAPON_MASS = 50000;
        protected readonly GameServices game;
        protected readonly NativeHelper effects;
        protected readonly Random random;
        private readonly PathFinder.PathFinder pathfinder;
        private readonly WeaponCamera camera;
        private readonly Thunder thunder;
        protected int weaponObject;
        private readonly HashSet<int> retiringWeapons = new HashSet<int>();
        private readonly WeaponHash weaponHash;
        private readonly Vector3 spawnPosition = new Vector3(0, 0, 2000);
        private Utilities.Timer weaponFxTimer;
        protected bool isBeingSummoned, isCloseToSummoningPed;
        private bool previouslyObstructed, idleOutOfRange;
        private Vector3 idleVelocity;
        private int idleInterval = 1000;
        private float previousHeight;
        protected AnimationActions[] throwActions;

        protected GodlyWeapon(WeaponHash weapon, GameServices game, NativeHelper effects,
            Random random, PathFinder.PathFinder pathfinder, WeaponCamera camera, Thunder thunder)
        {
            weaponHash = weapon;
            this.game = game;
            this.effects = effects;
            this.random = random;
            this.pathfinder = pathfinder;
            this.camera = camera;
            this.thunder = thunder;
        }

        public virtual void OnTick()
        {
            if (weaponObject != 0 && !game.Entities.Exists(weaponObject)) WeaponObject = 0;
            pathfinder.OnTick(weaponObject);
            weaponFxTimer?.OnTick();
            if (idleOutOfRange && !isBeingSummoned)
            {
                if (game.Host.GameTime % idleInterval == 0)
                {
                    idleInterval = (int)Utilities.Random.NextFloat(random, false) * 1000 + 1000;
                    idleVelocity = new Vector3(Utilities.Random.NextFloat(random), Utilities.Random.NextFloat(random),
                        Utilities.Random.NextFloat(random)).Normalized * Utilities.Random.NextFloat(random, false) * 30;
                }
                game.Entities.SetVelocity(weaponObject, Vector3.Lerp(game.Entities.Velocity(weaponObject),
                    idleVelocity, 0.5f * Utilities.Random.NextFloat(random, false)));
            }
        }

        public AnimationActions[] ThrowActions => throwActions;
        public WeaponHash WeaponHash => weaponHash;

        protected void HandleWeaponAvailability()
        {
            if (weaponObject == 0 || !game.Entities.Exists(weaponObject)) return;
            game.Entities.SetPersistent(weaponObject, true);
            game.Native.Call(Hash.SET_ENTITY_SHOULD_FREEZE_WAITING_ON_COLLISION, weaponObject, false);
            if (game.Entities.HeightAboveGround(weaponObject) - previousHeight > 50)
            {
                if (!isBeingSummoned) idleOutOfRange = true;
            }
            else previousHeight = game.Entities.HeightAboveGround(weaponObject);
            if (game.World.Raycast(game.Entities.Position(weaponObject), Vector3.UnitZ * -1000, IntersectFlags.Map).DidHit)
                idleOutOfRange = false;
        }

        public virtual Vector3 Velocity
        {
            get => weaponObject == 0 ? Vector3.Zero : game.Entities.Velocity(weaponObject);
            set => game.Entities.SetVelocity(weaponObject, value);
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
                    idleInterval = 1000;
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
            game.Native.Call(Hash.ACTIVATE_PHYSICS, value);
            game.Entities.SetCollisionEnabled(value, true);
            effects.Operations.SetObjectPhysicsParams(value, WEAPON_MASS);
            int blip = game.Entities.AddBlip(value);
            game.Entities.SetBlipFriendly(blip, true);
            game.Entities.SetBlipName(blip, "GodlyWeapon");
            pathfinder.SetNoCollision(value);
            weaponFxTimer = new Utilities.Timer(1000, () => effects.PlayThunderFx(weaponObject), () => game.Host.GameTime);
        }

        public Vector3 Position => weaponObject == 0 ? Vector3.Zero :
            game.Native.Call<Vector3>(Hash.GET_ENTITY_COORDS, weaponObject);

        public void SetSummonStatus(bool summoning, bool closeToPed = false)
        {
            isBeingSummoned = summoning;
            isCloseToSummoningPed = closeToPed;
        }

        public void ShowParticleFx()
        {
            if (!IsMoving) return;
            effects.Operations.PlayParticleFx("scr_familyscenem", "scr_meth_pipe_smoke", weaponObject);
            Vector3 direction = -game.Entities.Velocity(weaponObject).Normalized;
            float speed = game.Native.Call<float>(Hash.GET_ENTITY_SPEED, weaponObject);
            Vector3 position = game.Entities.Position(weaponObject);
            thunder.Shoot(position, position + direction * speed * 0.08f, -1, -1, false);
        }

        protected abstract bool ShouldApplyForcesToNearbyEntities();

        public void ApplyForcesToNearbyEntities()
        {
            if (weaponObject == 0 || !game.Entities.Exists(weaponObject) || !IsMoving || !ShouldApplyForcesToNearbyEntities()) return;
            foreach (int entity in game.World.NearbyEntities(game.Entities.Position(weaponObject), 1))
            {
                if (entity == game.Host.PlayerPed || entity == weaponObject || pathfinder.IsRelatedEntity(entity)) continue;
                game.Entities.ApplyForce(entity, game.Entities.Velocity(weaponObject));
                if (effects.Operations.IsPed(entity)) game.Peds.ApplyDamage(entity, 100);
            }
        }

        public void DestroyWeaponTrackCam() => camera.DestroyCamera();
        public void RenderWeaponTrackCam() => camera.RenderCamera(weaponObject);
        public void CancelRenderWeaponTrackCam() => camera.CancelRenderedCamera();

        public void Init(Vector3? spawnPos, bool forceReplacingOld = false)
        {
            if (!forceReplacingOld && weaponObject != 0) return;
            try
            {
                WeaponObject = effects.Operations.CreateWeaponObject(weaponHash, 1, spawnPos ?? spawnPosition);
                if (WeaponObject == 0) throw new InvalidOperationException("The hammer weapon asset could not be created.");
            }
            catch (Exception error)
            {
                game.Host.Notify("~r~Error occured when initializing GodlyWeapon. Please see the log file for more imformation.");
                Logger.Log("ERROR", error.ToString());
                throw;
            }
        }

        public bool MoveToTargets(HashSet<int> targets)
        {
            if (weaponObject == 0) return false;
            if (targets.Count == 0)
            {
                game.Entities.SetVelocity(weaponObject, Vector3.Zero);
                return false;
            }
            int target = targets.First();
            if (!game.Entities.Exists(target))
            {
                targets.Remove(target);
                return true;
            }
            float distance = (game.Entities.Position(target) - Position).Length();
            if (game.Entities.Position(target) != Vector3.Zero &&
                ((effects.Operations.IsPed(target) && distance <= 0.5f) ||
                (effects.Operations.IsVehicle(target) && distance <= 2)))
            {
                targets.Remove(target);
                Logger.Event("INFO", "hammer.targetReached", "target", target, "remaining", targets.Count);
            }
            else FindWaysToMoveToCoord(game.Entities.Position(target), false);
            return true;
        }

        public bool IsMoving => weaponObject != 0 && game.Entities.Exists(weaponObject) && game.Entities.Velocity(weaponObject).Length() > 0;

        private static float SpeedForDistance(float distance)
        {
            if (distance <= 3) return 35;
            if (distance <= 10) return 40;
            return 100;
        }

        public void FindWaysToMoveToCoord(Vector3 position, bool slowDownIfClose)
        {
            if (weaponObject == 0) return;
            bool blocked = HasObstaclesInBetween(position);
            if (blocked != previouslyObstructed)
                Logger.Throttled("INFO", "hammer.pathObstruction", 500, "blocked", blocked, "target", position.ToString());
            if (blocked && !IsInOpenAir())
            {
                if (!pathfinder.Position.HasValue) return;
                if (!previouslyObstructed) pathfinder.UpdateStartPosition(game.Entities.Position(weaponObject));
                previouslyObstructed = blocked;
                pathfinder.UpdateCurrentTarget(position);
                game.Entities.SetVelocity(weaponObject, pathfinder.GetTargetVelocity(game.Entities.Position(weaponObject)));
                return;
            }
            previouslyObstructed = blocked;
            Vector3 difference = position - Position;
            float speed = slowDownIfClose ? SpeedForDistance(difference.Length()) : 100;
            game.Entities.SetVelocity(weaponObject, difference.Normalized * speed);
        }

        private bool HasObstaclesInBetween(Vector3 position)
        {
            Vector3 current = game.Entities.Position(weaponObject);
            var hit = game.World.Capsule(current, position, 0.1f, IntersectFlags.Map);
            return hit.DidHit && current.DistanceTo(hit.Position) <= 20;
        }

        private bool IsInOpenAir()
        {
            Vector3 current = game.Entities.Position(weaponObject);
            var x = game.World.Capsule(current - Vector3.UnitX * 10, current + Vector3.UnitX * 10, 10, IntersectFlags.Map);
            var y = game.World.Capsule(current - Vector3.UnitY * 10, current + Vector3.UnitY * 10, 10, IntersectFlags.Map);
            var z = game.World.Capsule(current - Vector3.UnitZ * 10, current + Vector3.UnitZ * 10, 10, IntersectFlags.Map);
            return !x.DidHit && !y.DidHit && !z.DidHit;
        }
    }
}
