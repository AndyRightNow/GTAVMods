using GTA;
using GTAVMods.Game;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ModUtilsTest
{
    [TestClass]
    [TestCategory("PureFunction")]
    public class GameHandleValueTests
    {
        [TestMethod]
        public void HandleValuesPreserveIdentityAbsenceAndInputOrder()
        {
            Assert.AreEqual(27, GameHandles.Of(new Camera(27)));
            Assert.AreEqual(0, GameHandles.Of(null));
            CollectionAssert.AreEqual(new[] { 27, 0, 91 },
                GameHandles.OfMany(new PoolObject[] { new Camera(27), null, new Camera(91) }));
        }
    }
}
