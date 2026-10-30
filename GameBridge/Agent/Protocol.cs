using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Web.Script.Serialization;

namespace GTAVMods.Agent
{
    public static class Protocol
    {
        public const int Version = 1;
        public const int MaximumRequestBytes = 1024 * 1024;
        public const int MaximumSteps = 128;
        public static string Encode(object value)
        {
            var serializer = new JavaScriptSerializer { MaxJsonLength = 4 * 1024 * 1024, RecursionLimit = 32 };
            serializer.RegisterConverters(new[] { new GameValueConverter() });
            return serializer.Serialize(value);
        }
        public static Dictionary<string, object> Decode(string json) => new JavaScriptSerializer { MaxJsonLength = MaximumRequestBytes, RecursionLimit = 16 }.Deserialize<Dictionary<string, object>>(json);

        public static string Text(IDictionary<string, object> args, string key)
        {
            if (!args.TryGetValue(key, out var value) || !(value is string text) || string.IsNullOrWhiteSpace(text))
                throw new ArgumentException("Required nonempty string: " + key);
            return text;
        }

        public static double Number(IDictionary<string, object> args, string key, double minimum, double maximum, double fallback)
        {
            if (!args.TryGetValue(key, out var value)) return fallback;
            if (!(value is int || value is long || value is decimal || value is double || value is float))
                throw new ArgumentException("Required number: " + key);
            double number = Convert.ToDouble(value, CultureInfo.InvariantCulture);
            if (double.IsNaN(number) || number < minimum || number > maximum)
                throw new ArgumentOutOfRangeException(key, "Expected " + minimum + " through " + maximum);
            return number;
        }

        public static int Integer(IDictionary<string, object> args, string key, int minimum, int maximum, int fallback)
        {
            double value = Number(args, key, minimum, maximum, fallback);
            if (value != Math.Truncate(value)) throw new ArgumentException("Required integer: " + key);
            return (int)value;
        }

        public static Dictionary<string, object>[] Objects(IDictionary<string, object> args, string key, int maximum)
            => Items(args, key, maximum).Select(item => item as Dictionary<string, object>
                ?? throw new ArgumentException("Expected an object in " + key)).ToArray();

        public static string[] Strings(IDictionary<string, object> args, string key, int maximum)
            => Items(args, key, maximum).Select(item => item is string text && !string.IsNullOrWhiteSpace(text)
                ? text : throw new ArgumentException("Expected nonempty strings in " + key)).Distinct().ToArray();

        private static object[] Items(IDictionary<string, object> args, string key, int maximum)
        {
            if (!args.TryGetValue(key, out var value) || !(value is System.Collections.IEnumerable items) || value is string)
                throw new ArgumentException("Required array: " + key);
            var result = items.Cast<object>().ToArray();
            if (result.Length == 0 || result.Length > maximum) throw new ArgumentException("Invalid item count: " + key);
            return result;
        }

        public static void Fields(IDictionary<string, object> args, params string[] allowed)
        {
            foreach (string key in args.Keys)
                if (key != "op" && !allowed.Contains(key)) throw new ArgumentException("Unknown argument: " + key);
        }
    }
}
