using System;
using System.Collections.Generic;
using System.Linq;
using GTAVMods.Game;

namespace GTAVMods.Agent
{
    public static class StateRequests
    {
        public static StateQuery Parse(IDictionary<string, object> args, string[] available)
        {
            Protocol.Fields(args, "profile", "sections", "radius", "limit", "blipLimit", "types", "lineOfSight", "rays", "probeHeight");
            string profile = args.ContainsKey("profile") ? Protocol.Text(args, "profile") : "minimal";
            string[] sections;
            switch (profile)
            {
                case "minimal": sections = new[] { "player" }; break;
                case "walking": sections = new[] { "player", "navigation", "nearby", "probes" }; break;
                case "driving": sections = new[] { "player", "vehicle", "navigation", "nearby", "probes" }; break;
                case "combat": sections = new[] { "player", "weapons", "nearby", "camera" }; break;
                case "full": sections = available; break;
                default: throw new ArgumentException("Unknown state profile: " + profile);
            }
            if (args.ContainsKey("sections")) sections = Protocol.Strings(args, "sections", 32);
            foreach (string section in sections)
                if (!available.Contains(section)) throw new ArgumentException("Unknown state section: " + section);
            string[] types = args.ContainsKey("types") ? Protocol.Strings(args, "types", 3) : new[] { "ped", "vehicle", "object" };
            foreach (string type in types)
                if (type != "ped" && type != "vehicle" && type != "object") throw new ArgumentException("Unknown entity type: " + type);
            string visibility = args.ContainsKey("lineOfSight") ? Protocol.Text(args, "lineOfSight") : "off";
            if (visibility != "off" && visibility != "on") throw new ArgumentException("lineOfSight must be on or off.");
            var rays = new[] { -90f, -45f, 0f, 45f, 90f }.Select(yaw => new StateRay(yaw, 0, 12)).ToArray();
            if (args.ContainsKey("rays")) rays = Protocol.Objects(args, "rays", 16).Select(ray => {
                Protocol.Fields(ray, "yaw", "pitch", "distance");
                return new StateRay((float)Protocol.Number(ray, "yaw", -180, 180, 0),
                    (float)Protocol.Number(ray, "pitch", -90, 90, 0), (float)Protocol.Number(ray, "distance", .1, 200, 12));
            }).ToArray();
            return new StateQuery(sections, types, (float)Protocol.Number(args, "radius", 1, 1000, 80),
                Protocol.Integer(args, "limit", 1, 128, 24), Protocol.Integer(args, "blipLimit", 1, 256, 32),
                visibility == "on", rays, (float)Protocol.Number(args, "probeHeight", -5, 5, 1));
        }
    }
}
