using CitizenFX.Core;
using CitizenFX.Core.Native;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ModUtils
{
    public static class Controls
    {
        private static HashSet<string> ActiveKeys;
        private static string CommandName = "TrackKeyPressState";
        private static bool Initialized = false;
        private static string[] ReigsteredKeys = { "z", "o", "x", "r", "h", "t", "u", "y" };

        public static void Init()
        {
            foreach (string key in ReigsteredKeys)
            {
                var keyCommandName = $"{CommandName}_{key}";

                API.RegisterCommand($"+{keyCommandName}", new Action<int, List<object>, string>((source, args, rawCommand) =>
                {
                    ActiveKeys.Add(key);
                }), false);

                API.RegisterCommand($"-{keyCommandName}", new Action<int, List<object>, string>((source, args, rawCommand) =>
                {
                    ActiveKeys.Remove(key);
                }), false);

                API.RegisterKeyMapping($"+{keyCommandName}", $"Track {key} State", "keyboard", key);
            }
        }

        public static bool IsKeyPressed(string key)
        {
            return ActiveKeys.Contains(key);
        }
    }
}
