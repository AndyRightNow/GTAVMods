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
    public class Utilities_Test
    {
        [TestMethod]
        public void SelectionUsesWholeInputAndFiltersBeforeChoosing()
        {
            var random = new FixedRandom(2);
            Assert.AreEqual("last", Utilities.Random.PickOne(new[] { "first", "middle", "last" }, random));
            Assert.AreEqual("first", Utilities.Random.PickOne(new[] { "first", "last" }, new FixedRandom(0)));
            CollectionAssert.AreEqual(new[] { 3 }, random.Bounds);
            var filtered = new FixedRandom(1);
            var visited = new List<int>();
            Assert.AreEqual(8, Utilities.Random.PickOneIf(new[] { 1, 4, 3, 8 }, value => { visited.Add(value); return value % 2 == 0; }, filtered));
            CollectionAssert.AreEqual(new[] { 1, 4, 3, 8 }, visited);
            CollectionAssert.AreEqual(new[] { 2 }, filtered.Bounds);
            Assert.AreEqual(7, Utilities.Random.PickOne(new[] { 7 }));
            Assert.AreEqual(4, Utilities.Random.PickOneIf(new[] { 1, 4 }, value => value == 4));
        }

        [TestMethod]
        public void EmptySelectionsDoNotReadRandomnessAndInvalidCallbacksAreRejected()
        {
            var random = new FixedRandom();
            Assert.IsNull(Utilities.Random.PickOne<string>(null, random));
            Assert.AreEqual(0, Utilities.Random.PickOne(Array.Empty<int>(), random));
            Assert.AreEqual(0, Utilities.Random.PickOneIf<int>(null, value => true, random));
            Assert.AreEqual(0, Utilities.Random.PickOneIf(Array.Empty<int>(), value => true, random));
            Assert.IsNull(Utilities.Random.PickOneIf(new[] { "x" }, value => value == "y", random));
            Assert.HasCount(0, random.Bounds);
            Assert.ThrowsExactly<ArgumentNullException>(() => Utilities.Random.PickOne(new[] { 1 }, null));
            Assert.ThrowsExactly<ArgumentNullException>(() => Utilities.Random.PickOneIf(new[] { 1 }, null));
            Assert.ThrowsExactly<ArgumentNullException>(() => Utilities.Random.RandomNegation(null));
            Assert.ThrowsExactly<ArgumentNullException>(() => Utilities.Random.NextFloat(null));
        }

        [TestMethod]
        public void RandomSignsAndUnsignedMagnitudeFollowRequestedBounds()
        {
            Assert.AreEqual(-1, Utilities.Random.RandomNegation(new FixedRandom(0)));
            Assert.AreEqual(1, Utilities.Random.RandomNegation(new FixedRandom(1)));
            Assert.AreEqual(-0.75f, Utilities.Random.NextFloat(new FixedRandom(0, 0.75)));
            var unsigned = new FixedRandom(0, 0.75);
            Assert.AreEqual(0.75f, Utilities.Random.NextFloat(unsigned, false));
            Assert.HasCount(0, unsigned.Bounds);
            CollectionAssert.Contains(new[] { -1, 1 }, Utilities.Random.RandomNegation());
            float value = Utilities.Random.NextFloat(false);
            Assert.IsTrue(value >= 0 && value <= 1);
        }

        [TestMethod]
        [DataRow(1f, 0f, 0f)]
        [DataRow(0f, 1f, 0f)]
        [DataRow(1f, 2f, 0f)]
        [DataRow(1f, 2f, 3f)]
        public void PerpendicularVectorIsOrthogonalAndHasRequestedScale(float x, float y, float z)
        {
            var normal = new Vector3(x, y, z);
            var result = Utilities.Math.RandomVectorPerpendicularTo(normal, new FixedRandom());
            Assert.AreEqual(0f, Vector3.Dot(normal, result), 0.00001f);
            Assert.AreEqual(1f, result.Length(), 0.00001f);
            var unscaled = Utilities.Math.RandomVectorPerpendicularTo(normal, new FixedRandom(), false);
            Assert.AreEqual(0f, Vector3.Dot(normal, unscaled), 0.00001f);
            Assert.IsTrue(unscaled.Length() > 0);
            Assert.AreEqual(0f, Vector3.Dot(normal, Utilities.Math.RandomVectorPerpendicularTo(normal)), 0.0001f);
        }

        [TestMethod]
        [DataRow(0f, 0f, 0f)]
        [DataRow(0f, 0f, 1f)]
        [DataRow(1f, 0f, 0f)]
        public void ZeroRandomSamplesStillProduceAUsablePerpendicular(float x, float y, float z)
        {
            var normal = new Vector3(x, y, z);
            var result = Utilities.Math.RandomVectorPerpendicularTo(normal, new FixedRandom(0, 0));
            Assert.AreEqual(1f, result.Length(), 0.00001f);
            Assert.AreEqual(0f, Vector3.Dot(normal, result), 0.00001f);
        }

        [TestMethod]
        public void PlaneProjectsSignedCoordinatesAndIgnoresNormalDisplacement()
        {
            var center = new Vector3(10, 20, 30);
            var plane = new Plane(Vector3.UnitZ * 2, center, new FixedRandom(1, 0.5));
            Assert.AreEqual(center, plane.Center);
            Assert.AreEqual(Vector3.UnitZ * 2, plane.Normal);
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
            Assert.AreEqual(Vector3.Zero, new Plane(Vector3.UnitX, Vector3.Zero).GetWorldCoord(Vector2.Zero));
            Assert.ThrowsExactly<ArgumentException>(() => new Plane(Vector3.Zero, center));
        }

        [TestMethod]
        public void AnglesAndRotationsMatchKnownAxes()
        {
            Assert.AreEqual(180f, Utilities.Math.RadiansToDegrees((float)System.Math.PI), 0.001f);
            Assert.AreEqual((float)(System.Math.PI / 2), Utilities.Math.DegreesToRadians(90), 0.00001f);
            Assert.AreEqual(90f, Utilities.Math.Angle(new Vector2(1, 0), new Vector2(0, 1)), 0.00001f);
            Assert.AreEqual(-90f, Utilities.Math.Angle(new Vector2(0, 1), new Vector2(1, 0)), 0.00001f);
            Vector3 rotation = Utilities.Math.DirectionToRotation(Vector3.UnitX, 12);
            Assert.AreEqual(0f, rotation.X);
            Assert.AreEqual(12f, rotation.Y);
            Assert.AreEqual(-90f, rotation.Z, 0.001f);
            Assert.AreEqual(45f, Utilities.Math.DirectionToRotation(new Vector3(0, 1, 1)).X, 0.001f);
            Assert.AreEqual(-45f, Utilities.Math.DirectionToRotation(new Vector3(0, 1, -1)).X, 0.001f);
            Quaternion turn = Utilities.Math.DirectionToQuaternion(Vector3.UnitY * 5, Vector3.UnitX * 2);
            Assert.AreEqual(0f, turn.X, 0.00001f);
            Assert.AreEqual(0f, turn.Y, 0.00001f);
            Assert.AreEqual(-(float)System.Math.Sqrt(0.5), turn.Z, 0.00001f);
            Assert.AreEqual((float)System.Math.Sqrt(0.5), turn.W, 0.00001f);
            Assert.AreEqual(Vector3.Zero, Utilities.Math.QuaternionToEulerAngles(Quaternion.Identity));
            Assert.AreEqual(-90f, Utilities.Math.QuaternionToEulerAngles(turn).Z, 0.001f);
            Assert.AreEqual(90f, Utilities.Math.QuaternionToEulerAngles(new Quaternion(0, 1, 0, 1)).Y, 0.001f);
            Assert.AreEqual(-90f, Utilities.Math.QuaternionToEulerAngles(new Quaternion(0, -1, 0, 1)).Y, 0.001f);
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
        public void DistanceToleranceIncludesBothBoundaries()
        {
            Assert.IsTrue(Utilities.Math.CloseTo(8, 10, 2));
            Assert.IsTrue(Utilities.Math.CloseTo(12, 10, 2));
            Assert.IsFalse(Utilities.Math.CloseTo(7.99f, 10, 2));
            Assert.IsFalse(Utilities.Math.CloseTo(12.01f, 10, 2));
            Assert.IsTrue(Utilities.Math.CloseTo(Vector3.Zero, new Vector3(3, 4, 0), 5));
            Assert.IsFalse(Utilities.Math.CloseTo(Vector3.Zero, new Vector3(3, 4, 0), 4.99f));
            string left = "left", right = "right";
            Utilities.Swap(ref left, ref right);
            Assert.AreEqual("right", left);
            Assert.AreEqual("left", right);
        }

        [TestMethod]
        public void TimerFiresAtBoundaryWithoutCatchUpBurstsAndSurvivesGameTimeWrap()
        {
            int now = int.MaxValue - 50, calls = 0;
            var timer = new Utilities.Timer(100, () => calls++, () => now);
            now = unchecked(now + 99); timer.OnTick(); Assert.AreEqual(0, calls);
            now = unchecked(now + 1); timer.OnTick(); timer.OnTick(); Assert.AreEqual(1, calls);
            now = unchecked(now + 1000); timer.OnTick(); timer.OnTick(); Assert.AreEqual(2, calls);
            now = unchecked(now + 100); timer.OnTick(); Assert.AreEqual(3, calls);
            Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new Utilities.Timer(-1, () => { }, () => 0));
            Assert.ThrowsExactly<ArgumentNullException>(() => new Utilities.Timer(1, null, () => 0));
            Assert.ThrowsExactly<ArgumentNullException>(() => new Utilities.Timer(1, () => { }, null));
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
