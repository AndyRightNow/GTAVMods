using System;
using System.Drawing;
using System.Linq;
using DeveloperConsole;
using GTA.Math;
using GTA.Native;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ModUtilsTest {
    [TestClass]
    public class ConsoleGeometryTests {
        [TestMethod]
        [TestCategory("PureFunction")]
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
        [TestCategory("PureFunction")]
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
        [TestCategory("PlayerIntegration")]
        public void PlayerCanDrawAndLabelARotatedPracticeBoundaryThroughF8Expressions() {
            using (var fixture = new ConsoleFixture()) {
                fixture.Evaluate("new Line3D(new Vector3(8,17,26), new Vector3(8,17,35)).Draw(Color.FromArgb(67,13,29,41)); new Face3D(new Vector3(4,5,6), new Vector3(10,11,6), new Vector3(4,11,6), new Vector3(10,5,6)).Draw(Color.FromArgb(67,13,29,41)); return \"Boundary drawn\"");
                Assert.IsTrue(fixture.ShowsOutput("Boundary drawn"));
                CollectionAssert.AreEqual(new object[] {
                    8f, 17f, 26f, 8f, 17f, 35f, (byte)13, (byte)29, (byte)41, (byte)67
                }, fixture.Native.Calls.Single(c => c.Hash == Hash.DRAW_LINE).Arguments);
                CollectionAssert.AreEqual(new object[] {
                    4f, 5f, 6f, 10f, 11f, 6f, (byte)13, (byte)29, (byte)41, (byte)67
                }, fixture.Native.Calls.Single(c => c.Hash == Hash.DRAW_BOX).Arguments);
                fixture.Native.Calls.Clear();
                fixture.Evaluate("var box = new Rectangle3D(new Vector3(10,20,30), new Vector3(-1,-2,-3), new Vector3(1,2,3)); box.Rotate(FUNCTIONS.GetEntityQuaternion(21)); box.DrawWireFrame(Color.Blue); box.DrawWireFrame(Color.Red, true); box.Draw(Color.Purple); box.DrawDebug(Color.Yellow); return \"Eight corners labeled\"");
                Assert.IsTrue(fixture.ShowsOutput("Eight corners labeled"));
                Assert.AreEqual(36, fixture.Native.Calls.Count(c => c.Hash == Hash.DRAW_LINE));
                Assert.AreEqual(6, fixture.Native.Calls.Count(c => c.Hash == Hash.DRAW_BOX));
                var edges = fixture.Native.Calls.Where(c => c.Hash == Hash.DRAW_LINE).Take(12).ToArray();
                var distinctEdges = new System.Collections.Generic.HashSet<string>();
                foreach (var edge in edges) {
                    var a = edge.Arguments;
                    Assert.AreEqual(1, new[] { (float)a[0] != (float)a[3], (float)a[1] != (float)a[4], (float)a[2] != (float)a[5] }.Count(changed => changed),
                        "Each wireframe edge must follow a single cuboid axis.");
                    var endpoints = new[] { string.Join(",", a.Take(3)), string.Join(",", a.Skip(3).Take(3)) };
                    Array.Sort(endpoints, StringComparer.Ordinal);
                    Assert.IsTrue(distinctEdges.Add(string.Join(" / ", endpoints)), "No boundary edge may be drawn twice.");
                }
                Assert.AreEqual(21, fixture.Outputs.QuaternionEntity);
                var labels = fixture.Platform.DrawHistory.Where(t => t.Color == Color.Yellow).ToArray();
                // Geometry draws during expression execution, before the next console frame.
                CollectionAssert.AreEquivalent(new[] { "000", "001", "010", "011", "100", "101", "110", "111" },
                    labels.Select(t => t.Text).ToArray());
                Assert.IsTrue(labels.All(t => t.Position == new Point(300, 450) && t.Scale == .15f));
                Assert.AreEqual(new Vector3(9, 18, 33), fixture.Outputs.Projection);
            }
        }
    }
}
