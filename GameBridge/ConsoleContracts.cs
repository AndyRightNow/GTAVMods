using System.Drawing;
using GTA;
using GTA.Math;
using Font = GTA.UI.Font;

namespace GTAVMods.Game
{
    /// <summary>Player gameplay controls in input group zero.</summary>
    /// <remarks>Normal preserves signed fractions; Value is the integer device reading.
    /// Enable/Disable issue frame-scoped engine changes. Reading or writing may throw;
    /// callers owning temporary changes must retain their captured state until restoration succeeds.</remarks>
    public interface IConsoleInput
    {
        bool IsEnabled(Control control);
        float Normal(Control control);
        int Value(Control control);
        bool IsPressed(Control control);
        bool IsDisabledPressed(Control control);
        void Enable(Control control, bool enabled);
        void Disable(Control control, bool disabled);
        void EnableAll();
        void DisableAll();
    }

    /// <summary>Frame-scoped HUD and text commands.</summary>
    /// <remarks>Colors use RGBA byte channels. Text width is a normalized screen fraction,
    /// computed after font/scale, BeginTextWidth, and AppendText. Commands do not confirm rendering.</remarks>
    public interface IConsoleDrawing
    {
        void TextEdge(int size, Color color);
        void TextShadow(int distance, Color color);
        void TextRightJustify(bool enabled);
        void TextFont(Font font);
        void TextScale(float horizontal, float vertical);
        void BeginTextWidth();
        void AppendText(string text);
        float EndTextWidth();
        void DisplayRadar(bool visible);
        void DisplayHud(bool visible);
        void ShowMouse();
    }

    /// <summary>Entity physics flags for a nonzero engine handle.</summary>
    /// <remarks>Handles remain owned by the caller. These commands neither create nor delete
    /// entities; they issue changes and do not guarantee an entity remains present afterward.</remarks>
    public interface IConsoleEntities
    {
        int Type(int entity);
        void RecordsCollisions(int entity, bool enabled);
        void Collision(int entity, bool enabled, bool keepPhysics);
        void Proofs(int entity, bool bullet, bool fire, bool explosion, bool collision,
            bool melee, bool steam, bool unknown, bool drown);
        void LoadCollision(int entity, bool enabled);
        void Gravity(int entity, bool enabled);
        void Invincible(int entity, bool enabled);
    }

    /// <summary>Player and ped identity observations.</summary>
    /// <remarks>Player indices and entity handles are distinct. Ped/Vehicle return zero when
    /// absent. InAnyVehicle and SittingInAnyVehicle retain separate engine observations.</remarks>
    public interface IConsolePlayers
    {
        int Ped(int player);
        int Index(int ped);
        bool Invincible(int player);
        void SetInvincible(int player, bool enabled);
        bool InAnyVehicle(int ped);
        bool SittingInAnyVehicle(int ped);
        int Vehicle(int ped);
        int Group(int ped);
    }

    /// <summary>Model streaming, local vehicle creation, and world utility commands.</summary>
    /// <remarks>Positions/radii are world meters. RequestModel begins a streaming reference;
    /// ModelLoaded confirms readiness and ReleaseModel relinquishes the caller's reference.
    /// CreateVehicle requires a loaded model and returns a caller-owned local vehicle
    /// or zero on failure, facing zero degrees. AmbientPickup uses its default model.</remarks>
    public interface IConsoleWorld
    {
        int Hash(string name);
        int FirstBlip(int type);
        bool WaypointActive();
        void ClearObjects(Vector3 position, float radius, bool flag);
        void ClearProjectiles(Vector3 position, float radius, bool flag);
        void AmbientPickup(int hash, Vector3 position, int value);
        // Request/Loaded/Release express ownership separately. Create must only be used
        // after Loaded succeeds. A zero result means creation failed.
        void RequestModel(int model);
        bool ModelLoaded(int model);
        void ReleaseModel(int model);
        int CreateVehicle(int model, Vector3 position);
    }

    /// <summary>Network identity and control request primitives.</summary>
    /// <remarks>A request's bool is engine acceptance, not durable ownership. Poll
    /// HasEntityControl before an operation that requires ownership. NetworkId is an ID,
    /// not an entity handle. LocalInvincibleTime is milliseconds. No operation advances time.</remarks>
    public interface IConsoleNetwork
    {
        int PlayerCount();
        void Kick(int player);
        void LocalInvincibleTime(int milliseconds);
        void MarkScriptSafe();
        void RegisterScript();
        void TrackDamage(int networkId, bool enabled);
        void Spectate(int ped, bool enabled);
        // Request acceptance does not prove ownership; HasEntityControl is authoritative.
        bool RequestEntityControl(int entity);
        bool HasEntityControl(int entity);
        int NetworkId(int entity);
        bool RequestNetworkIdControl(int networkId);
    }

    /// <summary>Typed integer, floating-point, and boolean stat writes.</summary>
    /// <remarks>The hash identifies the stat; save is forwarded unchanged. These commands
    /// issue the write without treating its invocation as proof of remote persistence.</remarks>
    public interface IConsoleStats
    {
        void SetInt(int hash, int value, bool save);
        void SetFloat(int hash, float value, bool save);
        void SetBool(int hash, bool value, bool save);
    }
}
