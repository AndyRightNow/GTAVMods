using System;
using System.IO;

namespace ADModUtils
{
    /// <summary>Owns one reusable clip. Optional audio failures must not stop gameplay.</summary>
    public sealed class AudioClip : IDisposable
    {
        private readonly string path;
        private readonly float volume;
        private readonly IAudioPlaybackFactory factory;
        private readonly IEventLog log;
        private IAudioPlayback output;
        private bool unavailable;

        public AudioClip(string path, float volume, IAudioPlaybackFactory factory, IEventLog log)
        {
            this.path = path;
            this.volume = volume;
            this.factory = Require.NotNull(factory, nameof(factory));
            this.log = Require.NotNull(log, nameof(log));
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
                    output = factory.Open(path, volume);
                    log.Event("INFO", "audio.loaded", "file", path, "durationSeconds", output.DurationSeconds, "volume", volume);
                }
                if (output.IsPlaying && !restart) return;
                output.Stop();
                output.Rewind();
                output.Play();
                log.Throttled("INFO", "audio.play." + Path.GetFileName(path), 1000, "restart", restart);
            }
            catch (Exception ex)
            {
                Disable(ex);
            }
        }

        public void Stop()
        {
            try { output?.Stop(); }
            catch (Exception ex) { Disable(ex); }
        }

        private void Disable(Exception error)
        {
            log.Event("WARN", "audio.disabled", "file", path, "exception", error.ToString());
            Dispose();
        }

        public void Dispose()
        {
            unavailable = true;
            var previous = output;
            output = null;
            try { previous?.Dispose(); }
            catch (Exception ex) { log.Event("WARN", "audio.disposeFailed", "file", path, "exception", ex.ToString()); }
        }
    }
}
