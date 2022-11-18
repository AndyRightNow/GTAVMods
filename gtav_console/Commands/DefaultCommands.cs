using System;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using GTA;
using GTA.Math;
using GTA.Native;

namespace DeveloperConsole {
    public class DefaultCommands : Script {
        private DeveloperConsole _developerConsole;
        private bool _godEnabled;
        private bool _forceFieldEnabled;
        private bool _noClipEnabled;
        private Blip _lastWaypoint;
        private Player _player;

        public DefaultCommands() {
            this.RegisterConsoleScript(OnConsoleAttached);
        }

        private void OnConsoleAttached(DeveloperConsole dc) {

            _player = Game.Player;

            _developerConsole = dc;

            dc.PrintDebug("DefaultCommands loaded successfully.");
        }
    }
}