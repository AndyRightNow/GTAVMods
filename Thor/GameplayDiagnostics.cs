using System;
using System.Globalization;
using ADModUtils;
using GTA;
using GTA.Math;
using GTAVMods.Game;

namespace Thor
{
    public interface IThorAbility
    {
        bool IsAttachedToPed { get; }
        bool IsHoldingWeapon { get; }
        string DiagnosticState { get; }
        int WeaponObject { get; }
        void ApplyOn(int ped);
        void OnTick(bool onlyHandleWeaponHolding);
        void RemoveAbility();
        void ResetWeapon();
        void DisposeSounds();
        void DisposeWeapon();
    }

    public sealed class GameplayDiagnostics
    {
        private readonly GameServices game;
        private readonly IEventLog log;
        private readonly Func<long> milliseconds, droppedRecords;
        private long nextState, nextSnapshot, nextPerformance, ticks;
        private string previousState;
        private double totalMs, maxMs;

        public GameplayDiagnostics(GameServices game, IEventLog log, Func<long> milliseconds, Func<long> droppedRecords)
        {
            this.game = game;
            this.log = log;
            this.milliseconds = milliseconds;
            this.droppedRecords = droppedRecords;
        }

        public void RecordTick(IThorAbility ability, bool disabled, double durationMs)
        {
            ticks++;
            totalMs += durationMs;
            maxMs = Math.Max(maxMs, durationMs);
            long now = milliseconds();
            if (durationMs > 100)
                log.Throttled("WARN", "tick.slow", 5000, "durationMs", durationMs, "gameTime", game.Host.GameTime);
            if (now >= nextState)
            {
                nextState = now + 250;
                string state = "disabled=" + disabled + ";" + ability.DiagnosticState;
                if (state != previousState)
                {
                    log.Event("INFO", "ability.state", "gameTime", game.Host.GameTime, "previous", previousState, "current", state);
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
                log.Event("INFO", "performance", "ticks", ticks, "averageTickMs", totalMs / ticks,
                    "maxTickMs", maxMs, "frameSeconds", game.Host.LastFrameTime, "droppedLogRecords", droppedRecords());
                ticks = 0;
                totalMs = maxMs = 0;
            }
        }

        public void Snapshot(IThorAbility ability, string reason)
        {
            int ped = game.Host.PlayerPed;
            bool playerExists = ped != 0 && game.Entities.Exists(ped);
            int hammer = playerExists && ability.IsHoldingWeapon ? game.Weapons.CurrentObject(ped) : ability.WeaponObject;
            log.Event("INFO", "gameplay.snapshot", "reason", reason, "gameTime", game.Host.GameTime,
                "state", ability.DiagnosticState, "player", Describe(ped), "hammer", Describe(hammer),
                "health", playerExists ? game.Entities.Health(ped) : 0,
                "selectedWeapon", playerExists ? game.Weapons.Current(ped).ToString() : "none",
                "distance", playerExists && hammer != 0 && game.Entities.Exists(hammer)
                    ? game.Entities.Position(ped).DistanceTo(game.Entities.Position(hammer)) : -1,
                "paused", game.Host.IsPaused, "aim", game.Input.IsControlPressed(Control.Aim),
                "jump", game.Input.IsControlPressed(Control.Jump), "sprint", game.Input.IsControlPressed(Control.Sprint));
        }

        public string Describe(int entity)
        {
            if (entity == 0 || !game.Entities.Exists(entity)) return "missing";
            return "handle=" + entity + ";model=" + game.Entities.ModelHash(entity) +
                ";position=" + Vector(game.Entities.Position(entity)) + ";velocity=" + Vector(game.Entities.Velocity(entity)) +
                ";dead=" + game.Entities.IsDead(entity);
        }
        private static string Vector(Vector3 value) => string.Format(CultureInfo.InvariantCulture,
            "({0:F2},{1:F2},{2:F2})", value.X, value.Y, value.Z);
    }
}
