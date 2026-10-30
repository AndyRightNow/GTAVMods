using System;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;

namespace GTAVMods.Game
{
    [StructLayout(LayoutKind.Sequential)]
    public struct ClientRectangle { public int Left, Top, Right, Bottom; }
    public interface IWindowCaptureApi
    {
        IntPtr Window { get; }
        IntPtr Foreground { get; }
        bool ReadRectangle(IntPtr window, out ClientRectangle rectangle);
        bool ToScreen(IntPtr window, ref Point point);
        void Copy(Graphics graphics, Point source, Size size);
    }

    /// <summary>Raw Windows calls; foreground checks, failed observations and bitmap ownership live in WindowPixels/ScreenCapture.</summary>
    public sealed class NativeWindowCaptureApi : IWindowCaptureApi
    {
        public IntPtr Window { [GamePrimitive] get => Process.GetCurrentProcess().MainWindowHandle; }
        public IntPtr Foreground { [GamePrimitive] get => GetForegroundWindow(); }
        [GamePrimitive] public bool ReadRectangle(IntPtr window, out ClientRectangle rectangle) => GetClientRect(window, out rectangle);
        [GamePrimitive] public bool ToScreen(IntPtr window, ref Point point) => ClientToScreen(window, ref point);
        [GamePrimitive] public void Copy(Graphics graphics, Point source, Size size) => graphics.CopyFromScreen(source, Point.Empty, size, CopyPixelOperation.SourceCopy);
        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr window, out ClientRectangle rectangle);
        [DllImport("user32.dll")] private static extern bool ClientToScreen(IntPtr window, ref Point point);
    }
}
