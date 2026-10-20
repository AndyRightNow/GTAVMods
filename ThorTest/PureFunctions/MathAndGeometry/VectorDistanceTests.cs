using ADModUtils;
using GTA.Math;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Plane = ADModUtils.Plane;
using System.Collections.Generic;
using System;

namespace GTAVMods.Tests.PureFunctions.MathAndGeometry
{
    [TestClass, TestCategory("PureFunction")]
    public class VectorDistanceTests
    {
        [TestMethod]
        [DataRow(1f, 0f, 0f)]
        [DataRow(0f, 1f, 0f)]
        [DataRow(1f, 2f, 0f)]
        [DataRow(1f, 2f, 3f)]
        public void PerpendicularVectorIsOrthogonalAndUnitLength(float x, float y, float z)
        {
            var normal = new Vector3(x, y, z);
            var result = Utilities.Math.PerpendicularTo(normal, 0.5f, 0.5f, 0.5f);
            Assert.AreEqual(0f, Vector3.Dot(normal, result), 0.00001f);
            Assert.AreEqual(1f, result.Length(), 0.00001f);
        }

        [TestMethod]
        [DataRow(0f, 0f, 0f)]
        [DataRow(0f, 0f, 1f)]
        [DataRow(1f, 0f, 0f)]
        [DataRow(0f, 1f, 0f)]
        public void ZeroRandomSamplesStillProduceAUsablePerpendicular(float x, float y, float z)
        {
            var normal = new Vector3(x, y, z);
            var result = Utilities.Math.PerpendicularTo(normal, 0, 0, 0);
            Assert.AreEqual(1f, result.Length(), 0.00001f);
            Assert.AreEqual(0f, Vector3.Dot(normal, result), 0.00001f);
        }

        [TestMethod]
        public void UnnormalizedPerpendicularPreservesSampleMagnitude()
        {
            // The XZ plane is perpendicular to Y. Two half-unit samples have length sqrt(1/2).
            var raw = Utilities.Math.PerpendicularTo(Vector3.UnitY, 0.5f, 0.5f, 0.5f, false);
            Assert.AreEqual(0f, Vector3.Dot(Vector3.UnitY, raw), 0.00001f);
            Assert.AreEqual((float)System.Math.Sqrt(0.5), raw.Length(), 0.00001f);
            var unit = Utilities.Math.PerpendicularTo(Vector3.UnitY, 0.5f, 0.5f, 0.5f);
            Assert.AreEqual(1f, unit.Length(), 0.00001f);
            Assert.IsTrue((unit - raw.Normalized).Length() < 0.00001f);
        }

        [TestMethod]
        public void DistanceToleranceIncludesBothBoundaries()
        {
            Assert.IsTrue(Utilities.Math.CloseTo(8, 10, 2));
            Assert.IsTrue(Utilities.Math.CloseTo(12, 10, 2));
            Assert.IsFalse(Utilities.Math.CloseTo(7.99f, 10, 2));
            Assert.IsFalse(Utilities.Math.CloseTo(12.01f, 10, 2));
            Assert.IsTrue(Utilities.Math.CloseTo(Vector3.Zero, new Vector3(3, 4, 0), 5));
            Assert.IsFalse(Utilities.Math.CloseTo(Vector3.Zero, new Vector3(3, 4, 0), 4.99f));
            var first = new Vector3(10, -20, 30);
            var second = new Vector3(13, -16, 30);
            Assert.IsTrue(Utilities.Math.CloseTo(first, second, 5), "Translation must not change the five-unit separation.");
            Assert.IsFalse(Utilities.Math.CloseTo(second, first, 4.99f));
        }
    }
}
