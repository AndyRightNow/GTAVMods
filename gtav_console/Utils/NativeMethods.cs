using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using GTAVMods.Game;

namespace DeveloperConsole {
    internal interface IKeyboardNative {
        int ToUnicode(uint key, byte[] state, StringBuilder result);
        IntPtr ForegroundWindow();
        int WindowProcess(IntPtr window);
        int CurrentProcess { get; }
        short KeyState(int key);
    }

    internal sealed class NativeMethods {
        private readonly IKeyboardNative _native;
        internal NativeMethods(IKeyboardNative native) { _native = native; }
        internal string GetCharsFromKeys(Keys keys, bool shift, bool altGr) {
            var buffer = new StringBuilder(256);
            var state = new byte[256];
            if (shift) state[(int)Keys.ShiftKey] = 0xff;
            if (altGr) {
                state[(int)Keys.ControlKey] = 0xff;
                state[(int)Keys.Menu] = 0xff;
            }
            return _native.ToUnicode((uint)keys, state, buffer) == 1 ? buffer.ToString() : null;
        }
        internal bool ApplicationIsActivated() {
            var foreground = _native.ForegroundWindow();
            if (foreground == IntPtr.Zero) return false;
            return _native.WindowProcess(foreground) == _native.CurrentProcess;
        }
        internal short GetKeyState(int key) { return _native.KeyState(key); }
    }

    internal sealed class WindowsKeyboardNative : IKeyboardNative {
        [GamePrimitive] public int ToUnicode(uint key, byte[] state, StringBuilder result) {
            return ToUnicodeNative(key, 0, state, result, 256, 0);
        }
        [GamePrimitive] public IntPtr ForegroundWindow() { return GetForegroundWindow(); }
        [GamePrimitive] public int WindowProcess(IntPtr window) {
            int process;
            GetWindowThreadProcessId(window, out process);
            return process;
        }
        public int CurrentProcess { [GamePrimitive] get { return Process.GetCurrentProcess().Id; } }
        [GamePrimitive] public short KeyState(int key) { return GetKeyState(key); }
        [DllImport("user32.dll", CharSet = CharSet.Auto, ExactSpelling = true)]
        private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern int GetWindowThreadProcessId(IntPtr window, out int process);
        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern short GetKeyState(int key);
        [DllImport("user32.dll", EntryPoint = "ToUnicode")]
        private static extern int ToUnicodeNative(uint key, uint scan, byte[] state,
            [Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder result, int length, uint flags);
    }
}
