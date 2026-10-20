using GTAVMods.Game;
using NAudio.Wave;

namespace ADModUtils
{
    internal static class UtilityPrimitives
    {
        [GamePrimitive] internal static int GameTime() => GTA.Game.GameTime;
        [GamePrimitive] internal static IWavePlayer CreateAudioOutput() => new WaveOutEvent();
    }
}
