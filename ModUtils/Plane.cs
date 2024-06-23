using CitizenFX.Core;

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
            var yAxis = Vector3.Cross(normal, XAxis);
            yAxis.Normalize();
            YAxis = yAxis;
        }
        
        public Vector2 GetPlaneCoord(Vector3 worldPoint)
        {
            var centerToWorldPoint = worldPoint - Center;

            var worldPointXVec = Utilities.Math.Project(centerToWorldPoint, XAxis);
            var worldPointYVec = Utilities.Math.Project(centerToWorldPoint, YAxis);
            worldPointXVec.Normalize();
            worldPointYVec.Normalize();
            bool isXPositive = Vector3.Dot(worldPointXVec, XAxis) >= 0.0f;
            bool isYPositive = Vector3.Dot(worldPointYVec, YAxis) >= 0.0f;

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
