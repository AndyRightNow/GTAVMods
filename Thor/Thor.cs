using System;
using System.Diagnostics;
using System.IO;
using System.Windows.Forms;
using ADModUtils;
using GTA;
using GTAVMods.Game;

namespace Thor
{
    public sealed class ThorSessionInfo
    {
        public ThorSessionInfo(string logPath, string buildUtc, string apiVersion)
        {
            LogPath = logPath;
            BuildUtc = buildUtc;
            ApiVersion = apiVersion;
        }
        public string LogPath { get; }
        public string BuildUtc { get; }
        public string ApiVersion { get; }
        public static ThorSessionInfo Capture(string logPath) => new ThorSessionInfo(logPath,
            File.GetLastWriteTimeUtc(typeof(ThorController).Assembly.Location).ToString("O"),
            typeof(Script).Assembly.GetName().Version.ToString());
    }

    public sealed class ThorController
    {
        private readonly GameServices game;
        private readonly IThorAbility ability;
        private readonly GameplayDiagnostics diagnostics;
        private readonly IEventLog log;
        private readonly Action disposePathfinder;
        private readonly Func<long> timestamp;
        private readonly long frequency;
        private readonly ThorSessionInfo session;
        private readonly Func<long> droppedRecords;
        private int previousPedHandle = -1;
        private bool disabled, showCompatibilityNotice, needsRecovery;

        public ThorController(GameServices game, IThorAbility ability, GameplayDiagnostics diagnostics,
            IEventLog log, Action disposePathfinder, Func<long> timestamp, long frequency,
            ThorSessionInfo session, Func<long> droppedRecords)
        {
            this.game = game;
            this.ability = ability;
            this.diagnostics = diagnostics;
            this.log = log;
            this.disposePathfinder = disposePathfinder;
            this.timestamp = timestamp;
            this.frequency = frequency;
            this.session = session;
            this.droppedRecords = droppedRecords;
        }

        public void Start()
        {
            var executableVersion = EnhancedCompatibility.ExecutableVersion(game.Host.FileVersion, game.Host.IsEnhanced);
            log.Event("INFO", "session.start", "modVersion", typeof(ThorController).Assembly.GetName().Version.ToString(),
                "runtime", Environment.Version.ToString(), "is64Bit", Environment.Is64BitProcess,
                "logPath", session.LogPath, "buildUtc", session.BuildUtc,
                "executableVersion", executableVersion?.ToString(), "apiVersion", session.ApiVersion,
                "shvdneVersion", game.Host.FileVersion?.ToString(), "enhanced", game.Host.IsEnhanced);
            showCompatibilityNotice = !game.Host.IsEnhanced || !EnhancedCompatibility.IsAssessedBuild(executableVersion);
        }

        public void Tick()
        {
            long started = timestamp();
            try
            {
                if (disabled) return;
                int ped = game.Host.PlayerPed;
                if (ped == 0 || !game.Entities.Exists(ped) || game.Entities.IsDead(ped))
                {
                    if (ability.IsAttachedToPed) ability.RemoveAbility();
                    return;
                }
                if (showCompatibilityNotice)
                {
                    showCompatibilityNotice = false;
                    game.Host.Notify("Thor: compatibility baseline differs. See README and scripts/Logs.");
                    log.Event("WARN", "compatibility.baseline", "assessed", EnhancedCompatibility.AssessedBuild.ToString());
                }
                if (ped != previousPedHandle && ability.IsAttachedToPed) ability.RemoveAbility();
                previousPedHandle = ped;
                if (!ability.IsAttachedToPed) ability.ApplyOn(ped);
                ability.OnTick();
            }
            catch (Exception error)
            {
                disabled = true;
                needsRecovery = true;
                log.Event("ERROR", "tick.exception", "exception", error.ToString(), "state", ability.DiagnosticState);
                try { diagnostics.Snapshot(ability, "exception"); }
                catch (Exception diagnosticError) { log.Event("WARN", "diagnostics.exception", "exception", diagnosticError.ToString()); }
                TryCleanup(ability.RemoveAbility);
                game.Host.Notify("Thor paused after an error. See scripts/Logs. Ctrl+A+O retries.");
            }
            finally
            {
                try { diagnostics.RecordTick(ability, disabled, (timestamp() - started) * 1000.0 / frequency); }
                catch (Exception error) { log.Throttled("WARN", "diagnostics.exception", 10000, "exception", error.ToString()); }
            }
        }

        public void KeyDown(KeyEventArgs args)
        {
            Keys key = args.KeyCode;
            if (key == Keys.H || key == Keys.T || key == Keys.U || key == Keys.Y || key == Keys.Z || key == Keys.R || key == Keys.F9)
                log.Throttled("INFO", "input." + key, 250, "control", args.Control, "shift", args.Shift, "gameTime", game.Host.GameTime);
            if (key == Keys.F9)
            {
                try { diagnostics.Snapshot(ability, "manual-F9"); }
                catch (Exception error) { log.Event("WARN", "diagnostics.exception", "exception", error.ToString()); }
            }
            if (!args.Control || !game.Input.IsKeyPressed(Keys.A)) return;
            if (key == Keys.O) SetEnabled(true, "Ctrl+A+O");
            else if (key == Keys.F) SetEnabled(false, "Ctrl+A+F");
        }

        /// <summary>External agents use the same cleanup/recovery path as the player shortcut.</summary>
        public void SetEnabled(bool enabled, string reason = "external-agent")
        {
            if (enabled && disabled)
            {
                if (!TryCleanup(ability.RemoveAbility) || (needsRecovery && !TryCleanup(ability.ResetWeapon)))
                {
                    needsRecovery = true;
                    game.Host.Notify("Thor remains paused because cleanup failed. See scripts/Logs. Ctrl+A+O retries.");
                    return;
                }
                needsRecovery = false;
                disabled = false;
                log.Event("INFO", "ability.enabled", "reason", reason);
                game.Host.Notify("The Thor ability has been turned on.");
            }
            else if (!enabled && !disabled)
            {
                disabled = true;
                log.Event("INFO", "ability.disabled", "reason", reason);
                needsRecovery = !TryCleanup(ability.RemoveAbility);
                int ped = game.Host.PlayerPed;
                if (ped != 0 && game.Entities.Exists(ped) && !game.Entities.IsDead(ped))
                    game.Weapons.Give(ped, WeaponHash.Parachute, 1, false, false);
                game.Host.Notify("The Thor ability has been turned off.");
            }
        }

        public void Abort()
        {
            log.Event("INFO", "session.stopping", "reason", "script-aborted-or-reloaded");
            TryCleanup(ability.RemoveAbility);
            TryCleanup(ability.DisposeSounds);
            TryCleanup(ability.DisposeWeapon);
            TryCleanup(disposePathfinder);
            log.Event("INFO", "session.end", "droppedLogRecords", droppedRecords());
        }

        private bool TryCleanup(Action cleanup)
        {
            try { cleanup(); return true; }
            catch (Exception error)
            {
                log.Event("ERROR", "cleanup.exception", "exception", error.ToString());
                return false;
            }
        }
    }

    public static class ThorComposition
    {
        public static ThorController Create(GameServices game, IAudioPlaybackFactory audio, IEventLog log, Random random,
            Func<long> droppedRecords, ThorSessionInfo session,
            Func<long> milliseconds = null, Func<long> timestamp = null, long frequency = 0,
            GameplayTuning tuning = null)
        {
            tuning = tuning ?? new GameplayTuning();
            var effects = new NativeHelper(game, log, tuning);
            var pathfinder = new PathFinder.PathFinder(game, log, tuning.Navigation);
            var camera = new WeaponCamera(game, random, log, tuning.Camera);
            var thunder = new Thunder(effects, random, tuning.Effects);
            var weapon = new Mjolnir(game, effects, random, pathfinder, camera, thunder, audio, log, tuning);
            var ability = new MjolnirWorthyAbility(game, effects, random, weapon, audio, log, tuning);
            var clock = Stopwatch.StartNew();
            var diagnostics = new GameplayDiagnostics(game, log, milliseconds ?? (() => clock.ElapsedMilliseconds), droppedRecords);
            return new ThorController(game, ability, diagnostics, log, pathfinder.Dispose,
                timestamp ?? Stopwatch.GetTimestamp, frequency == 0 ? Stopwatch.Frequency : frequency, session, droppedRecords);
        }
    }

    public sealed class Thor : Script
    {
        private readonly ThorController controller;
        public static ThorController Current;
        [GamePrimitive]
        public Thor()
        {
            var game = GtaRuntime.Create(new GtaHost(), new GtaInput(),
                new GtaEntities(new GtaEntityLifetimeBindings()), new GtaPeds(), new GtaWeapons(),
                new GtaWorld(new GtaWorldQueries()), new GtaCameras(new GtaCameraLifetimeBindings()),
                new GtaRopes(new GtaRopeLifetimeBindings()), new GtaNative(), new ScalarArgumentMarshaller(new GtaArgumentMarshaller()), new GtaOutputs());
            controller = ThorComposition.Create(game, new NAudioPlaybackFactory(), Logger.Default,
                new Random(), new Func<long>(ReadDroppedRecords),
                ThorSessionInfo.Capture(Logger.LogFilePath));
            Current = controller;
            Tick += OnTick;
            KeyDown += OnKeyDown;
            Aborted += OnAborted;
            Interval = 0;
            controller.Start();
        }
        public static long ReadDroppedRecords() => Logger.DroppedRecords;
        [GamePrimitive] private void OnTick(object sender, EventArgs args) => controller.Tick();
        [GamePrimitive] private void OnKeyDown(object sender, KeyEventArgs args) => controller.KeyDown(args);
        [GamePrimitive] private void OnAborted(object sender, EventArgs args) => controller.Abort();
    }
}
