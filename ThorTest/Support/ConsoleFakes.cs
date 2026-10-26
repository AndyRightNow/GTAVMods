using System;
using System.Collections.Generic;
using System.Drawing;
using System.Reflection;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using DeveloperConsole;
using GTA;
using GTA.Math;
using GTA.Native;
using GTAVMods.Game;
using Font = GTA.UI.Font;
using Control = GTA.Control;
using ConsoleModel = DeveloperConsole.DeveloperConsole;

namespace GTAVMods.Tests.Support {
    internal sealed class ConsoleNative : INativeInvoker, IRawNative, IArgumentMarshaller {
        internal readonly List<(Hash Hash, object[] Arguments)> Calls = new List<(Hash, object[])>();
        internal readonly Dictionary<Hash, Func<object[], object>> Queries = new Dictionary<Hash, Func<object[], object>>();
        internal Action<Hash, object[]> Before;
        internal Action<Hash, object[]> Command;
        public void Call(Hash hash, params object[] arguments) {
            Calls.Add((hash, arguments));
            Before?.Invoke(hash, arguments);
            Command?.Invoke(hash, arguments);
        }
        public T Call<T>(Hash hash, params object[] arguments) {
            Calls.Add((hash, arguments));
            Before?.Invoke(hash, arguments);
            if (!Queries.TryGetValue(hash, out var query)) throw new InvalidOperationException("Unscripted query: " + hash);
            return (T)query(arguments);
        }
        internal void Returns(Hash hash, object value) { Queries[hash] = arguments => value; }
        private readonly Dictionary<InputArgument, object> marshalled = new Dictionary<InputArgument, object>();
        InputArgument IArgumentMarshaller.Convert(object value) {
            var token = new InputArgument((ulong)marshalled.Count + 1);
            marshalled.Add(token, value); return token;
        }
        void IRawNative.Call(Hash hash, InputArgument[] values) => Call(hash, values.Select(value => marshalled[value]).ToArray());
        T IRawNative.Call<T>(Hash hash, InputArgument[] values) => Call<T>(hash, values.Select(value => marshalled[value]).ToArray());
    }
    internal sealed class ConsoleHost : IGameHost {
        public int GameTime { get; set; }
        public float LastFrameTime => .016f;
        public bool IsPaused => false;
        public int PlayerHandle => 7;
        public int PlayerPed => 11;
        public Version FileVersion => new Version(1, 0, 1158, 16);
        public bool IsEnhanced => true;
        internal readonly List<int> Waits = new List<int>();
        internal int FrameMilliseconds = 1;
        internal Action OnWait;
        public void Wait(int milliseconds) {
            Waits.Add(milliseconds);
            GameTime = unchecked(GameTime + FrameMilliseconds);
            OnWait?.Invoke();
        }
        public void Yield() { throw new NotSupportedException(); }
        public void Notify(string text) { throw new NotSupportedException(); }
    }
    internal sealed class ConsoleWorld : IWorldAccess {
        internal bool GroundLoaded = true;
        internal float GroundHeight = 42;
        internal Vector3 GroundQuery;
        public bool GetGroundHeight(Vector3 position, out float height) {
            GroundQuery = position;
            height = GroundHeight;
            return GroundLoaded;
        }
        public int[] NearbyEntities(Vector3 position, float radius) { throw new NotSupportedException(); }
        public RayHit Raycast(Vector3 from, Vector3 to, IntersectFlags flags, int ignoreEntity = 0) { throw new NotSupportedException(); }
        public RayHit Capsule(Vector3 from, Vector3 to, float radius, IntersectFlags flags) { throw new NotSupportedException(); }
        public void DrawMarker(MarkerType type, Vector3 position, Vector3 direction, Vector3 rotation, Vector3 scale, Color color) { throw new NotSupportedException(); }
        public bool RequestModel(int hash, int timeoutMs) { throw new NotSupportedException(); }
        public void ReleaseModel(int hash) { throw new NotSupportedException(); }
        public int CreateVehicle(int hash, Vector3 position) { throw new NotSupportedException(); }
        public int CreateDriver(int vehicle) { throw new NotSupportedException(); }
        public int VehicleDriver(int vehicle) { throw new NotSupportedException(); }
    }
    internal sealed class ConsoleCamera : ICameraAccess {
        public Vector3 GameplayPosition { get; set; } = new Vector3(10, 20, 30);
        public Vector3 GameplayDirection => throw new NotSupportedException();
        public Vector3 GameplayRotation { get; set; }
        public int Create(Vector3 position, Vector3 rotation, float fieldOfView) { throw new NotSupportedException(); }
        public void SetPosition(int camera, Vector3 position) { throw new NotSupportedException(); }
        public void PointAt(int camera, int entity) { throw new NotSupportedException(); }
        public void SetActive(int camera, bool active) { throw new NotSupportedException(); }
        public int RenderingCamera => throw new NotSupportedException();
        public void StartRendering() { throw new NotSupportedException(); }
        public void StopRendering() { throw new NotSupportedException(); }
        public void Delete(int camera) { throw new NotSupportedException(); }
        public void Shake(CameraShake shake, float amplitude) { throw new NotSupportedException(); }
        public void SetHeadingLimits(float minimum, float maximum) { throw new NotSupportedException(); }
        public void SetPitchLimits(float minimum, float maximum) { throw new NotSupportedException(); }
    }
    internal sealed class ConsoleOutputs : IRawNativeOutputs {
        internal Quaternion Rotation = new Quaternion(0, 0, 1, 0);
        internal int QuaternionEntity;
        internal Vector3 Projection;
        public void EntityQuaternion(int entity, out float x, out float y, out float z, out float w) {
            QuaternionEntity = entity; x = Rotation.X; y = Rotation.Y; z = Rotation.Z; w = Rotation.W;
        }
        public bool WorldToScreen(float worldX, float worldY, float worldZ, out float x, out float y) {
            Projection = new Vector3(worldX, worldY, worldZ); x = .25f; y = .5f; return true;
        }
        public bool LastDamageBone(int entity, out int bone) { throw new NotSupportedException(); }
    }
    internal sealed class ConsolePlatform : LiveConsolePlatform {
        internal readonly ConsoleKeyboard Keyboard;
        internal readonly ConsoleClipboard ClipboardDevice;
        internal ConsolePlatform() : this(new ConsoleKeyboard(), new ConsoleClipboard()) { }
        private ConsolePlatform(ConsoleKeyboard keyboard, ConsoleClipboard clipboard)
            : base(new NativeMethods(keyboard), clipboard) { Keyboard = keyboard; ClipboardDevice = clipboard; }
        public override float Width => 1200;
        public override float Height => 900;
        public override int GameTime => Time;
        internal int Time;
        internal bool UseSystemClock;
        public override DateTime Now => UseSystemClock ? base.Now : new DateTime(2026, 10, 20, 12, 34, 56);
        internal string Clipboard { get => ClipboardDevice.Text; set => ClipboardDevice.Text = value; }
        internal string Translation { get => Keyboard.Text; set { Keyboard.Text = value ?? ""; Keyboard.Characters = value == null ? -1 : value.Length; } }
        internal readonly List<(string Text, Point Position, float Scale, Color Color, Font Font)> Text =
            new List<(string, Point, float, Color, Font)>();
        internal readonly List<(string Text, Point Position, float Scale, Color Color, Font Font)> DrawHistory =
            new List<(string, Point, float, Color, Font)>();
        internal readonly List<(Point Position, Size Size, Color Color)> Rectangles =
            new List<(Point, Size, Color)>();
        public override void DrawRectangle(Point position, Size size, Color color) { Rectangles.Add((position, size, color)); }
        public override void DrawText(string text, Point position, float scale, Color color, Font font) { Text.Add((text, position, scale, color, font)); DrawHistory.Add((text, position, scale, color, font)); }
        internal void ClearDrawing() { Text.Clear(); Rectangles.Clear(); }
    }
    internal sealed class ConsoleEnvironment : LiveCommandEnvironment {
        internal ConsoleEnvironment(GameServices game) : base(game) { }
        internal bool InVehicle;
        // The game constructs these handle-only SHVDNE wrappers internally. Calling the
        // nonpublic constructors supplies the same external observation without a native lookup.
        private static object Wrapper(Type type, int handle) => Activator.CreateInstance(type,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new object[] { handle }, null);
        public override object Player => Wrapper(typeof(Player), 7);
        public override object Character => Wrapper(typeof(Ped), 11);
        public override bool IsInVehicle => InVehicle;
        public override object Vehicle => InVehicle ? Wrapper(typeof(Vehicle), 22) : null;
        public override Vector3 Position => new Vector3(1, 2, 3);
        public override string PlayerName => "Franklin";
        public override int PlayerHandle => 7;
    }
    internal sealed class ConsoleKeyboard : IKeyboardNative {
        internal int Characters = 1;
        internal string Text = "a";
        internal byte[] State;
        internal uint Key;
        internal IntPtr Window = new IntPtr(23);
        internal int ForegroundProcess = 42;
        internal int ProcessQueries;
        internal int StateKey;
        internal short Caps;
        public int CurrentProcess => 42;
        public int ToUnicode(uint key, byte[] state, StringBuilder result) {
            Key = key; State = state; result.Append(Text); return Characters;
        }
        public IntPtr ForegroundWindow() { return Window; }
        public int WindowProcess(IntPtr window) {
            if (window != Window) throw new InvalidOperationException("Wrong foreground handle");
            ProcessQueries++; return ForegroundProcess;
        }
        public short KeyState(int key) { StateKey = key; return Caps; }
    }
    internal sealed class ConsoleClipboard : IClipboardNative {
        internal string Text = "";
        internal Exception Failure;
        internal System.Threading.ApartmentState Apartment;
        public string Read() { Apartment = System.Threading.Thread.CurrentThread.GetApartmentState(); if (Failure != null) throw Failure; return Text; }
        public void Write(string text) { Apartment = System.Threading.Thread.CurrentThread.GetApartmentState(); if (Failure != null) throw Failure; Text = text; }
    }
    internal sealed class ConsoleFixture : IDisposable {
        internal readonly ConsoleNative Native = new ConsoleNative();
        internal readonly ConsoleHost Host = new ConsoleHost();
        internal readonly ConsoleWorld World = new ConsoleWorld();
        internal readonly ConsoleCamera Camera = new ConsoleCamera();
        internal readonly ConsoleOutputs Outputs = new ConsoleOutputs();
        internal readonly ConsolePlatform Platform = new ConsolePlatform();
        internal readonly ConsoleEnvironment Environment;
        internal readonly HashSet<Control> Enabled = new HashSet<Control>();
        internal readonly HashSet<int> RequestedModels = new HashSet<int>();
        internal readonly Dictionary<Control, float> Analog = new Dictionary<Control, float>();
        internal bool ModelLoaded = true;
        internal int CreatedVehicle = 23;
        internal readonly GTAFuncs Functions;
        internal readonly GameServices Game;
        internal ConsoleModel Model;
        internal ConsoleFixture(bool createConsole = true, bool? debug = false) {
            Native.Queries[Hash.IS_CONTROL_ENABLED] = a => Enabled.Contains((Control)(int)a[1]);
            Native.Queries[Hash.GET_CONTROL_NORMAL] = a => {
                var control = (Control)(int)a[1];
                return Enabled.Contains(control) && Analog.TryGetValue(control, out var value) ? value : 0f;
            };
            Native.Returns(Hash.END_TEXT_COMMAND_GET_SCREEN_WIDTH_OF_DISPLAY_TEXT, .01f);
            Native.Returns(Hash.NETWORK_REQUEST_CONTROL_OF_ENTITY, true);
            Native.Returns(Hash.NETWORK_REQUEST_CONTROL_OF_NETWORK_ID, true);
            Native.Returns(Hash.NETWORK_GET_PLAYER_INDEX, 37);
            Native.Queries[Hash.HAS_MODEL_LOADED] = a => ModelLoaded;
            Native.Queries[Hash.CREATE_VEHICLE] = a => CreatedVehicle;
            Native.Command = (hash, a) => {
                if (hash == Hash.ENABLE_CONTROL_ACTION) {
                    if ((bool)a[2]) Enabled.Add((Control)(int)a[1]); else Enabled.Remove((Control)(int)a[1]);
                } else if (hash == Hash.DISABLE_CONTROL_ACTION) {
                    if ((bool)a[2]) Enabled.Remove((Control)(int)a[1]); else Enabled.Add((Control)(int)a[1]);
                } else if (hash == Hash.DISABLE_ALL_CONTROL_ACTIONS) Enabled.Clear();
                else if (hash == Hash.REQUEST_MODEL) RequestedModels.Add((int)a[0]);
                else if (hash == Hash.SET_MODEL_AS_NO_LONGER_NEEDED) RequestedModels.Remove((int)a[0]);
            };
            Game = GtaRuntime.Create(Host, null, null, null, null, World, Camera, null, Native, Native, Outputs);
            Environment = new ConsoleEnvironment(Game);
            Functions = new GTAFuncs(Game, Platform);
            if (createConsole) {
                Model = new ConsoleModel(Platform, Environment, Functions);
                if (debug.HasValue) Model.Debug = debug.Value;
            }
        }
        internal void Press(Keys key) { Model.KeyDown(new KeyEventArgs(key)); }
        internal void Type(Keys key, string text) { Platform.Translation = text; Press(key); Model.KeyUp(new KeyEventArgs(key)); }
        internal void Draw() { Platform.ClearDrawing(); Model.Tick(); }
        internal void Enter(string command) {
            if (!Model.IsOpen) Press(Keys.F8);
            int length = Model.Input.Length;
            for (int i = 0; i < length; i++) Press(Keys.Right);
            for (int i = 0; i < length; i++) Press(Keys.Back);
            Platform.Clipboard = command;
            Press(Keys.Control | Keys.V);
            Press(Keys.Enter);
        }
        internal void Submit(string command) {
            Enter(command);
            Draw();
        }
        internal void Evaluate(string code) { Submit("eval { " + code + " }"); }
        internal bool Shows(string text) => Platform.Text.Any(line => line.Text.Contains(text));
        internal bool ShowsOutput(string text) => Platform.Text.Any(line => line.Text == "12:34:56: " + text);
        public void Dispose() { Model?.Dispose(); }
    }
}
