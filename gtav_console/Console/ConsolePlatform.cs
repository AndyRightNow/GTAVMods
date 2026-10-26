using System;
using GTAVMods.Game;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;
using GTA.UI;
using Font = GTA.UI.Font;

namespace DeveloperConsole {
    public interface IConsoleDisplay {
        float Width { get; }
        float Height { get; }
        void DrawRectangle(Point position, Size size, Color color);
        void DrawText(string text, Point position, float scale, Color color, Font font);
    }

    public interface IConsolePlatform : IConsoleDisplay {
        int GameTime { get; }
        DateTime Now { get; }
        string ReadClipboard();
        void WriteClipboard(string text);
        string TranslateKey(Keys key, bool shift, bool alt);
        short CapsLockState { get; }
        bool IsForeground { get; }
    }

    /// <summary>Only primitive UI, operating-system and clock bindings live here.</summary>
    internal class LiveConsolePlatform : IConsolePlatform {
        private readonly NativeMethods _keyboard;
        private readonly IClipboardNative _clipboard;
        internal LiveConsolePlatform(NativeMethods keyboard, IClipboardNative clipboard) {
            _keyboard = keyboard;
            _clipboard = clipboard;
        }
        public virtual float Width { [GamePrimitive] get { return GTA.UI.Screen.Width; } }
        public virtual float Height { [GamePrimitive] get { return GTA.UI.Screen.Height; } }
        public virtual int GameTime { [GamePrimitive] get { return GTA.Game.GameTime; } }
        public virtual DateTime Now { get { return DateTime.Now; } }
        public short CapsLockState { get { return _keyboard.GetKeyState(0x14); } }
        public bool IsForeground { get { return _keyboard.ApplicationIsActivated(); } }
        public string TranslateKey(Keys key, bool shift, bool alt) {
            return _keyboard.GetCharsFromKeys(key, shift, alt);
        }
        [GamePrimitive]
        public virtual void DrawRectangle(Point position, Size size, Color color) {
            new ContainerElement(position, size, color).Draw();
        }
        [GamePrimitive]
        public virtual void DrawText(string text, Point position, float scale, Color color, Font font) {
            new TextElement(text, position, scale, color, font, Alignment.Left).Draw();
        }
        public string ReadClipboard() {
            string text = null;
            ClipboardThread.Run(() => text = _clipboard.Read());
            return text;
        }
        public void WriteClipboard(string text) {
            ClipboardThread.Run(() => _clipboard.Write(text));
        }
    }

    internal interface IClipboardNative {
        string Read();
        void Write(string text);
    }

    internal sealed class WindowsClipboardNative : IClipboardNative {
        [GamePrimitive] public string Read() { return Clipboard.GetText(TextDataFormat.Text); }
        [GamePrimitive] public void Write(string text) { Clipboard.SetText(text); }
    }

    internal static class ClipboardThread {
        // STA ownership and failure propagation are our policy and remain testable.
        internal static void Run(Action operation) {
            Exception failure = null;
            var thread = new Thread(() => {
                try { operation(); } catch (Exception error) { failure = error; }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();
            if (failure != null) throw new InvalidOperationException("Clipboard operation failed.", failure);
        }
    }
}
