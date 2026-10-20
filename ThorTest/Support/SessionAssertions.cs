using System.Linq;

namespace GTAVMods.Tests.Support
{
    internal static class SessionAssertions
    {
        internal static int FailureCount(PlayerSessionFixture session) =>
            session.Log.Records.Count(record => record.Level == "ERROR" || record.Name == "diagnostics.exception");
    }
}
