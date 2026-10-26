using System;
using NAudio.Wave;

namespace ADModUtils
{
    public interface IAudioPlayback : IDisposable
    {
        bool IsPlaying { get; }
        double DurationSeconds { get; }
        void Rewind();
        void Play();
        void Stop();
    }

    public interface IAudioPlaybackFactory
    {
        IAudioPlayback Open(string path, float volume);
    }

    /// <summary>Maps clip operations to NAudio; output creation is replaceable without an audio device.</summary>
    public sealed class NAudioPlaybackFactory : IAudioPlaybackFactory
    {
        private readonly Func<IWavePlayer> createOutput;
        public NAudioPlaybackFactory() : this(UtilityPrimitives.CreateAudioOutput) { }
        public NAudioPlaybackFactory(Func<IWavePlayer> createOutput)
        {
            this.createOutput = Require.NotNull(createOutput, nameof(createOutput));
        }

        public IAudioPlayback Open(string path, float volume)
        {
            var reader = new AudioFileReader(path);
            IWavePlayer output = null;
            try
            {
                reader.Volume = volume;
                output = createOutput();
                output.Init(reader);
                return new NAudioPlayback(output, reader);
            }
            catch
            {
                try { output?.Dispose(); }
                finally { reader.Dispose(); }
                throw;
            }
        }
    }

    public sealed class NAudioPlayback : IAudioPlayback
    {
        private readonly IWavePlayer output;
        private readonly AudioFileReader reader;
        public NAudioPlayback(IWavePlayer output, AudioFileReader reader)
        {
            this.output = output;
            this.reader = reader;
        }
        public bool IsPlaying => output.PlaybackState == PlaybackState.Playing;
        public double DurationSeconds => reader.TotalTime.TotalSeconds;
        public void Rewind() => reader.Position = 0;
        public void Play() => output.Play();
        public void Stop() => output.Stop();
        public void Dispose()
        {
            try { output.Dispose(); }
            finally { reader.Dispose(); }
        }
    }

}
