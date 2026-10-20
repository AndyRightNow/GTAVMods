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
        public void BoxCornersEdgesAndFacesFormAnAxisAlignedCuboidWithNoDiagonalEdges() {
            using (var fixture = new ConsoleFixture(false)) {
                var center = new Vector3(10, 20, 30);
                var min = new Vector3(-1, -2, -3);
                var max = new Vector3(1, 2, 3);
                var rectangle = new Rectangle3D(center, min, max, fixture.Functions);
                Assert.AreEqual(center, rectangle.Center);
                Assert.AreEqual(min, rectangle.Min);
                Assert.AreEqual(max, rectangle.Max);
                Assert.AreEqual(8, rectangle.Corners.Count);
                foreach (var corner in rectangle.Corners) {
                    Assert.AreEqual(center + new Vector3(corner.Key[0] == '0' ? -1 : 1,
                        corner.Key[1] == '0' ? -2 : 2, corner.Key[2] == '0' ? -3 : 3), corner.Value);
                }
                Assert.AreEqual(12, rectangle.Edges.Count);
                var cornerNames = rectangle.Corners.ToDictionary(corner => corner.Value, corner => corner.Key);
                var uniqueEdges = new System.Collections.Generic.HashSet<string>();
                foreach (var edge in rectangle.Edges) {
                    var delta = edge.Point2 - edge.Point1;
                    Assert.AreEqual(1, new[] { delta.X, delta.Y, delta.Z }.Count(axis => axis != 0));
                    var endpoints = new[] { cornerNames[edge.Point1], cornerNames[edge.Point2] };
                    Array.Sort(endpoints, StringComparer.Ordinal);
                    Assert.IsTrue(uniqueEdges.Add(string.Join("-", endpoints)), "Each cuboid edge appears exactly once.");
                }
                Assert.AreEqual(6, rectangle.Faces.Count);
                var boundaryPlanes = new System.Collections.Generic.HashSet<string>();
                foreach (var face in rectangle.Faces) {
                    Assert.AreEqual(face.BottomLeft + face.TopRight, face.TopLeft + face.BottomRight);
                    var corners = new[] { face.BottomLeft, face.BottomRight, face.TopLeft, face.TopRight };
                    Assert.AreEqual(4, corners.Select(corner => cornerNames[corner]).Distinct().Count());
                    var planes = new[] {
                        corners.All(corner => corner.X == center.X + min.X) ? "x-min" : null,
                        corners.All(corner => corner.X == center.X + max.X) ? "x-max" : null,
                        corners.All(corner => corner.Y == center.Y + min.Y) ? "y-min" : null,
                        corners.All(corner => corner.Y == center.Y + max.Y) ? "y-max" : null,
                        corners.All(corner => corner.Z == center.Z + min.Z) ? "z-min" : null,
                        corners.All(corner => corner.Z == center.Z + max.Z) ? "z-max" : null
                    }.Where(plane => plane != null).ToArray();
                    Assert.AreEqual(1, planes.Length, "Each face lies on one boundary plane.");
                    Assert.IsTrue(boundaryPlanes.Add(planes[0]), "Each cuboid boundary plane appears exactly once.");
                }
                CollectionAssert.AreEquivalent(new[] { "x-min", "x-max", "y-min", "y-max", "z-min", "z-max" },
                    boundaryPlanes.ToArray());
                Assert.AreSame(rectangle, rectangle.DrawWireFrame(Color.Blue));
                Assert.AreEqual(12, fixture.Native.Calls.Count(c => c.Hash == Hash.DRAW_LINE));
                fixture.Native.Calls.Clear();
                Assert.AreSame(rectangle, rectangle.DrawWireFrame(Color.Red, true));
                Assert.AreEqual(24, fixture.Native.Calls.Count(c => c.Hash == Hash.DRAW_LINE));
                fixture.Native.Calls.Clear();
                Assert.AreSame(rectangle, rectangle.Draw(Color.Purple));
                Assert.AreEqual(6, fixture.Native.Calls.Count(c => c.Hash == Hash.DRAW_BOX));
                Assert.AreSame(rectangle, rectangle.DrawDebug(Color.Yellow));
                CollectionAssert.AreEquivalent(rectangle.Corners.Keys.ToArray(), fixture.Platform.Text.Select(t => t.Text).ToArray());
                Assert.IsTrue(fixture.Platform.Text.All(t => t.Position == new Point(300, 450) && t.Color == Color.Yellow && t.Scale == .15f));
            }
        }

        [TestMethod]
        public void QuaternionRotationKeepsCenterAndRebuildsEdgesAndFaces() {
            using (var fixture = new ConsoleFixture(false)) {
                var center = new Vector3(10, 20, 30);
                var rectangle = new Rectangle3D(center, new Vector3(-1, -2, -3), new Vector3(1, 2, 3), fixture.Functions);
                var original = rectangle.Corners.ToDictionary(c => c.Key, c => c.Value);
                Assert.AreSame(rectangle, rectangle.Rotate(new Quaternion(0, 0, (float)Math.Sqrt(.5), (float)Math.Sqrt(.5))));
                foreach (var corner in rectangle.Corners) {
                    var prior = original[corner.Key] - center;
                    var expected = center + new Vector3(-prior.Y, prior.X, prior.Z);
                    Assert.AreEqual(expected.X, corner.Value.X, .00001);
                    Assert.AreEqual(expected.Y, corner.Value.Y, .00001);
                    Assert.AreEqual(expected.Z, corner.Value.Z, .00001);
                }
                Assert.AreEqual(rectangle.Corners["000"], rectangle.Edges[0].Point1);
                Assert.AreEqual(rectangle.Corners["001"], rectangle.Edges[0].Point2);
                Assert.AreEqual(rectangle.Corners["000"], rectangle.Faces[0].BottomLeft);
            }
        }

        [TestMethod]
        public void ConvenienceConstructorsPreserveGeometryWithoutCallingTheEngine() {
            var line = new Line3D(Vector3.Zero, Vector3.WorldUp);
            Assert.AreEqual(Vector3.Zero, line.Point1);
            Assert.AreEqual(Vector3.WorldUp, line.Point2);
            var face = new Face3D(Vector3.Zero, new Vector3(1, 1, 0), Vector3.WorldNorth, Vector3.WorldEast);
            Assert.AreEqual(Vector3.WorldNorth, face.TopLeft);
            var rectangle = new Rectangle3D(Vector3.Zero, -Vector3.One, Vector3.One);
            Assert.AreEqual(12, rectangle.Edges.Count);
        }
    }
}
