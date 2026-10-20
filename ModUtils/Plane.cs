using GTA.Math;

using System;

namespace ADModUtils
{
    public class Plane
    {
        public Vector3 XAxis { get; }

        public Vector3 YAxis { get; }

        public Vector3 Normal { get; }

        public Vector3 Center { get; }

        public Plane(Vector3 normal, Vector3 point)
            : this(normal, point, Utilities.Random.SystemRandomInstance)
        {
        }

        public Plane(Vector3 normal, Vector3 point, Random random)
        {
            if (normal == Vector3.Zero) throw new ArgumentException("A plane needs a nonzero normal.", nameof(normal));
            Normal = normal;
            Center = point;
            XAxis = Utilities.Math.RandomVectorPerpendicularTo(normal, random);
            YAxis = Vector3.Cross(normal, XAxis).Normalized;
        }
        
        public Vector2 GetPlaneCoord(Vector3 worldPoint)
        {
            var offset = worldPoint - Center;
            return new Vector2(Vector3.Dot(offset, XAxis), Vector3.Dot(offset, YAxis));
        }

        public Vector3 GetWorldCoord(Vector2 planePoint)
        {
            var worldPointYVec = YAxis * planePoint.Y;

            return Center + worldPointYVec + XAxis * planePoint.X;
        }
    }
}
