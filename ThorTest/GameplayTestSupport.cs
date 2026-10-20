using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ADModUtils;

namespace ModUtilsTest
{
    internal sealed class EventRecord
    {
        public string Name;
        public string Level;
        public int? Interval;
        public Dictionary<string, object> Data;
    }

    internal sealed class RecordingLog : IEventLog
    {
        public readonly List<EventRecord> Records = new List<EventRecord>();
        public string FailOn;
        public void Event(string level, string name, params object[] data) => Add(level, name, null, data);
        public void Throttled(string level, string name, int intervalMs, params object[] data) => Add(level, name, intervalMs, data);
        private void Add(string level, string name, int? interval, object[] data)
        {
            if (name == FailOn) throw new IOException("log unavailable");
            var values = new Dictionary<string, object>();
            for (int i = 0; i < data.Length; i += 2) values.Add((string)data[i], data[i + 1]);
            Records.Add(new EventRecord { Name = name, Level = level, Interval = interval, Data = values });
        }
        public EventRecord Last(string name) => Records.Last(record => record.Name == name);
    }

    internal sealed class SilentAudio : IAudioPlaybackFactory
    {
        internal sealed class Clip : IAudioPlayback
        {
            public bool IsPlaying { get; private set; }
            public double DurationSeconds => 1;
            public int Plays, Stops, Disposals;
            public event EventHandler Stopped { add { } remove { } }
            public void Rewind() { }
            public void Play() { IsPlaying = true; Plays++; }
            public void Stop() { IsPlaying = false; Stops++; }
            public void Dispose() { IsPlaying = false; Disposals++; }
        }
        public readonly Dictionary<string, Clip> Clips = new Dictionary<string, Clip>();
        public IAudioPlayback Open(string path, float volume)
        {
            var clip = new Clip();
            Clips.Add(Path.GetFileName(path), clip);
            return clip;
        }
    }

    internal sealed class AbilityProbe : Thor.IThorAbility
    {
        public bool IsAttachedToPed { get; set; }
        public bool IsHoldingWeapon { get; set; }
        public string DiagnosticState { get; set; } = "idle";
        public int WeaponObject { get; set; }
        public readonly List<string> Calls = new List<string>();
        public string Failure;
        private void Call(string name)
        {
            Calls.Add(name);
            if (name == Failure) throw new InvalidOperationException("ability failure: " + name);
        }
        public void ApplyOn(int ped) { Call("apply:" + ped); IsAttachedToPed = true; }
        public void OnTick(bool onlyHandleWeaponHolding) => Call("tick:" + onlyHandleWeaponHolding);
        public void RemoveAbility() { Call("remove"); IsAttachedToPed = false; }
        public void ResetWeapon() => Call("reset");
        public void DisposeSounds() => Call("sounds");
        public void DisposeWeapon() => Call("weapon");
    }
}
