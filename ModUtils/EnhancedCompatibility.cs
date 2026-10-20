using System;

namespace ADModUtils
{
    public static class EnhancedCompatibility
    {
        // Upstream SHV/SHVDNE release notes checked 2026-10-20.
        // This is an assessed dependency target, not an in-game certification.
        public static readonly Version AssessedBuild = new Version(1, 0, 1158, 13);
        // Story Mode, converted model and script reload smoke-tested locally on 2026-10-20.
        public static readonly Version SmokeTestedBuild = new Version(1, 0, 1158, 16);

        public static Version ExecutableVersion(Version apiVersion, bool isEnhanced)
        {
            // SHVDNE v1.1.0.6 increments Enhanced's major version for its Legacy API guards.
            if (apiVersion == null || !isEnhanced) return apiVersion;
            return new Version(apiVersion.Major - 1, apiVersion.Minor,
                apiVersion.Build, apiVersion.Revision);
        }

        public static bool IsAssessedBuild(Version version)
        {
            return AssessedBuild.Equals(version) || SmokeTestedBuild.Equals(version);
        }
    }
}
