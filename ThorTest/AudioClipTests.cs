using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ADModUtils;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NAudio.Wave;

namespace ModUtilsTest
{
    [TestClass]
    public class AudioClipTests
    {
        private sealed class Playback : IAudioPlayback
        {
            public readonly List<string> Calls = new List<string>();
            public string FailOn;
            public bool CompleteSynchronously;
            public bool IsPlaying { get; set; }
            public double DurationSeconds => 2.5;
            public event EventHandler Stopped;
            private void Call(string operation) { Calls.Add(operation); if (FailOn == operation) throw new IOException(operation + " failed"); }
            public void Rewind() => Call("rewind");
            public void Play() { Call("play"); IsPlaying = true; if (CompleteSynchronously) Complete(); }
            public void Stop() { Call("stop"); IsPlaying = false; }
            public void Dispose() => Call("dispose");
            public void Complete() => Stopped?.Invoke(this, EventArgs.Empty);
        }

        private sealed class Factory : IAudioPlaybackFactory
        {
            public readonly Playback Playback = new Playback();
            public Exception Failure;
            public readonly List<Tuple<string, float>> Opens = new List<Tuple<string, float>>();
            public IAudioPlayback Open(string path, float volume)
            {
                Opens.Add(Tuple.Create(path, volume));
                if (Failure != null) throw Failure;
                return Playback;
            }
        }

        private static DiagnosticLog Log(MemoryLogStorage storage) => new DiagnosticLog(
            new SessionLog(Path.GetTempPath(), Path.GetTempPath(), storage, new ManualLogClock()), () => 0);

        [TestMethod]
        public void ClipReusesPlaybackHonorsRestartAndDisposesOnlyOnce()
        {
            var factory = new Factory();
            var storage = new MemoryLogStorage();
            var clip = new AudioClip("hammer.wav", 0.3f, factory, Log(storage));
            clip.Stop();
            clip.Play(false); clip.Play(false); clip.Play(true); clip.Stop(); clip.Play(false);
            clip.Dispose(); clip.Dispose(); clip.Play(); clip.Stop();
            CollectionAssert.AreEqual(new[] { "stop", "rewind", "play", "stop", "rewind", "play", "stop", "stop", "rewind", "play", "dispose" }, factory.Playback.Calls);
            Assert.HasCount(1, factory.Opens);
            Assert.AreEqual(Tuple.Create("hammer.wav", 0.3f), factory.Opens[0]);
            var records = storage.Records;
            CollectionAssert.AreEqual(new[] { "audio.loaded", "audio.play.hammer.wav" }, records.Select(record => LogJson.Text(record, "event")).ToArray());
            Assert.AreEqual("2.5", LogJson.Text(records[0], "data/durationSeconds"));
            Assert.AreEqual("hammer.wav", LogJson.Text(records[0], "data/file"));
            Assert.AreEqual("false", LogJson.Text(records[1], "data/restart"));
        }

        [TestMethod]
        public void OpenFailureIsLoggedOnceAndDoesNotRetryEveryTick()
        {
            var factory = new Factory { Failure = new FileNotFoundException("missing clip") };
            var storage = new MemoryLogStorage();
            using (var clip = new AudioClip("missing.wav", 1, factory, Log(storage)))
            { clip.Play(); clip.Play(); clip.Stop(); }
            Assert.HasCount(1, factory.Opens);
            Assert.HasCount(0, factory.Playback.Calls);
            Assert.AreEqual("audio.disabled", LogJson.Text(storage.Records.Single(), "event"));
            StringAssert.Contains(LogJson.Text(storage.Records.Single(), "data/exception"), "missing clip");
        }

        [TestMethod]
        [DataRow("play")]
        [DataRow("rewind")]
        [DataRow("stop")]
        public void PlaybackFailureDisablesAndReleasesTheAcquiredResource(string operation)
        {
            var factory = new Factory();
            factory.Playback.FailOn = operation;
            var storage = new MemoryLogStorage();
            var clip = new AudioClip("hammer.wav", 1, factory, Log(storage));
            clip.Play(); clip.Play(); clip.Dispose();
            Assert.AreEqual(1, factory.Playback.Calls.Count(call => call == "dispose"));
            Assert.AreEqual("audio.disabled", LogJson.Text(storage.Records.Last(), "event"));
            StringAssert.Contains(LogJson.Text(storage.Records.Last(), "data/exception"), operation + " failed");
        }

        [TestMethod]
        public void StopAndDisposeFailuresCannotEscapeGameplayOrCauseRepeatedDisposal()
        {
            var factory = new Factory();
            var storage = new MemoryLogStorage();
            var clip = new AudioClip("hammer.wav", 1, factory, Log(storage));
            clip.Play(); factory.Playback.FailOn = "stop"; clip.Stop(); clip.Play();
            Assert.AreEqual(1, factory.Playback.Calls.Count(call => call == "dispose"));
            Assert.AreEqual("audio.disabled", LogJson.Text(storage.Records.Last(), "event"));
            var other = new Factory();
            var otherClip = new AudioClip("other.wav", 1, other, Log(storage));
            otherClip.Play(); other.Playback.FailOn = "dispose"; otherClip.Dispose(); otherClip.Dispose();
            Assert.AreEqual(1, other.Playback.Calls.Count(call => call == "dispose"));
            Assert.AreEqual("audio.disposeFailed", LogJson.Text(storage.Records.Last(), "event"));
        }

        [TestMethod]
        public void OneShotSubscribesBeforePlayAndReleasesOnCompletionOrFailure()
        {
            var factory = new Factory();
            factory.Playback.CompleteSynchronously = true;
            OneShotAudio.Play("short.wav", factory);
            factory.Playback.Complete();
            CollectionAssert.AreEqual(new[] { "play", "dispose" }, factory.Playback.Calls);
            Assert.AreEqual(Tuple.Create("short.wav", 1f), factory.Opens.Single());
            var failed = new Factory(); failed.Playback.FailOn = "play";
            Assert.ThrowsExactly<IOException>(() => OneShotAudio.Play("bad.wav", failed));
            failed.Playback.Complete();
            CollectionAssert.AreEqual(new[] { "play", "dispose" }, failed.Playback.Calls);
        }

        [TestMethod]
        public void PathsAndDefaultMissingAudioRetainThePublicContract()
        {
            string actual = AudioClip.ResourcePath("catch-hammer.wav");
            Assert.AreEqual(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Thor", "catch-hammer.wav"), actual);
            Assert.IsTrue(Path.IsPathRooted(actual));
            string missing = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".wav");
            using (var clip = new AudioClip(missing)) { clip.Play(); clip.Play(); clip.Stop(); }
            var failures = File.ReadAllLines(Logger.LogFilePath).Select(LogJson.Parse)
                .Where(record => LogJson.Text(record, "event") == "audio.disabled" && LogJson.Text(record, "data/file") == missing).ToArray();
            Assert.HasCount(1, failures);
            Assert.ThrowsExactly<FileNotFoundException>(() => Utilities.Audio.Play(missing));
            Assert.ThrowsExactly<ArgumentNullException>(() => new AudioClip("x", 1, null, Log(new MemoryLogStorage())));
            Assert.ThrowsExactly<ArgumentNullException>(() => new AudioClip("x", 1, new Factory(), null));
            Assert.ThrowsExactly<ArgumentNullException>(() => new NAudioPlaybackFactory(null));
        }

        private sealed class WavePlayer : IWavePlayer
        {
            public IWaveProvider Input;
            public bool FailInitialization, FailDisposal;
            public int Disposals, Plays, Stops;
            public PlaybackState PlaybackState { get; private set; }
            public float Volume { get; set; }
            public WaveFormat OutputWaveFormat => Input.WaveFormat;
            public event EventHandler<StoppedEventArgs> PlaybackStopped;
            public void Init(IWaveProvider waveProvider) { Input = waveProvider; if (FailInitialization) throw new IOException("init failed"); }
            public void Play() { Plays++; PlaybackState = PlaybackState.Playing; }
            public void Stop() { Stops++; PlaybackState = PlaybackState.Stopped; }
            public void Pause() => throw new NotSupportedException();
            public void Dispose() { Disposals++; if (FailDisposal) throw new IOException("dispose failed"); }
            public void Complete() => PlaybackStopped?.Invoke(this, new StoppedEventArgs(null));
        }

        private static string CreateWave()
        {
            string path = Path.Combine(Path.GetTempPath(), "ThorAudio-" + Guid.NewGuid() + ".wav");
            using (var writer = new WaveFileWriter(path, new WaveFormat(8000, 16, 1)))
            {
                var data = new byte[16000];
                for (int i = 0; i < data.Length; i += 2) { data[i] = 0; data[i + 1] = 64; }
                writer.Write(data, 0, data.Length);
            }
            return path;
        }

        [TestMethod]
        public void NAudioAdapterMapsVolumeRewindPlaybackAndStoppedEvents()
        {
            string path = CreateWave();
            try
            {
                var player = new WavePlayer();
                int completions = 0;
                using (var playback = new NAudioPlaybackFactory(() => player).Open(path, 0.25f))
                {
                    Assert.AreEqual(1d, playback.DurationSeconds, 0.0001d);
                    Assert.IsFalse(playback.IsPlaying);
                    var sample = new byte[4];
                    Assert.AreEqual(4, player.Input.Read(sample, 0, sample.Length));
                    Assert.AreEqual(0.125f, BitConverter.ToSingle(sample, 0), 0.00001f);
                    Assert.IsTrue(((AudioFileReader)player.Input).Position > 0);
                    playback.Rewind(); Assert.AreEqual(0L, ((AudioFileReader)player.Input).Position);
                    playback.Play(); Assert.IsTrue(playback.IsPlaying); Assert.AreEqual(1, player.Plays);
                    playback.Stop(); Assert.IsFalse(playback.IsPlaying); Assert.AreEqual(1, player.Stops);
                    player.Complete();
                    playback.Stopped += (sender, args) => { Assert.AreSame(playback, sender); completions++; };
                    player.Complete(); Assert.AreEqual(1, completions);
                }
                Assert.AreEqual(1, player.Disposals);
                player.Complete();
                Assert.AreEqual(1, completions, "Disposal must disconnect the output callback.");
                using (File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
            }
            finally { File.Delete(path); }
        }

        [TestMethod]
        public void NAudioAdapterReleasesReaderWhenCreationInitializationOrDisposalFails()
        {
            string path = CreateWave();
            try
            {
                Assert.ThrowsExactly<IOException>(() => new NAudioPlaybackFactory(() => throw new IOException("factory failed")).Open(path, 1));
                using (File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
                var failed = new WavePlayer { FailInitialization = true };
                Assert.ThrowsExactly<IOException>(() => new NAudioPlaybackFactory(() => failed).Open(path, 1));
                Assert.AreEqual(1, failed.Disposals);
                using (File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
                var badDisposal = new WavePlayer { FailDisposal = true };
                var playback = new NAudioPlaybackFactory(() => badDisposal).Open(path, 1);
                Assert.ThrowsExactly<IOException>(() => playback.Dispose());
                Assert.AreEqual(1, badDisposal.Disposals);
                using (File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
            }
            finally { File.Delete(path); }
        }
    }
}
