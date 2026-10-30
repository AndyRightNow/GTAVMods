using System;
using System.Collections.Generic;
using System.Linq;
using GTAVMods.Game;

namespace GTAVMods.Agent
{
    public sealed class WorldControl
    {
        private readonly IAgentWorld world;
        private readonly Func<long> clock;
        private (int Group, int Control, float Value)[] inputs;
        private long inputDeadline;
        public WorldControl(IAgentWorld world, Func<long> clock) { this.world = world; this.clock = clock; }
        public bool Paused { get; private set; }
        public object InputStatus() => new { active = inputs != null, count = inputs?.Length ?? 0, deadlineWallMs = inputDeadline };
        public object UpdateInputs((int Group, int Control, float Value)[] values, int milliseconds)
        {
            if (Paused) throw new InvalidOperationException("Resume before updating continuous controls.");
            inputs = values; inputDeadline = clock() + milliseconds;
            return InputStatus();
        }
        public void ClearInputs() { inputs = null; inputDeadline = 0; }
        public void Tick()
        {
            if (inputs == null) return;
            if (clock() >= inputDeadline) { Pause(true); return; }
            foreach (var input in inputs) world.SetControl(input.Group, input.Control, input.Value);
        }
        public void Pause(bool value) { ClearInputs(); world.Pause(value); Paused = value; }
        public void Release() { ClearInputs(); if (Paused) Pause(false); }
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
                control.ClearInputs();
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
