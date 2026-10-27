using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using GTA.Native;
using GTAVMods.Agent;
using GTAVMods.Tests.Support;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static GTAVMods.Tests.Support.AgentScene;

namespace GTAVMods.Tests.PlayerIntegration.AgentOperations
{
    [TestClass, TestCategory("PlayerIntegration")]
    public class TransportRecoveryTests
    {
        [TestMethod]
        public void ConnectedAgentsCanPauseAndResumeOnConsecutiveTicksWithoutTransportOnlyFrames()
        {
            var first = new AgentTransport(); var second = new AgentTransport(); int opened = 0;
            using (var s = new AgentScene(() => ++opened == 1 ? first : second))
            {
                s.Frame(); // Arm a listener before either client connects.
                var pause = s.Envelope("action"); pause["steps"] = new[] { Step("pause") };
                first.Request(Protocol.Encode(pause));
                Assert.IsFalse(s.Game.Native.Calls.Any(), "Receiving bytes must never call GTA.");
                s.Frame();
                Assert.IsNotNull(first.Outgoing, "The next game tick executes the prepared request.");
                Assert.AreEqual(true, s.Game.Native.Calls.Last(c => c.Hash == Hash.SET_GAME_PAUSED).Arguments[0]);
                first.WriteCompleted.SetResult(true);
                Assert.AreEqual(2, opened, "Rearm without waiting for another game tick.");
                var resume = s.Envelope("action"); resume["steps"] = new[] { Step("resume") };
                second.Request(Protocol.Encode(resume));
                Assert.AreEqual(1, s.Game.Native.Calls.Count);
                s.Frame();
                Assert.IsNotNull(second.Outgoing);
                Assert.AreEqual(false, s.Game.Native.Calls.Last(c => c.Hash == Hash.SET_GAME_PAUSED).Arguments[0]);
            }
        }

        [TestMethod]
        public void BufferedPauseRequestExecutesOnTheGameThreadWithoutWritingReceipts()
        {
            var transport = new AgentTransport();
            using (var s = new AgentScene(() => transport))
            {
                var request = s.Envelope("batch"); request["steps"] = new[] { Step("pause") };
                transport.Request(Protocol.Encode(request));
                Assert.IsFalse(s.Game.Native.Calls.Any());
                s.FramesUntil(() => transport.Outgoing != null);
                Assert.AreEqual(true, Protocol.Decode(transport.Response)["ok"]);
                Assert.AreEqual(1, s.Game.Native.Calls.Count(c => c.Hash == Hash.SET_GAME_PAUSED));
                s.Journal.Drain();
                Assert.IsEmpty(Directory.GetFiles(s.Root, "response-*.json"));
                transport.WriteCompleted.SetResult(true); s.Frame();
                Assert.IsTrue(transport.Disposed);
            }
        }

        [TestMethod]
        public void PartialPipeReadsExecuteOnceAndRequestIdsOnlyCorrelateResponses()
        {
            var transport = new AgentTransport { Chunk = 1 };
            using (var s = new AgentScene(() => transport))
            {
                var request = s.Envelope("batch"); request["steps"] = new[] { Step("pause") };
                string json = Protocol.Encode(request); transport.Request(json);
                Assert.IsNull(transport.Outgoing);
                Assert.IsFalse(s.Game.Native.Calls.Any());
                s.FramesUntil(() => transport.Outgoing != null);
                Assert.IsNotNull(transport.Outgoing);
                Assert.AreEqual(true, Protocol.Decode(transport.Response)["ok"]);
                Assert.AreEqual(1, s.Game.Native.Calls.Count(c => c.Hash == Hash.SET_GAME_PAUSED));
                transport.WriteCompleted.SetResult(true); s.Frame(); Assert.IsTrue(transport.Disposed);
                Assert.AreEqual(true, Protocol.Decode(s.Bridge.Dispatch(json))["ok"]); s.Frame();
                Assert.AreEqual(2, s.Game.Native.Calls.Count(c => c.Hash == Hash.SET_GAME_PAUSED));
                Assert.IsEmpty(Directory.GetFiles(s.Root, "request-*.json"));
            }
        }

        [TestMethod]
        [DataRow(0)] [DataRow(-1)] [DataRow(1048577)]
        public void InvalidFrameLengthsAreLoggedAndTheNextClientCanStillInspect(int length)
        {
            var bad = new AgentTransport { Incoming = BitConverter.GetBytes(length) };
            var next = new AgentTransport(); var connections = new Queue<IAgentConnection>(new[] { bad, next });
            using (var s = new AgentScene(() => connections.Dequeue()))
            {
                bad.Connected.SetResult(true); s.FramesUntil(() => bad.Disposed);
                Assert.IsTrue(bad.Disposed); StringAssert.Contains(s.Events, "Invalid request length");
                next.Request(Protocol.Encode(s.Envelope("status")));
                s.Frame(0, 999); Assert.IsNull(next.Outgoing);
                s.Frame(0, 1); s.FramesUntil(() => next.Outgoing != null);
                Assert.AreEqual(true, Protocol.Decode(next.Response)["ok"]); Assert.IsFalse(s.Game.Native.Calls.Any());
            }
        }

        [TestMethod]
        [DataRow(false)] [DataRow(true)]
        public void TruncatedOrInvalidUtf8RequestsNeverChangeTheWorld(bool invalidUtf8)
        {
            var bad = new AgentTransport { Incoming = invalidUtf8 ? new byte[] { 1, 0, 0, 0, 255 } : new byte[] { 1, 0, 0, 0 } };
            using (var s = new AgentScene(() => bad))
            {
                bad.Connected.SetResult(true);
                s.FramesUntil(() => bad.Disposed);
                Assert.IsTrue(bad.Disposed); Assert.IsNull(bad.Outgoing);
                StringAssert.Contains(s.Events, "agent.transport.error"); Assert.IsFalse(s.Game.Native.Calls.Any());
            }
        }

        [TestMethod]
        [DataRow(false)] [DataRow(true)]
        public void SilentReadingOrWritingClientsTimeoutWithoutBlockingSimulation(bool writing)
        {
            var connection = new AgentTransport { ReadStalls = !writing };
            using (var s = new AgentScene(() => connection))
            {
                connection.Request(Protocol.Encode(s.Envelope("status")));
                s.Frame(); s.Frame(); s.Frame(); s.Frame();
                if (writing) s.FramesUntil(() => connection.Outgoing != null);
                s.Frame(0, 10000);
                Assert.IsTrue(connection.Disposed); StringAssert.Contains(s.Events, "Pipe request timed out");
                Assert.AreEqual(true, s.Batch(Step("wait", ("ms", 1)))["ok"]);
                s.Frame(); s.Frame(1);
                StringAssert.Contains(s.Status, "\"state\":\"completed\"");
            }
        }

        [TestMethod]
        public void PipeCreationAndConnectionFailuresBackOffAndRemainInspectableFromFiles()
        {
            int opens = 0;
            var connection = new AgentTransport();
            using (var s = new AgentScene(() => { if (++opens == 1) throw new IOException("Pipe name unavailable"); return connection; }))
            {
                s.Frame(); s.Frame(0, 999); Assert.AreEqual(1, opens);
                s.Frame(0, 1); connection.Connected.SetException(new IOException("Client disappeared")); s.Frame();
                Assert.IsTrue(connection.Disposed); StringAssert.Contains(s.Events, "Pipe name unavailable");
                StringAssert.Contains(s.Events, "Client disappeared");
            }
        }

        [TestMethod]
        [DataRow("null", "Expected a JSON object")]
        [DataRow("{broken", "")]
        public void MalformedRequestsReturnStructuredErrorsWithoutAnyWorldAction(string json, string message)
        {
            using (var s = new AgentScene())
            {
                var response = Protocol.Decode(s.Bridge.Dispatch(json));
                Assert.AreEqual(false, response["ok"]); StringAssert.Contains((string)response["error"], message);
                Assert.IsFalse(s.Game.Native.Calls.Any());
            }
        }

        [TestMethod]
        [DataRow("id", "../escape", "GUID")]
        [DataRow("version", 2, "Expected 1 through 1")]
        [DataRow("session", "old-session", "Stale session")]
        [DataRow("op", "unknown", "Unknown request operation")]
        public void InvalidEnvelopeCannotExecuteARequest(string key, object value, string message)
        {
            using (var s = new AgentScene())
            {
                var request = s.Envelope("cancel"); request[key] = value;
                var response = Protocol.Decode(s.Bridge.Dispatch(Protocol.Encode(request)));
                Assert.AreEqual(false, response["ok"]); StringAssert.Contains((string)response["error"], message);
                Assert.IsFalse(s.Game.Native.Calls.Any());
            }
        }

        [TestMethod]
        public void MissingProtocolVersionIsRejectedBeforeAnyPlayerAction()
        {
            using (var s = new AgentScene())
            {
                var request = s.Envelope("cancel"); request.Remove("version");
                StringAssert.Contains((string)Protocol.Decode(s.Bridge.Dispatch(Protocol.Encode(request)))["error"], "Unsupported protocol");

                Assert.IsFalse(s.Game.Native.Calls.Any());
            }
        }

    }
}
