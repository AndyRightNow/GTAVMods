using System;
using System.Collections.Generic;

namespace GTAVMods.Agent
{
    /// <summary>A wall-clock dead-man switch for an external Python process.</summary>
    public sealed class AgentLease
    {
        private readonly Func<long> clock;
        private readonly Action stop;
        private string owner;
        private long deadline;
        private bool expired;
        public AgentLease(Func<long> clock, Action stop) { this.clock = clock; this.stop = stop; }
        public object Status() => new { active = owner != null, expired, deadlineWallMs = deadline };
        public void Tick()
        {
            if (owner == null || expired || clock() < deadline) return;
            expired = true;
            stop();
        }
        public object Update(IDictionary<string, object> args)
        {
            Protocol.Fields(args, "action", "token", "ms");
            string action = Protocol.Text(args, "action"), token = Protocol.Text(args, "token");
            if (!Guid.TryParseExact(token, "N", out var _)) throw new ArgumentException("Lease token must be a 32-character GUID.");
            int duration = Protocol.Integer(args, "ms", 1000, 30000, 5000);
            Tick();
            switch (action)
            {
                case "acquire":
                    if (owner != null && !expired) throw new InvalidOperationException("An agent already owns the lease.");
                    owner = token; expired = false; deadline = clock() + duration; break;
                case "heartbeat":
                    Authorize(token);
                    deadline = clock() + duration; break;
                case "release":
                    // Expired owners may release their lease, but may never resume it.
                    if (owner != token) throw new InvalidOperationException("Lease token does not match the owner.");
                    stop(); owner = null; expired = false; break;
                default: throw new ArgumentException("Lease action must be acquire, heartbeat or release.");
            }
            return Status();
        }
        public void Authorize(string token)
        {
            Tick();
            if (expired || owner != token) throw new InvalidOperationException("Missing, stale or expired agent lease. Inspect before acquiring a new lease.");
        }
    }
}
