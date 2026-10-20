using ADModUtils;
using GTA.Math;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Plane = ADModUtils.Plane;
using System.Collections.Generic;
using System;

namespace GTAVMods.Tests.PureFunctions.ValidationAndGameValues
{
    [TestClass, TestCategory("PureFunction")]
    public class RequiredInputDurationTests
    {
        [TestMethod]
        public void RequiredValuesRejectMissingOrEmptyConfigurationWithTheParameterName()
        {
            Assert.AreEqual("clip", Require.NotNull("clip", "path"));
            Assert.AreEqual("path", Assert.ThrowsExactly<ArgumentNullException>(() => Require.NotNull<string>(null, "path")).ParamName);
            CollectionAssert.AreEqual(new[] { 3, 7 }, Require.NonEmpty(new[] { 3, 7 }, "choices"));
            Assert.AreEqual("choices", Assert.ThrowsExactly<ArgumentNullException>(() => Require.NonEmpty<int>(null, "choices")).ParamName);
            Assert.AreEqual("choices", Assert.ThrowsExactly<ArgumentException>(() => Require.NonEmpty(Array.Empty<int>(), "choices")).ParamName);
        }

        [TestMethod]
        public void NonnegativeDurationsAcceptZeroAndRejectNegativeValues()
        {
            Assert.AreEqual(0, Require.NonNegative(0, "interval"));
            Assert.AreEqual(1000, Require.NonNegative(1000, "interval"));
            Assert.AreEqual("interval", Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => Require.NonNegative(-1, "interval")).ParamName);
        }
    }
}
