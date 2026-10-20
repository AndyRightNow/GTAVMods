using System;
using GTA;
using GTA.Native;

namespace GTAVMods.Game
{
    /// <summary>Raw handle existence observation and deletion request.</summary>
    /// <remarks>Used only by the measured lifetime adapter; Delete alone does not confirm absence.</remarks>
    public interface IHandleLifetimeBindings
    {
        bool Exists(int entity);
        void Delete(int entity);
    }

    // Domain-specific entry points prevent a camera or rope binding from being
    // supplied accidentally to the entity adapter, despite their shared wire type.
    public interface IEntityLifetimeBindings : IHandleLifetimeBindings { }
    public interface ICameraLifetimeBindings : IHandleLifetimeBindings { }
    public interface IRopeLifetimeBindings : IHandleLifetimeBindings { }

    /// <summary>Confirms absence before a caller may relinquish handle ownership.</summary>
    /// <remarks>
    /// A missing handle is already released. A present handle receives one deletion request followed by
    /// one existence observation, without yielding or retrying. Refusal or any host failure throws so
    /// callers retain ownership and decide when to retry.
    /// </remarks>
    public sealed class HandleLifetime
    {
        private readonly IHandleLifetimeBindings bindings;
        private readonly string resource;
        public HandleLifetime(IHandleLifetimeBindings bindings, string resource)
        { this.bindings = bindings; this.resource = resource; }
        public bool Exists(int entity) => bindings.Exists(entity);
        public void Delete(int entity)
        {
            if (!bindings.Exists(entity)) return;
            bindings.Delete(entity);
            if (bindings.Exists(entity))
                throw new InvalidOperationException(resource + " " + entity + " still exists after its deletion request.");
        }
    }

    public sealed class GtaEntityLifetimeBindings : IEntityLifetimeBindings
    {
        [GamePrimitive] public bool Exists(int entity) => Function.Call<bool>(Hash.DOES_ENTITY_EXIST, entity);
        [GamePrimitive] public void Delete(int entity) => Entity.FromHandle(entity).Delete();
    }

    public sealed class GtaCameraLifetimeBindings : ICameraLifetimeBindings
    {
        [GamePrimitive] public bool Exists(int camera) => new Camera(camera).Exists();
        [GamePrimitive] public void Delete(int camera) => new Camera(camera).Delete();
    }

    public sealed class GtaRopeLifetimeBindings : IRopeLifetimeBindings
    {
        [GamePrimitive] public bool Exists(int rope) => new Rope(rope).Exists();
        [GamePrimitive] public void Delete(int rope) => new Rope(rope).Delete();
    }
}
