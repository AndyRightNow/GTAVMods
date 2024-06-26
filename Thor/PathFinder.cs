using ADModUtils;
using CitizenFX.Core;
using CitizenFX.Core.Native;
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

            private async void Init()
            {
                if (initialized)
                {
                    return;
                }

                var planeModel = new Model(VehicleHash.Thrust);
                planeModel.Request();
                chasingVehicle = await World.CreateVehicle(planeModel, DEFAULT_SPAWN_POS);
                chasingVehicle.CreateRandomPedOnSeat(VehicleSeat.Driver);
                driver = chasingVehicle.GetPedOnSeat(VehicleSeat.Driver);

                initialized = true;
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

                ADModUtils.NativeHelper.StartHeliMission(driver, chasingVehicle, currentTargetPosition, ADModUtils.VehicleMissionType.GoTo, 100000.0f, 1.0f, -1, 5, -1, -1, ADModUtils.HeliMissionFlags.StartEngineImmediately);
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
                ADModUtils.NativeHelper.StartHeliMission(driver, chasingVehicle, currentTargetPed, ADModUtils.VehicleMissionType.Follow, 100000.0f, 1.0f, -1, 5, -1, -1, ADModUtils.HeliMissionFlags.StartEngineImmediately);
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
