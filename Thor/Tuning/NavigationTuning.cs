using GTA.Math;

namespace Thor
{
    /// <summary>Invisible navigation helper movement and route refresh settings.</summary>
    public sealed class NavigationTuning
    {
        public Vector3 SpawnPosition = new Vector3(500, 500, 500);
        public int ModelLoadTimeoutMs = 3000;
        public int HelperHealth = 100;
        public float VelocityMultiplier = 1.05f;
        public float PositionCorrection = 0.5f;
        public float RetargetDistance = 15;
        public float MissionSpeed = 100000;
        public float StoppingRange = 1;
        public int Orientation = -1;
        public int FlightHeight = 5;
        public int MinTerrainHeight = -1;
        public float SlowDownDistance = -1;
    }
}
