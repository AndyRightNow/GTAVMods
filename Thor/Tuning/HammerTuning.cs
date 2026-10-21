using GTA.Math;

namespace Thor
{
    /// <summary>Hammer movement, collision, recovery, whirling and presentation settings.</summary>
    public sealed class HammerTuning
    {
        public int AssetLoadTimeoutMs = 3000;
        public float Mass = 50000;
        public float RotationBlend = 0.4f;
        public Vector3 SpawnPosition = new Vector3(0, 0, 2000);
        public int LightningIntervalMs = 1000;
        public float TrailDurationSeconds = 0.08f;
        public float ImpactRadius = 1;
        public int ImpactDamage = 100;
        public float PedTargetReachDistance = 0.5f;
        public float VehicleTargetReachDistance = 2;
        public float NearRecallDistance = 3;
        public float MediumRecallDistance = 10;
        public float NearRecallSpeed = 35;
        public float MediumRecallSpeed = 40;
        public float CruiseSpeed = 100;
        public float ObstacleProbeRadius = 0.1f;
        public float ObstacleDistance = 20;
        public float OpenAirProbeRadius = 10;
        public float TerrainProbeEndZ = -1000;
        public float RecoveryHeightJump = 50;
        public int IdleIntervalMs = 1000;
        public int IdleIntervalJitterMs = 1000;
        public float IdleMaxSpeed = 30;
        public float IdleVelocityBlend = 0.5f;
        public float RopeLength = 0.19f;
        public Vector3 RopeAttachOffset = new Vector3(0, 0, -0.15f);
        public float PhysicalWhirlPlaneCorrection = 1;
        public float PhysicalWhirlTangentSpeed = 1500;
        public float PhysicalWhirlOutwardSpeed = 500;
        public float VisualWhirlRadius = 0.3f;
        public float WhirlVolume = 1;
    }
}
