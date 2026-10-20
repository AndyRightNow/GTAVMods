using System;
using System.Collections.Generic;
using System.Drawing;
using GTA;
using GTA.Math;
using GTA.UI;
using GTA.Native;
using Font = GTA.UI.Font;
using GTAVMods.Game;

namespace DeveloperConsole {
    public sealed class GTAFuncs {
        private readonly GameServices _game;
        private readonly IConsoleDisplay _display;
        public GTAFuncs(GameServices game, IConsoleDisplay display) {
            _game = game;
            _display = display;
        }
        public enum EntityType {
            Ped = 1,
            Vehicle = 2,
            Prop = 3
        }

        public EntityType GetEntityType(int e) {
            return (EntityType) _game.Native.Call<int>(Hash.GET_ENTITY_TYPE, e);
        }

        public int GetPlayerIndex(int p) {
            return _game.Native.Call<int>(Hash.NETWORK_GET_PLAYER_INDEX, p);
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
            var x = IsControlEnabled(Control.CursorX);
            var y = IsControlEnabled(Control.CursorY);

            SetControlAction(Control.CursorX, true);
            SetControlAction(Control.CursorY, true);

            try {
                return new Point((int)(GetControlNormal(Control.CursorX) * _display.Width),
                    (int)(GetControlNormal(Control.CursorY) * _display.Height));
            } finally {
                SetControlAction(Control.CursorX, x);
                SetControlAction(Control.CursorY, y);
            }
        }

        public bool IsControlPressed(Control c) {
            var e = IsControlEnabled(c);
            SetControlAction(c, true);
            try { return GetControlNormal(c) > 0; }
            finally { SetControlAction(c, e); }
        }

        public Vector2 GetControlPoint(Control c1, Control c2) {
            var b1 = IsControlEnabled(c1);
            var b2 = IsControlEnabled(c2);

            SetControlAction(c1, true);
            SetControlAction(c2, true);

            try { return new Vector2(GetControlNormal(c1), GetControlNormal(c2)); }
            finally {
                SetControlAction(c1, b1);
                SetControlAction(c2, b2);
            }
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
            return _game.Native.Call<bool>(Hash.IS_CONTROL_ENABLED, 0, (int) c);
        }

        public void SetEntityRecordsCollisions(int e, bool b) {
            _game.Native.Call(Hash.SET_ENTITY_RECORDS_COLLISIONS, e, b);
        }

        public void SetEntityCollision(int e, bool b, bool b1) {
            _game.Native.Call(Hash.SET_ENTITY_COLLISION, e, b, b1);
        }

        public void SetEntityProofs(int e, bool b1, bool b2, bool b3, bool b4, bool b5, bool b6, bool b7,
            bool b8) {
            _game.Native.Call(Hash.SET_ENTITY_PROOFS, e, b1, b2, b3, b4, b5, b6, b7, b8);
        }

        public void SetEntityLoadColissionFlag(int e, bool b) {
            _game.Native.Call(Hash.SET_ENTITY_LOAD_COLLISION_FLAG, e, b);
        }

        public void SetEntityGravity(int e, bool b) {
            _game.Native.Call(Hash.SET_ENTITY_HAS_GRAVITY, e, b);
        }

        public float GetControlNormal(Control c) {
            return _game.Native.Call<float>(Hash.GET_CONTROL_NORMAL, 0, (int) c);
        }

        public int GetControlValue(Control c) {
            return _game.Native.Call<int>(Hash.GET_CONTROL_VALUE, 0, (int) c);
        }

        public bool IsControlPressedIgnoreDisabled(Control c) {
            return _game.Native.Call<bool>(Hash.IS_DISABLED_CONTROL_PRESSED, 0, (int) c) ||
                   _game.Native.Call<bool>(Hash.IS_CONTROL_PRESSED, 0, (int) c);
        }

        public void SetTextEdge(int i, Color c) {
            _game.Native.Call(Hash.SET_TEXT_EDGE, i, c.R, c.G, c.B, c.A);
        }

        public void SetTextDropShadow(int i, Color c) {
            _game.Native.Call(Hash.SET_TEXT_DROP_SHADOW, i, c.R, c.G, c.B, c.A);
        }

        public Quaternion GetEntityQuaternion(int e) {
            return _game.Outputs.EntityQuaternion(e);
        }

        public void DrawLine(Vector3 v1, Vector3 v2, Color c) {
            _game.Native.Call(Hash.DRAW_LINE, v1.X, v1.Y, v1.Z, v2.X, v2.Y, v2.Z, c.R, c.G, c.B, c.A);
        }

        public void DrawBox(Vector3 v1, Vector3 v2, Color c) {
            _game.Native.Call(Hash.DRAW_BOX, v1.X, v1.Y, v1.Z, v2.X, v2.Y, v2.Z, c.R, c.G, c.B, c.A);
        }

        public void DrawPoly(Vector3 v1, Vector3 v2, Vector3 v3, Color c) {
            _game.Native.Call(Hash.DRAW_POLY, v1.X, v1.Y, v1.Z, v2.X, v2.Y, v2.Z, v3.X, v3.Y, v3.Z, c.R, c.G, c.B, c.A);
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
            _game.Native.Call(Hash.DISPLAY_RADAR, b);
        }

        public void DisplayHud(bool b) {
            _game.Native.Call(Hash.DISPLAY_HUD, b);
        }

        public void ShowMouseThisFrame() {
            _game.Native.Call(Hash.SET_MOUSE_CURSOR_THIS_FRAME);
        }

        public int GetNumNetworkPlayers() {
            return _game.Native.Call<int>(Hash.NETWORK_GET_NUM_CONNECTED_PLAYERS);
        }

        public bool GetPlayerInvincible(int p) {
            return _game.Native.Call<bool>(Hash.GET_PLAYER_INVINCIBLE, p);
        }

        public int GetFirstBlipInfoID(int i) {
            return _game.Native.Call<int>(Hash.GET_FIRST_BLIP_INFO_ID, i);
        }

        public bool IsWaypointActive() {
            return _game.Native.Call<bool>(Hash.IS_WAYPOINT_ACTIVE);
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
            var character = _game.Native.Call<int>(Hash.GET_PLAYER_PED, player);
            if (_game.Native.Call<bool>(Hash.IS_PED_IN_ANY_VEHICLE, character, false) ||
                _game.Native.Call<bool>(Hash.IS_PED_SITTING_IN_ANY_VEHICLE, character))
                return _game.Native.Call<int>(Hash.GET_VEHICLE_PED_IS_IN, character, false);
            return character;
        }
        public void KickPlayer(int p) {
            _game.Native.Call(Hash.NETWORK_SESSION_KICK_PLAYER, p);
        }

        public void AntiBan() {
            var playerGroup = GetPedGroupIndex(_game.Host.PlayerPed);
            _game.Native.Call(Hash.NETWORK_SET_SCRIPT_IS_SAFE_FOR_NETWORK_GAME);
            _game.Native.Call(Hash.NETWORK_SET_THIS_SCRIPT_IS_NETWORK_SCRIPT, 1, true, 1);
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
            _game.Native.Call(Hash.SET_ENTITY_INVINCIBLE, e, b);
        }

        public void SetSPInvinc(int p, bool b) {
            _game.Native.Call(Hash.SET_PLAYER_INVINCIBLE, p, b);
        }

        public void SetInvincTime(int time) {
            _game.Native.Call(Hash.NETWORK_SET_LOCAL_PLAYER_INVINCIBLE_TIME, time);
        }

        public int GetPedGroupIndex(int p) {
            return _game.Native.Call<int>(Hash.GET_PED_GROUP_INDEX, p);
        }

        public void StatSetInt(string hashKey, int value, bool b) {
            _game.Native.Call(Hash.STAT_SET_INT, GetHashKey(hashKey), value, b);
        }

        public void StatSetFloat(string hashKey, float value, bool b) {
            _game.Native.Call(Hash.STAT_SET_FLOAT, GetHashKey(hashKey), value, b);
        }

        public void StatSetBool(string hashKey, bool value, bool b) {
            _game.Native.Call(Hash.STAT_SET_BOOL, GetHashKey(hashKey), value, b);
        }

        public void CreateAmbientPickup(string hashKey, Vector3 pos, int value) {
            _game.Native.Call(Hash.CREATE_AMBIENT_PICKUP, GetHashKey(hashKey), pos.X, pos.Y, pos.Z, 0, value, 1, false, true);
        }

        public int GetHashKey(string s) {
            return _game.Native.Call<int>(Hash.GET_HASH_KEY, s);
        }

        public void SetTextRightJustifty(bool b) {
            _game.Native.Call(Hash.SET_TEXT_RIGHT_JUSTIFY, b);
        }

        public float GetTextWidth(string s, Font f, float scale) {
            _game.Native.Call(Hash.SET_TEXT_FONT, (int) f);
            _game.Native.Call(Hash.SET_TEXT_SCALE, scale, scale);
            _game.Native.Call(Hash.BEGIN_TEXT_COMMAND_GET_SCREEN_WIDTH_OF_DISPLAY_TEXT, "STRING"); //SET_TEXT_ENTRY_FOR_WIDTH
            _game.Native.Call(Hash.ADD_TEXT_COMPONENT_SUBSTRING_PLAYER_NAME, s);
            return _game.Native.Call<float>(Hash.END_TEXT_COMMAND_GET_SCREEN_WIDTH_OF_DISPLAY_TEXT, 1); //_GET_TEXT_SCREEN_WIDTH
        }


        public void EnableControls(List<Control> ctrls) {
            foreach (var c in ctrls) EnableControlAction(c, true);
        }

        public List<Control> DisableAllControls() {
            var enabled = new List<Control>();
            foreach (Control c in Enum.GetValues(typeof (Control))) {
                if (IsControlEnabled(c)) enabled.Add(c);
                DisableControlAction(c, true);
            }
            return enabled;
        }


        public void DisableControlAction(Control c, bool b) {
            _game.Native.Call(Hash.DISABLE_CONTROL_ACTION, 0, (int) c, b);
        }

        public void EnableControlAction(Control c, bool b) {
            _game.Native.Call(Hash.ENABLE_CONTROL_ACTION, 0, (int) c, b);
        }

        public void SetControlActions(bool b) {
            if (b) _game.Native.Call(Hash.ENABLE_ALL_CONTROL_ACTIONS, 0);
            else _game.Native.Call(Hash.DISABLE_ALL_CONTROL_ACTIONS, 0);
        }

        public void ClearAreaOfObjects(Vector3 v, float radius, bool b = false) {
            _game.Native.Call(Hash.CLEAR_AREA_OF_OBJECTS, v.X, v.Y, v.Z, radius, b);
        }

        public void ClearAreaOfProjectiles(Vector3 v, float radius, bool b = false) {
            _game.Native.Call(Hash.CLEAR_AREA_OF_PROJECTILES, v.X, v.Y, v.Z, radius, b);
        }

        public void ActivateDamageTrackerOnNetworkId(int id, bool active) {
            _game.Native.Call(Hash.ACTIVATE_DAMAGE_TRACKER_ON_NETWORK_ID, id, active);
        }

        public void SetInSpectatorMode(int p, bool spectating) {
            _game.Native.Call(Hash.NETWORK_SET_IN_SPECTATOR_MODE, spectating, _game.Native.Call<int>(Hash.GET_PLAYER_PED, p));
        }

        public int GetNetworkID(int p) {
            RequestPlayerEntityControl(p, 1);
            int netId = _game.Native.Call<int>(Hash.NETWORK_GET_NETWORK_ID_FROM_ENTITY,
                _game.Native.Call<int>(Hash.GET_PLAYER_PED, p));
            _game.Native.Call(Hash.NETWORK_REQUEST_CONTROL_OF_NETWORK_ID, netId);
            return netId;
        }

        public int SpawnVehicleProper(int modelHash, Vector3 pos) {
            _game.Native.Call(Hash.REQUEST_MODEL, modelHash);
            var remaining = 10000;
            while (!_game.Native.Call<bool>(Hash.HAS_MODEL_LOADED, modelHash) && remaining > 0) {
                remaining--;
                _game.Host.Wait(0);
            }
            return _game.Native.Call<int>(Hash.CREATE_VEHICLE, modelHash, pos.X, pos.Y, pos.Z, 0, 0);
        }

        public int RequestPlayerEntityControl(int player, int time = 10000) {
            return RequestEntityControl(_game.Native.Call<int>(Hash.GET_PLAYER_PED, player), time);
        }

        public int RequestEntityControl(int entity, int time = 10000) {
            _game.Native.Call(Hash.NETWORK_REQUEST_CONTROL_OF_ENTITY, entity);
            var remaining = time;
            while (!_game.Native.Call<bool>(Hash.NETWORK_HAS_CONTROL_OF_ENTITY, entity) && remaining > 0) {
                remaining--;
                _game.Host.Wait(0);
            }
            return entity;
        }
    }
}
