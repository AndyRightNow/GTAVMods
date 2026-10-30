"""Small identity-checked observations for actors with scripted movement/boarding."""
from ._validation import _identity


def observe_entity(game, target, *, relative_to=None):
    """Read a target and optional reference pose together on the game thread.

    Returns None when either supplied identity no longer matches. The result has
    position, velocity, health, dead, vehicle, attachedTo and relative (None when
    no reference is supplied). vehicle is ordinary seated occupancy; attachedTo
    is the direct engine parent and may instead explain scripted boarding.
    Neither a nearby pose nor attachment alone establishes a mission outcome.
    This read-only command does not resume, claim a lease or change any tasks.
    """
    handle, model = _identity(target)
    reference = ""
    relative = "(object)null"
    if relative_to is not None:
        ref_handle, ref_model = _identity(relative_to)
        reference = ("var reference=Entity.FromHandle(%d); "
                     "if(reference==null || !reference.Exists() || reference.Model.Hash!=%d) return null;") % (ref_handle, ref_model)
        relative = "(object)reference.GetPositionOffset(target.Position)"
    query = ('''/* gta-use:entity-observation */
        var target=Entity.FromHandle(%d);
        if(target==null || !target.Exists() || target.Model.Hash!=%d) return null;
        %s
        var ped=target as Ped;
        return new {position=target.Position, velocity=target.Velocity, health=target.Health,
            dead=target.IsDead, relative=%s,
            attachedTo=Function.Call<int>(Hash.GET_ENTITY_ATTACHED_TO,target.Handle),
            vehicle=ped==null ? 0 : Function.Call<int>(Hash.GET_VEHICLE_PED_IS_IN,ped.Handle,false)};
    ''') % (handle, model, reference, relative)
    result = game.command(query)
    if result is not None:
        result = dict(result, handle=handle, model=model)
    return result
