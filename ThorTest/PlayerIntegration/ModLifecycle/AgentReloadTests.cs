using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using GTA.Native;
using GTAVMods.Agent;
using GTAVMods.Tests.Support;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static GTAVMods.Tests.Support.AgentScene;

namespace GTAVMods.Tests.PlayerIntegration.ModLifecycle
{
    [TestClass, TestCategory("PlayerIntegration")]
    public class AgentReloadTests
    {
        [TestMethod]
        public void ScriptAbortReleasesPauseClosesThePipeAndRefusesNewCommands()
        {
            var connection = new AgentTransport();
            using (var s = new AgentScene(() => connection))
            {
                s.Batch(Step("pause")); s.Frame(); s.Bridge.Dispose(); s.Bridge.Dispose(); s.Frame();
                Assert.IsTrue(connection.Disposed); StringAssert.Contains(s.Published, "stopped");
                Assert.AreEqual(false, s.Game.Native.Calls.Last(c => c.Hash == Hash.SET_GAME_PAUSED).Arguments[0]);
                StringAssert.Contains((string)s.Request("observe")["error"], "Bridge stopped");
            }
        }

        // The external SHVDNE host boundary exposes Reload; it only records the request.
        // This does not pretend to simulate AppDomain teardown or native game cleanup.
        private static Assembly Runtime(Action reload, bool hasMethod = true)
        {
            var assembly = AppDomain.CurrentDomain.DefineDynamicAssembly(new AssemblyName("ObservedShvdne" + Guid.NewGuid().ToString("N")), AssemblyBuilderAccess.Run);
            var type = assembly.DefineDynamicModule("core").DefineType("ScriptHookVDotNet", TypeAttributes.Public);
            var callback = type.DefineField("Requested", typeof(Action), FieldAttributes.Public | FieldAttributes.Static);
            if (hasMethod)
            {
                var il = type.DefineMethod("Reload", MethodAttributes.Public | MethodAttributes.Static, typeof(void), Type.EmptyTypes).GetILGenerator();
                il.Emit(OpCodes.Ldsfld, callback); il.Emit(OpCodes.Callvirt, typeof(Action).GetMethod("Invoke")); il.Emit(OpCodes.Ret);
            }
            type.CreateType().GetField("Requested").SetValue(null, reload); return assembly;
        }

        [TestMethod]
        public void ReloadReleasesAgentPauseAndWaitsForItsReceiptToBeWrittenBeforeAskingTheRuntime()
        {
            int reloads = 0;
            var runtime = Runtime(() => reloads++); var connection = new AgentTransport();
            using (var s = new AgentScene(() => connection, () => new[] { typeof(object).Assembly, runtime }))
            {
                s.Batch(Step("pause")); s.Frame();
                connection.Request(Protocol.Encode(s.Envelope("reload")));
                s.Frame(); s.Frame(); s.Frame(); s.Frame();
                Assert.AreEqual(0, reloads); Assert.AreEqual(false, s.Game.Native.Calls.Last(c => c.Hash == Hash.SET_GAME_PAUSED).Arguments[0]);
                StringAssert.Contains(connection.Response, "accepted");
                connection.WriteCompleted.SetResult(true); s.Frame();
                Assert.AreEqual(1, reloads); StringAssert.Contains(s.Published, "reloading"); StringAssert.Contains(s.Events, "agent.reload.requested");
            }
        }

        [TestMethod]
        public void LostReloadReceiptCannotBeAcknowledgedByAnUnrelatedStatusResponse()
        {
            int reloads = 0; var runtime = Runtime(() => reloads++);
            var reload = new AgentTransport(); var status = new AgentTransport(); int opened = 0;
            using (var s = new AgentScene(() => opened++ == 0 ? reload : status, () => new[] { runtime }))
            {
                reload.Request(Protocol.Encode(s.Envelope("reload")));
                s.Frame(); s.Frame(); s.Frame(); s.Frame();
                reload.WriteCompleted.SetException(new IOException("client disconnected before receipt")); s.Frame();
                status.Request(Protocol.Encode(s.Envelope("status")));
                s.Frame(0, 1000); s.Frame(); s.Frame(); s.Frame(); status.WriteCompleted.SetResult(true); s.Frame();
                Assert.AreEqual(0, reloads); StringAssert.Contains(status.Response, "\"ok\":true");
                StringAssert.Contains(s.Events, "client disconnected before receipt");
            }
        }

        [TestMethod]
        [DataRow(false)] [DataRow(true)]
        public void UnsupportedRuntimeFailsVisiblyAndPausesForInspection(bool missingMethod)
        {
            var connection = new AgentTransport();
            var assemblies = missingMethod ? new[] { Runtime(() => { }, false) } : Array.Empty<Assembly>();
            using (var s = new AgentScene(() => connection, () => assemblies))
            {
                s.Batch(Step("pause")); s.Frame();
                connection.Request(Protocol.Encode(s.Envelope("reload")));
                s.Frame(); s.Frame(); s.Frame(); s.Frame(); connection.WriteCompleted.SetResult(true);
                var error = Assert.ThrowsExactly<NotSupportedException>(() => s.Frame());
                StringAssert.Contains(error.Message, missingMethod ? "compatible Reload" : "core is unavailable");
                StringAssert.Contains(s.Published, "error");
                Assert.AreEqual(true, s.Game.Native.Calls.Last(c => c.Hash == Hash.SET_GAME_PAUSED).Arguments[0]);
            }
        }

        [TestMethod]
        public void AgentCanDisableThorUsingTheSameCleanupAsThePlayerBeforeAStoryMission()
        {
            var s = new PlayerSessionFixture(); s.Frame();
            s.Controller.SetEnabled(false); s.Frame();
            Assert.AreEqual("external-agent", s.Log.Last("ability.disabled").Data["reason"]);
            Assert.AreEqual(200, s.Player.MaxHealth);
            Assert.IsFalse(s.Player.Invincible);
            Assert.IsEmpty(s.Game.Cameras.Cameras);
            s.Controller.SetEnabled(false);
            s.Controller.SetEnabled(true); s.Controller.SetEnabled(true); s.Frame();
            Assert.AreEqual("external-agent", s.Log.Last("ability.enabled").Data["reason"]);
            Assert.IsFalse(s.Player.CanSufferCriticalHits);
            Assert.AreEqual(10, s.Log.Last("ability.attach").Data["ped"]);
            s.Controller.Abort();
            s.AssertReleased();
        }
    }
}
