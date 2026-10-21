using ADModUtils;
using GTA.Math;
using GTA.Native;
using GTA;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Collections.Generic;
using System.Linq;
using System;

namespace GTAVMods.Tests.PureFunctions.ParsingAndFormatting
{
    [TestClass, TestCategory("PureFunction")]
    public class AnimationNameTimingTests
    {
        [TestMethod]
        [DataRow(false, false, 90f, "attack_0")]
        [DataRow(true, false, 44.99f, "attack_0")]
        [DataRow(true, false, -45f, "attack_+90")]
        [DataRow(true, false, 45f, "attack_-90")]
        [DataRow(true, false, -134.99f, "attack_+90")]
        [DataRow(true, false, 134.99f, "attack_-90")]
        [DataRow(true, false, -135f, "attack_+180")]
        [DataRow(true, false, 135f, "attack_-180")]
        [DataRow(true, true, -90f, "attack_90")]
        [DataRow(true, true, 90f, "attack_-90")]
        [DataRow(true, true, -179f, "attack_-180")]
        public void DirectionalClipFormattingHonorsSectorBoundariesAndTheCatalogSignConvention(bool angled, bool incomplete, float angle, string expected)
        {
            Assert.AreEqual(expected, Thor.NativeHelper.AnimationNameForAngle("attack_0", angle, angled, incomplete));
        }

        [TestMethod]
        public void TimingLookupReturnsTheChosenClipDelayAndDoesNotInventMissingDelays()
        {
            var timings = new Dictionary<string, Dictionary<string, int>> {
                { "swing", new Dictionary<string, int> { { "left", 400 }, { "right", 600 } } },
                { "empty", new Dictionary<string, int>() }
            };
            Assert.AreEqual(400, AnimationTiming.GetDelay(timings, "swing", "left"));
            Assert.AreEqual(600, AnimationTiming.GetDelay(timings, "swing", "right"));
            Assert.AreEqual(0, AnimationTiming.GetDelay(timings, "missing", "left"));
            Assert.AreEqual(0, AnimationTiming.GetDelay(timings, "empty", "left"));
        }
    }
}
