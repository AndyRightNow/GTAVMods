using GTA.Native;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Linq;

namespace GTAVMods.Tests.Support
{
    internal static class ConsoleAssertions
    {
        internal static void Command(ConsoleFixture scene, Hash native, params object[] arguments) {
            var calls = scene.Native.Calls.Where(c => c.Hash == native).ToArray();
            Assert.IsTrue(calls.Any(c => c.Arguments.SequenceEqual(arguments)), native + " must receive the requested player command.");
        }
    }
}
