using System;
using System.Collections.Generic;
using GTA;
using GTA.Math;

namespace ADModUtils
{
    public static class Utilities
    {
        public static class Random
        {
            public static T PickOne<T>(T[] arr)
            {
                if (arr == null || arr.Length == 0)
                {
                    return default(T);
                }

                int randomIndex = new System.Random().Next(0, arr.Length);

                return arr[randomIndex];
            }

            public static T PickOneIf<T>(T[] arr, Func<T, bool> predicate)
            {
                if (arr == null || arr.Length == 0)
                {
                    return default(T);
                }

                List<T> arrThatMeetPredicate = new List<T>();
                foreach (var el in arr)
                {
                    if (predicate(el))
                    {
                        arrThatMeetPredicate.Add(el);
                    }
                }

                int randomIndex = new System.Random().Next(0, arrThatMeetPredicate.Count);

                return arrThatMeetPredicate[randomIndex];
            }

            public static System.Random SystemRandomInstance = new System.Random();

            public static int RandomNegation()
            {
                return SystemRandomInstance.Next(0, 2) > 0 ? 1 : -1;
            }

            public static float NextFloat(bool allowNegative = true)
            {
                return Convert.ToSingle(SystemRandomInstance.NextDouble()) * (allowNegative ? RandomNegation() : 1.0f);
            }
        }

        public static class Math
        {
            public static Vector3 DirectionToRotation(Vector3 dir, float roll = 0.0f)
            {
                dir = dir.Normalized;
                Vector3 rot;
                rot.Z = - RadiansToDegrees(Convert.ToSingle(System.Math.Atan2(dir.X, dir.Y)));
                Vector3 vec = new Vector3(dir.Z, new Vector3(dir.X, dir.Y, 0.0f).Length(), 0.0f).Normalized;
                rot.X = RadiansToDegrees(Convert.ToSingle(System.Math.Atan2(vec.X, vec.Y)));
                rot.Y = roll;
                return rot;
            }

            public static float RadiansToDegrees(float radians)
            {
                return radians * 57.2958f;
            }

            public static float Angle(Vector2 v1, Vector2 v2)
            {
                return Convert.ToSingle(System.Math.Atan2(v1.X * v2.Y - v1.Y * v2.X, Vector2.Dot(v1, v2)) * 180 / System.Math.PI);
            }

            public static float HorizontalLength(Vector3 v)
            {
                return Convert.ToSingle(System.Math.Sqrt(v.X * v.X + v.Y * v.Y));
            }

            public static bool CloseTo(float source, float target, float delta)
            {
                return source <= target + delta && source >= target - delta;
            }

            public static bool CloseTo(Vector3 from, Vector3 to, float delta)
            {
                return (from - to).Length() <= delta;
            }

            public static Vector3 RandomVectorPerpendicularTo(Vector3 target, bool normalized = true)
            {

                var rand = Random.SystemRandomInstance;
                float x = Random.NextFloat(), y = Random.NextFloat(), z = 0.0f;

                if (target.Z == 0.0f)
                {
                    z = Random.NextFloat();
                    
                    if (target.Y != 0.0f)
                    {
                        y = -(target.X * x / target.Y);   
                    }
                }
                else
                {
                    z = -((target.X * x + target.Y * y) / target.Z);
                }

                var result = new Vector3(x, y, z);

                return normalized ? result.Normalized : result;
            }
        }

        public class Timer
        {
            private int startGameTime;
            private int interval;
            private int previouslyFiredGameTime;
            private TimerHandler handler;

            public delegate void TimerHandler();

            public Timer(int interval, TimerHandler handler)
            {
                this.interval = interval;
                this.handler = handler;
                startGameTime = Game.GameTime;
                previouslyFiredGameTime = startGameTime;
            }

            public void OnTick()
            {
                if (Game.GameTime - previouslyFiredGameTime >= interval)
                {
                    this.handler();
                    previouslyFiredGameTime = Game.GameTime;
                }
            }
        }

        public static void Swap<T>(ref T lhs, ref T rhs)
        {
            T temp;
            temp = lhs;
            lhs = rhs;
            rhs = temp;
        }

        public static class Audio
        {
            public static void Play(string filename)
            {
                var player = new NAudio.Wave.WaveOut();
                var audio = new NAudio.Wave.AudioFileReader(filename);
                player.Init(audio);
                player.Play();
                player.PlaybackStopped += (object sender, NAudio.Wave.StoppedEventArgs a) => {
                    player.Dispose();
                    player = null;
                    audio.Dispose();
                    audio = null;
                };
            }
        }
    }
}
