using System.Drawing;
using GTA;
using GTA.Math;
using GTA.Native;
using Font = GTA.UI.Font;

namespace GTAVMods.Game
{
    public sealed class NativeConsoleInput : IConsoleInput
    {
        private readonly INativeInvoker native;
        public NativeConsoleInput(INativeInvoker native) { this.native = native; }
        public bool IsEnabled(Control control) => native.Call<bool>(Hash.IS_CONTROL_ENABLED, 0, (int)control);
        public float Normal(Control control) => native.Call<float>(Hash.GET_CONTROL_NORMAL, 0, (int)control);
        public int Value(Control control) => native.Call<int>(Hash.GET_CONTROL_VALUE, 0, (int)control);
        public bool IsPressed(Control control) => native.Call<bool>(Hash.IS_CONTROL_PRESSED, 0, (int)control);
        public bool IsDisabledPressed(Control control) => native.Call<bool>(Hash.IS_DISABLED_CONTROL_PRESSED, 0, (int)control);
        public void Enable(Control control, bool enabled) => native.Call(Hash.ENABLE_CONTROL_ACTION, 0, (int)control, enabled);
        public void Disable(Control control, bool disabled) => native.Call(Hash.DISABLE_CONTROL_ACTION, 0, (int)control, disabled);
        public void DisableAll() => native.Call(Hash.DISABLE_ALL_CONTROL_ACTIONS, 0);
    }

    public sealed class NativeConsoleDrawing : IConsoleDrawing
    {
        private readonly INativeInvoker native;
        public NativeConsoleDrawing(INativeInvoker native) { this.native = native; }
        public void TextEdge(int size, Color color) => native.Call(Hash.SET_TEXT_EDGE, size, color.R, color.G, color.B, color.A);
        public void TextShadow(int distance, Color color) => native.Call(Hash.SET_TEXT_DROP_SHADOW, distance, color.R, color.G, color.B, color.A);
        public void TextRightJustify(bool enabled) => native.Call(Hash.SET_TEXT_RIGHT_JUSTIFY, enabled);
        public void TextFont(Font font) => native.Call(Hash.SET_TEXT_FONT, (int)font);
        public void TextScale(float horizontal, float vertical) => native.Call(Hash.SET_TEXT_SCALE, horizontal, vertical);
        public void BeginTextWidth() => native.Call(Hash.BEGIN_TEXT_COMMAND_GET_SCREEN_WIDTH_OF_DISPLAY_TEXT, "STRING");
        public void AppendText(string text) => native.Call(Hash.ADD_TEXT_COMPONENT_SUBSTRING_PLAYER_NAME, text);
        public float EndTextWidth() => native.Call<float>(Hash.END_TEXT_COMMAND_GET_SCREEN_WIDTH_OF_DISPLAY_TEXT, 1);
        public void DisplayRadar(bool visible) => native.Call(Hash.DISPLAY_RADAR, visible);
        public void DisplayHud(bool visible) => native.Call(Hash.DISPLAY_HUD, visible);
        public void ShowMouse() => native.Call(Hash.SET_MOUSE_CURSOR_THIS_FRAME);
    }

    public sealed class NativeConsoleEntities : IConsoleEntities
    {
        private readonly INativeInvoker native;
        public NativeConsoleEntities(INativeInvoker native) { this.native = native; }
        public int Type(int entity) => native.Call<int>(Hash.GET_ENTITY_TYPE, entity);
        public void RecordsCollisions(int entity, bool enabled) => native.Call(Hash.SET_ENTITY_RECORDS_COLLISIONS, entity, enabled);
        public void Collision(int entity, bool enabled, bool keepPhysics) => native.Call(Hash.SET_ENTITY_COLLISION, entity, enabled, keepPhysics);
        public void Proofs(int entity, bool bullet, bool fire, bool explosion, bool collision, bool melee, bool steam, bool unknown, bool drown)
            => native.Call(Hash.SET_ENTITY_PROOFS, entity, bullet, fire, explosion, collision, melee, steam, unknown, drown);
        public void LoadCollision(int entity, bool enabled) => native.Call(Hash.SET_ENTITY_LOAD_COLLISION_FLAG, entity, enabled);
        public void Gravity(int entity, bool enabled) => native.Call(Hash.SET_ENTITY_HAS_GRAVITY, entity, enabled);
        public void Invincible(int entity, bool enabled) => native.Call(Hash.SET_ENTITY_INVINCIBLE, entity, enabled);
    }

    public sealed class NativeConsolePlayers : IConsolePlayers
    {
        private readonly INativeInvoker native;
        public NativeConsolePlayers(INativeInvoker native) { this.native = native; }
        public int Ped(int player) => native.Call<int>(Hash.GET_PLAYER_PED, player);
        public int Index(int ped) => native.Call<int>(Hash.NETWORK_GET_PLAYER_INDEX, ped);
        public bool Invincible(int player) => native.Call<bool>(Hash.GET_PLAYER_INVINCIBLE, player);
        public void SetInvincible(int player, bool enabled) => native.Call(Hash.SET_PLAYER_INVINCIBLE, player, enabled);
        public bool InAnyVehicle(int ped) => native.Call<bool>(Hash.IS_PED_IN_ANY_VEHICLE, ped, false);
        public bool SittingInAnyVehicle(int ped) => native.Call<bool>(Hash.IS_PED_SITTING_IN_ANY_VEHICLE, ped);
        public int Vehicle(int ped) => native.Call<int>(Hash.GET_VEHICLE_PED_IS_IN, ped, false);
    }

    public sealed class NativeConsoleWorld : IConsoleWorld
    {
        // Argument contracts: pinned SHVDNE v1.1.0.6 source/scripting_v3/GTA/World.cs
        // https://github.com/Chiheb-Bacha/ScriptHookVDotNetEnhanced/blob/v1.1.0.6/source/scripting_v3/GTA/World.cs
        private readonly INativeInvoker native;
        public NativeConsoleWorld(INativeInvoker native) { this.native = native; }
        public int Hash(string name) => native.Call<int>(GTA.Native.Hash.GET_HASH_KEY, name);
        public int FirstBlip(int type) => native.Call<int>(GTA.Native.Hash.GET_FIRST_BLIP_INFO_ID, type);
        public bool WaypointActive() => native.Call<bool>(GTA.Native.Hash.IS_WAYPOINT_ACTIVE);
        public void ClearObjects(Vector3 position, float radius, bool flag)
            => native.Call(GTA.Native.Hash.CLEAR_AREA_OF_OBJECTS, position.X, position.Y, position.Z, radius, flag);
        public void ClearProjectiles(Vector3 position, float radius, bool flag)
            => native.Call(GTA.Native.Hash.CLEAR_AREA_OF_PROJECTILES, position.X, position.Y, position.Z, radius, flag);
        public void AmbientPickup(int hash, Vector3 position, int value)
            => native.Call(GTA.Native.Hash.CREATE_AMBIENT_PICKUP, hash, position.X, position.Y, position.Z, 0, value, 0, false, true);
        public void RequestModel(int model) => native.Call(GTA.Native.Hash.REQUEST_MODEL, model);
        public bool ModelLoaded(int model) => native.Call<bool>(GTA.Native.Hash.HAS_MODEL_LOADED, model);
        public void ReleaseModel(int model) => native.Call(GTA.Native.Hash.SET_MODEL_AS_NO_LONGER_NEEDED, model);
        public int CreateVehicle(int model, Vector3 position)
            => native.Call<int>(GTA.Native.Hash.CREATE_VEHICLE, model, position.X, position.Y, position.Z, 0f, false, false);
    }

    public sealed class NativeConsoleNetwork : IConsoleNetwork
    {
        private readonly INativeInvoker native;
        public NativeConsoleNetwork(INativeInvoker native) { this.native = native; }
        public bool RequestEntityControl(int entity) => native.Call<bool>(Hash.NETWORK_REQUEST_CONTROL_OF_ENTITY, entity);
        public bool HasEntityControl(int entity) => native.Call<bool>(Hash.NETWORK_HAS_CONTROL_OF_ENTITY, entity);
        public int NetworkId(int entity) => native.Call<int>(Hash.NETWORK_GET_NETWORK_ID_FROM_ENTITY, entity);
        public bool RequestNetworkIdControl(int networkId) => native.Call<bool>(Hash.NETWORK_REQUEST_CONTROL_OF_NETWORK_ID, networkId);
    }

}
