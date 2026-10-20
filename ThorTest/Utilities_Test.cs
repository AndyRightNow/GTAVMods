using System;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ADModUtils;

[assembly: DoNotParallelize]

namespace ModUtilsTest
{
    [TestClass]
    public class Utilities_Test
    {
        [TestMethod]
        public void PickOne_AlwaysReturnsAnInputValue()
        {
            int[] values = { 1, 2, 3, 4, 5 };
            for (int i = 0; i < 256; i++)
                CollectionAssert.Contains(values, Utilities.Random.PickOne(values));
        }

        [TestMethod]
        public void PickOne_EmptyOrNullReturnsDefault()
        {
            Assert.AreEqual(0, Utilities.Random.PickOne(Array.Empty<int>()));
            Assert.IsNull(Utilities.Random.PickOne<string>(null));
        }

        [TestMethod]
        public void PickOneIf_OnlyReturnsMatchingValues()
        {
            for (int i = 0; i < 256; i++)
                Assert.IsTrue(Utilities.Random.PickOneIf(new[] { 1, 2, 3, 4, 5 }, n => n >= 3) >= 3);
        }

        [TestMethod]
        public void PickOneIf_NoMatchReturnsDefault()
        {
            Assert.AreEqual(0, Utilities.Random.PickOneIf(new[] { 1, 2 }, n => n > 5));
            Assert.IsNull(Utilities.Random.PickOneIf(new[] { "x" }, s => s == "y"));
        }

        [TestMethod]
        public void PickOneIf_NullPredicateIsRejected()
        {
            Assert.ThrowsExactly<ArgumentNullException>(() => Utilities.Random.PickOneIf(new[] { 1 }, null));
        }

        [TestMethod]
        [DataRow("1.0.1158.13", true)]
        [DataRow("1.0.1158.16", true)]
        [DataRow("1.0.1013.34", false)]
        [DataRow("1.0.1158.14", false)]
        [DataRow("1.0.3889.0", false)]
        [DataRow(null, false)]
        public void Compatibility_DoesNotAssumeOtherBuildsAreVerified(string version, bool expected)
        {
            Assert.AreEqual(expected, EnhancedCompatibility.IsAssessedBuild(
                version == null ? null : new Version(version)));
        }

        [TestMethod]
        public void EnhancedApiVersion_IsConvertedToExecutableVersion()
        {
            Assert.AreEqual(new Version(1, 0, 1158, 16),
                EnhancedCompatibility.ExecutableVersion(new Version(2, 0, 1158, 16), true));
            Assert.AreEqual(new Version(1, 0, 3889, 0),
                EnhancedCompatibility.ExecutableVersion(new Version(1, 0, 3889, 0), false));
            Assert.IsNull(EnhancedCompatibility.ExecutableVersion(null, true));
        }

        [TestMethod]
        public void AudioResourcePath_IsRelativeToTheScriptDomain()
        {
            string expected = Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
                "Thor", "catch-hammer.wav");
            Assert.AreEqual(expected, AudioClip.ResourcePath("catch-hammer.wav"));
            Assert.IsTrue(Path.IsPathRooted(expected));
        }

        [TestMethod]
        public void Logger_LockedPrimaryFileUsesSessionLog()
        {
            string originalPath = Logger.LogFilePath;
            Directory.CreateDirectory(Path.GetDirectoryName(originalPath));
            string marker = "locked-log-test-" + Guid.NewGuid();
            string sessionPath = null;
            try
            {
                using (var locked = new FileStream(originalPath, FileMode.OpenOrCreate,
                    FileAccess.ReadWrite, FileShare.Read))
                {
                    Logger.Log("INFO", marker);
                    sessionPath = Logger.LogFilePath;
                    Assert.AreNotEqual(originalPath, sessionPath);
                    StringAssert.Contains(File.ReadAllText(sessionPath), marker);
                    Logger.Log("INFO", marker + "-again");
                    StringAssert.Contains(File.ReadAllText(sessionPath), marker + "-again");
                }
            }
            finally
            {
                if (sessionPath != null && sessionPath != originalPath) File.Delete(sessionPath);
            }
        }

        [TestMethod]
        public void MissingAudio_DoesNotInterruptGameplay()
        {
            using (var clip = new AudioClip(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".wav")))
            {
                clip.Play();
                clip.Play();
                clip.Stop();
            }
        }
    }
}
