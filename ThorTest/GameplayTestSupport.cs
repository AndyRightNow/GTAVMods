using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ADModUtils;
using NAudio.Wave;

namespace ModUtilsTest
{
    internal sealed class EventRecord
    {
        public string Name;
        public string Level;
        public Dictionary<string, object> Data;
    }

    internal sealed class SilentAudio : IAudioPlaybackFactory
    {
        // Only the physical output is substituted. Decoding, volume, seeking and
        // ownership all pass through the production NAudio adapter.
        internal sealed class Clip : IWavePlayer
        {
            public string Failure;
            public IWaveProvider Input;
            public readonly List<long> PlayPositions = new List<long>();
            public bool IsPlaying => PlaybackState == PlaybackState.Playing;
            public PlaybackState PlaybackState { get; private set; }
            public float Volume { get; set; }
            public WaveFormat OutputWaveFormat => Input.WaveFormat;
            public int Plays, Stops, Disposals;
            public event EventHandler<StoppedEventArgs> PlaybackStopped { add { } remove { } }
            private void Check(string operation) { if (Failure == operation) throw new IOException(operation + " failed"); }
            public void Init(IWaveProvider input) { Input = input; Check("init"); }
            public void Play()
            {
                Plays++;
                Check("play");
                var reader = (AudioFileReader)Input;
                PlayPositions.Add(reader.Position);
                // An output consumes samples; a subsequent catch must rewind the
                // real reader instead of resuming after the consumed prefix.
                var frame = new byte[Input.WaveFormat.BlockAlign];
                Input.Read(frame, 0, frame.Length);
                PlaybackState = PlaybackState.Playing;
            }
            public void Stop() { Stops++; Check("stop"); PlaybackState = PlaybackState.Stopped; }
            public void Pause() => throw new NotSupportedException();
            public void Dispose() { Disposals++; PlaybackState = PlaybackState.Stopped; Check("dispose"); }
        }
        public readonly Dictionary<string, Clip> Clips = new Dictionary<string, Clip>();
        public readonly List<Tuple<string, float>> Opens = new List<Tuple<string, float>>();
        public string Failure;
        public IAudioPlayback Open(string path, float volume)
        {
            Opens.Add(Tuple.Create(path, volume));
            return new NAudioPlaybackFactory(() =>
            {
                if (Failure == "create") throw new IOException("output creation failed");
                var clip = new Clip { Failure = Failure };
                Clips.Add(Path.GetFileName(path), clip);
                return clip;
            }).Open(Failure == "missing" ? path + ".missing" : path, volume);
        }
    }

}
