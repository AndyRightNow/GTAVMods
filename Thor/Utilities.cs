using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using GTA;

namespace Thor
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
        }

        public static class Math
        {
            public static float Angle(GTA.Math.Vector2 v1, GTA.Math.Vector2 v2)
            {
                return Convert.ToSingle(System.Math.Atan2(v1.X * v2.Y - v1.Y * v2.X, GTA.Math.Vector2.Dot(v1, v2)) * 180 / System.Math.PI);
            }

            public static float HorizontalLength(GTA.Math.Vector3 v)
            {
                return Convert.ToSingle(System.Math.Sqrt(v.X * v.X + v.Y * v.Y));
            }

            public static bool CloseTo(float source, float target, float delta)
            {
                return source <= target + delta && source >= target - delta;
            }
        }
    }
}
