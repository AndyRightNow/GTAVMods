using GTA.Math;

namespace Thor
{
    public class Plane
    {
        private Vector3 normal;
        private Vector3 center;
        private Vector3 planeCoordinateXAxis;
        private Vector3 planeCoordinateYAxis;

        public Vector3 XAxis
        {
            get
            {
                return planeCoordinateXAxis;
            }
        }

        public Vector3 YAxis
        {
            get
            {
                return planeCoordinateYAxis;
            }
        }

        public Vector3 Normal
        {
            get
            {
                return normal;
            }
        }

        public Vector3 Center
        {
            get
            {
                return center;
            }
        }

        public Plane(Vector3 normal, Vector3 point)
        {
            this.normal = normal;
            this.center = point;
            planeCoordinateXAxis = Utilities.Math.RandomVectorPerpendicularTo(normal);
            planeCoordinateYAxis = Vector3.Cross(normal, planeCoordinateXAxis).Normalized;
        }
        
        public Vector2 GetPlaneCoord(Vector3 worldPoint)
        {
            var cenetrToWorldPoint = worldPoint - center;

            var worldPointXVec = Vector3.Project(cenetrToWorldPoint, planeCoordinateXAxis);
            var worldPointYVec = Vector3.Project(cenetrToWorldPoint, planeCoordinateYAxis);

            bool isXPositive = Vector3.Dot(worldPointXVec.Normalized, planeCoordinateXAxis) >= 0.0f;
            bool isYPositive = Vector3.Dot(worldPointYVec.Normalized, planeCoordinateYAxis) >= 0.0f;

            float x = worldPointXVec.Length() * (isXPositive ? 1 : -1);
            float y = worldPointYVec.Length() * (isYPositive ? 1 : -1);

            return new Vector2(x, y);
        }

        public Vector3 GetWorldCoord(Vector2 planePoint)
        {
            var worldPointYVec = planeCoordinateYAxis * planePoint.Y;

            return center + worldPointYVec + planeCoordinateXAxis * planePoint.X;
        }
    }
}
