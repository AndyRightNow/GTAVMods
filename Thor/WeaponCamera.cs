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
        private int camera;
        private int? lastSetPosition;
        private MjolnirCameraType cameraType;
        private Vector3 sideDirection;
        private static readonly MjolnirCameraType[] Views = {
            MjolnirCameraType.SideFollow, MjolnirCameraType.FarSideFollow, MjolnirCameraType.FarSideFollow,
            MjolnirCameraType.StationaryViewFollow, MjolnirCameraType.StationaryViewFollow,
            MjolnirCameraType.StationaryViewFollow, MjolnirCameraType.StationaryViewFollow
        };

        public WeaponCamera(GameServices game, Random random)
        {
            this.game = game;
            this.random = random;
        }

        public void RenderCamera(int weapon)
        {
            if (camera == 0)
            {
                camera = game.Cameras.Create(game.Entities.Position(weapon), Vector3.Zero, 50);
                if (camera == 0) return;
                Logger.Event("INFO", "camera.created", "weapon", weapon);
            }
            Vector3 direction = game.Entities.Velocity(weapon).Normalized;
            int sign = random.Next(0, 2) > 0 ? 1 : -1;
            bool reset = !lastSetPosition.HasValue || unchecked(game.Host.GameTime - lastSetPosition.Value) >= 2000;
            if (reset)
            {
                cameraType = Views[random.Next(Views.Length)];
                lastSetPosition = game.Host.GameTime;
                sideDirection = Utilities.Math.RandomVectorPerpendicularTo(direction, random) * sign;
            }
            if (cameraType == MjolnirCameraType.SideFollow)
            {
                game.Cameras.SetPosition(camera, game.Entities.Position(weapon) + sideDirection * 5);
            }
            else if (cameraType == MjolnirCameraType.FarSideFollow)
                game.Cameras.SetPosition(camera, game.Entities.Position(weapon) + sideDirection * 70);
            else if (reset)
                game.Cameras.SetPosition(camera, game.Entities.Position(weapon) + direction * 50 + sideDirection * 5);
            game.Cameras.PointAt(camera, weapon);
            game.Cameras.SetActive(camera, true);
            game.Cameras.StartRendering();
        }

        public void DestroyCamera()
        {
            if (camera == 0) return;
            Logger.Event("INFO", "camera.deleted");
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
                Logger.Event("INFO", "camera.stopped");
                Cleanup.Run(game.Cameras.StopRendering, () => game.Cameras.SetActive(camera, false));
            }
        }
    }
}
