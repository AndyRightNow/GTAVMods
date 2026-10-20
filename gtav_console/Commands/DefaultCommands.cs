using GTAVMods.Game;
using GTA;

namespace DeveloperConsole {
    public sealed class DefaultCommands : Script {
        [GamePrimitive]
        public DefaultCommands() { this.RegisterConsoleScript(new DeveloperConsole.OnConsoleAttached(OnConsoleAttached)); }
        internal static void OnConsoleAttached(DeveloperConsole console) {
            console.PrintDebug("DefaultCommands loaded successfully.");
        }
    }
}
