using System;
using System.Drawing;
using System.IO;
using System.Linq;
using GTA.Native;
using GTAVMods.Agent;
using GTAVMods.Tests.Support;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static GTAVMods.Tests.Support.AgentScene;

namespace GTAVMods.Tests.PlayerIntegration.AgentOperations
{
    [TestClass, TestCategory("PlayerIntegration")]
    public class InspectionAndPersistenceTests
    {
        [TestMethod]
        public void LosingForegroundDuringACaptureKeepsThePlayerPausedUntilAnExplicitResume()
        {
            using (var s = new AgentScene())
            {
                s.Batch(Step("pause"), Step("capture")); s.Frame();
                s.Pixels.Foreground = new IntPtr(99); s.Frame();
                StringAssert.Contains(s.Status, "\"state\":\"failed\""); StringAssert.Contains(s.Status, "\"paused\":true");
                s.Batch(Step("resume")); s.Frame();
                StringAssert.Contains(s.Status, "\"paused\":false");
            }
        }
        [TestMethod]
        [DataRow("missing-window", "foreground")]
        [DataRow("background", "foreground")]
        [DataRow("rectangle-unavailable", "rectangle")]
        [DataRow("position-unavailable", "locate")]
        [DataRow("zero-width", "empty")]
        [DataRow("zero-height", "empty")]
        public void ScreenshotFailureIsActionableAndNeverReturnsAnotherAppsPixels(string failure, string message)
        {
            using (var s = new AgentScene())
            {
                switch (failure)
                {
                    case "missing-window": s.Pixels.Window = IntPtr.Zero; break;
                    case "background": s.Pixels.Foreground = new IntPtr(99); break;
                    case "rectangle-unavailable": s.Pixels.RectangleAvailable = false; break;
                    case "position-unavailable": s.Pixels.PositionAvailable = false; break;
                    case "zero-width": s.Pixels.Area = new Rectangle(0, 0, 0, 24); break;
                    case "zero-height": s.Pixels.Area = new Rectangle(0, 0, 48, 0); break;
                }
                var response = s.Request("capture");
                Assert.AreEqual(false, response["ok"]); StringAssert.Contains((string)response["error"], message);
                Assert.IsEmpty(Directory.GetFiles(s.Root, "frame-*.png"));
                var receipt = Directory.GetFiles(s.Root, "response-*.json").Single();
                Assert.AreEqual(Protocol.Encode(response), File.ReadAllText(receipt));
            }
        }

        [TestMethod]
        public void CannotRecordRequestMeansNoPlayerActionAndAUsefulFileError()
        {
            using (var s = new AgentScene())
            {
                s.Files.BeforeSave = path => { if (Path.GetFileName(path).StartsWith("request-")) throw new IOException("disk full"); };
                var response = s.Batch(Step("pause")); s.Frame();
                Assert.AreEqual(false, response["ok"]); StringAssert.Contains((string)response["error"], "disk full");
                Assert.IsFalse(s.Game.Native.Calls.Any()); StringAssert.Contains(s.Events, "agent.request.failed");
            }
        }

        [TestMethod]
        [DataRow(false)] [DataRow(true)]
        public void LostResultOrCompletionStatusStopsSequenceAndPausesForInspection(bool completionStatus)
        {
            using (var s = new AgentScene())
            {
                s.Batch(Step("pause"));
                bool failed = false;
                s.Files.BeforeSave = path => {
                    if (!failed && (completionStatus ? Path.GetFileName(path) == "batch-status.json" : Path.GetFileName(path).StartsWith("result-")))
                    { failed = true; throw new IOException("volume became unavailable"); }
                };
                s.Frame();
                StringAssert.Contains(s.Status, "\"state\":\"failed\"");
                StringAssert.Contains(File.ReadAllText(Path.Combine(s.Root, "batch-status.json")), "volume became unavailable");
                Assert.AreEqual(true, s.Game.Native.Calls.Last(c => c.Hash == Hash.SET_GAME_PAUSED).Arguments[0]);
                Assert.AreEqual(completionStatus ? 1 : 0, s.Results.Length);
            }
        }

        [TestMethod]
        public void UnwritableAuditLogStopsControlSubmissionAndReportsTheLostRecord()
        {
            var storage = new MemoryLogStorage();
            using (var s = new AgentScene(logStorage: storage))
            {
                s.Batch(Step("pause"), Step("controls", ("inputs", new[] { Step("", ("control", "Sprint")) })));
                s.Frame();
                storage.BeforeAppend = path => throw new IOException("log drive unavailable");
                storage.BeforeCreate = path => throw new IOException("log drive unavailable");
                s.Frame();
                Assert.IsFalse(s.Game.Native.Calls.Any(c => c.Hash == Hash.SET_CONTROL_VALUE_NEXT_FRAME));
                StringAssert.Contains(s.Status, "audit log unavailable"); StringAssert.Contains(s.Status, "\"state\":\"failed\"");
                Assert.AreEqual(true, s.Game.Native.Calls.Last(c => c.Hash == Hash.SET_GAME_PAUSED).Arguments[0]);
                Assert.IsGreaterThan(0L, s.Log.DroppedRecords);
                storage.BeforeAppend = null; storage.BeforeCreate = null; s.Clock.ElapsedMilliseconds += 6000;
            }
        }

        [TestMethod]
        public void RejectedBatchHasADurableReceiptAndRepeatingItsIdDoesNotRetry()
        {
            using (var s = new AgentScene())
            {
                var request = s.Envelope("batch"); request["steps"] = new[] { Step("misspelling") };
                string response = s.Bridge.Dispatch(Protocol.Encode(request));
                Assert.AreEqual(false, Protocol.Decode(response)["ok"]);
                Assert.AreEqual(response, s.Bridge.Dispatch(Protocol.Encode(request)));
                Assert.IsFalse(s.Game.Native.Calls.Any());
            }
        }
    }
}
