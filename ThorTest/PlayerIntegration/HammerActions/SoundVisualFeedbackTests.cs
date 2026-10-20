using ADModUtils;
using Control = GTA.Control;
using GTA.Math;
using GTA.Native;
using GTA;
using GTAVMods.Game;
using GTAVMods.Tests.Support;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NAudio.Wave;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using System;

namespace GTAVMods.Tests.PlayerIntegration.HammerActions
{
    [TestClass, TestCategory("PlayerIntegration")]
    public class SoundVisualFeedbackTests
    {
        private static void Whirl(PlayerSessionFixture s)
        {
            s.Game.Ropes.Creations.Enqueue(70);
            s.Frame(0, Keys.Z);
            s.Frame(0, Keys.Z);
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

        [TestMethod]
        public void CatchFeedbackStopsApproachAudioAndRepeatedCatchesKeepTheOriginalLightningSchedule()
        {
            var s = new PlayerSessionFixture();
            s.DropHammer(new Vector3(0, -30, 1));
            s.Frame(0, Keys.H);
            Assert.IsTrue(s.Audio.Clips["hammer-close-to-player.wav"].IsPlaying);
            s.Game.Host.OnWait = duration =>
            {
                Assert.AreEqual(1, duration);
                s.ObserveHeldHammer();
            };
            for (int catchNumber = 0; catchNumber < 2; catchNumber++)
            {
                if (catchNumber > 0) s.Frame(500, Keys.Y);
                s.Game.Entity(20).Position = Vector3.UnitZ;
                s.Game.World.Capsules.Enqueue(new RayHit(true, 10, Vector3.UnitZ));
                s.Frame(0, Keys.H);
                Assert.AreEqual(WeaponHash.Hammer, s.Player.CurrentWeapon);
            }
            Assert.AreEqual(2, s.Audio.Clips["catch-hammer.wav"].Plays);
            Assert.IsFalse(s.Audio.Clips["hammer-close-to-player.wav"].IsPlaying);
            Assert.HasCount(16, s.Game.Native.Calls.Where(call => call.Hash == Hash.FORCE_LIGHTNING_FLASH).ToArray());
            CollectionAssert.AreEqual(new object[] { 20, 10 }, s.LastNative(Hash.GIVE_WEAPON_OBJECT_TO_PED));
            var animation = s.LastNative(Hash.TASK_PLAY_ANIM);
            Assert.AreEqual("melee_damage_back", animation[2]);
            Assert.AreEqual(250, animation[5]);
            Assert.AreEqual((int)(AnimationFlags.UpperBodyOnly | AnimationFlags.Secondary), animation[6]);
            Func<int> bodyPulses = () => s.Game.Native.Calls.Count(call => call.Hash == Hash.START_PARTICLE_FX_NON_LOOPED_ON_PED_BONE);
            Assert.AreEqual(10, bodyPulses(), "Each catch emits one hand effect plus four body effects.");
            s.Frame(499);
            Assert.AreEqual(10, bodyPulses());
            s.Frame(1);
            Assert.AreEqual(14, bodyPulses(), "The second catch must not postpone the original one-second timer.");
            s.Frame(0);
            Assert.AreEqual(14, bodyPulses());
            s.Frame(0, Keys.Y);
            s.Frame(0);
            s.Frame(10001);
            Assert.AreEqual(14, bodyPulses(), "Expired power must stop the periodic body effects.");
        }

        [TestMethod]
        public void StationaryDroppedHammerEmitsOneLightningPulsePerSecondAndDeletionStopsOldPulses()
        {
            var s = new PlayerSessionFixture();
            s.DropHammer(new Vector3(12, -8, 30));
            s.Game.World.RaycastObservation = (from, to, flags, ignore) => new RayHit(true, 0, Vector3.Zero);
            s.Game.Native.Calls.Clear();
            foreach (int time in new[] { 0, 999, 1000, 1000, 1999, 2000 })
            {
                s.Game.Host.GameTime = time;
                s.Frame(0);
            }
            CollectionAssert.AreEqual(new[] { 20, 20 }, s.Game.Native.Calls.Where(call =>
                call.Hash == Hash.START_PARTICLE_FX_NON_LOOPED_ON_ENTITY).Select(call => (int)call.Arguments[1]).ToArray());
            s.Game.Observations[20].Exists = false;
            s.Game.Entities.CreatedBlips.Enqueue(2001);
            s.Frame(0);
            s.Frame(1000);
            Assert.IsFalse(s.Game.Native.Calls.Any(call => call.Hash == Hash.START_PARTICLE_FX_NON_LOOPED_ON_ENTITY &&
                (int)call.Arguments[1] == 0));
            Assert.HasCount(2, s.Game.Native.Calls.Where(call => call.Hash == Hash.START_PARTICLE_FX_NON_LOOPED_ON_ENTITY &&
                (int)call.Arguments[1] == 20).ToArray());
        }

        [TestMethod]
        public void MissingMapMarkerDoesNotPreventDropRecallEffectsOrCleanup()
        {
            var s = new PlayerSessionFixture();
            s.Game.Entities.CreatedBlips.Clear();
            s.Game.Entities.CreatedBlips.Enqueue(0);
            s.Frame(0);
            s.Game.Entity(20).CollisionEnabled = false;
            s.Frame(0, Keys.Y);
            Assert.IsTrue(s.Game.Entity(20).CollisionEnabled);
            CollectionAssert.AreEqual(new object[] { 20, 50000f, -1f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f },
                s.LastNative(Hash.SET_OBJECT_PHYSICS_PARAMS));
            CollectionAssert.AreEqual(new object[] { 10, false }, s.LastNative(Hash.GET_WEAPON_OBJECT_FROM_PED));
            Assert.IsFalse(s.Game.Calls.Any(call => call.Name == "blip.name" || call.Name == "blip.friendly"));
            CollectionAssert.AreEquivalent(new[] { 40, 41 }, s.Game.Calls
                .Where(call => call.Name == "entity.noCollision" && (int)call.Arguments[1] == 20)
                .Select(call => (int)call.Arguments[0]).Distinct().ToArray());
            s.Frame(1000);
            Assert.IsTrue(s.Game.Native.Calls.Any(call => call.Hash == Hash.START_PARTICLE_FX_NON_LOOPED_ON_ENTITY &&
                (int)call.Arguments[1] == 20));
            s.Game.Entity(20).Position = new Vector3(0, -30, 1);
            s.Frame(0, Keys.H);
            Assert.AreEqual(Vector3.UnitY * 100, s.Game.Entity(20).Velocity);
            s.Reload();
            s.AssertReleased();
        }
    }
}
