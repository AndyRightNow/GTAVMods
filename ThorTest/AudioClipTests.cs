using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using ADModUtils;
using GTA;
using GTA.Math;
using GTAVMods.Game;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NAudio.Wave;

namespace ModUtilsTest
{
    [TestClass, TestCategory("PlayerIntegration")]
    public class PlayerAudioTests
    {
        private static void Whirl(PlayerSessionFixture s)
        {
            s.Game.Ropes.Creations.Enqueue(70);
            s.Frame(0, Keys.Z);
            s.Frame(0, Keys.Z);
        }

        [TestMethod]
        public void RepeatedCatchRequestsRestartOneQuietClipUntilTheEngineConfirmsTheCatch()
        {
            var audio = new SilentAudio();
            var s = new PlayerSessionFixture(audio: audio);
            s.DropHammer(new Vector3(0, 0, 1));
            s.Game.World.CapsuleObservation = (from, to, radius, flags) =>
                new RayHit(true, 10, Vector3.UnitZ);
            s.Recall();
            s.Recall();
            var output = audio.Clips["catch-hammer.wav"];
            Assert.AreEqual(2, output.Plays);
            CollectionAssert.AreEqual(new long[] { 0, 0 }, output.PlayPositions,
                "Each catch begins at the start of the decoded WAV, after the previous output consumed samples.");
            Assert.HasCount(1, audio.Opens);
            Assert.AreEqual(0.3f, audio.Opens.Single().Item2);
            Assert.AreEqual(0.3f, ((AudioFileReader)output.Input).Volume);
            Assert.AreEqual(WeaponHash.Unarmed, s.Player.CurrentWeapon,
                "Playing the catch sound cannot fabricate the engine's weapon transfer.");
            s.ObserveHeldHammer();
            s.Frame(0);
            Assert.AreEqual(2, output.Plays, "The observed catch ends transfer retries and their audio.");
            s.Controller.Abort();
            Assert.AreEqual(1, output.Disposals);
        }

        [TestMethod]
        public void HoldingWhirlReusesItsSoundAndDisablingStopsItWithoutLeakingTheDevice()
        {
            var audio = new SilentAudio();
            var s = new PlayerSessionFixture(audio: audio);
            s.Frame(0);
            Whirl(s);
            var clip = audio.Clips["whirling-hammer.wav"];
            int firstPlay = clip.Plays;
            s.Frame(0, Keys.Z);
            Assert.AreEqual(firstPlay, clip.Plays, "Holding Z must not restart a sound that is still playing.");
            Assert.IsTrue(clip.IsPlaying);
            Assert.HasCount(1, audio.Opens.Where(open => Path.GetFileName(open.Item1) == "whirling-hammer.wav").ToArray());
            Assert.AreEqual(1f, audio.Opens.Single(open => Path.GetFileName(open.Item1) == "whirling-hammer.wav").Item2);
            Assert.AreEqual(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Thor", "whirling-hammer.wav"),
                audio.Opens.Single(open => Path.GetFileName(open.Item1) == "whirling-hammer.wav").Item1);
            s.Chord(Keys.F);
            Assert.IsFalse(clip.IsPlaying);
            Assert.IsEmpty(s.Game.Ropes.Ropes);
            Assert.AreEqual(200, s.Player.MaxHealth);
            s.Controller.Abort();
            s.Controller.Abort();
            Assert.IsTrue(audio.Clips.Values.All(output => output.Disposals == 1));
        }

        [TestMethod]
        [DataRow("missing")]
        [DataRow("play")]
        [DataRow("stop")]
        public void UnavailableWhirlAudioDoesNotPreventWhirlingAndWarnsOnlyOnce(string failure)
        {
            var audio = new SilentAudio { Failure = failure };
            var s = new PlayerSessionFixture(audio: audio);
            s.Frame(0);
            Whirl(s);
            for (int i = 0; i < 3; i++) s.Frame(0, Keys.Z);
            Assert.IsTrue(s.Game.Ropes.Ropes.Contains(70), "Audio failure cannot cancel the active hammer ability.");
            Assert.AreEqual(30000, s.Player.MaxHealth);
            var warnings = s.Log.Records.Where(record => record.Name == "audio.disabled" &&
                Path.GetFileName((string)record.Data["file"]) == "whirling-hammer.wav").ToArray();
            Assert.HasCount(1, warnings);
            StringAssert.Contains((string)warnings[0].Data["exception"],
                failure == "missing" ? "FileNotFoundException" : failure + " failed");
            Assert.HasCount(1, audio.Opens.Where(open => Path.GetFileName(open.Item1) == "whirling-hammer.wav").ToArray());
            s.Controller.Abort();
            Assert.IsEmpty(s.Game.Ropes.Ropes);
            Assert.IsTrue(audio.Clips.Values.All(output => output.Disposals == 1));
        }

        [TestMethod]
        [DataRow("stop")]
        [DataRow("dispose")]
        public void AudioReleaseFailureStillRestoresPlayerHealthAndReleasesTheHammerRope(string failure)
        {
            var audio = new SilentAudio();
            var s = new PlayerSessionFixture(audio: audio);
            s.Frame(0);
            Whirl(s);
            var output = audio.Clips["whirling-hammer.wav"];
            output.Failure = failure;
            s.Controller.Abort();
            s.Controller.Abort();
            Assert.AreEqual(1, output.Disposals);
            Assert.IsEmpty(s.Game.Ropes.Ropes);
            Assert.AreEqual(200, s.Player.MaxHealth);
            Assert.AreEqual("WARN", s.Log.Last(failure == "stop" ? "audio.disabled" : "audio.disposeFailed").Level);
        }

        private sealed class WavePlayer : IWavePlayer
        {
            public IWaveProvider Input;
            public string Failure;
            public int Disposals, Plays;
            public PlaybackState PlaybackState { get; private set; }
            public float Volume { get; set; }
            public WaveFormat OutputWaveFormat => Input.WaveFormat;
            public event EventHandler<StoppedEventArgs> PlaybackStopped;
            public void Init(IWaveProvider provider) { Input = provider; if (Failure == "init") throw new IOException("initialization failed"); }
            public void Play() { Plays++; PlaybackState = PlaybackState.Playing; }
            public void Stop() { PlaybackState = PlaybackState.Stopped; PlaybackStopped?.Invoke(this, new StoppedEventArgs(null)); }
            public void Pause() => throw new NotSupportedException();
            public void Dispose() { Disposals++; if (Failure == "dispose") throw new IOException("device disposal failed"); }
        }

        private sealed class WaveFiles : IAudioPlaybackFactory
        {
            public string Path;
            public string Failure;
            public readonly List<WavePlayer> Outputs = new List<WavePlayer>();
            public IAudioPlayback Open(string ignored, float volume)
            {
                if (Failure == "missing") return new NAudioPlaybackFactory().Open(Path + ".missing", volume);
                return new NAudioPlaybackFactory(() =>
                {
                    if (Failure == "create") throw new IOException("output creation failed");
                    var output = new WavePlayer { Failure = Failure };
                    Outputs.Add(output);
                    return output;
                }).Open(Path, volume);
            }
        }

        [TestMethod]
        [DataRow(null)]
        [DataRow("create")]
        [DataRow("missing")]
        [DataRow("init")]
        [DataRow("dispose")]
        public void PlayerWhirlingThroughNAudioReleasesTheWaveFileEvenWhenTheDeviceFails(string failure)
        {
            string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "Thor-wave-" + Guid.NewGuid() + ".wav");
            try
            {
                using (var writer = new WaveFileWriter(path, new WaveFormat(8000, 16, 1)))
                    writer.Write(new byte[16000], 0, 16000);
                var audio = new WaveFiles { Path = path, Failure = failure };
                var s = new PlayerSessionFixture(audio: audio);
                s.Frame(0);
                Whirl(s);
                Assert.IsTrue(s.Game.Ropes.Ropes.Contains(70));
                if (failure == null)
                {
                    Assert.IsTrue(audio.Outputs.Any(output => output.Plays > 0));
                    Assert.IsTrue(s.Log.Records.Any(record => record.Name == "audio.loaded" && Convert.ToDouble(record.Data["durationSeconds"]) == 1));
                }
                else if (failure != "dispose") Assert.IsTrue(s.Log.Records.Any(record => record.Name == "audio.disabled"));
                s.Controller.Abort();
                Assert.AreEqual(200, s.Player.MaxHealth);
                Assert.IsTrue(audio.Outputs.All(output => output.Disposals == 1));
                using (File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
            }
            finally { File.Delete(path); }
        }
    }
}
