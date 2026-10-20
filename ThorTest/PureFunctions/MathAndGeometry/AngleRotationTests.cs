using ADModUtils;
using GTA.Math;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Plane = ADModUtils.Plane;
using System.Collections.Generic;
using System;

namespace GTAVMods.Tests.PureFunctions.MathAndGeometry
{
    [TestClass, TestCategory("PureFunction")]
    public class AngleRotationTests
    {
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
    }
}
