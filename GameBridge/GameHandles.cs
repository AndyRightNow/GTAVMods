using System.Linq;
using GTA;

namespace GTAVMods.Game
{
    public static class GameHandles
    {
        public static int Of(PoolObject value) => value?.Handle ?? 0;
        public static int[] OfMany(PoolObject[] values) => values.Select(Of).ToArray();
    }
}
