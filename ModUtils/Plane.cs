using GTA.Math;

namespace ADModUtils
{
    public class Plane
    {
        public Vector3 XAxis { get; }

        public Vector3 YAxis { get; }

        public Vector3 Normal { get; }

        public Vector3 Center { get; }

        public Plane(Vector3 normal, Vector3 point)
        {
            Normal = normal;
            Center = point;
            XAxis = Utilities.Math.RandomVectorPerpendicularTo(normal);
            YAxis = Vector3.Cross(normal, XAxis).Normalized;
        }
        
        public Vector2 GetPlaneCoord(Vector3 worldPoint)
        {
            var cenetrToWorldPoint = worldPoint - Center;

            var worldPointXVec = Vector3.Project(cenetrToWorldPoint, XAxis);
            var worldPointYVec = Vector3.Project(cenetrToWorldPoint, YAxis);

            bool isXPositive = Vector3.Dot(worldPointXVec.Normalized, XAxis) >= 0.0f;
            bool isYPositive = Vector3.Dot(worldPointYVec.Normalized, YAxis) >= 0.0f;

            float x = worldPointXVec.Length() * (isXPositive ? 1 : -1);
            float y = worldPointYVec.Length() * (isYPositive ? 1 : -1);

            return new Vector2(x, y);
        }

        public Vector3 GetWorldCoord(Vector2 planePoint)
        {
            var worldPointYVec = YAxis * planePoint.Y;

            return Center + worldPointYVec + XAxis * planePoint.X;
        }
    }
}
