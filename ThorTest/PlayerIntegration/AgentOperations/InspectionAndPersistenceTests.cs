using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
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
                StringAssert.Contains(s.Events, "agent.request.failed");
                Assert.IsEmpty(Directory.GetFiles(s.Root, "response-*.json"));
            }
        }

        [TestMethod]
        [DataRow(false)] [DataRow(true)]
        public void UnavailableArtifactStorageDoesNotRejectOrStopPlayerActions(bool denied)
        {
            using (var s = new AgentScene())
            {
                s.Files.BeforeSave = path => { if (denied) throw new UnauthorizedAccessException("read only"); throw new IOException("disk full"); };
                Assert.AreEqual(true, s.Batch(Step("pause"))["ok"]); s.Frame();
                StringAssert.Contains(s.Status, "\"state\":\"completed\"");
                var response = s.Request("action", ("steps", new[] { Step("resume") }));
                Assert.AreEqual(true, response["ok"]);
                Assert.AreEqual(false, s.Game.Native.Calls.Last(c => c.Hash == Hash.SET_GAME_PAUSED).Arguments[0]);
                s.Journal.Drain();
                Assert.IsGreaterThan(0L, s.Journal.DroppedRecords);
                StringAssert.Contains(s.Events, "agent.file.failed");
            }
        }

        [TestMethod]
        public void BlockedStorageUsesABoundedQueueWhileActionsAndObservationsKeepWorking()
        {
            using (var entered = new ManualResetEventSlim())
            using (var release = new ManualResetEventSlim())
            using (var s = new AgentScene(journalCharacters: 4096))
            {
                s.Files.BeforeSave = path => { entered.Set(); release.Wait(); };
                try
                {
                    s.Batch(Step("pause"));
                    Assert.IsTrue(entered.Wait(5000), "The external storage boundary must be stalled.");
                    s.Frame();
                    Assert.IsFalse(s.Journal.Drain(0));
                    for (int i = 0; i < 100; i++) Assert.AreEqual(true, s.Request("state")["ok"]);
                    Assert.IsGreaterThan(0L, s.Journal.DroppedRecords);
                    Assert.AreEqual(true, s.Request("action", ("steps", new[] { Step("resume") }))["ok"]);
                    Assert.AreEqual(false, s.Game.Native.Calls.Last(c => c.Hash == Hash.SET_GAME_PAUSED).Arguments[0]);
                    Assert.IsEmpty(Directory.GetFiles(s.Root, "request-*.json"));
                }
                finally { release.Set(); s.Journal.Drain(); }
                s.Request("status");
                StringAssert.Contains(s.Events, "agent.response");
                Assert.HasCount(1, s.Results);
            }
        }

        [TestMethod]
        public void UnwritableDiagnosticLogReportsDropsWithoutStoppingPlayerControls()
        {
            var storage = new MemoryLogStorage();
            using (var s = new AgentScene(logStorage: storage))
            {
                s.Journal.Drain();
                storage.BeforeAppend = path => throw new IOException("log drive unavailable");
                storage.BeforeCreate = path => throw new IOException("log drive unavailable");
                s.Batch(Step("resume"), Step("controls", ("ms", 10), ("inputs", new[] { Step("", ("control", "Sprint")) })));
                s.Frame(); s.Frame(); s.Frame(10); s.Journal.Drain();
                Assert.IsTrue(s.Game.Native.Calls.Any(c => c.Hash == Hash.SET_CONTROL_VALUE_NEXT_FRAME));
                StringAssert.Contains(s.Status, "\"state\":\"completed\"");
                Assert.IsGreaterThan(0L, s.Journal.DroppedRecords);
                storage.BeforeAppend = null; storage.BeforeCreate = null; s.Clock.ElapsedMilliseconds += 6000;
            }
        }

        [TestMethod]
        public void RejectedActionDoesNotCacheTheRequestIdOrPersistReceipts()
        {
            using (var s = new AgentScene())
            {
                var request = s.Envelope("action"); request["steps"] = new[] { Step("misspelling") };
                Assert.AreEqual(false, Protocol.Decode(s.Bridge.Dispatch(Protocol.Encode(request)))["ok"]);
                request["steps"] = new[] { Step("pause") };
                Assert.AreEqual(true, Protocol.Decode(s.Bridge.Dispatch(Protocol.Encode(request)))["ok"]);
                Assert.AreEqual(true, s.Game.Native.Calls.Last(c => c.Hash == Hash.SET_GAME_PAUSED).Arguments[0]);
                s.Journal.Drain();
                Assert.IsEmpty(Directory.GetFiles(s.Root, "response-*.json"));
            }
        }
    }
}
