using System;
using ADModUtils;
using GTA;
using GTA.Math;
using GTAVMods.Game;

namespace Thor.PathFinder
{
    public sealed class PathFinder
    {
        private readonly GameServices game;
        private readonly IEventLog log;
        private readonly NavigationTuning tuning;
        private int driver, chasingVehicle;
        private Vector3? currentTargetPosition;
        private bool initialized;

        public PathFinder(GameServices game, IEventLog log, NavigationTuning tuning)
        { this.game = game; this.log = log; this.tuning = tuning; }

        private void Init()
        {
            // Successful initialization owns both nonzero handles until Dispose.
            if (initialized && game.Entities.Exists(chasingVehicle) && game.Entities.Exists(driver))
                return;
            if (driver != 0 || chasingVehicle != 0) Dispose();

            int model = (int)VehicleHash.Thruster;
            if (!game.World.RequestModel(model, tuning.ModelLoadTimeoutMs))
                throw new InvalidOperationException("Could not load the pathfinder vehicle model.");
            try
            {
                chasingVehicle = game.World.CreateVehicle(model, tuning.SpawnPosition);
                if (chasingVehicle == 0)
                    throw new InvalidOperationException("Could not create the pathfinder vehicle.");
                driver = game.World.CreateDriver(chasingVehicle);
                if (driver == 0)
                    throw new InvalidOperationException("Could not create the pathfinder driver.");
            }
            catch (Exception creationFailure)
            {
                try { Dispose(); }
                catch (Exception cleanupFailure) { throw new AggregateException(creationFailure, cleanupFailure); }
                throw;
            }
            finally { game.World.ReleaseModel(model); }

            initialized = true;
            log.Event("INFO", "pathfinder.created", "driver", driver, "vehicle", chasingVehicle);
        }

        public void Dispose()
        {
            if (driver != 0 || chasingVehicle != 0)
                log.Event("INFO", "pathfinder.dispose", "driver", driver, "vehicle", chasingVehicle);
            currentTargetPosition = null;
            initialized = false;
            Cleanup.Run(() => ReleaseEntity(ref driver), () => ReleaseEntity(ref chasingVehicle));
        }

        private void ReleaseEntity(ref int handle)
        {
            if (handle == 0) return;
            if (game.Entities.Exists(handle)) game.Entities.Delete(handle);
            handle = 0;
        }

        // Collision queries run after OnTick has acquired both nonzero helper handles.
        public bool IsRelatedEntity(int entity) => entity == chasingVehicle || entity == driver;

        public Vector3 GetTargetVelocity(Vector3 userPosition)
            => (game.Entities.Position(chasingVehicle) - userPosition) * tuning.PositionCorrection + game.Entities.Velocity(chasingVehicle);

        public void OnTick(int entityToDisableCollision)
        {
            Init();
            if (game.World.VehicleDriver(chasingVehicle) != driver)
                game.Peds.SetIntoVehicle(driver, chasingVehicle, VehicleSeat.Driver);
            game.Entities.SetInvincible(chasingVehicle, true);
            game.Entities.SetHealth(chasingVehicle, tuning.HelperHealth);
            game.Entities.SetVisible(chasingVehicle, false);
            game.Entities.SetInvincible(driver, true);
            game.Entities.SetHealth(driver, tuning.HelperHealth);
            game.Entities.SetVisible(driver, false);
            game.Entities.SetVelocity(chasingVehicle, game.Entities.Velocity(chasingVehicle) * tuning.VelocityMultiplier);
            SetNoCollision(entityToDisableCollision);
        }

        public void SetNoCollision(int entity)
        {
            if (!initialized || entity == 0 || !game.Entities.Exists(entity)) return;
            game.Entities.SetNoCollision(chasingVehicle, entity, true);
            game.Entities.SetNoCollision(chasingVehicle, game.Host.PlayerPed, true);
            game.Entities.SetNoCollision(driver, entity, true);
            game.Entities.SetNoCollision(driver, game.Host.PlayerPed, true);
        }

        public void UpdateStartPosition(Vector3 position) => game.Entities.SetPosition(chasingVehicle, position);

        public void UpdateCurrentTarget(Vector3 target)
        {
            if (currentTargetPosition.HasValue && target.DistanceTo(currentTargetPosition.Value) <= tuning.RetargetDistance) return;
            ClearAll();
            currentTargetPosition = target;
            game.Peds.StartHeliMissionToPosition(driver, chasingVehicle, target,
                VehicleMissionType.GoTo, tuning.MissionSpeed, tuning.StoppingRange, tuning.Orientation,
                tuning.FlightHeight, tuning.MinTerrainHeight, tuning.SlowDownDistance, HeliMissionFlags.StartEngineImmediately);
        }

        private void ClearAll()
        {
            game.Peds.ClearTasks(driver);
            currentTargetPosition = null;
        }
    }
}
