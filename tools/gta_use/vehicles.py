"""Bounded ordinary vehicle entry, exit and steering recovery for policies."""
import math
import time

from ._validation import _bounds, _identity
from .client import UncertainOutcome


def _observe(ctx, *, nearby=False, radius=55, minimum_health=100):
    filters = dict(sections=["player", "vehicle"])
    if nearby:
        filters.update(sections=["player", "vehicle", "nearby"], types=["vehicle"],
                       radius=radius, limit=128, lineOfSight="off")
    state = ctx.check(**filters)
    player = state["sections"].get("player")
    session = state["session"]
    if (not player or session["pauseMenu"] or session["cutscenePlaying"]
            or not session["playerControl"] or player["entity"]["health"] < minimum_health):
        ctx.uncertain("Vehicle operation interrupted by player health, control or menu transition.")
    return state, player, state["sections"].get("vehicle")


def acquire_vehicle(ctx, desired=None, *, passenger=False, companion=None, radius=55,
                    max_height_difference=4, max_attempts=4, entry_timeout=28,
                    minimum_health=100, check_interval=.3):
    """Enter an eligible nearby vehicle through ordinary entry/carjacking.

    desired/companion are freshly observed {handle, model} entities, never bare
    stale handles. Prefer stationary vehicles, then distance. A passenger seat
    may already contain the identified companion. No lock or occupant is changed.
    Returns only after observing the requested vehicle's actual driver seat.
    """
    if not isinstance(passenger, bool) or isinstance(max_attempts, bool) or not isinstance(max_attempts, int) or not 1 <= max_attempts <= 8:
        raise ValueError("Use a boolean passenger option and 1..8 entry attempts.")
    _bounds(radius=(radius, 1, 100), max_height_difference=(max_height_difference, .1, 20),
            entry_timeout=(entry_timeout, .2, 60), check_interval=(check_interval, .05, 2),
            minimum_health=(minimum_health, 1, 10000))
    wanted = _identity(desired) if desired is not None else None
    ally = _identity(companion) if companion is not None else None
    rejected, player_identity = set(), None
    for attempt in range(max_attempts):
        state, player, current = _observe(ctx, nearby=True, radius=radius, minimum_health=minimum_health)
        identity = _identity(player["entity"])
        if player_identity is not None and identity != player_identity:
            ctx.uncertain("Player identity changed during vehicle entry.")
        player_identity = identity
        if player["inVehicle"]:
            if current and current["driver"] == identity[0] and (wanted is None or _identity(current["entity"]) == wanted):
                if not current["driveable"]:
                    ctx.uncertain("Current vehicle is not driveable; exit and inspect another vehicle.")
                return current["entity"]
            ctx.uncertain("Exit the current vehicle before acquiring a different vehicle or driver seat.")
        candidates = [n["entity"] for n in state["sections"]["nearby"]["nodes"]
                      if n["kind"] == "vehicle" and not n["entity"]["dead"]
                      and _identity(n["entity"]) not in rejected
                      and (wanted is None or _identity(n["entity"]) == wanted)
                      and n["entity"]["relative"]["distance"] < radius
                      and abs(n["entity"]["position"]["z"] - player["entity"]["position"]["z"]) < max_height_difference]
        candidates.sort(key=lambda e: (math.hypot(e["velocity"]["x"], e["velocity"]["y"]) > 1,
                                       e["relative"]["distance"]))
        # All candidate eligibility checks run in one bridge call. Source is
        # reused by the bridge compiler cache while the observed set is unchanged.
        pairs = ",".join("new int[]{%d,%d}" % _identity(e) for e in candidates)
        observations = ctx.game.command("/* gta-use:vehicle-candidates */ " + """
            var result=new System.Collections.Generic.List<object>();
            foreach(var id in new int[][]{PAIRS}) {
                var v=Entity.FromHandle(id[0]) as Vehicle;
                if(v==null || !v.Exists()) continue;
                int occupant=Function.Call<int>(Hash.GET_PED_IN_VEHICLE_SEAT,v.Handle,0,false);
                var ped=Entity.FromHandle(occupant) as Ped;
                result.Add(new {handle=v.Handle, model=v.Model.Hash, driveable=v.IsDriveable,
                    locked=v.LockStatus.ToString(), passenger=v.IsSeatFree(VehicleSeat.Passenger),
                    occupant=occupant, occupantModel=ped!=null && ped.Exists() ? ped.Model.Hash : 0,
                    occupantAlive=ped!=null && ped.Exists() && !ped.IsDead});
            }
            return result;
        """.replace("PAIRS", pairs)) if candidates else []
        eligible = {o["handle"]: o for o in observations}
        car = None
        for candidate in candidates:
            observed = eligible.get(candidate["handle"])
            if (observed and _identity(observed) == _identity(candidate) and observed["driveable"]
                    and observed["locked"] in ("None", "Unlocked", "CanBeBrokenInto")
                    and (not passenger or observed["passenger"] or
                         (ally and observed["occupantAlive"] and (observed["occupant"], observed["occupantModel"]) == ally))):
                car = candidate
                break
        if car is None:
            ctx.uncertain("No nearby eligible road vehicle; inspect access or choose an observed route.")
        ctx.log("acquire-vehicle", vehicle=car, attempt=attempt)
        ctx.game.action("resume")
        owns_task = False
        try:
            ctx.game.command(('/* gta-use:enter-vehicle */ var v=Entity.FromHandle(%d) as Vehicle; '
                'if(v==null || !v.Exists() || v.Model.Hash!=%d || P.Handle!=%d || P.Model.Hash!=%d) '
                'throw new Exception("Vehicle entry identity changed"); '
                'P.Task.EnterVehicle(v,VehicleSeat.Driver,-1,2f,EnterVehicleFlags.ResumeIfInterupted);') %
                (*_identity(car), *identity))
            owns_task = True
            deadline = time.monotonic() + entry_timeout
            last_sample = 0
            while time.monotonic() < deadline:
                state, player, vehicle = _observe(ctx, minimum_health=minimum_health)
                if _identity(player["entity"]) != identity:
                    owns_task = False
                    ctx.uncertain("Player identity changed during vehicle entry.")
                if player["inVehicle"]:
                    owns_task = False
                    if vehicle and _identity(vehicle["entity"]) == _identity(car) and vehicle["driver"] == identity[0]:
                        ctx.progress("Verified driver entry", vehicle=vehicle["entity"])
                        return vehicle["entity"]
                    ctx.uncertain("Vehicle entry reached a different vehicle or seat.")
                if time.monotonic() - last_sample >= 1:
                    ctx.log("vehicle-entry-observation", vehicle=car["handle"], position=player["entity"]["position"])
                    last_sample = time.monotonic()
                time.sleep(check_interval)
        except UncertainOutcome:
            owns_task = False
            raise
        finally:
            if owns_task:
                # Revalidate in the game thread; never cancel a mission's task.
                ctx.game.command(('/* gta-use:clear-entry */ if(P.Handle==%d && P.Model.Hash==%d && '
                    'PLAYER.CanControlCharacter && !Function.Call<bool>(Hash.IS_CUTSCENE_PLAYING) && '
                    '!Function.Call<bool>(Hash.IS_PAUSE_MENU_ACTIVE)) P.Task.ClearAll();') % identity)
        rejected.add(_identity(car))
        if wanted is not None:
            ctx.uncertain("Required vehicle entry failed within its deadline.")
    ctx.uncertain("Eligible vehicle entry attempts exhausted.")


def leave_vehicle(ctx, *, timeout=15, minimum_health=100, check_interval=.2):
    """Brake and leave normally; do not claim success on task acceptance."""
    _bounds(timeout=(timeout, .2, 60), minimum_health=(minimum_health, 1, 10000),
            check_interval=(check_interval, .05, 2))
    state, player, vehicle = _observe(ctx, minimum_health=minimum_health)
    if not player["inVehicle"]:
        return state
    identity = _identity(player["entity"])
    ctx.game.action("resume")
    ctx.game.native("CLEAR_PED_TASKS", identity[0])
    if vehicle and vehicle["driver"] == identity[0]:
        ctx.game.action("controls", ms=600, inputs=[dict(control="VehicleHandbrake", value=1)])
    ctx.game.command("/* gta-use:leave-vehicle */ P.Task.LeaveVehicle(LeaveVehicleFlags.DontDefaultWarpIfDoorBlocked);")
    deadline = time.monotonic() + timeout
    while time.monotonic() < deadline:
        state, player, _ = _observe(ctx, minimum_health=minimum_health)
        if _identity(player["entity"]) != identity:
            ctx.uncertain("Player identity changed during vehicle exit.")
        if not player["inVehicle"]:
            ctx.progress("Observed vehicle exit", position=player["entity"]["position"])
            return state
        time.sleep(check_interval)
    ctx.uncertain("Ordinary vehicle exit exceeded its deadline.")


def recover_upright(ctx, *, attempts=12, minimum_health=100):
    """Try ordinary steering while rolled; never rotate or teleport the vehicle."""
    if isinstance(attempts, bool) or not isinstance(attempts, int) or not 1 <= attempts <= 20:
        raise ValueError("Use 1..20 bounded steering attempts.")
    _bounds(minimum_health=(minimum_health, 1, 10000))
    identity = None
    for attempt in range(attempts + 1):
        state, player, vehicle = _observe(ctx, minimum_health=minimum_health)
        current = (*_identity(player["entity"]), *_identity(vehicle["entity"])) if vehicle else None
        if not vehicle or not player["inVehicle"] or vehicle["driver"] != player["entity"]["handle"] or not vehicle["driveable"]:
            ctx.uncertain("Steering recovery requires the observed driver of a driveable vehicle.")
        if identity is not None and current != identity:
            ctx.uncertain("Vehicle identity changed during steering recovery.")
        identity = current
        if vehicle["onAllWheels"]:
            if attempt:
                ctx.progress("Observed vehicle back on wheels", vehicle=vehicle["entity"])
            return state
        if attempt == attempts:
            break
        ctx.log("vehicle-roll-recovery", attempt=attempt, vehicle=vehicle)
        if attempt == 0:
            ctx.game.action("resume")
        ctx.game.action("controls", ms=500, inputs=[dict(control="VehicleMoveLeftRight",
                         value=1 if vehicle["entity"]["rotation"]["y"] > 0 else -1)])
    ctx.uncertain("Vehicle did not recover upright within its steering allowance.")
