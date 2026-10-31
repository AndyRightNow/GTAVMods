"""Continuous vehicle policies using normal GTA tasks and observed outcomes."""
import math
import time

from .client import UncertainOutcome
from .entities import observe_entity
from .navigation import _float, _point
from ._validation import _bounds, _identity

# SHVDNE DrivingModeAvoidVehicles: alerted driving with braking and obstacle
# avoidance, without traffic-light waits. Not the reckless preset.
ALERTED = 786469


def _approach_speed(remaining, radius, cruise, arrival):
    # A gradual speed request avoids crawling the entire last 3*cruise metres.
    # This is a tuning curve, not a guarantee of the engine's braking distance.
    return min(cruise, max(arrival, math.sqrt(5 * max(0, remaining - radius))))


class _Driver:
    def __init__(self, ctx, minimum_health):
        self.ctx, self.game, self.minimum_health = ctx, ctx.game, minimum_health
        self.identity = None
        self.owns_task = self.uncertain = False

    def observe(self, *, allow_roll=False):
        state = self.ctx.check(sections=["player", "vehicle"])
        p, v = state["sections"].get("player"), state["sections"].get("vehicle")
        session = state["session"]
        if session["pauseMenu"] or not p:
            self.owns_task = False
            self.ctx.uncertain("Driving interrupted by missing player or pause menu.")
        if not session["playerControl"] or session["cutscenePlaying"]:
            self.owns_task = False  # The mission now owns its tasks.
            return state, None, None
        if not p["inVehicle"] or not v or v["driver"] != p["entity"]["handle"]:
            self.owns_task = False
            self.ctx.uncertain("Player is no longer the observed driver.")
        identity = (p["entity"]["handle"], p["entity"]["model"],
                    v["entity"]["handle"], v["entity"]["model"])
        if self.identity is not None and self.identity != identity:
            self.owns_task = False
            self.ctx.uncertain("Driver or vehicle identity changed.")
        self.identity = identity
        if p["entity"]["health"] < self.minimum_health or not v["driveable"] or (v["upsideDown"] and not allow_roll):
            self.ctx.uncertain("Driver health or vehicle condition needs recovery.")
        return state, p, v

    def close(self):
        if self.owns_task and not self.uncertain:
            self.game.native("CLEAR_PED_TASKS", self.identity[0])
            self.owns_task = False


def drive_to(ctx, destination, *, speed=28, arrival_speed=6, radius=5,
             timeout=900, stall_timeout=12, minimum_health=100,
             driving_style=ALERTED, check_interval=.4, on_tick=None):
    """Drive continuously, slow near the goal, and verify a stopped arrival.

    Returns outcome='arrived', 'transition' or 'policy'. A truthy on_tick(ctx,
    state) result ends driving for a policy decision. Expected cutscenes return
    without clearing mission tasks; callers can wait_for_control next.
    Arrival radius is 1..20 metres; speed is 1..60 metres/second. Use an observed
    curb/access point when the objective lies inside a building or off-road.
    No pause, compilation, checkpoint or model handoff occurs inside the helper.
    """
    goal = _point(destination)
    _bounds(speed=(speed, 1, 60), arrival_speed=(arrival_speed, 1, speed),
            radius=(radius, 1, 20), timeout=(timeout, 1, 1800),
            stall_timeout=(stall_timeout, .2, 120), minimum_health=(minimum_health, 1, 10000),
            check_interval=(check_interval, .05, 2))
    if isinstance(driving_style, bool) or not isinstance(driving_style, int) or not 0 <= driving_style <= 2147483647:
        raise ValueError("driving_style must be a nonnegative signed integer bitmask.")
    driver = _Driver(ctx, minimum_health)
    started = time.monotonic()
    try:
        state, p, v = driver.observe()
        if p is None:
            return dict(outcome="transition", state=state)
        handle, vehicle = p["entity"]["handle"], v["entity"]["handle"]
        position = _point(p["entity"]["position"])
        anchor, moved = position, started
        best = math.dist(position, goal)
        cells = {tuple(round(x / 10) for x in position)}
        requested_speed = _approach_speed(best, radius, speed, arrival_speed)
        ctx.game.action("resume")
        ctx.game.native("TASK_VEHICLE_DRIVE_TO_COORD_LONGRANGE", handle, vehicle,
                        *map(_float, goal), _float(requested_speed),
                        driving_style, _float(radius * .6))
        driver.owns_task = True
        ctx.log("drive-start", destination=destination, speed=speed, drivingStyle=driving_style)
        while time.monotonic() - started < timeout:
            state, p, v = driver.observe()
            if p is None:
                return dict(outcome="transition", state=state)
            if on_tick is not None and on_tick(ctx, state):
                return dict(outcome="policy", state=state)
            position = _point(p["entity"]["position"])
            remaining = math.dist(position, goal)
            now = time.monotonic()
            if remaining <= radius and v["speed"] <= 1.5:
                ctx.progress("Stopped at driving destination", remaining=remaining, speed=v["speed"])
                return dict(outcome="arrived", remaining=remaining, elapsed=now-started, state=state)
            desired = _approach_speed(remaining, radius, speed, arrival_speed)
            if abs(desired - requested_speed) >= 3 or (desired == arrival_speed and requested_speed != arrival_speed):
                ctx.game.native("SET_DRIVE_TASK_CRUISE_SPEED", handle, _float(desired))
                requested_speed = desired
            if math.dist(position, anchor) > 2:
                anchor, moved = position, now
            cell = tuple(round(x / 10) for x in position)
            if remaining < best - 5 or (cell not in cells and len(cells) < 2048):
                best = min(best, remaining)
                cells.add(cell)
                ctx.progress("Observed driving progress", remaining=remaining, speed=v["speed"])
            if now - moved >= stall_timeout:
                ctx.log("drive-stalled", remaining=remaining, speed=v["speed"], state=state)
                ctx.uncertain("Driving stalled; inspect obstruction or select an entrance waypoint.")
            time.sleep(check_interval)
        ctx.uncertain("Driving exceeded its overall deadline.")
    except UncertainOutcome:
        driver.uncertain = True
        raise
    finally:
        driver.close()


def wait_for_control(ctx, *, timeout=90, check_interval=.4, stable_for=0):
    """Observe a bounded mission transition, including actual cutscene progress.

    Control returning is not Mission Passed. Call only for an expected transition;
    this resumes simulation but never dismisses a game menu or failure modal.
    """
    _bounds(timeout=(timeout, 1, 300), check_interval=(check_interval, .05, 2),
            stable_for=(stable_for, 0, timeout))
    deadline = time.monotonic() + timeout
    previous, last_cutscene, stable_since = None, -1, None
    ctx.game.action("resume")
    while time.monotonic() < deadline:
        state = ctx.check(sections=["player"])
        s = state["session"]
        if s["pauseMenu"]:
            ctx.uncertain("A menu interrupted the expected transition.")
        signature = (s["playerControl"], s["cutscenePlaying"], s["missionActive"])
        playback = s.get("cutsceneTime", 0)
        if previous is not None and (signature != previous or (s["cutscenePlaying"] and playback > last_cutscene)):
            ctx.progress("Observed mission transition", session=s)
        previous, last_cutscene = signature, playback
        if s["playerControl"] and not s["cutscenePlaying"] and state["sections"].get("player"):
            stable_since = time.monotonic() if stable_since is None else stable_since
            if time.monotonic() - stable_since >= stable_for:
                return state
        else:
            stable_since = None
        time.sleep(check_interval)
    ctx.uncertain("Mission transition did not return control before its deadline.")


def pursue_vehicle(ctx, rider, vehicle, *, rider_model, vehicle_model,
                   distance=8, timeout=180, no_progress_timeout=25,
                   minimum_health=100, check_interval=.3, on_tick=None):
    """Track a moving rider continuously; no checkpoint merely for closing to 25m.

    on_tick(ctx, state, target) may run a bounded, verified engagement policy and
    return True to end the chase. It must not hide uncertain mutations. Closing
    and target damage count as progress; road travel cannot reset this budget.
    Target exit/death is a transition for inspection, not a mission success.
    """
    _bounds(distance=(distance, 2, 50), timeout=(timeout, 1, 600),
            no_progress_timeout=(no_progress_timeout, .2, 120),
            minimum_health=(minimum_health, 1, 10000), check_interval=(check_interval, .05, 2))
    for value in (rider, vehicle, rider_model, vehicle_model):
        if isinstance(value, bool) or not isinstance(value, int):
            raise ValueError("Observed handles and model hashes must be integers.")
    driver = _Driver(ctx, minimum_health)
    started, progress_at, best, health = time.monotonic(), time.monotonic(), math.inf, math.inf

    def target():
        observed = observe_entity(ctx.game, dict(handle=rider, model=rider_model),
                                  relative_to=dict(handle=vehicle, model=vehicle_model))
        if observed is None:
            ctx.uncertain("Pursuit target identity changed or disappeared.")
        return observed
    try:
        state, p, v = driver.observe()
        if p is None:
            return dict(outcome="transition", state=state)
        t = target()
        if t["vehicle"] != vehicle or t["health"] <= 0:
            ctx.uncertain("Start pursuit with the observed living rider on the target vehicle.")
        ctx.game.action("resume")
        ctx.game.native("TASK_VEHICLE_CHASE", driver.identity[0], rider)
        driver.owns_task = True
        ctx.game.native("SET_TASK_VEHICLE_CHASE_IDEAL_PURSUIT_DISTANCE", driver.identity[0], _float(distance))
        while time.monotonic() - started < timeout:
            state, p, v = driver.observe()
            if p is None:
                return dict(outcome="transition", state=state)
            t = target()
            gap = math.dist(_point(p["entity"]["position"]), _point(t["position"]))
            t["distance"] = gap
            if t["vehicle"] != vehicle or t["health"] <= 0:
                return dict(outcome="target-left-vehicle", target=t, state=state)
            now = time.monotonic()
            if gap < best - 2 or t["health"] < health:
                if math.isfinite(best):
                    ctx.progress("Pursuit closed or target health changed", target=t)
                best, health, progress_at = min(best, gap), min(health, t["health"]), now
            if on_tick is not None and on_tick(ctx, state, t):
                return dict(outcome="policy", target=t, state=state)
            if now - progress_at >= no_progress_timeout:
                ctx.log("pursuit-no-effect", target=t)
                ctx.uncertain("Pursuit is not closing or affecting its target; change tactics after inspection.")
            time.sleep(check_interval)
        ctx.uncertain("Pursuit exceeded its overall deadline.")
    except UncertainOutcome:
        driver.uncertain = True
        raise
    finally:
        driver.close()


def intercept_vehicle(ctx, rider, vehicle, *, rider_model, vehicle_model,
                      waypoints=(), waypoint_radius=35, engage_distance=180,
                      engagement="chase", speed=40, driving_style=786468,
                      timeout=240, no_progress_timeout=65, check_interval=.25,
                      minimum_health=100, recover_rolls=False, on_tick=None):
    """Continuous road interception followed by chase or explicitly chosen ram.

    Waypoints describe a caller-observed interception route. Closing within
    engage_distance switches directly to the moving target. Tasks persist across
    observations; no arrival braking or repeated half-second handoffs. Ram uses
    normal collision physics and may damage the rider/vehicle. It is opt-in.
    Rider separation is an inspection transition, never Mission Passed.
    """
    from .vehicles import recover_upright
    _identity(dict(handle=rider, model=rider_model))
    _identity(dict(handle=vehicle, model=vehicle_model))
    points = [_point(p) for p in waypoints]
    if len(points) > 32 or engagement not in ("chase", "ram") or not isinstance(recover_rolls, bool):
        raise ValueError("Use <=32 waypoints, chase/ram engagement and a boolean recovery option.")
    if isinstance(driving_style, bool) or not isinstance(driving_style, int) or not 0 <= driving_style <= 2147483647:
        raise ValueError("driving_style must be a nonnegative signed integer bitmask.")
    _bounds(waypoint_radius=(waypoint_radius, 1, 100), engage_distance=(engage_distance, 2, 500),
            speed=(speed, 1, 60), timeout=(timeout, 1, 600),
            no_progress_timeout=(no_progress_timeout, .2, 120), check_interval=(check_interval, .05, 2),
            minimum_health=(minimum_health, 1, 10000))
    # One immediate RPC, one game-thread observation, cached C# for this identity.
    # Avoid seven individually framed native requests per pursuit observation.
    query = ('''/* gta-use:pursuit-target */
        var rider=Entity.FromHandle(%d) as Ped; var vehicle=Entity.FromHandle(%d) as Vehicle;
        if(rider==null || !rider.Exists() || rider.Model.Hash!=%d ||
           vehicle==null || !vehicle.Exists() || vehicle.Model.Hash!=%d) return null;
        return new {position=rider.Position, health=rider.Health,
                    vehicle=Function.Call<int>(Hash.GET_VEHICLE_PED_IS_IN,rider.Handle,false)};
    ''') % (rider, vehicle, rider_model, vehicle_model)
    driver = _Driver(ctx, minimum_health)
    started = progress_at = window_at = time.monotonic()
    best = health = math.inf
    window_gap, route_index, plan, last_sample = None, 0, None, 0
    try:
        ctx.game.action("resume")
        while time.monotonic() - started < timeout:
            state, p, v = driver.observe(allow_roll=recover_rolls)
            if p is None:
                return dict(outcome="transition", state=state)
            if recover_rolls and v["upsideDown"]:
                recover_upright(ctx, minimum_health=minimum_health)
                state, p, v = driver.observe()
                if p is None:
                    return dict(outcome="transition", state=state)
            target = ctx.game.command(query)
            if target is None:
                ctx.uncertain("Pursuit target identity changed or disappeared.")
            target["distance"] = math.dist(_point(p["entity"]["position"]), _point(target["position"]))
            now = time.monotonic()
            if now - last_sample > 2:
                ctx.log("pursuit-sample", target=target, vehicle=v)
                last_sample = now
            if target["health"] <= 0 or target["vehicle"] != vehicle:
                return dict(outcome="rider-separated", target=target, state=state)
            if target["distance"] < best - 3 or target["health"] < health:
                if math.isfinite(best):
                    ctx.progress("Moving vehicle pursuit closing", target=target)
                best, health, progress_at = min(best, target["distance"]), min(health, target["health"]), now
            if window_gap is None:
                window_gap = target["distance"]
            if now - window_at >= 5:
                if window_gap - target["distance"] > 8:
                    progress_at = now
                    ctx.progress("Pursuit closed over observation window", before=window_gap, after=target["distance"])
                window_gap, window_at = target["distance"], now
            if on_tick is not None and on_tick(ctx, state, target):
                return dict(outcome="policy", target=target, state=state)
            if target["distance"] < engage_distance:
                route_index = len(points)
            while route_index < len(points) and math.dist(_point(p["entity"]["position"]), points[route_index]) < waypoint_radius:
                route_index += 1
            if plan != route_index:
                if route_index < len(points):
                    ctx.game.native("TASK_VEHICLE_DRIVE_TO_COORD_LONGRANGE", driver.identity[0], driver.identity[2],
                                    *map(_float, points[route_index]), _float(speed), driving_style, _float(3))
                    ctx.log("intercept-replan", road=dict(zip(("x", "y", "z"), points[route_index])), target=target)
                elif engagement == "ram":
                    ctx.game.native("TASK_VEHICLE_MISSION", driver.identity[0], driver.identity[2], vehicle,
                                    2, _float(speed), driving_style, _float(2), _float(15), True)
                    ctx.log("physical-vehicle-interception", target=target)
                else:
                    ctx.game.native("TASK_VEHICLE_CHASE", driver.identity[0], rider)
                driver.owns_task, plan = True, route_index
            if now - progress_at >= no_progress_timeout:
                ctx.log("pursuit-no-effect", target=target)
                ctx.uncertain("Moving vehicle pursuit did not close the gap or affect its target.")
            time.sleep(check_interval)
        ctx.uncertain("Pursuit exceeded its overall deadline.")
    except UncertainOutcome:
        driver.uncertain = True
        raise
    finally:
        driver.close()


def follow_entity(ctx, target, *, speed=28, min_distance=5, arrival_radius=10,
                  driving_style=ALERTED, timeout=180, no_progress_timeout=20,
                  minimum_health=100, check_interval=.3, on_tick=None):
    """Approach an observed moving entity using one persistent vehicle task.

    Unlike rider pursuit, the target need not occupy a vehicle: scripted actors
    may stand on trailers or board through animations. Returns near-target,
    target-dead, transition or policy. Proximity is not a boarding/mission result.
    on_tick(ctx, state, target) can observe a policy-specific transition; it must
    not replace the owned driving task. Normal return clears that task without
    pausing; uncertain transport is never replayed or followed by task cleanup.
    """
    target_handle, target_model = _identity(target)
    _bounds(speed=(speed, 1, 60), arrival_radius=(arrival_radius, 1, 50),
            timeout=(timeout, 1, 600), no_progress_timeout=(no_progress_timeout, .2, 120),
            minimum_health=(minimum_health, 1, 10000), check_interval=(check_interval, .05, 2))
    if isinstance(min_distance, bool) or not isinstance(min_distance, int) or not 0 <= min_distance <= 100:
        raise ValueError("min_distance must be an integer between 0 and 100 metres.")
    if isinstance(driving_style, bool) or not isinstance(driving_style, int) or not 0 <= driving_style <= 2147483647:
        raise ValueError("driving_style must be a nonnegative signed integer bitmask.")
    driver = _Driver(ctx, minimum_health)
    started = progress_at = window_at = time.monotonic()
    best, window_gap = math.inf, None
    try:
        while time.monotonic() - started < timeout:
            state, p, v = driver.observe()
            if p is None:
                return dict(outcome="transition", state=state)
            if target_handle in (driver.identity[0], driver.identity[2]):
                ctx.uncertain("Cannot follow the player's own actor or current vehicle.")
            observed = observe_entity(ctx.game, target, relative_to=v["entity"])
            if observed is None:
                ctx.uncertain("Follow target identity changed or disappeared.")
            observed.update(handle=target_handle, model=target_model,
                            distance=math.dist(_point(p["entity"]["position"]), _point(observed["position"])))
            if observed["dead"]:
                return dict(outcome="target-dead", state=state, target=observed)
            if on_tick is not None and on_tick(ctx, state, observed):
                return dict(outcome="policy", state=state, target=observed)
            gap, now = observed["distance"], time.monotonic()
            if gap <= arrival_radius:
                ctx.progress("Reached moving entity approach radius", target=observed)
                return dict(outcome="near-target", state=state, target=observed, elapsed=now-started)
            if not driver.owns_task:
                ctx.game.action("resume")
                ctx.game.native("TASK_VEHICLE_FOLLOW", driver.identity[0], driver.identity[2],
                                target_handle, _float(speed), driving_style, min_distance)
                driver.owns_task = True
                ctx.log("follow-start", target=target, speed=speed, minDistance=min_distance)
            if gap < best - 2:
                if math.isfinite(best):
                    ctx.progress("Moving entity approach closing", target=observed)
                best, progress_at = min(best, gap), now
            if window_gap is None:
                window_gap = gap
            if now - window_at >= min(5, no_progress_timeout / 2):
                if window_gap - gap > 2:
                    progress_at = now
                    ctx.progress("Moving entity approach closing over recent window", before=window_gap, after=gap)
                window_gap, window_at = gap, now
            if now - progress_at >= no_progress_timeout:
                ctx.log("follow-no-effect", target=observed, state=state)
                ctx.uncertain("Following did not close the gap; inspect the route or approach offset.")
            time.sleep(check_interval)
        ctx.uncertain("Following exceeded its overall deadline.")
    except UncertainOutcome:
        driver.uncertain = True
        raise
    finally:
        driver.close()


def boat_to(ctx, destination, *, waypoints=(), speed=28, radius=8,
            waypoint_radius=18, timeout=300, stall_timeout=8,
            route_progress_timeout=40, minimum_health=100,
            driving_style=ALERTED, check_interval=.25, on_tick=None):
    """Navigate a boat through observed water waypoints to a stopped arrival.

    Normal TASK_BOAT_MISSION navigation retains shore avoidance. Intermediate
    points are passage gates, not stops: choose them inside the water channel,
    beyond an entrance, with clearance for the hull. Destination Z must describe
    the observed water surface. The engine decides the route; this is not a
    straight-line steering controller or a guarantee against grounding.

    Requires the observed boat driver. Returns arrived, transition or policy.
    on_tick(ctx, state) may end the route for an objective/companion decision.
    Do not shoot beneath this task when it suppresses normal player firing.
    Stall, repeated-route and total deadlines are independent. No task replay or
    cleanup follows an uncertain receipt, changed identity or mission takeover.
    """
    goal = _point(destination)
    points = [_point(p) for p in waypoints] + [goal]
    if len(points) > 65:
        raise ValueError("Use at most 64 intermediate water waypoints.")
    _bounds(speed=(speed, 1, 50), radius=(radius, 2, 30),
            waypoint_radius=(waypoint_radius, 3, 60), timeout=(timeout, 1, 1800),
            stall_timeout=(stall_timeout, .2, 120),
            route_progress_timeout=(route_progress_timeout, .2, 180),
            minimum_health=(minimum_health, 1, 10000), check_interval=(check_interval, .05, 2))
    if isinstance(driving_style, bool) or not isinstance(driving_style, int) or not 0 <= driving_style <= 2147483647:
        raise ValueError("driving_style must be a nonnegative signed integer bitmask.")
    driver = _Driver(ctx, minimum_health)
    started = time.monotonic()
    try:
        state, p, v = driver.observe()
        if p is None:
            return dict(outcome="transition", state=state)
        if not ctx.game.native("IS_THIS_MODEL_A_BOAT", v["entity"]["model"], returns="bool"):
            ctx.uncertain("Water navigation requires an observed boat.")
        position = _point(p["entity"]["position"])
        anchor, moved, advanced = position, started, started
        cells = {tuple(round(x / 8) for x in position[:2])}
        index, submitted, best = 0, -1, math.inf
        ctx.game.action("resume")
        while time.monotonic() - started < timeout:
            state, p, v = driver.observe()
            if p is None:
                return dict(outcome="transition", state=state)
            if on_tick is not None and on_tick(ctx, state):
                return dict(outcome="policy", state=state)
            position = _point(p["entity"]["position"])
            remaining = math.dist(position, points[index])
            now = time.monotonic()
            final = index == len(points) - 1
            if not final and remaining <= waypoint_radius:
                ctx.progress("Passed water waypoint", index=index, remaining=remaining)
                index += 1
                best, advanced = math.inf, now
                remaining = math.dist(position, points[index])
                final = index == len(points) - 1
            if final and remaining <= radius and v["speed"] <= 1.5:
                ctx.progress("Stopped at water destination", remaining=remaining, speed=v["speed"])
                return dict(outcome="arrived", remaining=remaining, elapsed=now-started, state=state)
            if submitted != index:
                ctx.game.native("TASK_BOAT_MISSION", driver.identity[0], driver.identity[2], 0, 0,
                                *map(_float, points[index]), 4, _float(speed), driving_style,
                                _float(radius * .5 if final else 1), 7)
                driver.owns_task = True
                submitted = index
                ctx.log("boat-route", index=index, destination=points[index], final=final, speed=speed)
            if math.dist(position, anchor) > 2:
                anchor, moved = position, now
            cell = tuple(round(x / 8) for x in position[:2])
            if remaining < best - 4 or (cell not in cells and len(cells) < 2048):
                # First observation establishes a baseline, not movement.
                if math.isfinite(best):
                    ctx.progress("Observed water route progress", index=index, remaining=remaining, speed=v["speed"])
                    advanced = now
                best = min(best, remaining)
                cells.add(cell)
            if now - moved >= stall_timeout:
                ctx.log("boat-stalled", state=state, index=index, remaining=remaining)
                ctx.uncertain("Boat stopped making physical progress; inspect water clearance.")
            if now - advanced >= route_progress_timeout:
                ctx.uncertain("Boat repeated its route without progress; inspect the exit and choose a new route.")
            time.sleep(check_interval)
        ctx.uncertain("Water navigation exceeded its overall deadline.")
    except UncertainOutcome:
        driver.uncertain = True
        raise
    finally:
        driver.close()


def steer_water_route(ctx, waypoints, *, speed=24, turn_speed=12, radius=7,
                      timeout=180, stall_timeout=5, progress_timeout=20,
                      minimum_health=100, steering_angle=18, damping=.012,
                      check_interval=.12, on_tick=None):
    """Steer a boat through an explicitly observed, clear water route.

    Uses ordinary throttle and steering rather than an engine pathfinding task.
    Points are passage gates; returns passed while still moving, transition on
    mission takeover, or policy when on_tick(ctx, state) is truthy. It releases
    held controls on normal exit but does not brake or pause. The caller owns
    route choice, obstacle clearance, destination braking and objective checks.

    This is NOT obstacle avoidance. Include the center of narrow entrances and
    bends; do not draw a straight line across banks, piers or unseen geometry.
    Existing player tasks are cleared once when taking over the observed driver.
    Physical stall, failure to approach a gate, health and total deadlines stop
    the run. Unknown receipts stop without replay or further input cleanup.
    """
    points = [_point(p)[:2] for p in waypoints]
    if not 1 <= len(points) <= 128:
        raise ValueError("Use between 1 and 128 water route points.")
    _bounds(speed=(speed, 1, 50), turn_speed=(turn_speed, 1, speed), radius=(radius, 2, 40),
            timeout=(timeout, 1, 1800), stall_timeout=(stall_timeout, .2, 120),
            progress_timeout=(progress_timeout, .2, 180), minimum_health=(minimum_health, 1, 10000),
            steering_angle=(steering_angle, 5, 90), damping=(damping, 0, .1),
            check_interval=(check_interval, .05, .5))
    driver = _Driver(ctx, minimum_health)
    started = time.monotonic()
    holding, uncertain = False, False
    try:
        state, p, v = driver.observe()
        if p is None:
            return dict(outcome="transition", state=state)
        if not ctx.game.native("IS_THIS_MODEL_A_BOAT", v["entity"]["model"], returns="bool"):
            ctx.uncertain("Water steering requires an observed boat.")
        ctx.game.action("resume")
        ctx.game.native("CLEAR_PED_TASKS", driver.identity[0])
        index, best = 0, math.inf
        anchor = _point(p["entity"]["position"])[:2]
        moved = advanced = started
        previous_heading = previous_time = None
        while time.monotonic() - started < timeout:
            state, p, v = driver.observe()
            if p is None:
                return dict(outcome="transition", state=state)
            if on_tick is not None and on_tick(ctx, state):
                return dict(outcome="policy", state=state)
            position, now = _point(p["entity"]["position"])[:2], time.monotonic()
            remaining = math.dist(position, points[index])
            if remaining <= radius:
                ctx.progress("Passed steering route gate", index=index, position=position)
                index += 1
                if index == len(points):
                    return dict(outcome="passed", elapsed=now-started, state=state)
                best, advanced = math.inf, now
                remaining = math.dist(position, points[index])
            if remaining < best - 2:
                if math.isfinite(best):
                    ctx.progress("Approaching water route gate", index=index, remaining=remaining)
                best, advanced = remaining, now
            if math.dist(position, anchor) > 2:
                anchor, moved = position, now
            if now - moved >= stall_timeout:
                ctx.uncertain("Water steering stalled; inspect hull and channel clearance.")
            if now - advanced >= progress_timeout:
                ctx.uncertain("Water steering did not approach its gate; inspect the route.")
            heading = v["entity"]["rotation"]["z"]
            target = math.degrees(math.atan2(-(points[index][0]-position[0]), points[index][1]-position[1]))
            error = (target-heading+180) % 360-180
            rate = 0 if previous_heading is None else ((heading-previous_heading+180) % 360-180) / max(.05, now-previous_time)
            steer = max(-1, min(1, -error/steering_angle+rate*damping))
            limit = speed if abs(error) < 15 else turn_speed
            throttle = (1 if abs(error) < 45 else .5) if v["speed"] <= limit else 0
            ctx.game.set_inputs([dict(control="VehicleAccelerate", value=throttle),
                                 dict(control="VehicleMoveLeftRight", value=steer)], ttl_ms=1800)
            holding = True
            previous_heading, previous_time = heading, now
            time.sleep(check_interval)
        ctx.uncertain("Water steering exceeded its overall deadline.")
    except UncertainOutcome:
        uncertain = True
        raise
    finally:
        if holding and not uncertain:
            ctx.game.release_inputs()
