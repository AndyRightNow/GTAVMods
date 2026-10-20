using GTA;
using GTA.Math;
using System.Collections.Generic;

namespace Thor
{
    enum MjolnirCameraType
    {
        StationaryViewFollow,
        SideFollow,
        FarSideFollow,
        None
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
        private static float CAMERA_FAR_SIDE_DISTANCE_MULTIPLIER = 70.0f;
        private static MjolnirCameraType cameraType;
        private float lastSetCameraPositionTimestamp;
        private Vector3 currentSideDirection;

        private WeaponCamera()
        {
            lastSetCameraPositionTimestamp = DEFAULT_CAMERA_POSITION_NOT_SET;
            cameraType = MjolnirCameraType.None;
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
                hammerTrackCam = Camera.Create(ScriptedCameraNameHash.DefaultScriptedCamera,
                    weaponObject.Position, Vector3.Zero, CAMERA_FOV);
                if (hammerTrackCam == null) return;
                ADModUtils.Logger.Event("INFO", "camera.created", "weapon", weaponObject.Handle);
            }
            Vector3 weaponDirection = weaponObject.Velocity.Normalized;
            int randomNegationFactor = ADModUtils.Utilities.Random.RandomNegation();
            bool isJustReset = false;

            if (ShouldResetCamera())
            {
                cameraType = ADModUtils.Utilities.Random.PickOne(new List<MjolnirCameraType>()
                    {
                        MjolnirCameraType.SideFollow,
                        MjolnirCameraType.FarSideFollow,
                        MjolnirCameraType.FarSideFollow,
                        MjolnirCameraType.StationaryViewFollow,
                        MjolnirCameraType.StationaryViewFollow,
                        MjolnirCameraType.StationaryViewFollow,
                        MjolnirCameraType.StationaryViewFollow,
                    }.ToArray());
                lastSetCameraPositionTimestamp = Game.GameTime;
                currentSideDirection = ADModUtils.Utilities.Math.RandomVectorPerpendicularTo(weaponDirection) * randomNegationFactor;
                isJustReset = true;
            }

            switch (cameraType)
            {
                case MjolnirCameraType.SideFollow:
                    hammerTrackCam.Position = weaponObject.Position +
                        currentSideDirection * CAMERA_SIDE_DISTANCE_MULTIPLIER;
                    hammerTrackCam.PointAt(weaponObject);
                    break;
                case MjolnirCameraType.StationaryViewFollow:
                    if (isJustReset)
                    {
                        hammerTrackCam.Position = weaponObject.Position +
                            weaponDirection * CAMERA_FRONT_DISTANCE_MULTIPLIER +
                            currentSideDirection * CAMERA_SIDE_DISTANCE_MULTIPLIER;
                    }
                    hammerTrackCam.PointAt(weaponObject);
                    break;
                case MjolnirCameraType.FarSideFollow:
                    hammerTrackCam.Position = weaponObject.Position +
                        currentSideDirection * CAMERA_FAR_SIDE_DISTANCE_MULTIPLIER;
                    hammerTrackCam.PointAt(weaponObject);
                    break;
                default:
                    break;
            }


            hammerTrackCam.IsActive = true;
            ScriptCameraDirector.StartRendering();
        }

        public void DestroyCamera()
        {
            if (hammerTrackCam != null)
            {
                ADModUtils.Logger.Event("INFO", "camera.deleted");
                CancelRenderedCamera();
                hammerTrackCam.Delete();
                hammerTrackCam = null;
            }
        }

        public void CancelRenderedCamera()
        {
            if (hammerTrackCam != null && ScriptCameraDirector.RenderingCam == hammerTrackCam)
            {
                ADModUtils.Logger.Event("INFO", "camera.stopped");
                ScriptCameraDirector.StopRendering();
                hammerTrackCam.IsActive = false;
            }
        }
    }
}
