using System;
using System.Collections.Generic;
using ADModUtils;
using GTA.Math;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Plane = ADModUtils.Plane;

[assembly: DoNotParallelize]

namespace ModUtilsTest
{
    internal sealed class FixedRandom : System.Random
    {
        private readonly int choice;
        private readonly double sample;
        public readonly List<int> Bounds = new List<int>();
        public FixedRandom(int choice = 1, double sample = 0.5) { this.choice = choice; this.sample = sample; }
        public override int Next(int maxValue) { Bounds.Add(maxValue); return choice; }
        public override int Next(int minValue, int maxValue) { Assert.AreEqual(0, minValue); return Next(maxValue); }
        public override double NextDouble() => sample;
    }

    [TestClass]
    [TestCategory("PureFunction")]
    public class Utilities_Test
    {
        [TestMethod]
        public void RequiredValuesRejectMissingOrEmptyConfigurationWithTheParameterName()
        {
            Assert.AreEqual("clip", Require.NotNull("clip", "path"));
            Assert.AreEqual("path", Assert.ThrowsExactly<ArgumentNullException>(() => Require.NotNull<string>(null, "path")).ParamName);
            CollectionAssert.AreEqual(new[] { 3, 7 }, Require.NonEmpty(new[] { 3, 7 }, "choices"));
            Assert.AreEqual("choices", Assert.ThrowsExactly<ArgumentNullException>(() => Require.NonEmpty<int>(null, "choices")).ParamName);
            Assert.AreEqual("choices", Assert.ThrowsExactly<ArgumentException>(() => Require.NonEmpty(Array.Empty<int>(), "choices")).ParamName);
        }

        [TestMethod]
        public void NonnegativeDurationsAcceptZeroAndRejectNegativeValues()
        {
            Assert.AreEqual(0, Require.NonNegative(0, "interval"));
            Assert.AreEqual(1000, Require.NonNegative(1000, "interval"));
            Assert.AreEqual("interval", Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => Require.NonNegative(-1, "interval")).ParamName);
        }

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

        [TestMethod]
        public void AngleUnitsConvertKnownQuarterAndHalfTurns()
        {
            Assert.AreEqual(180f, Utilities.Math.RadiansToDegrees((float)System.Math.PI), 0.001f);
            Assert.AreEqual((float)(System.Math.PI / 2), Utilities.Math.DegreesToRadians(90), 0.00001f);
        }

        [TestMethod]
        public void SignedPlanarAnglesDistinguishClockwiseAndCounterclockwiseTurns()
        {
            Assert.AreEqual(90f, Utilities.Math.Angle(new Vector2(1, 0), new Vector2(0, 1)), 0.00001f);
            Assert.AreEqual(-90f, Utilities.Math.Angle(new Vector2(0, 1), new Vector2(1, 0)), 0.00001f);
        }

        [TestMethod]
        public void DirectionRotationPreservesRollAndSignsPitchAndHeading()
        {
            Vector3 rotation = Utilities.Math.DirectionToRotation(Vector3.UnitX, 12);
            Assert.AreEqual(0f, rotation.X);
            Assert.AreEqual(12f, rotation.Y);
            Assert.AreEqual(-90f, rotation.Z, 0.001f);
            Assert.AreEqual(45f, Utilities.Math.DirectionToRotation(new Vector3(0, 1, 1)).X, 0.001f);
            Assert.AreEqual(-45f, Utilities.Math.DirectionToRotation(new Vector3(0, 1, -1)).X, 0.001f);
        }

        [TestMethod]
        public void DirectionQuaternionRepresentsAKnownClockwiseQuarterTurn()
        {
            Quaternion turn = Utilities.Math.DirectionToQuaternion(Vector3.UnitY * 5, Vector3.UnitX * 2);
            Assert.AreEqual(0f, turn.X, 0.00001f);
            Assert.AreEqual(0f, turn.Y, 0.00001f);
            Assert.AreEqual(-(float)System.Math.Sqrt(0.5), turn.Z, 0.00001f);
            Assert.AreEqual((float)System.Math.Sqrt(0.5), turn.W, 0.00001f);
            Assert.AreEqual(-90f, Utilities.Math.QuaternionToEulerAngles(turn).Z, 0.001f);
        }

        [TestMethod]
        public void EulerConversionHandlesIdentityAndBothPitchLimits()
        {
            Assert.AreEqual(Vector3.Zero, Utilities.Math.QuaternionToEulerAngles(Quaternion.Identity));
            Assert.AreEqual(90f, Utilities.Math.QuaternionToEulerAngles(new Quaternion(0, 1, 0, 1)).Y, 0.001f);
            Assert.AreEqual(-90f, Utilities.Math.QuaternionToEulerAngles(new Quaternion(0, -1, 0, 1)).Y, 0.001f);
        }

        [TestMethod]
        public void MissingSourceOrTargetDirectionProducesNoRotation()
        {
            Assert.AreEqual(Quaternion.Identity, Utilities.Math.DirectionToQuaternion(Vector3.Zero, Vector3.UnitY));
            Assert.AreEqual(Quaternion.Identity, Utilities.Math.DirectionToQuaternion(Vector3.UnitY, Vector3.Zero));
        }

        [TestMethod]
        [DataRow(1f, 0f, 0f)]
        [DataRow(0f, 1f, 0f)]
        public void OppositeDirectionsProduceAHalfTurnAroundAPerpendicularAxis(float x, float y, float z)
        {
            var forward = new Vector3(x, y, z);
            var rotation = Utilities.Math.DirectionToQuaternion(forward * 5, -forward * 3);
            Assert.AreEqual(1f, rotation.Length(), 0.00001f);
            Assert.IsTrue((rotation * forward + forward).Length() < 0.00001f,
                "Applying the quaternion must reverse the forward vector.");
        }

        [TestMethod]
        [DataRow(1f, 0f, 0f, 1f, 1f, 0f)]
        [DataRow(1f, 2f, 2f, 2f, 1f, 2f)]
        [DataRow(1f, 2f, 2f, -2f, 1f, -2f)]
        public void DirectionQuaternionRotatesAcuteAndObtuseNonAxisDirections(
            float fx, float fy, float fz, float tx, float ty, float tz)
        {
            var from = new Vector3(fx, fy, fz);
            var to = new Vector3(tx, ty, tz);
            var rotation = Utilities.Math.DirectionToQuaternion(from * 7, to * 3);
            Assert.AreEqual(1f, rotation.Length(), 0.00001f);
            Assert.IsTrue((rotation * from.Normalized - to.Normalized).Length() < 0.00001f,
                "The rotation must map the source direction onto the target, independent of vector magnitude.");
        }

        [TestMethod]
        [DataRow(0.5f, 0.5f, 90f)]
        [DataRow(-0.5f, -0.5f, -90f)]
        public void EulerAnglesRepresentKnownCyclicAxisRotations(float x, float y, float roll)
        {
            // A 120-degree turn about (1,1,1) cycles X->Y->Z; reflecting X/Y reverses roll.
            // These are the same orientations as +/-90 degrees of roll followed by 90 degrees of yaw.
            var euler = Utilities.Math.QuaternionToEulerAngles(new Quaternion(x, y, 0.5f, 0.5f));
            Assert.AreEqual(roll, euler.X, 0.001f);
            Assert.AreEqual(0f, euler.Y, 0.001f);
            Assert.AreEqual(90f, euler.Z, 0.001f);
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

        [TestMethod]
        [DataRow("1.0.1158.13", true)]
        [DataRow("1.0.1158.16", true)]
        [DataRow("1.0.1013.34", false)]
        [DataRow("1.0.1158.14", false)]
        [DataRow("1.0.3889.0", false)]
        [DataRow(null, false)]
        public void CompatibilityDoesNotAssumeOtherBuildsAreVerified(string version, bool expected)
        {
            Assert.AreEqual(expected, EnhancedCompatibility.IsAssessedBuild(version == null ? null : new Version(version)));
        }

        [TestMethod]
        public void EnhancedApiVersionIsConvertedToExecutableVersion()
        {
            Assert.AreEqual(new Version(1, 0, 1158, 16), EnhancedCompatibility.ExecutableVersion(new Version(2, 0, 1158, 16), true));
            Assert.AreEqual(new Version(1, 0, 3889, 0), EnhancedCompatibility.ExecutableVersion(new Version(1, 0, 3889, 0), false));
            Assert.IsNull(EnhancedCompatibility.ExecutableVersion(null, true));
        }
    }
}
