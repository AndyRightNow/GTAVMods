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
using Thor;

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
            var tuning = new GameplayTuning();
            tuning.Ability.CatchVolume = .17f;
            var s = new PlayerSessionFixture(audio: audio, tuning: tuning);
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
            Assert.AreEqual(tuning.Ability.CatchVolume, audio.Opens.Single().Item2);
            Assert.AreEqual(tuning.Ability.CatchVolume, ((AudioFileReader)output.Input).Volume);
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
            var tuning = new GameplayTuning();
            tuning.Hammer.WhirlVolume = .63f;
            var s = new PlayerSessionFixture(audio: audio, tuning: tuning);
            s.Frame(0);
            Whirl(s);
            var clip = audio.Clips["whirling-hammer.wav"];
            int firstPlay = clip.Plays;
            s.Frame(0, Keys.Z);
            Assert.AreEqual(firstPlay, clip.Plays, "Holding Z must not restart a sound that is still playing.");
            Assert.IsTrue(clip.IsPlaying);
            Assert.HasCount(1, audio.Opens.Where(open => Path.GetFileName(open.Item1) == "whirling-hammer.wav").ToArray());
            Assert.AreEqual(tuning.Hammer.WhirlVolume, ((AudioFileReader)clip.Input).Volume);
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
            int activeMaxHealth = s.Player.MaxHealth;
            Whirl(s);
            for (int i = 0; i < 3; i++) s.Frame(0, Keys.Z);
            Assert.IsTrue(s.Game.Ropes.Ropes.Contains(70), "Audio failure cannot cancel the active hammer ability.");
            Assert.AreEqual(activeMaxHealth, s.Player.MaxHealth, "An optional audio failure must not change the player's power.");
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
            var tuning = new GameplayTuning();
            tuning.Ability.ThunderIntervalMs = 337;
            tuning.Ability.CatchAnimationMs = 173;
            tuning.Ability.PowerFadeMs = 607;
            tuning.Ability.ThunderFlashesPerBurst = 3;
            tuning.Ability.ApproachVolume = .47f;
            var s = new PlayerSessionFixture(tuning: tuning);
            s.DropHammer(new Vector3(0, -30, 1));
            s.Frame(0, Keys.H);
            Assert.IsTrue(s.Audio.Clips["hammer-close-to-player.wav"].IsPlaying);
            Assert.AreEqual(tuning.Ability.ApproachVolume,
                ((AudioFileReader)s.Audio.Clips["hammer-close-to-player.wav"].Input).Volume);
            s.Game.Host.OnWait = duration =>
            {
                Assert.AreEqual(1, duration);
                s.ObserveHeldHammer();
            };
            int catchSpacing = tuning.Ability.ThunderIntervalMs / 3;
            Func<int> bodyPulses = () => s.Game.Native.Calls.Count(call => call.Hash == Hash.START_PARTICLE_FX_NON_LOOPED_ON_PED_BONE);
            for (int catchNumber = 0; catchNumber < 2; catchNumber++)
            {
                if (catchNumber > 0) s.Frame(catchSpacing, Keys.Y);
                int effectsBeforeCatch = bodyPulses();
                s.Game.Entity(20).Position = Vector3.UnitZ;
                s.Game.World.Capsules.Enqueue(new RayHit(true, 10, Vector3.UnitZ));
                s.Frame(0, Keys.H);
                Assert.AreEqual(WeaponHash.Hammer, s.Player.CurrentWeapon);
                Assert.IsTrue(bodyPulses() > effectsBeforeCatch, "Each observed catch emits player effects.");
            }
            Assert.AreEqual(2, s.Audio.Clips["catch-hammer.wav"].Plays);
            Assert.IsFalse(s.Audio.Clips["hammer-close-to-player.wav"].IsPlaying);
            Assert.IsTrue(s.Game.Native.Calls.Any(call => call.Hash == Hash.FORCE_LIGHTNING_FLASH));
            CollectionAssert.AreEqual(new object[] { 20, 10 }, s.LastNative(Hash.GIVE_WEAPON_OBJECT_TO_PED));
            var animation = s.LastNative(Hash.TASK_PLAY_ANIM);
            Assert.AreEqual("melee_damage_back", animation[2]);
            Assert.AreEqual(tuning.Ability.CatchAnimationMs, animation[5]);
            Assert.AreEqual((int)(AnimationFlags.UpperBodyOnly | AnimationFlags.Secondary), animation[6]);
            int effectsAfterCatches = bodyPulses();
            s.Frame(tuning.Ability.ThunderIntervalMs - catchSpacing - 1);
            Assert.AreEqual(effectsAfterCatches, bodyPulses());
            s.Frame(1);
            Assert.IsTrue(bodyPulses() > effectsAfterCatches, "The second catch must not postpone the original effect timer.");
            int effectsAfterPulse = bodyPulses();
            s.Frame(0);
            Assert.AreEqual(effectsAfterPulse, bodyPulses(), "Sampling the same instant must not repeat a pulse.");
            s.Frame(0, Keys.Y);
            s.Frame(0);
            s.Frame((int)tuning.Ability.PowerFadeMs + 1);
            Assert.AreEqual(effectsAfterPulse, bodyPulses(), "Expired power must stop the periodic body effects.");
        }

        [TestMethod]
        public void StationaryDroppedHammerUsesTheConfiguredLightningIntervalAndDeletionStopsOldPulses()
        {
            var tuning = new GameplayTuning();
            tuning.Hammer.LightningIntervalMs = 271;
            var s = new PlayerSessionFixture(tuning: tuning);
            s.DropHammer(new Vector3(12, -8, 30));
            s.Game.World.RaycastObservation = (from, to, flags, ignore) => new RayHit(true, 0, Vector3.Zero);
            s.Game.Native.Calls.Clear();
            Func<int> hammerPulses = () => s.Game.Native.Calls.Count(call =>
                call.Hash == Hash.START_PARTICLE_FX_NON_LOOPED_ON_ENTITY && (int)call.Arguments[1] == 20);
            s.Frame(tuning.Hammer.LightningIntervalMs - 1);
            Assert.AreEqual(0, hammerPulses());
            s.Frame(1);
            Assert.AreEqual(1, hammerPulses());
            s.Frame(0);
            Assert.AreEqual(1, hammerPulses(), "A duplicate frame must not replay the pulse.");
            s.Frame(tuning.Hammer.LightningIntervalMs - 1);
            Assert.AreEqual(1, hammerPulses());
            s.Frame(1);
            CollectionAssert.AreEqual(new[] { 20, 20 }, s.Game.Native.Calls.Where(call =>
                call.Hash == Hash.START_PARTICLE_FX_NON_LOOPED_ON_ENTITY).Select(call => (int)call.Arguments[1]).ToArray());
            s.Game.Observations[20].Exists = false;
            s.Game.Entities.CreatedBlips.Enqueue(2001);
            s.Frame(0);
            s.Frame(tuning.Hammer.LightningIntervalMs);
            Assert.IsFalse(s.Game.Native.Calls.Any(call => call.Hash == Hash.START_PARTICLE_FX_NON_LOOPED_ON_ENTITY &&
                (int)call.Arguments[1] == 0));
            Assert.HasCount(2, s.Game.Native.Calls.Where(call => call.Hash == Hash.START_PARTICLE_FX_NON_LOOPED_ON_ENTITY &&
                (int)call.Arguments[1] == 20).ToArray());
        }

        [TestMethod]
        public void MissingMapMarkerDoesNotPreventDropRecallEffectsOrCleanup()
        {
            var tuning = new GameplayTuning();
            tuning.Hammer.Mass = 3200;
            tuning.Hammer.CruiseSpeed = 27;
            tuning.Hammer.LightningIntervalMs = 219;
            var s = new PlayerSessionFixture(tuning: tuning);
            s.Game.Entities.CreatedBlips.Clear();
            s.Game.Entities.CreatedBlips.Enqueue(0);
            s.Frame(0);
            s.Game.Entity(20).CollisionEnabled = false;
            s.Frame(0, Keys.Y);
            Assert.IsTrue(s.Game.Entity(20).CollisionEnabled);
            CollectionAssert.AreEqual(new object[] { 20, tuning.Hammer.Mass, -1f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f },
                s.LastNative(Hash.SET_OBJECT_PHYSICS_PARAMS));
            CollectionAssert.AreEqual(new object[] { 10, false }, s.LastNative(Hash.GET_WEAPON_OBJECT_FROM_PED));
            Assert.IsFalse(s.Game.Calls.Any(call => call.Name == "blip.name" || call.Name == "blip.friendly"));
            CollectionAssert.AreEquivalent(new[] { 40, 41 }, s.Game.Calls
                .Where(call => call.Name == "entity.noCollision" && (int)call.Arguments[1] == 20)
                .Select(call => (int)call.Arguments[0]).Distinct().ToArray());
            s.Frame(tuning.Hammer.LightningIntervalMs);
            Assert.IsTrue(s.Game.Native.Calls.Any(call => call.Hash == Hash.START_PARTICLE_FX_NON_LOOPED_ON_ENTITY &&
                (int)call.Arguments[1] == 20));
            s.Game.Entity(20).Position = new Vector3(0, -30, 1);
            s.Frame(0, Keys.H);
            Assert.AreEqual(Vector3.UnitY * tuning.Hammer.CruiseSpeed, s.Game.Entity(20).Velocity);
            s.Reload();
            s.AssertReleased();
        }

        [TestMethod]
        public void MovingHammerTrailFollowsObservedPositionVelocityAndSuppliedAppearance()
        {
            var tuning = new GameplayTuning();
            tuning.Hammer.TrailDurationSeconds = .2f;
            tuning.Effects.TrailInteriorPoints = 4;
            tuning.Effects.TrailLateralSwayRatio = .07f;
            tuning.Effects.TrailParticleScalePerMetre = .31f;
            var position = new Vector3(12, -8, 30);
            var velocity = new Vector3(0, 12, 0);
            var trace = ObserveTrail(tuning, position, velocity);
            AssertTrailFollowsMotion(trace, position, velocity, tuning);
            Assert.IsTrue(trace.Any(point => Vector3.Cross(point.Position - position, velocity.Normalized).Length() > .001f),
                "A nonzero sway setting produces visible lateral lightning detail.");

            var translation = new Vector3(17, -11, 7);
            var translated = ObserveTrail(tuning, position + translation, velocity);
            Assert.IsTrue(trace.All(point => translated.Any(other =>
                other.Position.DistanceTo(point.Position + translation) < .001f)));
            Assert.IsTrue(translated.All(point => trace.Any(other =>
                other.Position.DistanceTo(point.Position - translation) < .001f)),
                "Translating the observed hammer must translate the whole trail, not change its shape.");

            var fasterTurn = new Vector3(-24, 0, 0);
            var turned = ObserveTrail(tuning, position, fasterTurn);
            AssertTrailFollowsMotion(turned, position, fasterTurn, tuning);
            Assert.IsTrue(turned.Max(point => point.Position.DistanceTo(position)) >
                trace.Max(point => point.Position.DistanceTo(position)),
                "Faster observed motion produces a longer trail in the new trailing direction.");

            float originalScale = trace.Max(point => point.Scale);
            tuning.Effects.TrailParticleScalePerMetre *= 2;
            var brighter = ObserveTrail(tuning, position, velocity);
            Assert.AreEqual(originalScale * 2, brighter.Max(point => point.Scale), .001f,
                "The supplied appearance scale controls the emitted native particles.");

            tuning.Effects.TrailLateralSwayRatio = 0;
            var straight = ObserveTrail(tuning, position, velocity);
            Assert.IsTrue(straight.All(point => Vector3.Cross(point.Position - position, velocity.Normalized).Length() < .001f),
                "Zero sway keeps every emitted location on the line of travel.");
            tuning.Effects.TrailInteriorPoints *= 2;
            var denser = ObserveTrail(tuning, position, velocity);
            Assert.IsTrue(denser.Select(point => point.Position).Distinct().Count() >
                straight.Select(point => point.Position).Distinct().Count(),
                "Increasing the supplied detail produces more distinct visible locations without fixing a tessellation formula.");
        }

        private static (Vector3 Position, float Scale)[] ObserveTrail(GameplayTuning tuning, Vector3 position, Vector3 velocity)
        {
            var s = new PlayerSessionFixture(seed: 37, tuning: tuning);
            s.DropHammer(position);
            s.Game.Entity(20).Velocity = velocity;
            s.Game.Native.Calls.Clear();
            s.Frame(0);
            var particles = s.Game.Native.Calls
                .Where(call => call.Hash == Hash.START_PARTICLE_FX_NON_LOOPED_AT_COORD &&
                    (string)call.Arguments[0] == "muz_railgun")
                .Select(call => (new Vector3((float)call.Arguments[1], (float)call.Arguments[2], (float)call.Arguments[3]),
                    (float)call.Arguments[7])).ToArray();
            s.Reload();
            s.AssertNoErrors();
            return particles;
        }

        private static void AssertTrailFollowsMotion((Vector3 Position, float Scale)[] trace,
            Vector3 position, Vector3 velocity, GameplayTuning tuning)
        {
            Assert.IsNotEmpty(trace, "A moving loose hammer must submit visible world-space particles.");
            Vector3 trailingDirection = -velocity.Normalized;
            float length = velocity.Length() * tuning.Hammer.TrailDurationSeconds;
            Assert.IsTrue(trace.Any(point => point.Position.DistanceTo(position) < .001f),
                "The trail starts at the observed hammer, not at a local-space origin.");
            Assert.IsTrue(trace.Any(point => point.Position.DistanceTo(position + trailingDirection * length) < .001f),
                "Trail extent follows the configured time span of the observed velocity.");
            foreach (var point in trace)
            {
                Vector3 offset = point.Position - position;
                float along = Vector3.Dot(offset, trailingDirection);
                Assert.IsTrue(along >= -.001f && along <= length + .001f,
                    "Particles must lie behind the moving hammer within the configured extent.");
                Assert.IsTrue((offset - trailingDirection * along).Length() <=
                    length * tuning.Effects.TrailLateralSwayRatio + .001f,
                    "Random lightning sway stays within its configured envelope.");
                Assert.IsTrue(point.Scale > 0 && !float.IsInfinity(point.Scale) && !float.IsNaN(point.Scale));
            }
        }
    }
}
