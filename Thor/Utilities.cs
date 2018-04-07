using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using GTA;

namespace Thor
{
    class Utilities
    {
        public class Random
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
        }
    }
}
