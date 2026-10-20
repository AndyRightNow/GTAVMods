using ADModUtils;
using GTA;
using GTA.Math;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Thor
{
    namespace PathFinder
    {
        public class PathFinder
        {
            private static PathFinder instance = null;
            private Ped driver = null;
            private Vehicle chasingVehicle = null;
            private Ped currentTargetPed = null;
            private Vector3 currentTargetPosition = Vector3.Zero;
            private bool initialized = false;
            private static Vector3 DEFAULT_SPAWN_POS = new Vector3(500.0f, 500.0f, 500.0f);

            private PathFinder() { }

            public static PathFinder Instance
            {
                get
                {
                    if (instance is null)
                    {
                        instance = new PathFinder();
                    }

                    return instance;
                }
            }

            private void Init()
            {
                if (initialized && chasingVehicle != null && chasingVehicle.Exists() &&
                    driver != null && driver.Exists())
                {
                    return;
                }
                if (initialized) Dispose();

                var planeModel = new Model(VehicleHash.Thruster);
                if (!planeModel.Request(3000))
                    throw new InvalidOperationException("Could not load the pathfinder vehicle model.");
                try
                {
                    chasingVehicle = World.CreateVehicle(planeModel, DEFAULT_SPAWN_POS);
                    if (chasingVehicle == null)
                        throw new InvalidOperationException("Could not create the pathfinder vehicle.");
                    driver = chasingVehicle.CreateRandomPedOnSeat(VehicleSeat.Driver);
                    if (driver == null)
                        throw new InvalidOperationException("Could not create the pathfinder driver.");
                }
                catch
                {
                    Dispose();
                    throw;
                }
                finally { planeModel.MarkAsNoLongerNeeded(); }

                initialized = true;
                Logger.Event("INFO", "pathfinder.created", "driver", driver.Handle, "vehicle", chasingVehicle.Handle);
            }

            public void Dispose()
            {
                if (driver != null || chasingVehicle != null) Logger.Event("INFO", "pathfinder.dispose", "driver", driver?.Handle, "vehicle", chasingVehicle?.Handle);
                driver?.Delete();
                chasingVehicle?.Delete();
                driver = null;
                chasingVehicle = null;
                currentTargetPed = null;
                currentTargetPosition = Vector3.Zero;
                initialized = false;
            }

            public bool IsRelatedEntity(Entity entity)
            {
                return entity == chasingVehicle || entity == driver;
            }

            public Vector3 GetTargetVelocity(Vector3 userPosition, float scaler = 0.5f)
            {
                if (chasingVehicle is null)
                {
                    return Vector3.Zero;
                }

                return (chasingVehicle.Position - userPosition) * scaler + chasingVehicle.Velocity;
            }

            public Vector3? Position
            {
                get
                {
                    if (chasingVehicle is null)
                    {
                        return null;
                    }

                    return chasingVehicle.Position;
                }
            }

            public void OnTick(Entity entityToDisableCollision)
            {
                Init();

                if (chasingVehicle.Driver != driver)
                {
                    driver.SetIntoVehicle(chasingVehicle, VehicleSeat.Driver);
                }

                chasingVehicle.IsInvincible = true;
                chasingVehicle.Health = 100;
                chasingVehicle.IsVisible = false;

                driver.IsInvincible = true;
                driver.Health = 100;

                driver.IsVisible = false;

                chasingVehicle.Velocity *= 1.05f;

                SetNoCollision(entityToDisableCollision);
            }

            public void SetNoCollision(Entity entityToDisableCollision)
            {
                if (chasingVehicle == null || driver == null || entityToDisableCollision == null)
                {
                    return;
                }

                chasingVehicle.SetNoCollision(entityToDisableCollision, true);
                chasingVehicle.SetNoCollision(Game.Player.Character, true);
                driver.SetNoCollision(entityToDisableCollision, true);
                driver.SetNoCollision(Game.Player.Character, true);
            }

            public void UpdateStartPosition(Vector3 position)
            {
                chasingVehicle.Position = position;
            }

            public void UpdateCurrentTarget(Vector3 target)
            {
                if (currentTargetPosition.Equals(target) || World.GetDistance(target, currentTargetPosition) <= 15.0f)
                {
                    return;
                }

                ClearAll();

                currentTargetPosition = target;

                driver.Task.StartHeliMission(chasingVehicle, currentTargetPosition, VehicleMissionType.GoTo, 100000.0f, 1.0f, -1, 5, -1, -1, HeliMissionFlags.StartEngineImmediately);
            }

            public void UpdateCurrentTarget(Ped targetPed)
            {
                if (targetPed is null)
                {
                    return;
                }

                if (currentTargetPed != null && targetPed.Handle == currentTargetPed.Handle)
                {
                    return;
                }

                ClearAll();

                currentTargetPed = targetPed;
                driver.Task.StartHeliMission(chasingVehicle, currentTargetPed, VehicleMissionType.Follow, 100000.0f, 1.0f, -1, 5, -1, -1, HeliMissionFlags.StartEngineImmediately);
            }

            private void ClearAll()
            {
                driver.Task.ClearAll();
                currentTargetPed = null;
                currentTargetPosition = Vector3.Zero;
            }
        }
    }
}
