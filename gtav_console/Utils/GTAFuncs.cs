using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Runtime.ExceptionServices;
using GTA;
using GTA.Math;
using GTA.UI;
using Font = GTA.UI.Font;
using GTAVMods.Game;

namespace DeveloperConsole {
    public sealed class GTAFuncs {
        private readonly GameServices _game;
        private readonly IConsoleDisplay _display;
        private readonly IConsoleInput _input;
        private readonly IConsoleWorld _world;
        private readonly IConsoleNetwork _network;
        private readonly Dictionary<Control, bool> _pendingControls = new Dictionary<Control, bool>();
        private int? _pendingModel;
        public GTAFuncs(GameServices game, IConsoleDisplay display)
            : this(game, display, game.ConsoleInput, game.ConsoleWorld, game.ConsoleNetwork) { }
        public GTAFuncs(GameServices game, IConsoleDisplay display, IConsoleInput input,
            IConsoleWorld world, IConsoleNetwork network) {
            _game = game;
            _display = display;
            _input = input;
            _world = world;
            _network = network;
        }
        public enum EntityType {
            Ped = 1,
            Vehicle = 2,
            Prop = 3
        }

        public EntityType GetEntityType(int e) {
            return (EntityType) _game.ConsoleEntities.Type(e);
        }

        public int GetPlayerIndex(int p) {
            return _game.ConsolePlayers.Index(p);
        }

        public Vector2 ValidateScreenPoint(Vector2 v) {
            if (v.X > _display.Width) v.X = _display.Width;
            else if (v.X < 0) v.X = 0;
            if (v.Y > _display.Height) v.Y = _display.Height;
            else if (v.Y < 0) v.Y = 0;
            return v;
        }

        public bool IsLeftMouseClicked() {
            return IsControlPressed(Control.CursorAccept);
        }

        public bool IsRightMouseClicked() {
            return IsControlPressed(Control.CursorCancel);
        }

        public bool IsSelectPressed() {
            return IsControlPressed(Control.ScriptSelect);
        }

        public Vector2 GetLeftStick() {
            return GetControlPoint(Control.ScriptLeftAxisX, Control.ScriptLeftAxisY);
        }

        public Vector2 GetRightStick() {
            return GetControlPoint(Control.ScriptRightAxisX, Control.ScriptRightAxisY);
        }

        public Point GetMousePos() {
            return WithEnabledControls(new[] { Control.CursorX, Control.CursorY },
                () => new Point((int)(GetControlNormal(Control.CursorX) * _display.Width),
                    (int)(GetControlNormal(Control.CursorY) * _display.Height)));
        }

        public bool IsControlPressed(Control c) {
            return WithEnabledControls(new[] { c }, () => GetControlNormal(c) > 0);
        }

        public Vector2 GetControlPoint(Control c1, Control c2) {
            return WithEnabledControls(new[] { c1, c2 }, () => new Vector2(GetControlNormal(c1), GetControlNormal(c2)));
        }

        private T WithEnabledControls<T>(Control[] controls, Func<T> read) {
            RestorePendingControls();
            // Capture every state before the first mutation, including controls originally disabled.
            var original = controls.Select(c => new KeyValuePair<Control, bool>(c, IsControlEnabled(c))).ToArray();
            var failures = new List<Exception>();
            T result = default(T);
            try {
                foreach (var control in controls) SetControlAction(control, true);
                result = read();
            } catch (Exception error) { failures.Add(error); }
            try { RestoreStates(original); }
            catch (Exception error) { failures.Add(error); }
            ThrowFailures(failures);
            return result;
        }

        internal static void ThrowFailures(List<Exception> failures) {
            if (failures.Count == 1) ExceptionDispatchInfo.Capture(failures[0]).Throw();
            if (failures.Count > 1) throw new AggregateException("Console operations failed.", failures);
        }

        public Vector2 GetMoveInputVector() {
            var upDown = Control.MoveUpDown;
            var leftRight = Control.MoveLeftRight;
            return new Vector2(GetControlNormal(leftRight), GetControlNormal(upDown));
        }

        public Vector2 GetLookInputVector() {
            var upDown = Control.LookUpDown;
            var leftRight = Control.LookLeftRight;
            return new Vector2(GetControlNormal(leftRight), GetControlNormal(upDown));
        }

        private void SetControlAction(Control c, bool b) {
            if (b) EnableControlAction(c, true);
            else DisableControlAction(c, true);
        }

        public bool IsControlEnabled(Control c) {
            return _input.IsEnabled(c);
        }

        public void SetEntityRecordsCollisions(int e, bool b) {
            _game.ConsoleEntities.RecordsCollisions(e, b);
        }

        public void SetEntityCollision(int e, bool b, bool b1) {
            _game.ConsoleEntities.Collision(e, b, b1);
        }

        public void SetEntityProofs(int e, bool b1, bool b2, bool b3, bool b4, bool b5, bool b6, bool b7,
            bool b8) {
            _game.ConsoleEntities.Proofs(e, b1, b2, b3, b4, b5, b6, b7, b8);
        }

        public void SetEntityLoadColissionFlag(int e, bool b) {
            _game.ConsoleEntities.LoadCollision(e, b);
        }

        public void SetEntityGravity(int e, bool b) {
            _game.ConsoleEntities.Gravity(e, b);
        }

        public float GetControlNormal(Control c) {
            return _input.Normal(c);
        }

        public int GetControlValue(Control c) {
            return _input.Value(c);
        }

        public bool IsControlPressedIgnoreDisabled(Control c) {
            return _input.IsDisabledPressed(c) ||
                   _input.IsPressed(c);
        }

        public void SetTextEdge(int i, Color c) {
            _game.ConsoleDrawing.TextEdge(i, c);
        }

        public void SetTextDropShadow(int i, Color c) {
            _game.ConsoleDrawing.TextShadow(i, c);
        }

        public Quaternion GetEntityQuaternion(int e) {
            return _game.Outputs.EntityQuaternion(e);
        }

        public void DrawLine(Vector3 v1, Vector3 v2, Color c) {
            _game.Drawing.DrawLine(v1, v2, c);
        }

        public void DrawBox(Vector3 v1, Vector3 v2, Color c) {
            _game.Drawing.DrawBox(v1, v2, c);
        }

        public void DrawPoly(Vector3 v1, Vector3 v2, Vector3 v3, Color c) {
            _game.Drawing.DrawPoly(v1, v2, v3, c);
        }

        public void DrawText(string text, Point position, float scale, Color color, Font font) {
            _display.DrawText(text, position, scale, color, font);
        }

        public Vector2 WorldToScreen(Vector3 world) {
            float x, y;
            _game.Outputs.WorldToScreen(world, out x, out y);
            return new Vector2(x * _display.Width, y * _display.Height);
        }
        public void DisplayRadar(bool b) {
            _game.ConsoleDrawing.DisplayRadar(b);
        }

        public void DisplayHud(bool b) {
            _game.ConsoleDrawing.DisplayHud(b);
        }

        public void ShowMouseThisFrame() {
            _game.ConsoleDrawing.ShowMouse();
        }

        public int GetNumNetworkPlayers() {
            return _network.PlayerCount();
        }

        public bool GetPlayerInvincible(int p) {
            return _game.ConsolePlayers.Invincible(p);
        }

        public int GetFirstBlipInfoID(int i) {
            return _world.FirstBlip(i);
        }

        public bool IsWaypointActive() {
            return _world.WaypointActive();
        }

        public Vector3 GetGroundPos(Vector2 pos) {
            float height;
            if (!_game.World.GetGroundHeight(new Vector3(pos.X, pos.Y, 1000.0f), out height))
                throw new InvalidOperationException("Ground collision is not loaded at that location.");
            return new Vector3(pos.X, pos.Y, height + 1);
        }

        public Vector3 GetCoordsFromCam(int distance) {
            var rot = _game.Cameras.GameplayRotation;
            var coord = _game.Cameras.GameplayPosition;

            var tZ = rot.Z*0.0174532924f;
            var tX = rot.X*0.0174532924f;
            var num = Math.Abs((float) Math.Cos(tX));

            coord.X = coord.X + (float) (-Math.Sin(tZ))*(num + distance);
            coord.Y = coord.Y + (float) (Math.Cos(tZ))*(num + distance);
            coord.Z = coord.Z + (float) (Math.Sin(tX))*8;

            return coord;
        }

        public int GetPlayerEntity(int player) {
            var character = _game.ConsolePlayers.Ped(player);
            if (_game.ConsolePlayers.InAnyVehicle(character) ||
                _game.ConsolePlayers.SittingInAnyVehicle(character))
                return _game.ConsolePlayers.Vehicle(character);
            return character;
        }
        public void KickPlayer(int p) {
            _network.Kick(p);
        }

        public void AntiBan() {
            var playerGroup = GetPedGroupIndex(_game.Host.PlayerPed);
            _network.MarkScriptSafe();
            _network.RegisterScript();
            StatSetInt("MP" + playerGroup + "_BAD_SPORT_BITSET", 0, true);
            StatSetInt("MP" + playerGroup + "_CHEAT_BITSET", 0, true);
            StatSetFloat("MPPLY_OVERALL_BADSPORT", 0, true);
            StatSetBool("MPPLY_CHAR_IS_BADSPORT", false, true);
            StatSetInt("MPPLY_BECAME_BADSPORT_NUM", 0, true);
            StatSetInt("MPPLY_REPORT_STRENGTH", 32, true);
            StatSetInt("MPPLY_COMMEND_STRENGTH", 100, true);
            StatSetInt("MPPLY_FRIENDLY", 100, true);
            StatSetInt("MPPLY_HELPFUL", 100, true);
            StatSetInt("MPPLY_GRIEFING", 0, true);
            StatSetInt("MPPLY_OFFENSIVE_LANGUAGE", 0, true);
            StatSetInt("MPPLY_OFFENSIVE_UGC", 0, true);
            StatSetInt("MPPLY_VC_HATE", 0, true);
            StatSetInt("MPPLY_GAME_EXPLOITS", 0, true);
            StatSetInt("MPPLY_ISPUNISHED", 0, true);
        }

        public void SetEntityInvinc(int e, bool b) {
            _game.ConsoleEntities.Invincible(e, b);
        }

        public void SetSPInvinc(int p, bool b) {
            _game.ConsolePlayers.SetInvincible(p, b);
        }

        public void SetInvincTime(int time) {
            _network.LocalInvincibleTime(time);
        }

        public int GetPedGroupIndex(int p) {
            return _game.ConsolePlayers.Group(p);
        }

        public void StatSetInt(string hashKey, int value, bool b) {
            _game.ConsoleStats.SetInt(GetHashKey(hashKey), value, b);
        }

        public void StatSetFloat(string hashKey, float value, bool b) {
            _game.ConsoleStats.SetFloat(GetHashKey(hashKey), value, b);
        }

        public void StatSetBool(string hashKey, bool value, bool b) {
            _game.ConsoleStats.SetBool(GetHashKey(hashKey), value, b);
        }

        public void CreateAmbientPickup(string hashKey, Vector3 pos, int value) {
            _world.AmbientPickup(GetHashKey(hashKey), pos, value);
        }

        public int GetHashKey(string s) {
            return _world.Hash(s);
        }

        public void SetTextRightJustifty(bool b) {
            _game.ConsoleDrawing.TextRightJustify(b);
        }

        public float GetTextWidth(string s, Font f, float scale) {
            _game.ConsoleDrawing.TextFont(f);
            _game.ConsoleDrawing.TextScale(scale, scale);
            _game.ConsoleDrawing.BeginTextWidth(); //SET_TEXT_ENTRY_FOR_WIDTH
            _game.ConsoleDrawing.AppendText(s);
            return _game.ConsoleDrawing.EndTextWidth(); //_GET_TEXT_SCREEN_WIDTH
        }


        public void EnableControls(List<Control> ctrls) {
            foreach (var c in ctrls) EnableControlAction(c, true);
        }

        public List<Control> DisableAllControls() {
            var enabled = CaptureEnabledControls();
            try {
                foreach (Control c in Enum.GetValues(typeof(Control))) DisableControlAction(c, true);
            } catch (Exception error) {
                try { RestoreControls(enabled); }
                catch (Exception restoreError) {
                    throw new AggregateException("Console operations failed.", error, restoreError);
                }
                throw;
            }
            return enabled;
        }

        public List<Control> CaptureEnabledControls() {
            RestorePendingControls();
            return Enum.GetValues(typeof(Control)).Cast<Control>().Where(IsControlEnabled).ToList();
        }

        public void RestoreControls(ICollection<Control> enabled) {
            RestoreStates(Enum.GetValues(typeof(Control)).Cast<Control>()
                .Select(control => new KeyValuePair<Control, bool>(control, enabled.Contains(control))));
        }

        public void RestorePendingControls() {
            RestoreStates(_pendingControls.ToArray());
        }

        private void RestoreStates(IEnumerable<KeyValuePair<Control, bool>> states) {
            var failures = new List<Exception>();
            foreach (var state in states) {
                try {
                    SetControlAction(state.Key, state.Value);
                    _pendingControls.Remove(state.Key);
                } catch (Exception error) {
                    _pendingControls[state.Key] = state.Value;
                    failures.Add(error);
                }
            }
            ThrowFailures(failures);
        }

        public void DisableControlAction(Control c, bool b) {
            _input.Disable(c, b);
        }

        public void EnableControlAction(Control c, bool b) {
            _input.Enable(c, b);
        }

        public void SetControlActions(bool b) {
            if (b) _input.EnableAll();
            else _input.DisableAll();
        }

        public void ClearAreaOfObjects(Vector3 v, float radius, bool b = false) {
            _world.ClearObjects(v, radius, b);
        }

        public void ClearAreaOfProjectiles(Vector3 v, float radius, bool b = false) {
            _world.ClearProjectiles(v, radius, b);
        }

        public void ActivateDamageTrackerOnNetworkId(int id, bool active) {
            _network.TrackDamage(id, active);
        }

        public void SetInSpectatorMode(int p, bool spectating) {
            _network.Spectate(_game.ConsolePlayers.Ped(p), spectating);
        }

        public int GetNetworkID(int p) {
            int entity = RequestPlayerEntityControl(p, 1);
            if (entity == 0) return 0;
            int netId = _network.NetworkId(entity);
            return _network.RequestNetworkIdControl(netId) ? netId : 0;
        }

        /// <summary>Creates a vehicle after observing its requested model as loaded.</summary>
        /// <remarks>A nonzero result transfers vehicle ownership to the caller even if releasing
        /// the streaming model fails. That model release remains owned here and is retried before
        /// another spawn, by ReleasePendingModel, or when the owning console is disposed.
        /// With no created vehicle, operation and release errors are reported together.</remarks>
        public int SpawnVehicleProper(int modelHash, Vector3 pos, int timeoutMs = 10000) {
            ReleasePendingModel();
            _world.RequestModel(modelHash);
            _pendingModel = modelHash;
            int vehicle = 0;
            var failures = new List<Exception>();
            try {
                vehicle = CreateVehicleWhenLoaded(modelHash, pos, timeoutMs);
            } catch (Exception error) { failures.Add(error); }
            try { ReleasePendingModel(); }
            catch (Exception error) {
                // Do not hide a created vehicle handle behind an unrelated model-release failure.
                if (vehicle == 0) failures.Add(error);
            }
            ThrowFailures(failures);
            return vehicle;
        }

        private int CreateVehicleWhenLoaded(int modelHash, Vector3 pos, int timeoutMs) {
            int started = _game.Host.GameTime;
            while (!_world.ModelLoaded(modelHash)) {
                if (unchecked(_game.Host.GameTime - started) >= timeoutMs) return 0;
                _game.Host.Wait(0);
            }
            return _world.CreateVehicle(modelHash, pos);
        }

        /// <summary>Retries the owned model release; failure retains it and preserves the exception.</summary>
        public void ReleasePendingModel() {
            if (!_pendingModel.HasValue) return;
            _world.ReleaseModel(_pendingModel.Value);
            _pendingModel = null;
        }

        public int RequestPlayerEntityControl(int player, int time = 10000) {
            return RequestEntityControl(_game.ConsolePlayers.Ped(player), time);
        }

        public int RequestEntityControl(int entity, int time = 10000) {
            if (entity == 0) return 0;
            _network.RequestEntityControl(entity);
            int started = _game.Host.GameTime;
            while (!_network.HasEntityControl(entity)) {
                if (unchecked(_game.Host.GameTime - started) >= time) return 0;
                _game.Host.Wait(0);
            }
            return entity;
        }
    }
}
