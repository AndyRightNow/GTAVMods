using Control = GTA.Control;
using DeveloperConsole;
using GTA.Math;
using GTA.Native;
using GTA;
using GTAVMods.Tests.Support;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using System;
using static GTAVMods.Tests.Support.ConsoleAssertions;

namespace GTAVMods.Tests.PlayerIntegration.ConsoleOperations
{
    [TestClass, TestCategory("PlayerIntegration")]
    public class DrawingOutputTests
    {
        [TestMethod]
        public void CommandOutputUsesTheCurrentLocalClockWhenThePlayerSubmitsIt() {
            using (var scene = new ConsoleFixture()) {
                scene.Platform.UseSystemClock = true;
                var before = DateTime.Now;
                scene.Submit("eval 45");
                var after = DateTime.Now;
                var result = scene.Platform.Text.Single(t => t.Text.EndsWith(": 45")).Text;
                var expectedPrefixes = Enumerable.Range(0, (int)Math.Ceiling((after - before).TotalSeconds) + 1)
                    .Select(second => before.AddSeconds(second).ToString("HH:mm:ss") + ": ");
                Assert.IsTrue(expectedPrefixes.Any(prefix => result.StartsWith(prefix)));
            }
        }

        [TestMethod]
        public void ConsoleMessagesRetainSeverityAndLongLinesContinueAtMeasuredPixelPositions() {
            using (var scene = new ConsoleFixture()) {
                scene.Evaluate("CONSOLE.PrintWarning(\"practice save\"); CONSOLE.PrintError(\"example failure\"); CONSOLE.PrintLine(\"note\", Color.Purple); CONSOLE.PrintDebug(\"hidden debug\"); CONSOLE.Debug = true; CONSOLE.PrintDebug(\"debug visible\")");
                Assert.IsTrue(scene.Platform.Text.Any(t => t.Text.EndsWith("[Warning] practice save") && t.Color == Color.FromArgb(ConsoleSettings.TextAlpha, Color.Yellow)));
                Assert.IsTrue(scene.Platform.Text.Any(t => t.Text.EndsWith("[Error] example failure") && t.Color == Color.FromArgb(ConsoleSettings.TextAlpha, Color.Red)));
                Assert.IsTrue(scene.Platform.Text.Any(t => t.Text.EndsWith(": note") && t.Color == Color.Purple));
                Assert.IsFalse(scene.Platform.Text.Any(t => t.Text.EndsWith("[Debug] hidden debug")));
                Assert.IsTrue(scene.Platform.Text.Any(t => t.Text == "12:34:56: [Debug] debug visible"
                    && t.Color == Color.FromArgb(ConsoleSettings.TextAlpha, Color.LightGray)));
                scene.Evaluate("CONSOLE.Debug = false; CONSOLE.ClearLines(); CONSOLE.PrintLine(new string('x', 200))");
                var chunks = scene.Platform.Text.Take(3).ToArray();
                CollectionAssert.AreEqual(new[] { 99, 99, 12 }, chunks.Select(t => t.Text.Length).ToArray());
                CollectionAssert.AreEqual(new[] { 5, 17, 29 }, chunks.Select(t => t.Position.X).ToArray());
                scene.Evaluate("CONSOLE.ClearLines(); CONSOLE.RemoveLastLine(); CONSOLE.PrintLine(\"temporary\"); CONSOLE.RemoveLastLine(); CONSOLE.PrintLine(new string('x', 89))");
                Assert.AreEqual(99, scene.Platform.Text.First().Text.Length);
                Assert.IsFalse(scene.Shows("temporary"));
            }
        }

        [TestMethod]
        [DataRow("DEV", true)] [DataRow("1.2.3", false)]
        public void DevelopmentBuildShowsExpressionDiagnosticsWhileReleaseShowsOnlyTheResult(string version, bool debug) {
            var prior = ConsoleSettings.Version;
            try {
                ConsoleSettings.Version = version;
                using (var scene = new ConsoleFixture(debug: null)) {
                    scene.Submit("eval 25");
                    Assert.IsTrue(scene.ShowsOutput("25"));
                    Assert.AreEqual(debug, scene.Shows("Found token"));
                }
            }
            finally { ConsoleSettings.Version = prior; }
        }

        [TestMethod]
        public void PlayerCanAnnotatePracticeGeometryWithExactCoordinatesAndColors() {
            using (var scene = new ConsoleFixture()) {
                scene.Evaluate("Color c = Color.FromArgb(7, 11, 13, 17); FUNCTIONS.DrawLine(new Vector3(1,2,3), new Vector3(4,5,6), c); FUNCTIONS.DrawBox(new Vector3(1,2,3), new Vector3(4,5,6), c); FUNCTIONS.DrawPoly(new Vector3(1,2,3), new Vector3(4,5,6), new Vector3(7,8,9), c); return \"Practice boundary drawn\"");
                Assert.IsTrue(scene.ShowsOutput("Practice boundary drawn"));
                Command(scene, Hash.DRAW_LINE, 1f, 2f, 3f, 4f, 5f, 6f, (byte)11, (byte)13, (byte)17, (byte)7);
                Command(scene, Hash.DRAW_BOX, 1f, 2f, 3f, 4f, 5f, 6f, (byte)11, (byte)13, (byte)17, (byte)7);
                Command(scene, Hash.DRAW_POLY, 1f, 2f, 3f, 4f, 5f, 6f, 7f, 8f, 9f, (byte)11, (byte)13, (byte)17, (byte)7);
            }
        }

        [TestMethod]
        public void ScreenshotSetupChangesHudAndTextStylingWithoutChangingPlayerIdentity() {
            using (var scene = new ConsoleFixture()) {
                scene.Native.Returns(Hash.GET_PLAYER_INVINCIBLE, true);
                scene.Evaluate("FUNCTIONS.DisplayRadar(false); FUNCTIONS.DisplayHud(false); FUNCTIONS.ShowMouseThisFrame(); FUNCTIONS.SetTextEdge(2, Color.FromArgb(7,11,13,17)); FUNCTIONS.SetTextDropShadow(3, Color.FromArgb(7,11,13,17)); FUNCTIONS.SetTextRightJustifty(true); FUNCTIONS.SetSPInvinc(PLAYER.Handle, true); return FUNCTIONS.GetPlayerInvincible(PLAYER.Handle)");
                Assert.IsTrue(scene.ShowsOutput("True"));
                Command(scene, Hash.DISPLAY_RADAR, false); Command(scene, Hash.DISPLAY_HUD, false);
                Command(scene, Hash.SET_MOUSE_CURSOR_THIS_FRAME);
                Command(scene, Hash.SET_TEXT_EDGE, 2, (byte)11, (byte)13, (byte)17, (byte)7);
                Command(scene, Hash.SET_TEXT_DROP_SHADOW, 3, (byte)11, (byte)13, (byte)17, (byte)7);
                Command(scene, Hash.SET_TEXT_RIGHT_JUSTIFY, true);
                Command(scene, Hash.SET_PLAYER_INVINCIBLE, 7, true);
            }
        }

        [TestMethod]
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
