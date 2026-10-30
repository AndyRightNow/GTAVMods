"""Supervised on-foot routing with bounded, observed climb/vault recovery."""
import math
import time

from .client import UncertainOutcome
from ._validation import _bounds, _identity


def _point(value):
    point = tuple(float(value[axis]) for axis in ("x", "y", "z"))
    if not all(math.isfinite(v) for v in point):
        raise ValueError("Navigation coordinates must be finite.")
    return point


def _float(value):
    return {"type": "float", "value": value}


def climb_toward(ctx, facing, *, timeout=7, minimum_health=100, check_interval=.2):
    """Turn toward an observed nearby edge, attempt one normal climb, observe landing.

    Caller supplies a reachable approach and a point beyond the edge, including
    stationary vehicle/roof edges. This never moves to the point, forces a pose,
    repeats a jump or claims arrival. Check outcome and final position before
    another action. Mission control loss returns transition without clearing its task.
    """
    point = _point(facing)
    _bounds(timeout=(timeout, 1, 20), minimum_health=(minimum_health, 1, 10000),
            check_interval=(check_interval, .05, 1))
    identity = None

    def observe():
        nonlocal identity
        state = ctx.check(sections=["player"])
        p, session = state["sections"].get("player"), state["session"]
        if not p or session["pauseMenu"] or p["inVehicle"]:
            ctx.uncertain("Climb interrupted by missing player, menu or vehicle transition.")
        current = _identity(p["entity"])
        if identity is not None and current != identity:
            ctx.uncertain("Player identity changed during climb.")
        identity = current
        if p["entity"]["health"] < minimum_health or p["swimming"]:
            ctx.uncertain("Climb stopped for health or water recovery.")
        return state, p, _point(p["entity"]["position"])

    def transition(state):
        return not state["session"]["playerControl"] or state["session"]["cutscenePlaying"]

    def settled(p):
        return not any(p[k] for k in ("climbing", "vaulting", "jumping", "falling", "inAir", "ragdoll"))

    state, player, before = observe()
    if transition(state) or not settled(player) or player["inCover"]:
        ctx.uncertain("Start a climb settled, controllable, on foot and out of cover.")
    horizontal = math.hypot(point[0]-before[0], point[1]-before[1])
    if not .2 <= horizontal <= 8 or abs(point[2]-before[2]) > 8:
        raise ValueError("Climb facing point must describe a nearby observed edge (within 8 m).")
    ctx.game.action("resume")
    ctx.game.native("TASK_TURN_PED_TO_FACE_COORD", identity[0], *map(_float, point), 650)
    turn_end = time.monotonic() + .85
    while time.monotonic() < turn_end:
        time.sleep(check_interval)
        state, player, position = observe()
        if transition(state):
            return dict(outcome="transition", state=state)
    angle = (math.degrees(math.atan2(point[0]-position[0], point[1]-position[1]))
             + player["entity"]["rotation"]["z"] + 180) % 360 - 180
    if not settled(player) or abs(angle) > 35:
        ctx.uncertain("Climb approach did not settle facing the observed edge.")
    ctx.game.native("TASK_CLIMB", identity[0], False)
    deadline = time.monotonic() + timeout
    moved = climbed = False
    stable_samples = 0
    while time.monotonic() < deadline:
        time.sleep(check_interval)
        state, player, position = observe()
        if transition(state):
            return dict(outcome="transition", state=state)
        climbed = climbed or player["climbing"] or player["vaulting"]
        moved = moved or not settled(player)
        stable_samples = stable_samples + 1 if settled(player) else 0
        if moved and stable_samples >= 2:
            result = dict(outcome="climbed" if climbed else "landed", position=dict(zip(("x", "y", "z"), position)),
                          displacement=math.dist(before, position), heightChange=position[2]-before[2], state=state)
            ctx.log("climb-observed", **{k: v for k, v in result.items() if k != "state"})
            if result["displacement"] >= .5:
                ctx.progress("Observed settled climb landing", position=position, climbed=climbed)
            return result
    if not settled(player):
        ctx.uncertain("Climb did not settle within its deadline; preserve the animation for inspection.")
    return dict(outcome="no-effect", position=dict(zip(("x", "y", "z"), position)), state=state)


def navigate_to(ctx, destination, *, waypoints=(), parkour=True, route_mode="navmesh", radius=.75,
                timeout=60, stall_timeout=2.5, max_climbs=3, climb_timeout=7,
                minimum_health=100, move_blend_ratio=2.5, check_interval=.2, allow_transition=False):
    """Run a short route, climbing nearby obstacles only when progress stalls.

    With allow_transition=True, an expected control/cutscene takeover after
    starting the route returns arrived=False and outcome="transition". It never
    clears mission-owned tasks or treats the takeover as arrival/completion.
    The caller must inspect the next objective or wait for control.

    Returns observed arrival data; it does not create a mission checkpoint or
    pause on success. Explicit waypoints should describe the intended route,
    including the far side of a fence. Requires an on-foot, controllable player.
    Native task acceptance and climb animation alone never count as arrival.
    Use direct mode only for observed short crossings: each leg is limited to
    30 m and may use the same bounded obstacle recovery instead of a navmesh detour.
    """
    if not isinstance(allow_transition, bool):
        raise ValueError("allow_transition must be a boolean.")
    points = [_point(p) for p in (*waypoints, destination)]
    if not 1 <= len(points) <= 32 or not isinstance(parkour, bool):
        raise ValueError("Use at most 32 route points and a boolean parkour option.")
    if route_mode not in ("navmesh", "direct"):
        raise ValueError("route_mode must be navmesh or direct.")
    if isinstance(max_climbs, bool) or not isinstance(max_climbs, int) or not 0 <= max_climbs <= 12:
        raise ValueError("max_climbs must be an integer from 0 through 12.")
    for name, value, low, high in (("radius", radius, .2, 5), ("timeout", timeout, 1, 600),
            ("stall_timeout", stall_timeout, .2, 20), ("climb_timeout", climb_timeout, .5, 20),
            ("minimum_health", minimum_health, 1, 10000), ("move_blend_ratio", move_blend_ratio, 1, 3),
            ("check_interval", check_interval, .05, 1)):
        if isinstance(value, bool) or not math.isfinite(value) or not low <= value <= high:
            raise ValueError(f"{name} must be finite and between {low} and {high}.")
    game = ctx.game
    deadline = time.monotonic() + timeout
    identity = None
    owns_task = False
    uncertain = False
    climbs = 0

    class MissionTransition(Exception):
        def __init__(self, state):
            self.state = state

    def observe(**extra):
        nonlocal identity, owns_task
        state = ctx.check(sections=["player", *( ["probes"] if extra else [])], **extra)
        player = state["sections"].get("player")
        session = state["session"]
        control_transition = not session["playerControl"] or session["cutscenePlaying"]
        expected = allow_transition and owns_task
        if control_transition:
            owns_task = False
        if not player or session["pauseMenu"] or player["inVehicle"]:
            owns_task = False  # Do not clear a task installed by a mission transition.
            ctx.uncertain("Navigation interrupted by player/control/vehicle or menu transition.")
        observed_identity = (player["entity"]["handle"], player["entity"]["model"])
        if identity is not None and observed_identity != identity:
            owns_task = False
            ctx.uncertain("Player identity changed during navigation.")
        identity = observed_identity
        if player["entity"]["health"] < minimum_health or player["swimming"]:
            ctx.uncertain("Navigation stopped for health or water recovery.")
        if control_transition:
            if expected:
                raise MissionTransition(state)
            ctx.uncertain("Navigation interrupted by unexpected control or cutscene transition.")
        if time.monotonic() >= deadline:
            ctx.uncertain("Navigation exceeded its overall deadline.")
        return state, player, _point(player["entity"]["position"])

    def settled(player):
        return not any(player[k] for k in ("climbing", "vaulting", "jumping", "falling", "inAir", "ragdoll"))

    def clear():
        nonlocal owns_task
        game.native("CLEAR_PED_TASKS", identity[0])
        owns_task = False

    def route(point):
        nonlocal owns_task
        if route_mode == "direct":
            heading = math.degrees(math.atan2(position[0] - point[0], point[1] - position[1])) % 360
            game.native("TASK_GO_STRAIGHT_TO_COORD", identity[0], *map(_float, point),
                        _float(move_blend_ratio), -1, _float(heading), _float(.25))
        else:
            # Pinned SHVDNE default final heading is 40000; -1 disables timeout warping.
            # ENAV_NEVER_ENTER_WATER=32. No persistent ped path settings are changed.
            game.native("TASK_FOLLOW_NAV_MESH_TO_COORD", identity[0], *map(_float, point),
                        _float(move_blend_ratio), -1, _float(.25), 32, _float(40000))
        owns_task = True

    try:
        _, player, position = observe()
        if not settled(player) or player["inCover"]:
            ctx.uncertain("Start navigation settled on foot and out of cover.")
        if route_mode == "direct" and any(math.dist(a, b) > 30 for a, b in zip([position, *points], points)):
            raise ValueError("Direct crossings require observed route legs no longer than 30 m.")
        game.action("resume")
        for index, goal in enumerate(points):
            route(goal)
            anchor, moved_at = position, time.monotonic()
            best = math.dist(position, goal)
            direct_best = best
            visited = {tuple(round(v / 2) for v in position)}
            while True:
                _, player, position = observe()
                remaining = math.dist(position, goal)
                if settled(player) and remaining <= radius:
                    clear()
                    ctx.progress("Reached navigation waypoint", waypoint=index, position=position, remaining=remaining)
                    break
                if route_mode == "direct" and remaining < direct_best - .5:
                    direct_best, moved_at = remaining, time.monotonic()
                elif route_mode == "navmesh" and math.dist(position, anchor) >= .5:
                    anchor, moved_at = position, time.monotonic()
                cell = tuple(round(v / 2) for v in position)
                if remaining < best - 1 or (cell not in visited and len(visited) < 512):
                    best = min(best, remaining)
                    visited.add(cell)
                    ctx.progress("Observed on-foot route progress", waypoint=index, position=position, remaining=remaining)
                if not settled(player) or time.monotonic() - moved_at < stall_timeout:
                    time.sleep(check_interval)
                    continue
                route_result = game.native("GET_NAVMESH_ROUTE_RESULT", identity[0], returns="int")
                if not parkour or climbs >= max_climbs:
                    ctx.log("navigation-blocked", waypoint=index, routeResult=route_result, climbs=climbs, position=position)
                    ctx.uncertain("Route stalled and the bounded parkour allowance is exhausted or disabled.")
                dx, dy = goal[0] - position[0], goal[1] - position[1]
                horizontal = math.hypot(dx, dy)
                if horizontal < .2:
                    ctx.uncertain("Destination is on another level; supply an observed stair, ladder or climb approach.")
                yaw = (math.degrees(math.atan2(dx, dy)) + player["entity"]["rotation"]["z"] + 180) % 360 - 180
                # A narrow fan catches a fence edge blocking the ped's body even
                # when the centre ray passes through the adjacent opening.
                sensed, player, position = observe(probeHeight=.25,
                    rays=[{"yaw": (yaw + offset + 180) % 360 - 180, "pitch": 0,
                           "distance": min(6, horizontal)} for offset in (0, -20, 20)])
                probes = sensed["sections"]["probes"]
                hit = next((p for p in probes if p["hit"] and p["position"] is not None), None)
                if hit is None:
                    # Entity origin is near the pelvis. The upper fan can pass
                    # over a low garden fence that still blocks walking.
                    sensed, player, position = observe(probeHeight=-.4,
                        rays=[{"yaw": (yaw + offset + 180) % 360 - 180, "pitch": 0,
                               "distance": min(6, horizontal)} for offset in (0, -20, 20)])
                    probes = [*probes, *sensed["sections"]["probes"]]
                    hit = next((p for p in probes if p["hit"] and p["position"] is not None), None)
                ctx.log("navigation-obstacle-probe", waypoint=index, position=position, goal=goal, probes=probes)
                if hit is None:
                    ctx.uncertain("No nearby obstacle was observed; inspect the route or add an approach waypoint.")
                obstacle = _point(hit["position"])
                entity = hit["entity"]
                if entity and game.native("GET_ENTITY_TYPE", entity, returns="int") != 3:
                    ctx.uncertain("A ped or vehicle blocks the route; do not climb a moving actor.")
                dx, dy = obstacle[0] - position[0], obstacle[1] - position[1]
                length = math.hypot(dx, dy)
                if length < .05:
                    ctx.uncertain("Obstacle probe starts inside collision; inspect a clear approach.")
                direction = (dx / length, dy / length)
                facing_point = (obstacle[0] + direction[0], obstacle[1] + direction[1], goal[2])
                clear()
                if math.hypot(obstacle[0] - position[0], obstacle[1] - position[1]) > 1.6:
                    approach = (obstacle[0] - direction[0] * .8,
                                obstacle[1] - direction[1] * .8, position[2])
                    heading = math.degrees(math.atan2(-dx, dy)) % 360
                    game.native("TASK_GO_STRAIGHT_TO_COORD", identity[0], *map(_float, approach),
                                _float(1.5), -1, _float(heading), _float(.1))
                    owns_task = True
                    approach_deadline = time.monotonic() + 5
                    while math.dist(position, approach) > .7:
                        if time.monotonic() >= approach_deadline:
                            ctx.uncertain("Obstacle approach stalled; inspect a reachable edge.")
                        time.sleep(check_interval)
                        _, player, position = observe()
                    clear()
                # Turn naturally before asking the engine to find a nearby climbable edge.
                game.native("TASK_TURN_PED_TO_FACE_COORD", identity[0], *map(_float, facing_point), 500)
                owns_task = True
                turn_deadline = time.monotonic() + .65
                while time.monotonic() < turn_deadline:
                    time.sleep(check_interval)
                    _, player, position = observe()
                if not settled(player):
                    ctx.uncertain("Player became unstable before the obstacle attempt.")
                facing = (math.degrees(math.atan2(facing_point[0] - position[0], facing_point[1] - position[1]))
                          + player["entity"]["rotation"]["z"] + 180) % 360 - 180
                if abs(facing) > 35:
                    ctx.uncertain("Player did not face the obstacle; inspect the approach before jumping.")
                before, before_distance = position, math.dist(position, goal)
                game.native("TASK_CLIMB", identity[0], False)
                climbs += 1
                ctx.log("navigation-climb-attempt", waypoint=index, attempt=climbs, obstacle=obstacle,
                        position=position, routeResult=route_result)
                climb_deadline = time.monotonic() + climb_timeout
                moving = False
                while time.monotonic() < climb_deadline:
                    time.sleep(check_interval)
                    _, player, position = observe()
                    moving = moving or not settled(player)
                    if settled(player) and math.dist(position, goal) < before_distance - .5:
                        ctx.progress("Landed with navigation progress", waypoint=index, position=position, attempt=climbs)
                        break
                    if moving and settled(player):
                        break
                if not settled(player):
                    # Never cancel an unfinished climb/fall to manufacture a stable pose.
                    owns_task = False
                    ctx.uncertain("Climb did not settle within its deadline; inspect the animation and landing.")
                ctx.log("navigation-climb-observed", attempt=climbs, position=position,
                        displacement=math.dist(before, position), remaining=math.dist(position, goal))
                route(goal)
                anchor, moved_at = position, time.monotonic()
                direct_best = math.dist(position, goal)
        return {"arrived": True, "position": dict(zip(("x", "y", "z"), position)),
                "remaining": math.dist(position, points[-1]), "waypoints": len(points), "climbAttempts": climbs}
    except MissionTransition as transition:
        return dict(arrived=False, outcome="transition", state=transition.state, climbAttempts=climbs)
    except UncertainOutcome:
        uncertain = True
        raise
    finally:
        # A lost receipt must be inspected, never followed by a blind task resubmission.
        if owns_task and not uncertain:
            clear()


def follow_on_foot(ctx, target, *, offset=None, move_blend_ratio=3, stopping_range=2,
                   timeout=180, stall_timeout=8, max_gap=120, minimum_health=100,
                   check_interval=.2, on_tick=None):
    """Follow an observed moving actor with one persistent normal on-foot task.

    Returns transition, target-dead, target-boarded or policy; no proximity alone
    ends following. on_tick(ctx, state, target) chooses the mission objective.
    Stops on lost identities, player vehicle entry, water, health, excessive gap
    or stalled movement. This does not infer obstacle crossings: after a stall,
    inspect geometry and use navigate_to/climb_toward for bounded route recovery.
    Success leaves simulation running. An uncertain receipt is never replayed.
    """
    from .entities import observe_entity
    target_handle, target_model = _identity(target)
    point = _point(offset or dict(x=0, y=-2, z=0))
    if math.dist(point, (0, 0, 0)) > 10:
        raise ValueError("Follow offset must be within 10 metres of the actor.")
    _bounds(move_blend_ratio=(move_blend_ratio, 1, 3), stopping_range=(stopping_range, .5, 10),
            timeout=(timeout, 1, 600), stall_timeout=(stall_timeout, .2, 60),
            max_gap=(max_gap, 2, 500), minimum_health=(minimum_health, 1, 10000),
            check_interval=(check_interval, .05, 2))
    identity = None
    owns_task = uncertain = False
    started = moved = time.monotonic()
    anchor = None
    cells = set()
    try:
        while time.monotonic() - started < timeout:
            state = ctx.check(sections=["player"])
            p, session = state["sections"].get("player"), state["session"]
            if not p or session["pauseMenu"]:
                owns_task = False
                ctx.uncertain("On-foot following interrupted by missing player or menu.")
            if not session["playerControl"] or session["cutscenePlaying"]:
                owns_task = False
                return dict(outcome="transition", state=state)
            current = _identity(p["entity"])
            if p["inVehicle"] or (identity is not None and current != identity):
                owns_task = False
                ctx.uncertain("On-foot following interrupted by player identity or vehicle change.")
            identity = current
            if target_handle == identity[0]:
                raise ValueError("Cannot follow the player's own actor.")
            if p["entity"]["health"] < minimum_health or p["swimming"]:
                ctx.uncertain("On-foot following stopped for health or water recovery.")
            observed = observe_entity(ctx.game, target, relative_to=p["entity"])
            if observed is None:
                ctx.uncertain("On-foot follow target changed or disappeared.")
            position = _point(p["entity"]["position"])
            gap = math.dist(position, _point(observed["position"]))
            observed["distance"] = gap
            if observed["dead"]:
                return dict(outcome="target-dead", state=state, target=observed)
            if on_tick is not None and on_tick(ctx, state, observed):
                return dict(outcome="policy", state=state, target=observed)
            if observed["inVehicle"]:
                return dict(outcome="target-boarded", state=state, target=observed)
            if gap > max_gap:
                ctx.uncertain("On-foot target exceeded the bounded following distance.")
            if not owns_task:
                ctx.game.action("resume")
                source = ('''/* gta-use:foot-follow */
                    var target=Entity.FromHandle(%d);
                    if(target==null || !target.Exists() || target.Model.Hash!=%d) throw new Exception("Follow target identity changed");
                    P.Task.FollowToOffsetFromEntity(target,new Vector3(%sf,%sf,%sf),%sf,-1,%sf,true);
                ''') % (target_handle, target_model, *map(str, point), float(move_blend_ratio), float(stopping_range))
                ctx.game.command(source)
                owns_task = True
                ctx.log("foot-follow-start", target=target, offset=point)
            now = time.monotonic()
            if anchor is None or math.dist(position, anchor) > .75:
                anchor, moved = position, now
            cell = tuple(round(x / 2) for x in position)
            if cell not in cells and len(cells) < 4096:
                if cells:
                    ctx.progress("Observed on-foot route progress", position=position, gap=gap)
                cells.add(cell)
            if now - moved >= stall_timeout:
                ctx.log("foot-follow-stalled", state=state, target=observed)
                ctx.uncertain("On-foot following stalled; inspect the target or obstacle before recovery.")
            time.sleep(check_interval)
        ctx.uncertain("On-foot following exceeded its overall deadline.")
    except UncertainOutcome:
        uncertain = True
        raise
    finally:
        if owns_task and not uncertain:
            ctx.game.native("CLEAR_PED_TASKS", identity[0])
