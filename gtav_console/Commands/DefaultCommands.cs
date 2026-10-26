using GTAVMods.Game;
using GTA;
using System;
using System.Globalization;

namespace DeveloperConsole {
    public sealed class DefaultCommands : Script {
        [GamePrimitive]
        public DefaultCommands() { this.RegisterConsoleScript(new DeveloperConsole.OnConsoleAttached(OnConsoleAttached)); }
        internal static void OnConsoleAttached(DeveloperConsole console) {
            var help = new CommandDispatcher.Command("help", "List commands or explain one command.", args => {
                if (args.ArgIndex == 0) {
                    foreach (var command in console.CommandDispatcher.Commands.Values)
                        console.PrintLine(command.Name + ": " + command.Description);
                } else console.PrintCommandInfo((string)args.Tokens[0].Eval);
            });
            help.AddArgumentSet();
            help.AddArgumentSet(new CommandDispatcher.CommandArgument("command", "Command to explain.", typeof(string)));
            console.CommandDispatcher.RegisterCommand(help, true);
            console.CommandDispatcher.RegisterCommand(
                new CommandDispatcher.Command("clear", "Clear the console output.", args => console.ClearLines()), true);
            var evaluate = new CommandDispatcher.Command("eval",
                "Evaluate C# or display a value. C# can use CONSOLE, FUNCTIONS, PLAYER, PED, POS and MP_ID.",
                args => {
                    var value = args.Tokens[0].Eval;
                    console.PrintLine(value == null ? "null" : Convert.ToString(value, CultureInfo.InvariantCulture));
                });
            evaluate.AddArgumentSet(new CommandDispatcher.CommandArgument("value", "A value or { C# statements }.", typeof(object)));
            console.CommandDispatcher.RegisterCommand(evaluate, true);
            console.PrintDebug("DefaultCommands loaded successfully.");
        }
    }
}
