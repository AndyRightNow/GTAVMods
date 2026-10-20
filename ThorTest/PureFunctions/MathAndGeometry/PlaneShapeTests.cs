using ADModUtils;
using DeveloperConsole;
using GTA.Math;
using GTA.Native;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Plane = ADModUtils.Plane;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System;

namespace GTAVMods.Tests.PureFunctions.MathAndGeometry
{
    [TestClass, TestCategory("PureFunction")]
    public class PlaneShapeTests
    {
        [TestMethod]
        public void CuboidCornersUseEveryBoundaryCombinationWithoutDuplicatingVertices() {
            var center = new Vector3(10, 20, 30);
            var corners = Rectangle3D.CreateCorners(center, new Vector3(-1,-2,-3), new Vector3(1,2,3));
            Assert.AreEqual(8, corners.Count);
            Assert.AreEqual(8, corners.Values.Distinct().Count());
            foreach (var corner in corners) {
                Assert.AreEqual(center + new Vector3(corner.Key[0] == '0' ? -1 : 1,
                    corner.Key[1] == '0' ? -2 : 2, corner.Key[2] == '0' ? -3 : 3), corner.Value);
            }
        }

        [TestMethod]
        public void QuaternionRotationReturnsQuarterTurnAroundCenterWithoutMutatingInput() {
            var center = new Vector3(10,20,30);
            var corners = Rectangle3D.CreateCorners(center, new Vector3(-1,-2,-3), new Vector3(1,2,3));
            var original = corners.ToDictionary(c => c.Key, c => c.Value);
            var rotated = Rectangle3D.RotateCorners(corners, center, new Quaternion(0,0,(float)Math.Sqrt(.5),(float)Math.Sqrt(.5)));
            foreach (var corner in rotated) {
                var prior = original[corner.Key] - center;
                var expected = center + new Vector3(-prior.Y, prior.X, prior.Z);
                Assert.AreEqual(expected.X, corner.Value.X, .00001);
                Assert.AreEqual(expected.Y, corner.Value.Y, .00001);
                Assert.AreEqual(expected.Z, corner.Value.Z, .00001);
                Assert.AreEqual(original[corner.Key], corners[corner.Key]);
            }
        }

        [TestMethod]
        public void PlaneProjectsSignedCoordinatesAndIgnoresNormalDisplacement()
        {
            var center = new Vector3(10, 20, 30);
            var plane = new Plane(Vector3.UnitZ * 2, center);
            Assert.AreEqual(1f, plane.XAxis.Length(), 0.00001f);
            Assert.AreEqual(1f, plane.YAxis.Length(), 0.00001f);
            Assert.AreEqual(0f, Vector3.Dot(plane.XAxis, plane.YAxis), 0.00001f);
            float rootTwo = (float)System.Math.Sqrt(2);
            var coordinates = plane.GetPlaneCoord(new Vector3(10 + 3 * rootTwo, 20 - rootTwo, 80));
            Assert.AreEqual(2f, coordinates.X, 0.00001f);
            Assert.AreEqual(-4f, coordinates.Y, 0.00001f);
            var negative = plane.GetPlaneCoord(new Vector3(10 - 3 * rootTwo, 20 + rootTwo, -20));
            Assert.AreEqual(-2f, negative.X, 0.00001f);
            Assert.AreEqual(4f, negative.Y, 0.00001f);
            var world = plane.GetWorldCoord(new Vector2(-2, -2));
            Assert.AreEqual(10f, world.X, 0.00001f);
            Assert.AreEqual(20f - (float)(2 * System.Math.Sqrt(2)), world.Y, 0.00001f);
            Assert.AreEqual(30f, world.Z, 0.00001f);
            Assert.AreEqual(Vector2.Zero, plane.GetPlaneCoord(center));
            Assert.AreEqual(0f, Vector3.Dot(world - center, plane.Normal), 0.00001f,
                "A reconstructed point must stay in the plane even when its normal is not unit length.");
            Assert.ThrowsExactly<ArgumentException>(() => new Plane(Vector3.Zero, center));
        }
    }
}
