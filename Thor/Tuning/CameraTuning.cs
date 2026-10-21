namespace Thor
{
    /// <summary>Hammer follow-camera framing and view-change settings.</summary>
    public sealed class CameraTuning
    {
        public float FieldOfView = 50;
        public int ViewDurationMs = 2000;
        public float NearDistance = 5;
        public float FarDistance = 70;
        public float StationaryLeadDistance = 50;
        public MjolnirCameraType[] Views = {
            MjolnirCameraType.SideFollow, MjolnirCameraType.FarSideFollow, MjolnirCameraType.FarSideFollow,
            MjolnirCameraType.StationaryViewFollow, MjolnirCameraType.StationaryViewFollow,
            MjolnirCameraType.StationaryViewFollow, MjolnirCameraType.StationaryViewFollow
        };
    }
}
