"""Supervised on-foot routing with bounded, observed climb/vault recovery."""
import math
import time

from .client import UncertainOutcome


def _point(value):
    point = tuple(float(value[axis]) for axis in ("x", "y", "z"))
    if not all(math.isfinite(v) for v in point):
        raise ValueError("Navigation coordinates must be finite.")
    return point


def _float(value):
    return {"type": "float", "value": value}


def navigate_to(ctx, destination, *, waypoints=(), parkour=True, radius=.75,
                timeout=60, stall_timeout=2.5, max_climbs=3, climb_timeout=7,
                minimum_health=100, move_blend_ratio=2.5, check_interval=.2):
    """Run a short route, climbing nearby obstacles only when progress stalls.

    Returns observed arrival data; it does not create a mission checkpoint or
    pause on success. Explicit waypoints should describe the intended route,
    including the far side of a fence. Requires an on-foot, controllable player.
    Native task acceptance and climb animation alone never count as arrival.
    """
    points = [_point(p) for p in (*waypoints, destination)]
    if not 1 <= len(points) <= 32 or not isinstance(parkour, bool):
        raise ValueError("Use at most 32 route points and a boolean parkour option.")
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

    def observe(**extra):
        nonlocal identity, owns_task
        state = ctx.check(sections=["player", *( ["probes"] if extra else [])], **extra)
        player = state["sections"].get("player")
        session = state["session"]
        if (not player or not session["playerControl"] or session["cutscenePlaying"]
                or session["pauseMenu"] or player["inVehicle"]):
            owns_task = False  # Do not clear a task installed by a mission transition.
            ctx.uncertain("Navigation interrupted by player/control/vehicle or menu transition.")
        observed_identity = (player["entity"]["handle"], player["entity"]["model"])
        if identity is not None and observed_identity != identity:
            owns_task = False
            ctx.uncertain("Player identity changed during navigation.")
        identity = observed_identity
        if player["entity"]["health"] < minimum_health or player["swimming"]:
            ctx.uncertain("Navigation stopped for health or water recovery.")
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
        # Pinned SHVDNE default final heading is 40000; -1 disables timeout warping.
        # ENAV_NEVER_ENTER_WATER=32. No persistent ped path settings are changed.
        game.native("TASK_FOLLOW_NAV_MESH_TO_COORD", identity[0], *map(_float, point),
                    _float(move_blend_ratio), -1, _float(.25), 32, _float(40000))
        owns_task = True

    try:
        _, player, position = observe()
        if not settled(player) or player["inCover"]:
            ctx.uncertain("Start navigation settled on foot and out of cover.")
        game.action("resume")
        for index, goal in enumerate(points):
            route(goal)
            anchor, moved_at = position, time.monotonic()
            best = math.dist(position, goal)
            visited = {tuple(round(v / 2) for v in position)}
            while True:
                _, player, position = observe()
                remaining = math.dist(position, goal)
                if settled(player) and remaining <= radius:
                    clear()
                    ctx.progress("Reached navigation waypoint", waypoint=index, position=position, remaining=remaining)
                    break
                if math.dist(position, anchor) >= .5:
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
                # One forward probe only on a stall, not a costly scan on every poll.
                sensed, player, position = observe(probeHeight=.25,
                    rays=[{"yaw": yaw, "pitch": 0, "distance": min(6, horizontal)}])
                hit = sensed["sections"]["probes"][0]
                if not hit["hit"] or hit["position"] is None:
                    ctx.uncertain("No nearby obstacle was observed; inspect the route or add an approach waypoint.")
                obstacle = _point(hit["position"])
                entity = hit["entity"]
                if entity and game.native("GET_ENTITY_TYPE", entity, returns="int") != 3:
                    ctx.uncertain("A ped or vehicle blocks the route; do not climb a moving actor.")
                direction = (dx / horizontal, dy / horizontal)
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
                game.native("TASK_TURN_PED_TO_FACE_COORD", identity[0], *map(_float, goal), 500)
                owns_task = True
                turn_deadline = time.monotonic() + .65
                while time.monotonic() < turn_deadline:
                    time.sleep(check_interval)
                    _, player, position = observe()
                if not settled(player):
                    ctx.uncertain("Player became unstable before the obstacle attempt.")
                facing = (math.degrees(math.atan2(dx, dy)) + player["entity"]["rotation"]["z"] + 180) % 360 - 180
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
        return {"arrived": True, "position": dict(zip(("x", "y", "z"), position)),
                "remaining": math.dist(position, points[-1]), "waypoints": len(points), "climbAttempts": climbs}
    except UncertainOutcome:
        uncertain = True
        raise
    finally:
        # A lost receipt must be inspected, never followed by a blind task resubmission.
        if owns_task and not uncertain:
            clear()
