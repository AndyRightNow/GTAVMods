using ADModUtils;
using GTA.Math;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Plane = ADModUtils.Plane;
using System.Collections.Generic;
using System;

namespace GTAVMods.Tests.PureFunctions.ValidationAndGameValues
{
    [TestClass, TestCategory("PureFunction")]
    public class RuntimeCompatibilityTests
    {
        [TestMethod]
        [DataRow("1.0.1158.13", true)]
        [DataRow("1.0.1158.16", true)]
        [DataRow("1.0.1013.34", false)]
        [DataRow("1.0.1158.14", false)]
        [DataRow("1.0.3889.0", false)]
        [DataRow(null, false)]
        public void CompatibilityDoesNotAssumeOtherBuildsAreVerified(string version, bool expected)
        {
            Assert.AreEqual(expected, EnhancedCompatibility.IsAssessedBuild(version == null ? null : new Version(version)));
        }

        [TestMethod]
        public void EnhancedApiVersionIsConvertedToExecutableVersion()
        {
            Assert.AreEqual(new Version(1, 0, 1158, 16), EnhancedCompatibility.ExecutableVersion(new Version(2, 0, 1158, 16), true));
            Assert.AreEqual(new Version(1, 0, 3889, 0), EnhancedCompatibility.ExecutableVersion(new Version(1, 0, 3889, 0), false));
            Assert.IsNull(EnhancedCompatibility.ExecutableVersion(null, true));
        }
    }
}
