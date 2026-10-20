using GTA.Math;
using System.Drawing;
using GTA.Native;
using GTAVMods.Game;

namespace ADModUtils
{
    public class Line
    {
        public Line(Vector3 start, Vector3 end, Color color)
        {
            Start = start;
            End = end;
            Color = color;
        }

        public Vector3 Start { get; }

        public Vector3 End { get; }

        private Color Color { get; }

        public void Draw(INativeInvoker native)
        {
            native.Call(Hash.DRAW_LINE, Start.X, Start.Y, Start.Z, End.X, End.Y, End.Z,
                Color.R, Color.G, Color.B, Color.A);
        }

        public override string ToString()
        {
            return string.Format("Line: Start {0}, End {1}, Color {2}", Start, End, Color);
        }
    }
}
