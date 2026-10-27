using System;
using System.Collections.Generic;
using System.Linq;

namespace GTAVMods.Agent
{
    /// <summary>Preparation validates input without changing the world. Run advances one frame at a time.</summary>
    public interface IAgentStep
    {
        bool Run();
        object Result { get; }
        void Cancel();
    }

    public sealed class ImmediateStep : IAgentStep
    {
        private readonly Func<object> action;
        public ImmediateStep(Func<object> action) { this.action = action; }
        public object Result { get; private set; }
        public bool Run() { Result = action(); return true; }
        public void Cancel() { }
    }

    public sealed class CommandDefinition
    {
        public CommandDefinition(string name, string description, object arguments, Func<IDictionary<string, object>, IAgentStep> prepare)
        { Name = name; Description = description; Arguments = arguments; Prepare = prepare; }
        public string Name { get; }
        public string Description { get; }
        public object Arguments { get; }
        internal Func<IDictionary<string, object>, IAgentStep> Prepare { get; }
    }

    /// <summary>Mods may register namespaced operations before or between batches. No scheduler changes are required.</summary>
    public sealed class CommandRegistry
    {
        private readonly Dictionary<string, CommandDefinition> commands = new Dictionary<string, CommandDefinition>(StringComparer.Ordinal);
        public void Register(CommandDefinition definition) => commands.Add(definition.Name, definition);
        public object Describe() => commands.Values.OrderBy(c => c.Name).Select(c => new { op = c.Name, description = c.Description, arguments = c.Arguments }).ToArray();
        public IAgentStep Prepare(IDictionary<string, object> arguments)
        {
            string name = Protocol.Text(arguments, "op");
            if (!commands.TryGetValue(name, out var command)) throw new ArgumentException("Unknown operation: " + name);
            return command.Prepare(arguments);
        }
    }
}
