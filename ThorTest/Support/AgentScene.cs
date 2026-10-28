using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using ADModUtils;
using GTA;
using GTA.Math;
using GTA.Native;
using GTAVMods.Agent;
using GTAVMods.Game;

namespace GTAVMods.Tests.Support
{
    internal sealed class IdleAgentConnection : IAgentConnection
    {
        private readonly TaskCompletionSource<bool> pending = new TaskCompletionSource<bool>();
        public Task Connect() => pending.Task;
        public Task<int> Read(byte[] buffer, int offset, int count) => throw new IOException("No client connected.");
        public Task Write(byte[] buffer) => throw new IOException("No client connected.");
        public void Dispose() { pending.TrySetCanceled(); }
    }

    internal sealed class ObservedWindowPixels : IWindowCaptureApi
    {
        public IntPtr Window { get; set; } = new IntPtr(42);
        public IntPtr Foreground { get; set; } = new IntPtr(42);
        internal Rectangle Area = new Rectangle(20, 30, 48, 24);
        internal bool RectangleAvailable = true, PositionAvailable = true;
        public bool ReadRectangle(IntPtr window, out ClientRectangle rect)
        {
            rect = new ClientRectangle { Right = Area.Width, Bottom = Area.Height }; return RectangleAvailable;
        }
        public bool ToScreen(IntPtr window, ref Point point) { point = Area.Location; return PositionAvailable; }
        public void Copy(Graphics graphics, Point source, Size size) => graphics.Clear(Color.DarkOrange);
    }

    internal sealed class FaultableAgentFiles : IAgentFiles
    {
        private readonly AgentFiles disk = new AgentFiles();
        public Action<string> BeforeSave;
        public void Save(string path, string text) { BeforeSave?.Invoke(path); disk.Save(path, text); }
    }

    internal sealed class AgentScene : IDisposable
    {
        internal readonly string Root = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "agent-test-sessions", Guid.NewGuid().ToString("N"));
        internal readonly string SessionId = Guid.NewGuid().ToString("N");
        internal readonly FakeGame Game = new FakeGame();
        internal readonly ManualLogClock Clock = new ManualLogClock();
        internal readonly FaultableAgentFiles Files = new FaultableAgentFiles();
        internal readonly ObservedWindowPixels Pixels = new ObservedWindowPixels();
        internal readonly AgentJournal Journal;
        internal readonly DiagnosticLog Log;
        internal readonly BridgeSession Bridge;
        internal readonly AgentWorld World;
        internal string Published;
        internal Blip[] Blips = new[] { new Blip(90) };
        internal AgentScene(Func<IAgentConnection> open = null, Func<IEnumerable<Assembly>> assemblies = null, ILogStorage logStorage = null, int compilationLimit = 128, int journalCharacters = 2 * 1024 * 1024)
        {
            Directory.CreateDirectory(Root);
            Game.AddEntity(10).Position = new Vector3(1, 2, 3);
            Game.AddEntity(20).Position = new Vector3(20, 0, 0);
            Game.AddEntity(30).Position = new Vector3(5, 0, 0);
            Game.Outputs.Projection = position => Tuple.Create(true, .25f, .75f);
            Game.World.Nearby = (position, radius) => new[] { 10, 20, 30 };
            Game.Native.Returns(Hash.GET_MISSION_FLAG, true);
            Game.Native.Returns(Hash.IS_CUTSCENE_ACTIVE, false);
            Game.Native.Returns(Hash.IS_CUTSCENE_PLAYING, false);
            Game.Native.Returns(Hash.HAS_CUTSCENE_FINISHED, false);
            Game.Native.Returns(Hash.GET_CUTSCENE_TIME, 0);
            Game.Native.Returns(Hash.GET_CUTSCENE_TOTAL_DURATION, 0);
            Game.Native.Returns(Hash.IS_SCREEN_FADED_OUT, false);
            Game.Native.Returns(Hash.GET_PLAYER_WANTED_LEVEL, 2);
            Game.Native.Returns(Hash.GET_VEHICLE_PED_IS_IN, 0);
            Game.Native.Returns(Hash.GET_ENTITY_TYPE, 1);
            Game.Native.Returns(Hash.GET_BLIP_SPRITE, 1);
            Game.Native.Returns(Hash.GET_BLIP_COLOUR, 5);
            Game.Native.Returns(Hash.GET_BLIP_COORDS, new Vector3(100, 200, 30));
            Game.Native.Returns(Hash.IS_PLAYER_CONTROL_ON, true);
            Game.Native.Returns(Hash.IS_MOBILE_PHONE_CALL_ONGOING, false);
            Game.Native.Returns(Hash.IS_HELP_MESSAGE_ON_SCREEN, false);
            Game.Native.Returns(Hash.IS_MESSAGE_BEING_DISPLAYED, false);
            Game.Native.Returns(Hash.GET_PED_ARMOUR, 25);
            Game.Native.Returns(Hash.IS_PED_SWIMMING, false);
            Game.Native.Returns(Hash.IS_PED_HUMAN, true);
            Game.Native.Returns(Hash.IS_PED_CLIMBING, false);
            Game.Native.Returns(Hash.IS_PED_VAULTING, false);
            Game.Native.Returns(Hash.IS_PED_JUMPING, false);
            Game.Native.Returns(Hash.IS_PED_FALLING, false);
            Game.Native.Returns(Hash.IS_PED_IN_COVER, false);
            Game.Native.Returns(Hash.IS_PED_GOING_INTO_COVER, false);
            Game.Native.Returns(Hash.IS_PED_AIMING_FROM_COVER, false);
            Game.Native.Returns(Hash.IS_PED_IN_COVER_FACING_LEFT, false);
            Game.Native.Returns(Hash.IS_PED_WEAPON_READY_TO_SHOOT, false);
            Game.Native.Returns(Hash.IS_PED_RELOADING, false);
            Log = new DiagnosticLog(new SessionLog(Path.Combine(Root, "logs"), Path.Combine(Root, "fallback"), logStorage ?? new FileLogStorage(), Clock), () => Clock.ElapsedMilliseconds);
            Journal = new AgentJournal(Root, SessionId, Files, Log, journalCharacters);
            World = new AgentWorld(Game.Services, new NativeInvoker(Game.Native, Game.Native), () => Blips);
            var capture = new ScreenCapture(new WindowPixels(Pixels), Root);
            Bridge = new BridgeSession(World, Journal, () => Clock.ElapsedMilliseconds, open ?? (() => new IdleAgentConnection()),
                assemblies ?? (() => AppDomain.CurrentDomain.GetAssemblies()), capture.Capture, value => Published = Protocol.Encode(value), compilationLimit);
        }
        internal Dictionary<string, object> Request(string op, params (string Key, object Value)[] fields)
        {
            var request = Envelope(op);
            foreach (var pair in fields) request[pair.Key] = pair.Value;
            return Protocol.Decode(Bridge.Dispatch(Protocol.Encode(request)));
        }
        internal Dictionary<string, object> Envelope(string op) => new Dictionary<string, object> {
            { "id", Guid.NewGuid().ToString("N") }, { "version", 1 }, { "session", SessionId }, { "op", op }
        };
        internal static Dictionary<string, object> Step(string op, params (string Key, object Value)[] fields)
        {
            var result = new Dictionary<string, object> { { "op", op } };
            foreach (var pair in fields) result[pair.Key] = pair.Value;
            return result;
        }
        internal Dictionary<string, object> Batch(params Dictionary<string, object>[] steps) => Request("batch", ("steps", steps));
        internal void Frame(int gameMs = 0, int wallMs = 0)
        {
            Game.Host.GameTime = unchecked(Game.Host.GameTime + gameMs);
            Clock.ElapsedMilliseconds += wallMs;
            Bridge.Tick();
        }
        internal void FramesUntil(Func<bool> observed)
        {
            var elapsed = System.Diagnostics.Stopwatch.StartNew();
            while (!observed() && elapsed.ElapsedMilliseconds < 5000)
            {
                Frame();
                System.Threading.Thread.Sleep(1);
            }
            Microsoft.VisualStudio.TestTools.UnitTesting.Assert.IsTrue(observed(), "Expected transport/game outcome did not arrive within five seconds.");
        }
        internal string Status => Protocol.Encode(Bridge.Status());
        internal string Events { get {
            Journal.Drain();
            using (var stream = new FileStream(Log.FilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var reader = new StreamReader(stream)) return reader.ReadToEnd();
        } }
        internal Dictionary<string, object>[] Results { get { Journal.Drain(); return Directory.GetFiles(Root, "result-*.json").Select(path => Protocol.Decode(File.ReadAllText(path))).OrderBy(row => (int)row["step"]).ToArray(); } }
        public void Dispose()
        {
            Files.BeforeSave = null;
            Bridge.Dispose();
            Directory.Delete(Root, true);
        }
    }
}
