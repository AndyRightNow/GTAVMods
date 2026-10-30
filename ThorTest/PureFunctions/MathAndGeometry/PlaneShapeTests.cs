using ADModUtils;

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
        public void PlaneProjectsSignedCoordinatesAndIgnoresNormalDisplacement()
        {
            var center = new Vector3(10, 20, 30);
            var plane = new Plane(Vector3.UnitZ * 2, center);
            Assert.AreEqual(1f, plane.XAxis.Length(), 0.00001f);
            Assert.AreEqual(1f, plane.YAxis.Length(), 0.00001f);
            Assert.AreEqual(0f, Vector3.Dot(plane.XAxis, plane.YAxis), 0.00001f);
            Assert.AreEqual(0f, Vector3.Dot(plane.Normal, plane.XAxis), 0.00001f);
            Assert.AreEqual(0f, Vector3.Dot(plane.Normal, plane.YAxis), 0.00001f);
            // A plane may choose any orthonormal basis. Coordinates are measured in that basis,
            // while projection back to world space must remove only normal displacement.
            var coordinates = plane.GetPlaneCoord(center + 2 * plane.XAxis - 4 * plane.YAxis + new Vector3(0, 0, 50));
            Assert.AreEqual(2f, coordinates.X, 0.00001f);
            Assert.AreEqual(-4f, coordinates.Y, 0.00001f);
            var negative = plane.GetPlaneCoord(center - 2 * plane.XAxis + 4 * plane.YAxis - new Vector3(0, 0, 50));
            Assert.AreEqual(-2f, negative.X, 0.00001f);
            Assert.AreEqual(4f, negative.Y, 0.00001f);
            var world = plane.GetWorldCoord(plane.GetPlaneCoord(new Vector3(13, 19, 80)));
            Assert.AreEqual(13f, world.X, 0.00001f);
            Assert.AreEqual(19f, world.Y, 0.00001f);
            Assert.AreEqual(30f, world.Z, 0.00001f);
            Assert.AreEqual(Vector2.Zero, plane.GetPlaneCoord(center));
            Assert.AreEqual(0f, Vector3.Dot(world - center, plane.Normal), 0.00001f,
                "A reconstructed point must stay in the plane even when its normal is not unit length.");
            Assert.ThrowsExactly<ArgumentException>(() => new Plane(Vector3.Zero, center));
        }
    }
}
