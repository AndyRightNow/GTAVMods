using System;
using System.IO;
using NAudio.Wave;

namespace ADModUtils
{
    /// <summary>Owns one reusable clip. Optional audio failures must not stop gameplay.</summary>
    public sealed class AudioClip : IDisposable
    {
        private readonly string path;
        private readonly float volume;
        private WaveOutEvent output;
        private AudioFileReader reader;
        private bool unavailable;

        public AudioClip(string path, float volume = 1.0f)
        {
            this.path = path;
            this.volume = volume;
        }

        public static string ResourcePath(string name)
        {
            // SHVDNE shadow-copies DLLs; Assembly.Location points into its cache.
            // The script AppDomain base remains the configured scripts folder.
            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Thor", name);
        }

        public void Play(bool restart = true)
        {
            if (unavailable) return;
            try
            {
                if (output == null)
                {
                    reader = new AudioFileReader(path) { Volume = volume };
                    output = new WaveOutEvent();
                    output.Init(reader);
                    Logger.Event("INFO", "audio.loaded", "file", path, "durationSeconds", reader.TotalTime.TotalSeconds, "volume", volume);
                }
                if (output.PlaybackState == PlaybackState.Playing && !restart) return;
                output.Stop();
                reader.Position = 0;
                output.Play();
                Logger.Throttled("INFO", "audio.play." + Path.GetFileName(path), 1000, "restart", restart);
            }
            catch (Exception ex)
            {
                Logger.Event("WARN", "audio.disabled", "file", path, "exception", ex.ToString());
                Dispose();
            }
        }

        public void Stop()
        {
            output?.Stop();
        }

        public void Dispose()
        {
            unavailable = true;
            try { output?.Dispose(); }
            finally
            {
                output = null;
                reader?.Dispose();
                reader = null;
            }
        }
    }
}
