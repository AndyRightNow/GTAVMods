using GTA.Math;

namespace Thor
{
    /// <summary>Player power, controls, flight and ability presentation settings.</summary>
    public sealed class AbilityTuning
    {
        public int FullPowerMaxHealth = 30000;
        public float StoredPowerFraction = 0.5f;
        public float PowerFadeMs = 10000;
        public float MovementMultiplier = 1.5f;
        public float CatchRadius = 0.2f;
        public float ApproachSoundDistance = 140;
        public float UprightDistance = 5;
        public float ReturnDelayMs = 700;
        public int CatchAnimationMs = 250;
        public float ThrowSpeed = 100;
        public Vector3 ThrowVelocityOffset = new Vector3(0, 0, 5);
        public float AimRayStartDistance = 10;
        public float AimRayDistance = 100000;
        public float DefaultThrowAngleDegrees = 45;
        public float UpwardFlightSpeed = 50;
        public float ForwardFlightSpeed = 70;
        public float ThrowFlightSpeedMultiplier = 2;
        public int ThrowFlightDurationMs = 2000;
        public float SprintExtraSpeedMultiplier = 4;
        public float LandingSpeed = 200;
        public float MeleeRange = 20;
        public float FlightContactRange = 5;
        public float GrabRange = 1.5f;
        public int ThunderIntervalMs = 1000;
        public int ThunderFlashesPerBurst = 4;
        public int MaxTargets = 15;
        public int HealthRecoveryIntervalMs = 200;
        public float HealthRecoveryFraction = 0.1f;
        public int UnpoweredMaxHealth = 100;
        public float CatchVolume = 0.3f;
        public float ApproachVolume = 0.3f;
        public float HeldLightningScale = 0.5f;
        public float CatchLightningScale = 0.8f;
        public float FlightShakeAmplitude = 0.01f;
        public float CatchShakeAmplitude = 0.01f;
        public int FlightRagdollMinMs = 2;
        public int FlightRagdollMaxMs = 1000;
        public float HoverUpDotMinimum = 0.85f;
        public float ForwardHammerOffset = 0.3f;
        public Vector3 HoverOffset = new Vector3(0, 0, 0.1f);
        public Vector3 HiddenHoverSpawnPosition = new Vector3(0, 0, 10);
        public Vector3 TargetMarkerOffset = new Vector3(0, 0, 2);
        public Vector3 TargetMarkerScale = Vector3.One;
        public System.Drawing.Color TargetMarkerColor = System.Drawing.Color.Red;
    }
}
