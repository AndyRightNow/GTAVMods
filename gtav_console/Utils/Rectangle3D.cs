using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Media.Media3D;
using GTA;
using GTA.Math;
using GTA.UI;
using Quaternion = GTA.Math.Quaternion;
using GTAVMods.Game;

namespace DeveloperConsole {
    /// <summary>
    ///     Represents a 3D line
    /// </summary>
    public class Line3D {
        private readonly GTAFuncs _game;
        /// <summary>
        ///     Create a Line3D
        /// </summary>
        /// <param name="p1">The first point in the line</param>
        /// <param name="p2">The second point in the line</param>
        public Line3D(Vector3 p1, Vector3 p2)
            : this(p1, p2, DeveloperConsole.GeometryFunctions) { }
        public Line3D(Vector3 p1, Vector3 p2, GTAFuncs game) {
            _game = game;
            Point1 = p1;
            Point2 = p2;
        }

        /// <summary>
        ///     The first point in the line
        /// </summary>
        public Vector3 Point1 { get; private set; }

        /// <summary>
        ///     The second point in the line
        /// </summary>
        public Vector3 Point2 { get; private set; }

        /// <summary>
        /// 	Draw the line
        /// </summary>
        public void Draw(Color c) {
            _game.DrawLine(Point1, Point2, c);
        }
    }

    /// <summary>
    ///     Represents a 3D face
    /// </summary>
    public class Face3D {
        private readonly GTAFuncs _game;
        /// <summary>
        ///     Create a Face3D
        /// </summary>
        /// <param name="bottomLeft">The bottom left corner of the face</param>
        /// <param name="topRight">The top right corner of the face</param>
        /// <param name="topLeft">The top left corner of the face</param>
        /// <param name="bottomRight">The bottom right corner of the face</param>
        public Face3D(Vector3 bottomLeft, Vector3 topRight, Vector3 topLeft, Vector3 bottomRight)
            : this(bottomLeft, topRight, topLeft, bottomRight, DeveloperConsole.GeometryFunctions) { }
        public Face3D(Vector3 bottomLeft, Vector3 topRight, Vector3 topLeft, Vector3 bottomRight, GTAFuncs game) {
            _game = game;
            BottomLeft = bottomLeft;
            TopRight = topRight;
            TopLeft = topLeft;
            BottomRight = bottomRight;
        }

        /// <summary>
        ///     The bottom left corner of the face
        /// </summary>
        public Vector3 BottomLeft { get; private set; }

        /// <summary>
        ///     The top right corner of the face
        /// </summary>
        public Vector3 TopRight { get; private set; }

        /// <summary>
        ///     The top left corner of the face
        /// </summary>
        public Vector3 TopLeft { get; private set; }

        /// <summary>
        ///     The bottom right corner of the face
        /// </summary>
        public Vector3 BottomRight { get; private set; }

        /// <summary>
        ///     Draws the face
        /// </summary>
        /// <param name="c">The color of the face</param>
        public void Draw(Color c) {
            _game.DrawBox(BottomLeft, TopRight, c);
        }
    }

    /// <summary>
    ///     Represents a 3D Rectangle
    /// </summary>
    public class Rectangle3D {
        private readonly GTAFuncs _game;
        /// <summary>
        ///     Create a Rectangle3D
        /// </summary>
        /// <param name="center">The center of the rectangle.</param>
		/// <param name="minimum">The minimum dimensions.</param>
		/// <param name="maximum">The maximum dimensions.</param>
        public Rectangle3D(Vector3 center, Vector3 min, Vector3 max)
            : this(center, min, max, DeveloperConsole.GeometryFunctions) { }
        public Rectangle3D(Vector3 center, Vector3 min, Vector3 max, GTAFuncs game) {
            _game = game;
            Center = center;
            Min = min;
            Max = max;
                
            Corners = CreateCorners(center, min, max);
            GenerateEdges();
            GenerateFaces();
        }

        internal static Dictionary<string, Vector3> CreateCorners(Vector3 center, Vector3 min, Vector3 max) {
            return new Dictionary<string, Vector3> {
                {"000", center + new Vector3(min.X,min.Y,min.Z)},
                {"100", center + new Vector3(max.X,min.Y,min.Z)},
                {"010", center + new Vector3(min.X,max.Y,min.Z)},
                {"001", center + new Vector3(min.X,min.Y,max.Z)},
                {"110", center + new Vector3(max.X,max.Y,min.Z)},
                {"101", center + new Vector3(max.X,min.Y,max.Z)},
                {"011", center + new Vector3(min.X,max.Y,max.Z)},
                {"111", center + new Vector3(max.X,max.Y,max.Z)}
            };
        }

        /// <summary>
        ///     The center position of the rectangle
        /// </summary>
        public Vector3 Center { get; private set; }

        /// <summary>
        ///     The minimum dimensions.
        /// </summary>
        public Vector3 Min { get; private set; }

        /// <summary>
        ///     The maximum dimensions.
        /// </summary>
        public Vector3 Max { get; private set; }

        /// <summary>
        ///     The corners of the rectangle where the string is the corner vector and the value is the location of the corner
        /// </summary>
        public Dictionary<string, Vector3> Corners { get; private set; }

        /// <summary>
        ///     The edges of the rectangle
        /// </summary>
        public List<Line3D> Edges { get; private set; }

        /// <summary>
        ///     The faces of the rectangle
        /// </summary>
        public List<Face3D> Faces { get; private set; }

        /// <summary>
        ///     Generate the edges of the rectangle, should be called when modifying corners
        /// </summary>
        private void GenerateEdges() {
            Edges = new List<Line3D> {
                new Line3D(Corners["000"], Corners["001"], _game),
                new Line3D(Corners["000"], Corners["100"], _game),
                new Line3D(Corners["100"], Corners["101"], _game),
                new Line3D(Corners["001"], Corners["101"], _game),
                new Line3D(Corners["001"], Corners["011"], _game),
                new Line3D(Corners["000"], Corners["010"], _game),
                new Line3D(Corners["100"], Corners["110"], _game),
                new Line3D(Corners["101"], Corners["111"], _game),
                new Line3D(Corners["110"], Corners["111"], _game),
                new Line3D(Corners["011"], Corners["111"], _game),
                new Line3D(Corners["010"], Corners["110"], _game),
                new Line3D(Corners["010"], Corners["011"], _game)
            };
        }

        /// <summary>
        ///     Generate the faces of the rectangl, should be called when modifying corners
        /// </summary>
        private void GenerateFaces() {
            Faces = new List<Face3D> {
                new Face3D(Corners["000"], Corners["101"], Corners["001"], Corners["100"], _game),
                new Face3D(Corners["100"], Corners["111"], Corners["101"], Corners["110"], _game),
                new Face3D(Corners["110"], Corners["011"], Corners["111"], Corners["010"], _game),
                new Face3D(Corners["010"], Corners["001"], Corners["011"], Corners["000"], _game),
                new Face3D(Corners["101"], Corners["011"], Corners["001"], Corners["111"], _game),
                new Face3D(Corners["100"], Corners["010"], Corners["000"], Corners["110"], _game)
            };
        }

        /// <summary>
        ///     Rotate the rectangle by a quaternion
        /// </summary>
        /// <param name="rot">The quaternion to rotate by</param>
        /// <returns>The current rectangle instance</returns>
        public Rectangle3D Rotate(Quaternion rot) {
            Corners = RotateCorners(Corners, Center, rot);
            GenerateEdges();
            GenerateFaces();
            return this;
        }

        internal static Dictionary<string, Vector3> RotateCorners(
            IDictionary<string, Vector3> corners, Vector3 center, Quaternion rotation) {
            var q = new QuaternionRotation3D(new System.Windows.Media.Media3D.Quaternion(rotation.X, rotation.Y, rotation.Z, rotation.W));
            var transform = new RotateTransform3D(q, ToPoint3D(center));
            var result = new Dictionary<string, Vector3>();
            foreach (var corner in corners)
                result.Add(corner.Key, ToVector3(transform.Transform(ToPoint3D(corner.Value))));
            return result;
        }

        /// <summary>
        ///     Draw the rectangles wireframe without diagonals
        /// </summary>
        /// <param name="c">The color of the wire frame</param>
        /// <returns>The current rectangle instance</returns>
        public Rectangle3D DrawWireFrame(Color c) {
            return DrawWireFrame(c, false);
        }

        /// <summary>
        ///     Draw the rectangles wireframe
        /// </summary>
        /// <param name="c">The color of the wireframe</param>
        /// <param name="diagonals">Whether or not to draw diagonals</param>
        /// <returns>The current rectangle instance</returns>
        public Rectangle3D DrawWireFrame(Color c, bool diagonals) {
            foreach (var e in Edges) e.Draw(c);
            if (diagonals) {
                foreach (var f in Faces) {
                    new Line3D(f.BottomLeft, f.TopRight, _game).Draw(c);
                    new Line3D(f.BottomRight, f.TopLeft, _game).Draw(c);
                }
            }
            return this;
        }

        /// <summary>
        ///     Draw all the rectangle
        /// </summary>
        /// <param name="c">The color of the rectangle</param>
        /// <returns>The current rectangle instance</returns>
        public Rectangle3D Draw(Color c) {
            foreach (var f in Faces) f.Draw(c);
            return this;
        }

        /// <summary>
        ///     Draw a 2D overlay of the 3D rectangle debug
        /// </summary>
        /// <param name="c">The color of the debug overlay</param>
        /// <returns>The current rectangle instance</returns>
        public Rectangle3D DrawDebug(Color c) {
            foreach (var v in Corners) {
                var w = _game.WorldToScreen(v.Value);
                _game.DrawText(v.Key, new Point((int) w.X, (int) w.Y), .15f, c, 0);
            }
            return this;
        }

        /// <summary>
        ///     Converts a Point3D to a Vector3
        /// </summary>
        /// <param name="p">The Point3D</param>
        /// <returns>The Vector3</returns>
        private static Vector3 ToVector3(Point3D p) {
            return new Vector3(Convert.ToSingle(p.X), Convert.ToSingle(p.Y), Convert.ToSingle(p.Z));
        }

        /// <summary>
        ///     Converts a Vector3 to a Point3D
        /// </summary>
        /// <param name="v">The Vector3</param>
        /// <returns>The Point3D</returns>
        private static Point3D ToPoint3D(Vector3 v) {
            return new Point3D(v.X, v.Y, v.Z);
        }
    }
}
