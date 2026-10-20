using System.Collections.Generic;
using ADModUtils;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ModUtilsTest
{
    [TestClass, TestCategory("PureFunction")]
    public class AnimationTimingTests
    {
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
