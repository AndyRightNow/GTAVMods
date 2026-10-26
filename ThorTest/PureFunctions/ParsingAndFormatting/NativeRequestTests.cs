using System;
using System.Collections.Generic;
using GTAVMods.Agent;
using GTA.Native;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace GTAVMods.Tests.PureFunctions.ParsingAndFormatting
{
    [TestClass, TestCategory("PureFunctions")]
    public class NativeRequestTests
    {
        [TestMethod]
        public void NativeNamesAndCustomHashesResolveWithoutCompilingCSharp()
        {
            Assert.AreEqual(Hash.GET_MISSION_FLAG, AgentActions.NativeHash("get_mission_flag"));
            Assert.AreEqual((Hash)0x1234567890ABCDEFUL, AgentActions.NativeHash("0x1234567890ABCDEF"));
            Assert.ThrowsExactly<ArgumentException>(() => AgentActions.NativeHash("unknown"));
            Assert.ThrowsExactly<ArgumentException>(() => AgentActions.NativeHash("123"));
            Assert.ThrowsExactly<FormatException>(() => AgentActions.NativeHash("0xGG"));
        }
        [TestMethod]
        public void ScalarArgumentsPreserveIntegerAndFloatNativeContracts()
        {
            foreach (object value in new object[] { true, "label", 7, 2147483648L }) Assert.AreEqual(value, AgentActions.NativeArgument(value));
            Assert.AreEqual(1.25f, AgentActions.NativeArgument(1.25m)); Assert.AreEqual(2.5f, AgentActions.NativeArgument(2.5d));
            foreach (var pair in new[] { ("float", (object)3f), ("int", (object)3), ("uint", (object)3u), ("long", (object)3L), ("ulong", (object)3UL) })
                Assert.AreEqual(pair.Item2, AgentActions.NativeArgument(new Dictionary<string, object> { { "type", pair.Item1 }, { "value", 3 } }));
            Assert.AreEqual(4f, AgentActions.NativeArgument(new Dictionary<string, object> { { "type", "float" }, { "value", 4L } }));
            Assert.AreEqual(4f, AgentActions.NativeArgument(new Dictionary<string, object> { { "type", "float" }, { "value", 4m } }));
            Assert.AreEqual(4.5f, AgentActions.NativeArgument(new Dictionary<string, object> { { "type", "float" }, { "value", 4.5d } }));
        }
        [TestMethod]
        public void NativePointersObjectsFractionalIntegersAndUnknownTypesAreRejected()
        {
            Assert.ThrowsExactly<ArgumentException>(() => AgentActions.NativeArgument(null));
            Assert.ThrowsExactly<ArgumentException>(() => AgentActions.NativeArgument(new object[] { 1 }));
            Assert.ThrowsExactly<ArgumentException>(() => AgentActions.NativeArgument(new Dictionary<string, object> { { "type", "int" } }));
            Assert.ThrowsExactly<ArgumentException>(() => AgentActions.NativeArgument(new Dictionary<string, object> { { "type", "int" }, { "value", "3" } }));
            Assert.ThrowsExactly<ArgumentException>(() => AgentActions.NativeArgument(new Dictionary<string, object> { { "type", "int" }, { "value", 3.2m } }));
            Assert.ThrowsExactly<ArgumentException>(() => AgentActions.NativeArgument(new Dictionary<string, object> { { "type", "pointer" }, { "value", 3 } }));
            Assert.ThrowsExactly<OverflowException>(() => AgentActions.NativeArgument(new Dictionary<string, object> { { "type", "uint" }, { "value", -1 } }));
        }
        [TestMethod]
        public void GameValueSerializationNeverConstructsNativeObjectsFromUntrustedJson()
            => Assert.ThrowsExactly<NotSupportedException>(() => new GameValueConverter().Deserialize(new Dictionary<string, object> { { "handle", 42 } }, typeof(GTA.Ped), null));
    }
}
