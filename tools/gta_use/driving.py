"""Continuous vehicle policies using normal GTA tasks and observed outcomes."""
import math
import time

from .client import UncertainOutcome
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
        game = ctx.game
        for handle, model in ((rider, rider_model), (vehicle, vehicle_model)):
            if not game.native("DOES_ENTITY_EXIST", handle, returns="bool") or game.native("GET_ENTITY_MODEL", handle, returns="int") != model:
                ctx.uncertain("Pursuit target identity changed or disappeared.")
        return dict(position=game.native("GET_ENTITY_COORDS", rider, False, returns="vector3"),
                    health=game.native("GET_ENTITY_HEALTH", rider, returns="int"),
                    vehicle=game.native("GET_VEHICLE_PED_IS_IN", rider, False, returns="int"))
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
