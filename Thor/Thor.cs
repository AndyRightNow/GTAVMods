using GTA;
using System;
using System.Windows.Forms;
using DeveloperConsole;
using ADModUtils;
using GTA.UI;

namespace Thor
{
    public class Thor : Script
    {
        private readonly MjolnirWorthyAbility mjolnirAbility;
        private int previousPedHandle;
        private bool abilityHasBeenTurnedOff;
        private bool showCompatibilityNotice;
        private readonly GameplayDiagnostics diagnostics = new GameplayDiagnostics();

        public Thor()
        {
            mjolnirAbility = MjolnirWorthyAbility.Instance;
            previousPedHandle = -1;
            Tick += OnTick;
            KeyDown += OnKeyDown;
            Aborted += OnAborted;
            Interval = 0;
            this.RegisterConsoleScript(dc => Logger.Init(dc));

            Logger.Event("INFO", "session.start", "modVersion", typeof(Thor).Assembly.GetName().Version.ToString(),
                "runtime", Environment.Version.ToString(), "is64Bit", Environment.Is64BitProcess,
                "logPath", Logger.LogFilePath, "buildUtc", System.IO.File.GetLastWriteTimeUtc(typeof(Thor).Assembly.Location).ToString("O"));

            var executableVersion = EnhancedCompatibility.ExecutableVersion(Game.FileVersion, Game.isEnhanced);
            Logger.Log("INFO", "Game executable: " + executableVersion, "SHVDNE version: " + Game.FileVersion,
                "Enhanced: " + Game.isEnhanced,
                "SHVDN API: " + typeof(Script).Assembly.GetName().Version);
            showCompatibilityNotice = !Game.isEnhanced ||
                !EnhancedCompatibility.IsAssessedBuild(executableVersion);
        }

        private void OnTick(object sender, EventArgs e)
        {
            var started = System.Diagnostics.Stopwatch.GetTimestamp();
            try
            {
                var ped = Game.Player.Character;
                if (ped == null || !ped.Exists() || ped.IsDead)
                {
                    if (mjolnirAbility.IsAttachedToPed) mjolnirAbility.RemoveAbility();
                    return;
                }
                if (showCompatibilityNotice)
                {
                    showCompatibilityNotice = false;
                    Notification.PostTicker("Thor: compatibility baseline differs. See README and scripts/Logs.", false);
                    Logger.Log("WARN", "Upstream compatibility baseline: Enhanced " +
                        EnhancedCompatibility.AssessedBuild + ". In-game validation is still required.");
                }
                if (abilityHasBeenTurnedOff) return;

                if (ped.Handle != previousPedHandle && mjolnirAbility.IsAttachedToPed)
                    mjolnirAbility.RemoveAbility();
                previousPedHandle = ped.Handle;

                if (!mjolnirAbility.IsAttachedToPed) mjolnirAbility.ApplyOn(ped);
                mjolnirAbility.OnTick(false);
            }
            catch (Exception ex)
            {
                abilityHasBeenTurnedOff = true;
                Logger.Event("ERROR", "tick.exception", "exception", ex.ToString(), "state", mjolnirAbility.DiagnosticState);
                try { GameplayDiagnostics.Snapshot(mjolnirAbility, "exception"); }
                catch (Exception diagnosticError) { Logger.Log("WARN", diagnosticError.ToString()); }
                TryCleanup(() => mjolnirAbility.RemoveAbility());
                Notification.PostTicker("Thor paused after an error. See scripts/Logs. Ctrl+A+O retries.", false);
            }
            finally
            {
                try { diagnostics.RecordTick(mjolnirAbility, abilityHasBeenTurnedOff,
                    (System.Diagnostics.Stopwatch.GetTimestamp() - started) * 1000.0 / System.Diagnostics.Stopwatch.Frequency); }
                catch (Exception ex) { Logger.Throttled("WARN", "diagnostics.exception", 10000, "exception", ex.ToString()); }
            }
        }

        private void OnKeyDown(object sender, KeyEventArgs e)
        {
            if (DeveloperConsole.DeveloperConsole.Instance?.IsOpen == true) return;
            // Record only these mod bindings, rather than arbitrary keystrokes.
            if (e.KeyCode == Keys.H || e.KeyCode == Keys.T || e.KeyCode == Keys.U || e.KeyCode == Keys.Y ||
                e.KeyCode == Keys.Z || e.KeyCode == Keys.R || e.KeyCode == Keys.F9)
                Logger.Throttled("INFO", "input." + e.KeyCode, 250, "control", e.Control, "shift", e.Shift, "gameTime", Game.GameTime);
            if (e.KeyCode == Keys.F9)
            {
                try { GameplayDiagnostics.Snapshot(mjolnirAbility, "manual-F9"); }
                catch (Exception ex) { Logger.Log("WARN", ex.ToString()); }
            }
            if (!e.Control || !Game.IsKeyPressed(Keys.A)) return;
            if (e.KeyCode == Keys.O && abilityHasBeenTurnedOff)
            {
                abilityHasBeenTurnedOff = false;
                Logger.Event("INFO", "ability.enabled", "reason", "Ctrl+A+O");
                Notification.PostTicker("The Thor ability has been turned on.", false);
            }
            else if (e.KeyCode == Keys.F && !abilityHasBeenTurnedOff)
            {
                abilityHasBeenTurnedOff = true;
                Logger.Event("INFO", "ability.disabled", "reason", "Ctrl+A+F");
                TryCleanup(() => mjolnirAbility.RemoveAbility());
                var ped = Game.Player.Character;
                if (ped != null && ped.Exists() && !ped.IsDead)
                    ped.Weapons.Give(WeaponHash.Parachute, 1, false, false);
                Notification.PostTicker("The Thor ability has been turned off.", false);
            }
        }

        private void OnAborted(object sender, EventArgs e)
        {
            Logger.Event("INFO", "session.stopping", "reason", "script-aborted-or-reloaded");
            // Aborted handlers must not yield or request new streamed assets.
            TryCleanup(() => mjolnirAbility.RemoveAbility());
            TryCleanup(() => mjolnirAbility.DisposeSounds());
            TryCleanup(() => mjolnirAbility.Weapon.Dispose());
            TryCleanup(() => PathFinder.PathFinder.Instance.Dispose());
            Logger.Event("INFO", "session.end", "droppedLogRecords", Logger.DroppedRecords);
        }

        private static void TryCleanup(Action cleanup)
        {
            try { cleanup(); }
            catch (Exception ex) { Logger.Log("ERROR", "Cleanup:", ex.ToString()); }
        }
    }
}
