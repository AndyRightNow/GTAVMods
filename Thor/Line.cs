using GTA.Math;
using System;
using System.Drawing;

namespace Thor
{
    public class Line
    {
        private Vector3 start;
        private Vector3 end;
        private Color color;

        public Line(Vector3 start, Vector3 end, Color color)
        {
            this.start = start;
            this.end = end;
            this.color = color;
        }

        public Vector3 Start
        {
            get
            {
                return start;
            }
        }

        public Vector3 End
        {
            get
            {
                return end;
            }
        }

        public Color Color
        {
            get
            {
                return color;
            }
        }

        public void Draw()
        {
            NativeHelper.DrawLine(start, end, color);
        }

        public override string ToString()
        {
            return String.Format("Line: Start {0}, End {1}, Color {2}", start, end, color);
        }
    }
}
