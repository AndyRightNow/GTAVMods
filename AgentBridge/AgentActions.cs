using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using GTA.Native;
using GTAVMods.Game;

namespace GTAVMods.Agent
{
    public static class AgentActions
    {
        public static void Register(CommandRegistry registry, IAgentWorld world, WorldControl control, Func<long> clock)
        {
            registry.Register(new CommandDefinition("native", "Invoke a GTA native on the game thread. Task submission is not completion; inspect subsequent state.",
                new { name = "Hash enum name or 0x hexadecimal hash", returns = "void/bool/int/uint/float/string/vector3", args = "0..32 scalars; typed numeric objects {type:float|int|uint|long|ulong,value:number} are supported" }, args => {
                    Protocol.Fields(args, "name", "returns", "args");
                    Hash hash = NativeHash(Protocol.Text(args, "name"));
                    string result = args.ContainsKey("returns") ? Protocol.Text(args, "returns") : "void";
                    if (!new[] { "void", "bool", "int", "uint", "float", "string", "vector3" }.Contains(result)) throw new ArgumentException("Unsupported native return type.");
                    if (!args.TryGetValue("args", out var values) || !(values is IList array) || array.Count > 32) throw new ArgumentException("Expected 0..32 native arguments.");
                    object[] parameters = array.Cast<object>().Select(NativeArgument).ToArray();
                    return new ImmediateStep(() => world.Invoke(hash, result, parameters));
                }));
            registry.Register(new CommandDefinition("aim", "Track an observed ped bone every frame, optionally firing only with clear line of sight. Ends when the target disappears/dies or duration expires.",
                new { entity = "ped handle", bone = "bone ID; default 31086 (head)", ms = "1..30000", shoot = "boolean; default false" }, args => {
                    Protocol.Fields(args, "entity", "bone", "ms", "shoot");
                    if (!args.ContainsKey("entity")) throw new ArgumentException("Missing target entity.");
                    bool shoot = false;
                    if (args.TryGetValue("shoot", out var value)) shoot = value is bool flag ? flag : throw new ArgumentException("shoot must be boolean.");
                    return new TrackingAim(world, Protocol.Integer(args, "entity", 1, int.MaxValue, 0), Protocol.Integer(args, "bone", 0, 65535, 31086), shoot,
                        new TimedControls(world, control, clock, Protocol.Integer(args, "ms", 1, 30000, 300), Array.Empty<(int, int, float)>(), false));
                }));
        }
        public static Hash NativeHash(string name)
        {
            if (name.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) return (Hash)ulong.Parse(name.Substring(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            if (!Enum.TryParse(name, true, out Hash hash) || !Enum.IsDefined(typeof(Hash), hash)) throw new ArgumentException("Unknown native hash: " + name);
            return hash;
        }
        public static object NativeArgument(object value)
        {
            if (value is string || value is bool || value is int || value is long) return value;
            if (value is decimal || value is double) return Convert.ToSingle(value, CultureInfo.InvariantCulture);
            if (!(value is Dictionary<string, object> typed)) throw new ArgumentException("Native arguments must be scalars or typed numeric objects.");
            Protocol.Fields(typed, "type", "value");
            string type = Protocol.Text(typed, "type");
            if (!typed.TryGetValue("value", out var number) || !(number is int || number is long || number is decimal || number is double)) throw new ArgumentException("Typed native argument needs a number.");
            decimal numeric = Convert.ToDecimal(number, CultureInfo.InvariantCulture);
            if (type != "float" && numeric != decimal.Truncate(numeric)) throw new ArgumentException("Integer native argument cannot contain a fraction.");
            switch (type)
            {
                case "float": return (float)numeric;
                case "int": return checked((int)numeric);
                case "uint": return checked((uint)numeric);
                case "long": return checked((long)numeric);
                case "ulong": return checked((ulong)numeric);
                default: throw new ArgumentException("Unknown native argument type: " + type);
            }
        }
    }
    public sealed class TrackingAim : IAgentStep
    {
        private readonly IAgentWorld world;
        private readonly int target, bone;
        private readonly bool shoot;
        private readonly TimedControls duration;
        private bool available = true;
        public TrackingAim(IAgentWorld world, int target, int bone, bool shoot, TimedControls duration)
        { this.world = world; this.target = target; this.bone = bone; this.shoot = shoot; this.duration = duration; }
        public object Result => new { entity = target, targetAvailable = available, timing = duration.Result };
        public bool Run()
        {
            if (duration.Run()) return true;
            available = world.AimAt(target, bone, shoot);
            return !available;
        }
        public void Cancel() { }
    }
}
