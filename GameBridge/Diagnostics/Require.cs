using System;

namespace ADModUtils
{
    /// <summary>Pure validation shared by composition and resource configuration.</summary>
    public static class Require
    {
        public static T NotNull<T>(T value, string name) where T : class
            => value ?? throw new ArgumentNullException(name);

        public static int NonNegative(int value, string name)
            => value >= 0 ? value : throw new ArgumentOutOfRangeException(name);

        public static T[] NonEmpty<T>(T[] value, string name)
        {
            NotNull(value, name);
            return value.Length != 0 ? value : throw new ArgumentException("At least one choice is required.", name);
        }
    }
}
