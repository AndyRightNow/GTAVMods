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
            registry.Register(new CommandDefinition("aim", "Track an observed entity every frame with on-foot or vehicle controls; optional aim-only warm-up before gated fire. Ends when target disappears/dies or duration expires.",
                new { entity = "entity handle", bone = "ped bone ID; default 31086 (head); non-peds use entity origin", height = "world Z offset in metres; -20..20; default 0", ms = "1..30000 total duration including warmup", warmupMs = "0..ms aim-only preparation; default 0", shoot = "boolean; default false", requireAimTarget = "boolean; default false. Withhold Attack unless the engine aim target matches; unknown also withholds. Returns bounded frame observations, not hits." }, args => {
                    Protocol.Fields(args, "entity", "bone", "height", "ms", "warmupMs", "shoot", "requireAimTarget");
                    if (!args.ContainsKey("entity")) throw new ArgumentException("Missing target entity.");
                    bool shoot = false;
                    if (args.TryGetValue("shoot", out var value)) shoot = value is bool flag ? flag : throw new ArgumentException("shoot must be boolean.");
                    bool requireAimTarget = false;
                    if (args.TryGetValue("requireAimTarget", out var gate)) requireAimTarget = gate is bool required ? required : throw new ArgumentException("requireAimTarget must be boolean.");
                    int milliseconds = Protocol.Integer(args, "ms", 1, 30000, 300);
                    return new TrackingAim(world, Protocol.Integer(args, "entity", 1, int.MaxValue, 0), Protocol.Integer(args, "bone", 0, 65535, 31086), shoot, requireAimTarget,
                        new TimedControls(world, control, clock, milliseconds, Array.Empty<(int, int, float)>(), false),
                        Protocol.Integer(args, "warmupMs", 0, milliseconds, 0), (float)Protocol.Number(args, "height", -20, 20, 0));
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
        private readonly bool shoot, requireAimTarget;
        private readonly TimedControls duration;
        private readonly int warmupMs;
        private readonly float height;
        private int? start;
        private AimObservation observation = new AimObservation(true, null, null, false);
        private int frames, lineOfSightFrames, aimTargetMatchFrames, unknownAimTargetFrames, attackRequestedFrames;
        private int inCoverFrames, aimingFromCoverFrames, weaponReadyFrames;
        public TrackingAim(IAgentWorld world, int target, int bone, bool shoot, bool requireAimTarget, TimedControls duration, int warmupMs = 0, float height = 0)
        { this.world = world; this.target = target; this.bone = bone; this.shoot = shoot; this.requireAimTarget = requireAimTarget; this.duration = duration; this.warmupMs = warmupMs; this.height = height; }
        public object Result => new { entity = target, targetAvailable = observation.Available, timing = duration.Result,
            requireAimTarget, warmupMs, height, frames, lineOfSightFrames, aimTargetMatchFrames, unknownAimTargetFrames, attackRequestedFrames,
            inCoverFrames, aimingFromCoverFrames, weaponReadyFrames,
            last = new { targetAvailable = observation.Available, lineOfSight = observation.LineOfSight, aimTarget = observation.AimTarget, attackRequested = observation.AttackRequested,
                inCover = observation.InCover, aimingFromCover = observation.AimingFromCover, weaponReady = observation.WeaponReady } };
        public bool Run()
        {
            if (duration.Run()) return true;
            if (!start.HasValue) start = world.GameTime;
            bool fire = shoot && unchecked((uint)(world.GameTime - start.Value)) >= warmupMs;
            observation = world.AimAt(target, bone, fire, requireAimTarget, height);
            frames++;
            if (observation.LineOfSight == true) lineOfSightFrames++;
            if (observation.AimTarget == target) aimTargetMatchFrames++;
            if (observation.AimTarget == null) unknownAimTargetFrames++;
            if (observation.AttackRequested) attackRequestedFrames++;
            if (observation.InCover == true) inCoverFrames++;
            if (observation.AimingFromCover == true) aimingFromCoverFrames++;
            if (observation.WeaponReady == true) weaponReadyFrames++;
            return !observation.Available;
        }
        public void Cancel() { }
    }
}
