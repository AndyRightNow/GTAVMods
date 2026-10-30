using System;
using System.Collections.Generic;
using System.Linq;

namespace GTAVMods.Agent
{
    /// <summary>One active sequence, no worker-thread game calls, no replay after uncertain execution.</summary>
    public sealed class BatchRunner
    {
        private readonly CommandRegistry registry;
        private readonly AgentJournal journal;
        private readonly Action release;
        private readonly Action pauseAfterFailure;
        private IAgentStep[] steps;
        private string[] names;
        private int index;
        private bool started;
        public BatchRunner(CommandRegistry registry, AgentJournal journal, Action release, Action pauseAfterFailure)
        { this.registry = registry; this.journal = journal; this.release = release; this.pauseAfterFailure = pauseAfterFailure; }
        public string Id { get; private set; }
        public string State { get; private set; } = "idle";
        public string Error { get; private set; }
        public bool Busy => State == "running";
        public object Status() => new { batch = Id, state = State, step = index, total = steps?.Length ?? 0, error = Error };

        public object Start(Dictionary<string, object>[] definitions)
        {
            if (Busy) throw new InvalidOperationException("A batch is running. Inspect or cancel it first.");
            // Validate EVERY operation before any player action occurs.
            var prepared = definitions.Select(registry.Prepare).ToArray();
            string id = Guid.NewGuid().ToString("N");
            journal.Save("batch-" + id, new { id, session = journal.Session, steps = definitions });
            journal.Record("batch.accepted", new { id, steps = definitions.Length });
            Id = id; steps = prepared; names = definitions.Select(d => Protocol.Text(d, "op")).ToArray();
            index = 0; started = false; Error = null; State = "running";
            Persist();
            return Status();
        }

        public void Tick()
        {
            if (!Busy) return;
            try
            {
                if (!started)
                {
                    journal.Record("step.started", new { batch = Id, step = index, op = names[index] });
                    started = true;
                }
                if (!steps[index].Run()) return;
                string resultPath = journal.Save("result-" + Id + "-" + index, new {
                    batch = Id, step = index, op = names[index], result = steps[index].Result, session = journal.Session
                });
                journal.Record("step.completed", new { batch = Id, step = index, resultPath });
                index++; started = false;
                if (index == steps.Length) State = "completed";
                Persist();
            }
            catch (Exception error)
            {
                State = "failed"; Error = error.ToString();
                StopCurrent(pauseAfterFailure);
                Persist();
            }
        }

        public void Cancel(string reason)
        {
            if (Busy)
            {
                State = "cancelled"; Error = reason;
                StopCurrent(release);
                Persist();
            }
            else release();
        }

        private void StopCurrent(Action finish)
        {
            try { if (index < steps.Length) steps[index].Cancel(); }
            finally { finish(); }
        }
        private void Persist()
        {
            // Publish each batch independently: acceptance may arrive before its
            // background writes, and a previous batch's status must never be mistaken for it.
            var status = Status();
            journal.Save("batch-" + Id + "-status", status);
            journal.Save("batch-status", status);
        }
    }
}
