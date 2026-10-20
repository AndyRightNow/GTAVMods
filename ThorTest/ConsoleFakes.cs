using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Drawing;
using System.Reflection;
using System.Text;
using System.Windows.Forms;
using DeveloperConsole;
using GTA;
using GTA.Math;
using GTA.Native;
using GTAVMods.Game;
using Font = GTA.UI.Font;
using ConsoleModel = DeveloperConsole.DeveloperConsole;

namespace ModUtilsTest {
    internal sealed class ConsoleNative : INativeInvoker {
        internal readonly List<(Hash Hash, object[] Arguments)> Calls = new List<(Hash, object[])>();
        internal readonly Dictionary<Hash, Func<object[], object>> Queries = new Dictionary<Hash, Func<object[], object>>();
        public void Call(Hash hash, params object[] arguments) { Calls.Add((hash, arguments)); }
        public T Call<T>(Hash hash, params object[] arguments) {
            Calls.Add((hash, arguments));
            if (!Queries.TryGetValue(hash, out var query)) throw new InvalidOperationException("Unscripted query: " + hash);
            return (T)query(arguments);
        }
        internal void Returns(Hash hash, object value) { Queries[hash] = arguments => value; }
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
        public void Wait(int milliseconds) { Waits.Add(milliseconds); }
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
    internal sealed class ConsoleOutputs : INativeOutputs {
        internal Quaternion Rotation = new Quaternion(0, 0, 1, 0);
        internal int QuaternionEntity;
        internal Vector3 Projection;
        public Quaternion EntityQuaternion(int entity) { QuaternionEntity = entity; return Rotation; }
        public bool WorldToScreen(Vector3 position, out float x, out float y) {
            Projection = position; x = .25f; y = .5f; return true;
        }
        public bool LastDamageBone(int entity, out int bone) { throw new NotSupportedException(); }
    }
    internal sealed class ConsolePlatform : IConsolePlatform {
        public float Width { get; set; } = 1200;
        public float Height { get; set; } = 900;
        public int GameTime { get; set; }
        public DateTime Now { get; set; } = new DateTime(2026, 10, 20, 12, 34, 56);
        public short CapsLockState { get; set; }
        public bool IsForeground { get; set; } = true;
        internal string Clipboard = "";
        internal string Translation = "a";
        internal (Keys Key, bool Shift, bool Alt) LastTranslation;
        internal readonly List<(string Text, Point Position, float Scale, Color Color, Font Font)> Text =
            new List<(string, Point, float, Color, Font)>();
        internal readonly List<(Point Position, Size Size, Color Color)> Rectangles =
            new List<(Point, Size, Color)>();
        public string ReadClipboard() { return Clipboard; }
        public void WriteClipboard(string text) { Clipboard = text; }
        public string TranslateKey(Keys key, bool shift, bool alt) {
            LastTranslation = (key, shift, alt);
            return Translation;
        }
        public void DrawRectangle(Point position, Size size, Color color) { Rectangles.Add((position, size, color)); }
        public void DrawText(string text, Point position, float scale, Color color, Font font) { Text.Add((text, position, scale, color, font)); }
        internal void ClearDrawing() { Text.Clear(); Rectangles.Clear(); }
    }
    internal sealed class ConsoleEnvironment : ICommandEnvironment {
        public object Player { get; set; } = new object();
        public object Character { get; set; } = new object();
        public bool IsInVehicle { get; set; }
        public object Vehicle { get; set; }
        public Vector3 Position { get; set; } = new Vector3(1, 2, 3);
        public string PlayerName => "Franklin";
        public int PlayerHandle => 7;
        internal int Compilations;
        internal CompilerParameters Parameters;
        internal string Source;
        internal Func<CompilerResults> Compiler = () => new CompilerResults(new TempFileCollection()) {
            CompiledAssembly = typeof(ConsoleEnvironment).Assembly
        };
        public CompilerResults Compile(CompilerParameters parameters, string source) {
            Compilations++; Parameters = parameters; Source = source;
            return Compiler();
        }
    }
    internal sealed class ConsoleMessages : IDeveloperConsole {
        internal readonly List<string> Errors = new List<string>();
        internal readonly List<string> Debug = new List<string>();
        public void PrintError(string text) { Errors.Add(text); }
        public void PrintDebug(string text) { Debug.Add(text); }
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
        internal short Caps = 1;
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
        internal string Text;
        public string Read() { return Text; }
        public void Write(string text) { Text = text; }
    }
    internal sealed class ConsoleFixture : IDisposable {
        internal readonly ConsoleNative Native = new ConsoleNative();
        internal readonly ConsoleHost Host = new ConsoleHost();
        internal readonly ConsoleWorld World = new ConsoleWorld();
        internal readonly ConsoleCamera Camera = new ConsoleCamera();
        internal readonly ConsoleOutputs Outputs = new ConsoleOutputs();
        internal readonly ConsolePlatform Platform = new ConsolePlatform();
        internal readonly ConsoleEnvironment Environment = new ConsoleEnvironment();
        internal readonly GTAFuncs Functions;
        internal ConsoleModel Model;
        internal ConsoleFixture(bool createConsole = true) {
            Native.Returns(Hash.IS_CONTROL_ENABLED, false);
            Native.Returns(Hash.END_TEXT_COMMAND_GET_SCREEN_WIDTH_OF_DISPLAY_TEXT, .01f);
            Functions = new GTAFuncs(new GameServices(Host, null, null, null, null, World, Camera, null, Native, Outputs), Platform);
            if (createConsole) Model = new ConsoleModel(Platform, Environment, Functions) { Debug = false };
        }
        internal void Press(Keys key) { Model.KeyDown(new KeyEventArgs(key)); }
        internal void Type(Keys key, string text) { Platform.Translation = text; Press(key); Model.KeyUp(new KeyEventArgs(key)); }
        internal void Draw() { Platform.ClearDrawing(); Model.Tick(); }
        public void Dispose() { Model?.Dispose(); }
    }
}

namespace DeveloperConsoleCodeEval {
    // Fake compiler result: real reflection executes this independently of GTA.
    public class DeveloperConsoleCodeEval {
        public static object Result;
        public static int Evaluations;
        public object EvalCode() { Evaluations++; return Result; }
    }
}
