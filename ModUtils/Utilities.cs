using System;
using System.Collections.Generic;
using System.Linq;
using GTA;
using GTA.Math;
using GTAVMods.Game;

namespace ADModUtils
{
    public static class Utilities
    {
        public static class Random
        {
            public static T PickOne<T>(T[] arr, System.Random random)
            {
                Require.NotNull(random, nameof(random));
                Require.NonEmpty(arr, nameof(arr));
                lock (random)
                    return arr[random.Next(arr.Length)];
            }

            public static T PickOneIf<T>(T[] arr, Func<T, bool> predicate, System.Random random)
            {
                return PickOne(Require.NotNull(arr, nameof(arr)).Where(Require.NotNull(predicate, nameof(predicate))).ToArray(), random);
            }

            public static int RandomNegation(System.Random random)
            {
                Require.NotNull(random, nameof(random));
                lock (random)
                    return random.Next(0, 2) > 0 ? 1 : -1;
            }

            public static float NextFloat(System.Random random, bool allowNegative = true)
            {
                Require.NotNull(random, nameof(random));
                lock (random)
                    return Convert.ToSingle(random.NextDouble()) * (allowNegative ? RandomNegation(random) : 1.0f);
            }
        }

        public static class Math
        {
            public const float DegreePerRadian = 57.2958f;

            public static Vector3 DirectionToRotation(Vector3 dir, float roll = 0.0f)
            {
                dir = dir.Normalized;
                Vector3 rot = new Vector3();
                rot.Z = -RadiansToDegrees(Convert.ToSingle(System.Math.Atan2(dir.X, dir.Y)));
                Vector3 vec = new Vector3(dir.Z, new Vector3(dir.X, dir.Y, 0.0f).Length(), 0.0f).Normalized;
                var x = RadiansToDegrees(Convert.ToSingle(System.Math.Atan2(vec.X, vec.Y)));
                rot.X = x;

                rot.Y = roll;
                return rot;
            }
            public static Quaternion DirectionToQuaternion(Vector3 forward, Vector3 dir)
            {
                var normalizedDir = dir.Normalized;
                var normalizedForward = forward.Normalized;
                if (normalizedForward == Vector3.Zero || normalizedDir == Vector3.Zero)
                    return Quaternion.Identity;
                float dot = Vector3.Dot(normalizedForward, normalizedDir);
                if (dot <= -0.999999f)
                {
                    // Opposite directions have no cross-product axis. Choose a stable orthogonal axis.
                    var reference = System.Math.Abs(normalizedForward.X) < 0.9f ? Vector3.UnitX : Vector3.UnitY;
                    return new Quaternion(Vector3.Cross(normalizedForward, reference).Normalized, (float)System.Math.PI);
                }
                var cross = Vector3.Cross(normalizedForward, normalizedDir);
                return Quaternion.Normalize(new Quaternion(cross.X, cross.Y, cross.Z, 1 + dot));
            }

            public static Vector3 QuaternionToEulerAngles(Quaternion q)
            {
                Vector3 angles = new Vector3();

                // roll (x-axis rotation)

                double sinr_cosp = 2 * (q.W * q.X + q.Y * q.Z);
                double cosr_cosp = 1 - 2 * (q.X * q.X + q.Y * q.Y);
                angles.X = Convert.ToSingle(System.Math.Atan2(sinr_cosp, cosr_cosp));

                // pitch (y-axis rotation)
                double sinp = 2 * (q.W * q.Y - q.Z * q.X);
                if (System.Math.Abs(sinp) >= 1)
                    angles.Y = Convert.ToSingle((System.Math.PI / 2) * (sinp < 0 ? -1 : 1)); // use 90 degrees if out of range
                else
                    angles.Y = Convert.ToSingle(System.Math.Asin(sinp));

                // yaw (z-axis rotation)
                double siny_cosp = 2 * (q.W * q.Z + q.X * q.Y);
                double cosy_cosp = 1 - 2 * (q.Y * q.Y + q.Z * q.Z);
                angles.Z = Convert.ToSingle(System.Math.Atan2(siny_cosp, cosy_cosp));

                return angles * DegreePerRadian;
            }

            public static float RadiansToDegrees(float radians)
            {
                return radians * DegreePerRadian;
            }

            public static float DegreesToRadians(float degrees)
            {
                return degrees / DegreePerRadian;
            }

            public static float Angle(Vector2 v1, Vector2 v2)
            {
                return Convert.ToSingle(System.Math.Atan2(v1.X * v2.Y - v1.Y * v2.X, Vector2.Dot(v1, v2)) * 180 / System.Math.PI);
            }

            public static bool CloseTo(float source, float target, float delta)
            {
                return source <= target + delta && source >= target - delta;
            }

            public static bool CloseTo(Vector3 from, Vector3 to, float delta)
            {
                return (from - to).Length() <= delta;
            }

            public static Vector3 RandomVectorPerpendicularTo(Vector3 target, System.Random random, bool normalized = true)
            {
                return PerpendicularTo(target, Random.NextFloat(random), Random.NextFloat(random),
                    target.Z == 0 ? Random.NextFloat(random) : 0, normalized);
            }

            public static Vector3 PerpendicularTo(Vector3 target, float x, float y, float z, bool normalized = true)
            {
                if (target.Z == 0.0f)
                {
                    if (target.Y != 0.0f)
                    {
                        y = -(target.X * x / target.Y);
                    }
                    else if (target.X != 0.0f)
                    {
                        x = 0.0f;
                    }
                }
                else
                {
                    z = -((target.X * x + target.Y * y) / target.Z);
                }

                var result = new Vector3(x, y, z);
                // A valid random source may produce all zeroes. Keep the basis usable.
                if (result == Vector3.Zero)
                    result = target == Vector3.Zero ? Vector3.UnitX :
                        Vector3.Cross(target, target.X == 0 ? Vector3.UnitX : Vector3.UnitY);

                return normalized ? result.Normalized : result;
            }
        }

        public class Timer
        {
            private readonly int interval;
            private int previouslyFiredGameTime;
            private readonly TimerHandler handler;
            private readonly Func<int> clock;

            public delegate void TimerHandler();

            [GamePrimitive]
            public Timer(int interval, TimerHandler handler)
                : this(interval, handler, new Func<int>(UtilityPrimitives.GameTime))
            {
            }

            public Timer(int interval, TimerHandler handler, Func<int> clock)
            {
                this.interval = Require.NonNegative(interval, nameof(interval));
                this.handler = Require.NotNull(handler, nameof(handler));
                this.clock = Require.NotNull(clock, nameof(clock));
                previouslyFiredGameTime = clock();
            }

            public void OnTick()
            {
                int now = clock();
                if (unchecked(now - previouslyFiredGameTime) >= interval)
                {
                    handler();
                    previouslyFiredGameTime = now;
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

    }
}
