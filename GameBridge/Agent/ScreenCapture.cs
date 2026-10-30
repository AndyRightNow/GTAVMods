using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using GTAVMods.Game;

namespace GTAVMods.Agent
{
    public interface IWindowPixels
    {
        IntPtr Window { get; }
        IntPtr Foreground { get; }
        Rectangle Bounds(IntPtr window);
        void Copy(Rectangle bounds, Bitmap bitmap);
    }

    public sealed class WindowPixels : IWindowPixels
    {
        private readonly IWindowCaptureApi api;
        public WindowPixels(IWindowCaptureApi api) { this.api = api; }
        public IntPtr Window => api.Window;
        public IntPtr Foreground => api.Foreground;
        public Rectangle Bounds(IntPtr window)
        {
            if (!api.ReadRectangle(window, out var rect)) throw new InvalidOperationException("Cannot read GTA client rectangle.");
            var point = new Point(0, 0);
            if (!api.ToScreen(window, ref point)) throw new InvalidOperationException("Cannot locate GTA client area.");
            return new Rectangle(point.X, point.Y, rect.Right, rect.Bottom);
        }
        public void Copy(Rectangle bounds, Bitmap bitmap)
        {
            using (var graphics = Graphics.FromImage(bitmap))
                api.Copy(graphics, bounds.Location, bounds.Size);
        }
    }

    public sealed class ScreenCapture
    {
        private readonly IWindowPixels pixels;
        private readonly string directory;
        public ScreenCapture(IWindowPixels pixels, string directory) { this.pixels = pixels; this.directory = directory; }
        public string Capture()
        {
            IntPtr window = pixels.Window;
            if (window == IntPtr.Zero || window != pixels.Foreground) throw new InvalidOperationException("GTA must be the foreground window for a screenshot.");
            Rectangle bounds = pixels.Bounds(window);
            if (bounds.Width <= 0 || bounds.Height <= 0) throw new InvalidOperationException("GTA client area is empty.");
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, "frame-" + Guid.NewGuid().ToString("N") + ".png");
            using (var bitmap = new Bitmap(bounds.Width, bounds.Height))
            {
                pixels.Copy(bounds, bitmap);
                bitmap.Save(path, ImageFormat.Png);
            }
            return path;
        }
    }
}
