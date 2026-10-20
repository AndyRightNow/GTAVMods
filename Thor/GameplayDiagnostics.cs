using System;
using System.Diagnostics;
using System.Globalization;
using ADModUtils;
using GTA;
using GTA.Math;

namespace Thor
{
    internal sealed class GameplayDiagnostics
    {
        private readonly Stopwatch clock = Stopwatch.StartNew();
        private long nextState, nextSnapshot, nextPerformance;
        private string previousState;
        private long ticks;
        private double totalMs, maxMs;

        public void RecordTick(MjolnirWorthyAbility ability, bool disabled, double durationMs)
        {
            ticks++; totalMs += durationMs; maxMs = System.Math.Max(maxMs, durationMs);
            long now = clock.ElapsedMilliseconds;
            if (durationMs > 100)
                Logger.Throttled("WARN", "tick.slow", 5000, "durationMs", durationMs, "gameTime", Game.GameTime);
            if (now >= nextState)
            {
                nextState = now + 250;
                string state = "disabled=" + disabled + ";" + ability.DiagnosticState;
                if (state != previousState)
                {
                    Logger.Event("INFO", "ability.state", "gameTime", Game.GameTime, "previous", previousState, "current", state);
                    previousState = state;
                }
            }
            if (now >= nextSnapshot)
            {
                nextSnapshot = now + 5000;
                Snapshot(ability, "periodic");
            }
            if (now >= nextPerformance)
            {
                nextPerformance = now + 10000;
                Logger.Event("INFO", "performance", "ticks", ticks, "averageTickMs", ticks == 0 ? 0 : totalMs / ticks,
                    "maxTickMs", maxMs, "frameSeconds", Game.LastFrameTime, "droppedLogRecords", Logger.DroppedRecords);
                ticks = 0; totalMs = 0; maxMs = 0;
            }
        }

        public static void Snapshot(MjolnirWorthyAbility ability, string reason)
        {
            var ped = Game.Player.Character;
            var hammer = ability.Weapon.WeaponObject;
            if (ped != null && ped.Exists() && ability.IsHoldingWeapon)
                hammer = ped.Weapons.CurrentWeaponObject;
            Logger.Event("INFO", "gameplay.snapshot", "reason", reason, "gameTime", Game.GameTime,
                "state", ability.DiagnosticState, "player", Describe(ped), "hammer", Describe(hammer),
                "health", ped != null && ped.Exists() ? ped.Health : 0,
                "selectedWeapon", ped != null && ped.Exists() ? ped.Weapons.Current.Hash.ToString() : "none",
                "distance", ped != null && ped.Exists() && hammer != null && hammer.Exists() ? ped.Position.DistanceTo(hammer.Position) : -1,
                "paused", Game.IsPaused, "aim", Game.IsControlPressed(Control.Aim),
                "jump", Game.IsControlPressed(Control.Jump), "sprint", Game.IsControlPressed(Control.Sprint));
        }

        public static string Describe(Entity entity)
        {
            if (entity == null || !entity.Exists()) return "missing";
            return "handle=" + entity.Handle + ";model=" + entity.Model.Hash +
                ";position=" + Vector(entity.Position) + ";velocity=" + Vector(entity.Velocity) + ";dead=" + entity.IsDead;
        }
        private static string Vector(Vector3 v) => string.Format(CultureInfo.InvariantCulture, "({0:F2},{1:F2},{2:F2})", v.X, v.Y, v.Z);
    }
}
