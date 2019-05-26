using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using GTA;
using GTA.Native;
using GTA.Math;

namespace Thor
{
    enum MOLNIR_CAMERA_TYPE
    {
        STATIONARY_VIEW_FOLLOW,
        SIDE_FOLLOW,
        STATIONARY_REAR,
        NONE
    }

    class WeaponCamera
    {
        private Camera hammerTrackCam;
        private static WeaponCamera instance;
        private static float CAMERA_FOV = 50.0f;
        private static float DEFAULT_CAMERA_POSITION_NOT_SET = -1.0f;
        private static float CAMERA_POSITION_RESET_INTERVAL = 2000.0f;
        private static float CAMERA_FRONT_DISTANCE_MULTIPLIER = 50.0f;
        private static float CAMERA_SIDE_DISTANCE_MULTIPLIER = 5.0f;
        private static MOLNIR_CAMERA_TYPE cameraType;
        private float lastSetCameraPositionTimestamp;
        private Vector3 currentSideDirection;

        private WeaponCamera()
        {
            lastSetCameraPositionTimestamp = DEFAULT_CAMERA_POSITION_NOT_SET;
            cameraType = MOLNIR_CAMERA_TYPE.NONE;
        }

        private bool ShouldResetCamera()
        {
            return lastSetCameraPositionTimestamp == DEFAULT_CAMERA_POSITION_NOT_SET || 
                Game.GameTime - lastSetCameraPositionTimestamp >= CAMERA_POSITION_RESET_INTERVAL;
        }

        public static WeaponCamera Instance
        {
            get
            {
                if (instance == null)
                {
                    instance = new WeaponCamera();
                }

                return instance;
            }
        }

        public void RenderCamera(Entity weaponObject)
        {
            if (hammerTrackCam == null)
            {
                hammerTrackCam = World.CreateCamera(weaponObject.Position, Vector3.Zero, CAMERA_FOV);
            }
            Vector3 weaponDirection = weaponObject.Velocity.Normalized;
            int randomNegationFactor = ADModUtils.Utilities.Random.RandomNegation();
            bool isJustReset = false;

            if (ShouldResetCamera())
            {
                cameraType = ADModUtils.Utilities.Random.PickOne(new List<MOLNIR_CAMERA_TYPE>()
                    {
                        MOLNIR_CAMERA_TYPE.SIDE_FOLLOW,
                        MOLNIR_CAMERA_TYPE.STATIONARY_VIEW_FOLLOW,
                        MOLNIR_CAMERA_TYPE.STATIONARY_VIEW_FOLLOW,
                        MOLNIR_CAMERA_TYPE.STATIONARY_VIEW_FOLLOW,
                        MOLNIR_CAMERA_TYPE.STATIONARY_VIEW_FOLLOW
                    }.ToArray());
                lastSetCameraPositionTimestamp = Game.GameTime;
                currentSideDirection = ADModUtils.Utilities.Math.RandomVectorPerpendicularTo(weaponDirection) * randomNegationFactor;
                isJustReset = true;
            }

            switch (cameraType)
            {
                case MOLNIR_CAMERA_TYPE.SIDE_FOLLOW:
                    hammerTrackCam.Position = weaponObject.Position +
                        currentSideDirection * CAMERA_SIDE_DISTANCE_MULTIPLIER;
                    hammerTrackCam.PointAt(weaponObject);
                    break;
                case MOLNIR_CAMERA_TYPE.STATIONARY_REAR:
                    if (isJustReset)
                    {
                        hammerTrackCam.Position = weaponObject.Position +
                            weaponDirection * CAMERA_FRONT_DISTANCE_MULTIPLIER +
                            currentSideDirection * CAMERA_SIDE_DISTANCE_MULTIPLIER;
                        hammerTrackCam.PointAt(weaponObject.Position + weaponDirection * CAMERA_FRONT_DISTANCE_MULTIPLIER);
                    }
                    break;
                case MOLNIR_CAMERA_TYPE.STATIONARY_VIEW_FOLLOW:
                    if (isJustReset)
                    {
                        hammerTrackCam.Position = weaponObject.Position +
                            weaponDirection * CAMERA_FRONT_DISTANCE_MULTIPLIER +
                            currentSideDirection * CAMERA_SIDE_DISTANCE_MULTIPLIER;
                    }
                    hammerTrackCam.PointAt(weaponObject);
                    break;
                default:
                    break;
            }


            World.RenderingCamera = hammerTrackCam;
        }

        public void DestroyCamera()
        {
            if (hammerTrackCam != null)
            {
                hammerTrackCam.Destroy();
                hammerTrackCam = null;
            }
        }

        public void CancelRenderedCamera()
        {
            World.RenderingCamera = null;
        }
    }
}
