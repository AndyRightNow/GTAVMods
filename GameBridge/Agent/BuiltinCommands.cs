using System;
using System.Collections.Generic;
using System.Linq;
using GTA;
using GTAVMods.Game;

namespace GTAVMods.Agent
{
    public static class BuiltinCommands
    {
        public static void Register(CommandRegistry registry, IAgentWorld world, WorldControl control, Func<long> clock,
            CSharpCommands csharp, Func<string> capture)
        {
            AgentActions.Register(registry, world, control, clock);
            registry.Register(new CommandDefinition("state", "Structured world state with selective sections and bounded optional scans/probes. Minimal profile avoids all scans.",
                new { profiles = new[] { "minimal", "walking", "driving", "combat", "full" }, sections = world.StateSections,
                    radius = "1..1000 metres", limit = "1..128 nearby nodes", blipLimit = "1..256", types = "array: ped, vehicle, object",
                    lineOfSight = "on/off (default off)", rays = "optional 1..16 objects: yaw -180..180, pitch -90..90, distance .1..200", probeHeight = "-5..5 metres (default 1)" }, args => {
                var query = StateRequests.Parse(args, world.StateSections);
                return new ImmediateStep(() => world.State(query));
            }));
            registry.Register(new CommandDefinition("pause", "Pause simulation while the agent inspects state.", new { }, args => {
                Protocol.Fields(args); return new ImmediateStep(() => { control.Pause(true); return new { paused = true }; });
            }));
            registry.Register(new CommandDefinition("resume", "Resume simulation.", new { }, args => {
                Protocol.Fields(args); return new ImmediateStep(() => { control.Pause(false); return new { paused = false }; });
            }));
            registry.Register(new CommandDefinition("wait", "Advance game time without injecting input. Resume first. Durations are frame-granular.", new { ms = "integer 1..30000", clock = "game (default) or wall" }, args => {
                Protocol.Fields(args, "ms", "clock");
                return new TimedControls(world, control, clock, Protocol.Integer(args, "ms", 1, 30000, 500), Array.Empty<(int, int, float)>(), UseWallClock(args));
            }));
            registry.Register(new CommandDefinition("controls", "Apply simultaneous analog or digital GTA controls every frame for a duration; release automatically.",
                new { ms = "integer 1..30000", clock = "game (default) or wall; use wall for menu controls", inputs = "1..32 objects: control (GTA.Control name or integer), group (0..2, default 0), value (-1..1, default 1)" }, args => {
                Protocol.Fields(args, "ms", "inputs", "clock");
                var inputs = Protocol.Objects(args, "inputs", 32).Select(ParseControl).ToArray();
                return new TimedControls(world, control, clock, Protocol.Integer(args, "ms", 1, 30000, 500), inputs, UseWallClock(args));
            }));
            registry.Register(new CommandDefinition("controls.update", "Replace the complete continuous input set and return immediately. Inputs continue during observations; renew before the wall-time deadline or simulation pauses. Resume first. Timed controls/wait/aim, pause, cancel, lease loss and reload clear the set.",
                new { ttlMs = "integer 50..5000, default 1000; independent of lease heartbeat", inputs = "1..32 control/group/value objects, same format as controls" }, args => {
                Protocol.Fields(args, "ttlMs", "inputs");
                var inputs = Protocol.Objects(args, "inputs", 32).Select(ParseControl).ToArray();
                int ttl = Protocol.Integer(args, "ttlMs", 50, 5000, 1000);
                return new ImmediateStep(() => control.UpdateInputs(inputs, ttl));
            }));
            registry.Register(new CommandDefinition("controls.release", "Release continuous inputs without changing simulation pause or native tasks.", new { }, args => {
                Protocol.Fields(args);
                return new ImmediateStep(() => { control.ClearInputs(); return control.InputStatus(); });
            }));
            registry.Register(new CommandDefinition("observe", "Capture player, world, nearby entities, camera, mission/cutscene flags and blips as JSON.",
                new { radius = "metres 1..1000, default 150", limit = "integer 1..256, default 64" }, args => {
                Protocol.Fields(args, "radius", "limit");
                float radius = (float)Protocol.Number(args, "radius", 1, 1000, 150);
                int limit = Protocol.Integer(args, "limit", 1, 256, 64);
                return new ImmediateStep(() => world.Observe(radius, limit));
            }));
            registry.Register(new CommandDefinition("capture", "Save a PNG of the foreground GTA client area. Requires the game window to be visible.", new { }, args => {
                Protocol.Fields(args); return new ImmediateStep(() => new { screenshot = capture() });
            }));
            registry.Register(new CommandDefinition("command", "Execute trusted C# statements on the script thread. Return a value; use CONTEXT.Print(value) for file output. P/V/PLAYER/PED/POS aliases and GTA natives are available.",
                new { code = "C# statements, e.g. return P.Position;" }, args => {
                Protocol.Fields(args, "code"); return new ImmediateStep(csharp.Compile(Protocol.Text(args, "code")));
            }));
        }

        private static bool UseWallClock(IDictionary<string, object> args)
        {
            if (!args.ContainsKey("clock")) return false;
            string clock = Protocol.Text(args, "clock");
            if (clock != "wall" && clock != "game") throw new ArgumentException("clock must be game or wall.");
            return clock == "wall";
        }

        private static (int, int, float) ParseControl(Dictionary<string, object> input)
        {
            Protocol.Fields(input, "control", "group", "value");
            int id;
            if (input.TryGetValue("control", out var value) && value is string name)
            {
                if (!Enum.TryParse(name, true, out Control parsed) || !Enum.IsDefined(typeof(Control), parsed))
                    throw new ArgumentException("Unknown GTA.Control: " + name);
                id = (int)parsed;
            }
            else
            {
                if (!input.ContainsKey("control")) throw new ArgumentException("Missing control.");
                id = Protocol.Integer(input, "control", 0, 1023, 0);
            }
            return (Protocol.Integer(input, "group", 0, 2, 0), id, (float)Protocol.Number(input, "value", -1, 1, 1));
        }
    }
}
