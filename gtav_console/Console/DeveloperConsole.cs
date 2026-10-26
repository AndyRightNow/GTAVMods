using System;
using System.Collections.Generic;
using GTAVMods.Game;
using System.Drawing;
using System.Windows.Forms;
using GTA;
using Control = GTA.Control;
using Font = GTA.UI.Font;

public static class ScriptExtender {
    [GamePrimitive]
    public static void RegisterConsoleScript(this Script script, DeveloperConsole.DeveloperConsole.OnConsoleAttached callback) {
        new DeveloperConsole.ScriptRegistration(script, callback);
    }
}

namespace DeveloperConsole {
    internal sealed class ScriptRegistration {
        private readonly Script _owner;
        [GamePrimitive]
        internal ScriptRegistration(Script owner, DeveloperConsole.OnConsoleAttached callback) {
            _owner = owner;
            DeveloperConsole.RegisterConsoleScript(owner, callback);
            owner.Aborted += OnAborted;
        }
        [GamePrimitive]
        private void OnAborted(object sender, EventArgs args) {
            DeveloperConsole.UnregisterConsoleScript(_owner);
        }
    }

    /// <summary>GTA lifecycle wiring; console behavior is in DeveloperConsole.</summary>
    public sealed class DeveloperConsoleScript : Script {
        private readonly DeveloperConsole _console;
        [GamePrimitive]
        public DeveloperConsoleScript() {
            var game = GtaRuntime.Create(new GtaHost(), new GtaInput(),
                new GtaEntities(new GtaEntityLifetimeBindings()), new GtaPeds(), new GtaWeapons(),
                new GtaWorld(new GtaWorldQueries()), new GtaCameras(new GtaCameraLifetimeBindings()),
                new GtaRopes(new GtaRopeLifetimeBindings()), new GtaNative(), new GtaArgumentMarshaller(), new GtaOutputs());
            var platform = new LiveConsolePlatform(new NativeMethods(new WindowsKeyboardNative()), new WindowsClipboardNative());
            _console = new DeveloperConsole(platform, new LiveCommandEnvironment(game), new GTAFuncs(game, platform));
            Tick += OnTick;
            KeyDown += OnKeyDown;
            KeyUp += OnKeyUp;
            Aborted += OnAborted;
        }
        [GamePrimitive] private void OnTick(object sender, EventArgs args) { _console.Tick(); }
        [GamePrimitive] private void OnKeyDown(object sender, KeyEventArgs args) { _console.KeyDown(args); }
        [GamePrimitive] private void OnKeyUp(object sender, KeyEventArgs args) { _console.KeyUp(args); }
        [GamePrimitive] private void OnAborted(object sender, EventArgs args) { _console.Dispose(); }
    }

    public sealed class DeveloperConsole : IDeveloperConsole, IDisposable {
        public delegate void OnConsoleAttached(DeveloperConsole developerConsole);
        private readonly IConsolePlatform _platform;
        private readonly ICommandEnvironment _environment;
        private readonly GTAFuncs _game;
        private readonly List<string> _inputHistory = new List<string>();
        private readonly HashSet<Keys> _keyWasDown = new HashSet<Keys>();
        private readonly List<KeyValuePair<string, Color>> _lines = new List<KeyValuePair<string, Color>>();
        private List<Control> _disabledControls;
        private string _cursorChar = "";
        private int _historyCursor = -1;
        private int _inputOffset;
        private bool _isHidden = true;
        private int _lastBlinkTime;
        private int _lineOffset;
        private string _input = "";
        public bool Debug = ConsoleSettings.IsDevBuild;
        public string Input {
            get { return _input; }
            private set {
                _input = value;
                _inputOffset = Math.Min(_inputOffset, _input.Length);
            }
        }

        public DeveloperConsole(IConsolePlatform platform, ICommandEnvironment environment, GTAFuncs game) {
            _platform = platform;
            _environment = environment;
            _game = game;
            Instance = this;
            CommandDispatcher = new CommandDispatcher();
            DefaultCommands.OnConsoleAttached(this);
            PrintLine("Developer console: F8 opens/closes. Run 'help' for commands. F4 opens the SHVDN console.");
            foreach (var script in RegisteredScripts.ToArray()) script.Value(this);
        }

        public CommandDispatcher CommandDispatcher { get; }
        /// <summary>Game helpers available to expressions entered in this console.</summary>
        public GTAFuncs GameFunctions => _game;
        public static DeveloperConsole Instance { get; private set; }
        // Console-created geometry shares the current console's rendering context.
        internal static GTAFuncs GeometryFunctions => Instance._game;
        public bool IsOpen { get { return !_isHidden; } }
        public static List<KeyValuePair<object, OnConsoleAttached>> RegisteredScripts { get; } =
            new List<KeyValuePair<object, OnConsoleAttached>>();

        public static void RegisterConsoleScript(object owner, OnConsoleAttached callback) {
            RegisteredScripts.Add(new KeyValuePair<object, OnConsoleAttached>(owner, callback));
            if (Instance != null) callback(Instance);
        }
        public static void UnregisterConsoleScript(object owner) {
            RegisteredScripts.RemoveAll(entry => ReferenceEquals(entry.Key, owner));
        }
        public void Dispose() {
            var failures = new List<Exception>();
            try { ShowConsole(false); }
            catch (Exception error) { failures.Add(error); }
            try { _game.ReleasePendingModel(); }
            catch (Exception error) { failures.Add(error); }
            GTAFuncs.ThrowFailures(failures);
            if (Instance == this) {
                Instance = null;
                RegisteredScripts.Clear();
            }
        }

        public void KeyUp(KeyEventArgs args) { _keyWasDown.Remove(args.KeyCode); }
        public void KeyDown(KeyEventArgs args) {
            if (Array.IndexOf(ConsoleSettings.HideKeys, args.KeyCode) >= 0) {
                ShowConsole(_isHidden);
                args.SuppressKeyPress = true;
                return;
            }
            if (args.KeyCode == Keys.Escape) {
                ShowConsole(false);
                args.SuppressKeyPress = true;
                return;
            }
            if (_isHidden) return;
            switch (args.KeyCode) {
                case ConsoleSettings.ScrollUpKey:
                    if (_lines.Count - ConsoleSettings.NumLines + _lineOffset > 0) _lineOffset--;
                    break;
                case ConsoleSettings.ScrollDownKey:
                    if (_lineOffset < 0) _lineOffset++;
                    break;
                case ConsoleSettings.HistoryLastKey:
                    _inputOffset = 0;
                    if (_historyCursor < _inputHistory.Count - 1) _historyCursor++;
                    if (_inputHistory.Count > 0) Input = _inputHistory[_historyCursor];
                    break;
                case ConsoleSettings.HistoryNextKey:
                    _inputOffset = 0;
                    if (_historyCursor > 0) Input = _inputHistory[--_historyCursor];
                    else { _historyCursor = -1; Input = ""; }
                    break;
                case Keys.C:
                    if (args.Control) { _platform.WriteClipboard(Input); break; }
                    goto default;
                case Keys.V:
                    if (args.Control) {
                        Input = Input.Insert(Input.Length - _inputOffset, _platform.ReadClipboard());
                        break;
                    }
                    goto default;
                case ConsoleSettings.CursorLeftKey:
                    if (_inputOffset < Input.Length) _inputOffset++;
                    break;
                case ConsoleSettings.CursorRightKey:
                    if (_inputOffset > 0) _inputOffset--;
                    break;
                case ConsoleSettings.DelKey:
                    if (_inputOffset > 0) {
                        var position = Input.Length - _inputOffset;
                        _inputOffset--;
                        Input = Input.Remove(position, 1);
                    }
                    break;
                case ConsoleSettings.BackSpaceKey:
                    if (Input.Length - _inputOffset > 0) Input = Input.Remove(Input.Length - _inputOffset - 1, 1);
                    break;
                case Keys.Enter: RunCommand(); break;
                default:
                    _historyCursor = -1;
                    var text = _platform.TranslateKey(args.KeyCode, args.Shift, args.Alt);
                    if (!string.IsNullOrEmpty(text) && _keyWasDown.Add(args.KeyCode)) {
                        var character = text[0];
                        var caps = _platform.CapsLockState;
                        if ((caps & 0x8000) != 0 || (caps & 1) != 0 && char.IsLetter(character))
                            character = char.IsUpper(character) ? char.ToLower(character) : char.ToUpper(character);
                        if (_platform.IsForeground && !char.IsControl(character))
                            Input = Input.Insert(Input.Length - _inputOffset, character.ToString());
                    }
                    break;
            }
            args.SuppressKeyPress = true;
        }

        public void Tick() {
            if (_isHidden) return;
            SetConsoleControls();
            DrawConsole();
        }

        private void DrawConsole() {
            var width = (int)_platform.Width;
            var thirdHeight = (int)_platform.Height / 3;
            var offset = thirdHeight - 20;
            var height = offset / ConsoleSettings.NumLines;
            _platform.DrawRectangle(Point.Empty, new Size(width, thirdHeight), ConsoleSettings.DefaultBgColor);
            var start = Math.Max(0, _lines.Count - ConsoleSettings.NumLines + _lineOffset);
            var amount = Math.Min(ConsoleSettings.NumLines, _lines.Count - start);
            for (var index = 0; index < amount; index++) {
                var line = _lines[start + index];
                DrawLargeString(line.Key, new Point(5, ConsoleSettings.NumLines * height - height * (amount - index) + 5),
                    ConsoleSettings.FontSize, line.Value, 0);
            }
            const int scrollbarWidth = 8;
            const int scrollbarPadding = 2;
            if (_lines.Count > ConsoleSettings.NumLines) {
                var maxHeight = offset - 6;
                var scrollHeight = Convert.ToInt32((double)ConsoleSettings.NumLines / _lines.Count * maxHeight);
                var yOffset = Convert.ToInt32((double)(_lines.Count + _lineOffset) / _lines.Count * maxHeight - scrollHeight);
                yOffset = Math.Min(yOffset, maxHeight - scrollHeight);
                _platform.DrawRectangle(new Point(width - scrollbarPadding - scrollbarWidth, yOffset + scrollbarPadding / 2),
                    new Size(scrollbarWidth, scrollHeight), Color.FromArgb(100, 118, 91, 227));
            }
            _platform.DrawText(ConsoleSettings.Version, new Point(width - 20 - scrollbarWidth - scrollbarPadding,
                ConsoleSettings.NumLines * height - 12), ConsoleSettings.FontSize, ConsoleSettings.DefaultTextColor, 0);
            _platform.DrawRectangle(new Point(0, thirdHeight - 20), new Size(width, 1),
                Color.FromArgb(ConsoleSettings.Alpha, 255, 255, 255));
            if (_platform.GameTime - _lastBlinkTime > 600) {
                _cursorChar = _cursorChar == "" ? ConsoleSettings.CursorCharacter : "";
                _lastBlinkTime = _platform.GameTime;
            }
            DrawLargeString(ConsoleSettings.PreString + Input.Insert(Input.Length - _inputOffset, _cursorChar),
                new Point(5, offset + 3), ConsoleSettings.FontSize,
                Color.FromArgb(ConsoleSettings.TextAlpha, 255, 255, 255), 0);
        }

        private void DrawLargeString(string text, Point position, float scale, Color color, Font font) {
            var x = 0f;
            for (var index = 0; index < text.Length; index += 99) {
                var chunk = text.Substring(index, Math.Min(99, text.Length - index));
                _platform.DrawText(chunk, new Point(position.X + Convert.ToInt32(x), position.Y), scale, color, font);
                x += _platform.Width * _game.GetTextWidth(chunk, font, scale);
            }
        }

        public void PrintCommandInfo(CommandDispatcher.Command command) {
            PrintLine("Command: " + command.Name);
            PrintLine("Description: " + command.Description);
            foreach (var argumentSet in command.ExpectedArgs) {
                var line = command.Name + " ";
                foreach (var argument in argumentSet) line += "<" + argument.Type.ToString() + " " + argument.Name + "> ";
                PrintLine(line);
                foreach (var argument in argumentSet)
                    if (!string.IsNullOrWhiteSpace(argument.Description))
                        PrintLine(argument.Name + ": " + argument.Description);
            }
        }
        public void PrintCommandInfo(string command) {
            if (CommandDispatcher.Commands.ContainsKey(command)) PrintCommandInfo(CommandDispatcher.Commands[command]);
            else PrintError("Failed to print info for command '" + command + "', command does not exist.");
        }
        public void PrintWarning(string text) { PrintLine("[Warning] " + text, Color.FromArgb(ConsoleSettings.TextAlpha, Color.Yellow)); }
        public void PrintError(string text) { PrintLine("[Error] " + text, Color.FromArgb(ConsoleSettings.TextAlpha, Color.Red)); }
        public void PrintDebug(string text) {
            if (Debug) PrintLine("[Debug] " + text, Color.FromArgb(ConsoleSettings.TextAlpha, Color.LightGray));
        }
        public void PrintLine(string text, Color color) {
            _lines.Add(new KeyValuePair<string, Color>(_platform.Now.ToString("HH:mm:ss") + ": " + text, color));
        }
        public void PrintLine(string text) { PrintLine(text, ConsoleSettings.DefaultTextColor); }
        public void ClearLines() { _lines.Clear(); _lineOffset = 0; }
        public void RemoveLastLine() {
            if (_lines.Count == 0) return;
            _lines.RemoveAt(_lines.Count - 1);
            _lineOffset = Math.Max(_lineOffset, Math.Min(0, ConsoleSettings.NumLines - _lines.Count));
        }

        public void RunCommand() {
            var commandText = Input;
            Input = "";
            _inputOffset = 0;
            _inputHistory.Insert(0, commandText);
            PrintLine(ConsoleSettings.PreString + commandText);
            var parsed = new CommandParser(commandText, this, _environment);
            if (parsed.Tokens.Count == 0 || parsed.Tokens[0].Kind != CommandTokenKind.Word) {
                PrintError("Command '' not found!");
                return;
            }
            var name = parsed.Tokens[0].String;
            CommandDispatcher.Command command;
            if (!CommandDispatcher.Commands.TryGetValue(name, out command)) {
                PrintError("Command '" + name + "' not found!");
                return;
            }
            var tokens = parsed.Tokens.GetRange(1, parsed.Tokens.Count - 1);
            var argumentSet = command.AreTokensValid(tokens);
            if (argumentSet < 0) {
                PrintError("Provided arguments are not valid for command '" + name + "'");
                PrintCommandInfo(command);
            } else command.Callback(new CommandDispatcher.CommandEventArgs(name, tokens, argumentSet));
        }

        private void ShowConsole(bool show) {
            _lineOffset = 0;
            _historyCursor = -1;
            if (show) {
                RestoreOwnedControls();
                _disabledControls = _game.CaptureEnabledControls();
                try {
                    SetConsoleControls();
                    _isHidden = false;
                } catch (Exception error) {
                    try { RestoreOwnedControls(); }
                    catch (Exception restoreError) {
                        throw new AggregateException("Console operations failed.", error, restoreError);
                    }
                    throw;
                }
            } else {
                _isHidden = true;
                RestoreOwnedControls();
                _keyWasDown.Clear();
            }
        }
        private void RestoreOwnedControls() {
            if (_disabledControls == null) {
                _game.RestorePendingControls();
                return;
            }
            var controls = _disabledControls;
            // GTAFuncs retains each failed restoration. Successful restores no longer belong
            // to this console and must not overwrite later changes by other scripts.
            _disabledControls = null;
            _game.RestoreControls(controls);
        }
        private void SetConsoleControls() {
            _game.DisableControls();
            foreach (var control in new[] {
                Control.MoveLeftRight, Control.MoveUpDown, Control.VehicleAccelerate, Control.VehicleBrake,
                Control.VehicleDriveLook, Control.VehicleDriveLook2, Control.VehicleMoveLeftRight,
                Control.VehicleMoveUpDown, Control.LookLeftRight, Control.LookUpDown, Control.FlyUpDown,
                Control.FlyLeftRight, Control.VehicleFlyRollLeftRight, Control.VehicleFlyPitchUpDown,
                Control.VehicleFlyYawLeft, Control.VehicleFlyYawRight, Control.VehicleFlyThrottleDown,
                Control.VehicleFlyThrottleUp }) _game.EnableControlAction(control, true);
        }
    }
}
