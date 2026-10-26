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
        public void PartialPipeReadsExecuteOnceAndDuplicateRequestReturnsTheSavedReceipt()
        {
            var transport = new AgentTransport { Chunk = 1 };
            using (var s = new AgentScene(() => transport))
            {
                var request = s.Envelope("batch"); request["steps"] = new[] { Step("pause") };
                string json = Protocol.Encode(request); transport.Request(json);
                for (int i = 0; i < transport.Incoming.Length + 10 && transport.Outgoing == null; i++) s.Frame();
                Assert.IsNotNull(transport.Outgoing);
                Assert.AreEqual(true, Protocol.Decode(transport.Response)["ok"]);
                Assert.AreEqual(1, s.Game.Native.Calls.Count(c => c.Hash == Hash.SET_GAME_PAUSED));
                transport.WriteCompleted.SetResult(true); s.Frame(); Assert.IsTrue(transport.Disposed);
                Assert.AreEqual(transport.Response, s.Bridge.Dispatch(json));
                Assert.AreEqual(1, s.Game.Native.Calls.Count(c => c.Hash == Hash.SET_GAME_PAUSED));
                Assert.AreEqual(json, File.ReadAllText(Path.Combine(s.Root, "request-" + request["id"] + ".json")));
                StringAssert.Contains(File.ReadAllText(Path.Combine(s.Root, "response-" + request["id"] + ".json")), "\"ok\":true");
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
                bad.Connected.SetResult(true); s.Frame(); s.Frame(); s.Frame();
                Assert.IsTrue(bad.Disposed); StringAssert.Contains(s.Events, "Invalid request length");
                next.Request(Protocol.Encode(s.Envelope("status")));
                s.Frame(0, 999); Assert.IsNull(next.Outgoing);
                s.Frame(0, 1); s.Frame(); s.Frame(); s.Frame();
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
                s.Frame(); s.Frame(); s.Frame(); s.Frame();
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
        public void MissingProtocolVersionAndUncertainPriorExecutionAreNeverReplayed()
        {
            using (var s = new AgentScene())
            {
                var request = s.Envelope("cancel"); request.Remove("version");
                StringAssert.Contains((string)Protocol.Decode(s.Bridge.Dispatch(Protocol.Encode(request)))["error"], "Unsupported protocol");
                request["version"] = 1;
                s.Journal.Save("request-" + request["id"], request);
                StringAssert.Contains((string)Protocol.Decode(s.Bridge.Dispatch(Protocol.Encode(request)))["error"], "uncertain");
                Assert.IsFalse(s.Game.Native.Calls.Any());
            }
        }

    }
}
