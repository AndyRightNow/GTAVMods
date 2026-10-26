using System;
using System.Collections.Generic;
using GTAVMods.Agent;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace GTAVMods.Tests.PureFunctions.ParsingAndFormatting
{
    [TestClass, TestCategory("PureFunctions")]
    public class AgentProtocolTests
    {
        [TestMethod]
        [DataRow(null)] [DataRow(1)] [DataRow("")] [DataRow("  ")]
        public void RequiredCommandTextRejectsMissingOrNonTextValues(object value)
        {
            var args = new Dictionary<string, object>();
            if (value != null) args["code"] = value;
            Assert.ThrowsExactly<ArgumentException>(() => Protocol.Text(args, "code"));
        }

        [TestMethod]
        public void NumericArgumentsPreserveSupportedRepresentationsAndDefaults()
        {
            foreach (object value in new object[] { 2, 2L, 2m, 2d, 2f })
                Assert.AreEqual(2d, Protocol.Number(new Dictionary<string, object> { { "value", value } }, "value", -3, 3, 1));
            Assert.AreEqual(1d, Protocol.Number(new Dictionary<string, object>(), "value", -3, 3, 1));
        }

        [TestMethod]
        [DataRow("2")] [DataRow(true)] [DataRow(null)]
        public void NumericArgumentsDoNotCoerceStringsBooleansOrNull(object value)
            => Assert.ThrowsExactly<ArgumentException>(() => Protocol.Number(new Dictionary<string, object> { { "value", value } }, "value", 0, 3, 1));

        [TestMethod]
        [DataRow(double.NaN)] [DataRow(double.PositiveInfinity)] [DataRow(double.NegativeInfinity)] [DataRow(-1d)] [DataRow(4d)]
        public void AnalogInputRejectsNonFiniteAndOutOfRangeValues(double value)
            => Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => Protocol.Number(new Dictionary<string, object> { { "value", value } }, "value", 0, 3, 1));

        [TestMethod]
        public void MillisecondsMustBeWholeNumbers()
            => Assert.ThrowsExactly<ArgumentException>(() => Protocol.Integer(new Dictionary<string, object> { { "ms", 1.5 } }, "ms", 1, 30000, 500));

        [TestMethod]
        [DataRow(0)] [DataRow(1)] [DataRow(2)] [DataRow(3)] [DataRow(4)] [DataRow(5)]
        public void BatchShapeRejectsMissingScalarTextNonObjectEmptyAndOversizedArrays(int shape)
        {
            var args = new Dictionary<string, object>();
            if (shape == 1) args["steps"] = 1;
            if (shape == 2) args["steps"] = "pause";
            if (shape == 3) args["steps"] = new object[] { null };
            if (shape == 4) args["steps"] = Array.Empty<object>();
            if (shape == 5) args["steps"] = new[] { new Dictionary<string, object>(), new Dictionary<string, object>() };
            Assert.ThrowsExactly<ArgumentException>(() => Protocol.Objects(args, "steps", 1));
        }

        [TestMethod]
        public void UnknownArgumentsAreRejectedInsteadOfSilentlyChangingAgentIntent()
            => Assert.ThrowsExactly<ArgumentException>(() => Protocol.Fields(new Dictionary<string, object> { { "millisecnds", 500 } }, "ms"));
    }
}
