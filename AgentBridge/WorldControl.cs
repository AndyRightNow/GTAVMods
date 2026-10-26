using System;
using System.Collections.Generic;
using System.Linq;
using GTAVMods.Game;

namespace GTAVMods.Agent
{
    public sealed class WorldControl
    {
        private readonly IAgentWorld world;
        public WorldControl(IAgentWorld world) { this.world = world; }
        public bool Paused { get; private set; }
        public void Pause(bool value) { world.Pause(value); Paused = value; }
        public void Release() { if (Paused) Pause(false); }
    }

    public sealed class TimedControls : IAgentStep
    {
        private readonly IAgentWorld world;
        private readonly WorldControl control;
        private readonly Func<long> clock;
        private readonly int milliseconds;
        private readonly bool wallClock;
        private readonly (int Group, int Control, float Value)[] inputs;
        private bool started;
        private int startGameTime;
        private long startWallTime;
        public TimedControls(IAgentWorld world, WorldControl control, Func<long> clock, int milliseconds,
            (int Group, int Control, float Value)[] inputs, bool wallClock)
        { this.world = world; this.control = control; this.clock = clock; this.milliseconds = milliseconds; this.inputs = inputs; this.wallClock = wallClock; }
        public object Result { get; private set; }
        public bool Run()
        {
            if (!started)
            {
                if (control.Paused && !wallClock) throw new InvalidOperationException("Resume before advancing simulation time.");
                startGameTime = world.GameTime; startWallTime = clock(); started = true;
            }
            uint elapsed = unchecked((uint)(world.GameTime - startGameTime));
            long wall = clock() - startWallTime;
            if ((wallClock ? wall : elapsed) >= milliseconds)
            {
                Result = new { requestedMs = milliseconds, elapsedGameMs = elapsed, elapsedWallMs = wall };
                return true;
            }
            if (clock() - startWallTime > milliseconds + 30000L) throw new TimeoutException("Simulation did not advance; input released.");
            foreach (var input in inputs) world.SetControl(input.Group, input.Control, input.Value);
            return false;
        }
        // SET_CONTROL_VALUE_NEXT_FRAME applies for one frame; ceasing submission releases only our input.
        public void Cancel() { }
    }
}
