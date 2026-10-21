using System;
using ADModUtils;
using GTA.Math;
using GTAVMods.Game;

namespace Thor
{
    public enum MjolnirCameraType { StationaryViewFollow, SideFollow, FarSideFollow }

    public sealed class WeaponCamera
    {
        private readonly GameServices game;
        private readonly Random random;
        private readonly IEventLog log;
        private readonly CameraTuning tuning;
        private int camera;
        private int? lastSetPosition;
        private MjolnirCameraType cameraType;
        private Vector3 sideDirection;

        public WeaponCamera(GameServices game, Random random, IEventLog log, CameraTuning tuning)
        {
            this.game = game;
            this.tuning = tuning;
            this.random = random;
            this.log = log;
        }

        public void RenderCamera(int weapon)
        {
            if (camera == 0)
            {
                camera = game.Cameras.Create(game.Entities.Position(weapon), Vector3.Zero, tuning.FieldOfView);
                if (camera == 0) return;
                log.Event("INFO", "camera.created", "weapon", weapon);
            }
            Vector3 direction = game.Entities.Velocity(weapon).Normalized;
            int sign = random.Next(0, 2) > 0 ? 1 : -1;
            bool reset = !lastSetPosition.HasValue || unchecked(game.Host.GameTime - lastSetPosition.Value) >= tuning.ViewDurationMs;
            if (reset)
            {
                cameraType = tuning.Views[random.Next(tuning.Views.Length)];
                lastSetPosition = game.Host.GameTime;
                sideDirection = Utilities.Math.RandomVectorPerpendicularTo(direction, random) * sign;
            }
            if (cameraType == MjolnirCameraType.SideFollow)
            {
                game.Cameras.SetPosition(camera, game.Entities.Position(weapon) + sideDirection * tuning.NearDistance);
            }
            else if (cameraType == MjolnirCameraType.FarSideFollow)
                game.Cameras.SetPosition(camera, game.Entities.Position(weapon) + sideDirection * tuning.FarDistance);
            else if (reset)
                game.Cameras.SetPosition(camera, game.Entities.Position(weapon) + direction * tuning.StationaryLeadDistance + sideDirection * tuning.NearDistance);
            game.Cameras.PointAt(camera, weapon);
            game.Cameras.SetActive(camera, true);
            game.Cameras.StartRendering();
        }

        public void DestroyCamera()
        {
            if (camera == 0) return;
            log.Event("INFO", "camera.deleted");
            Cleanup.Run(CancelRenderedCamera, () =>
            {
                game.Cameras.Delete(camera);
                camera = 0;
                lastSetPosition = null;
            });
        }

        public void CancelRenderedCamera()
        {
            if (camera != 0 && game.Cameras.RenderingCamera == camera)
            {
                log.Event("INFO", "camera.stopped");
                Cleanup.Run(game.Cameras.StopRendering, () => game.Cameras.SetActive(camera, false));
            }
        }
    }
}
