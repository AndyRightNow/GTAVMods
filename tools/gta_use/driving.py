"""Continuous vehicle policies using normal GTA tasks and observed outcomes."""
import math
import time

from .client import UncertainOutcome
from .navigation import _float, _point

# SHVDNE DrivingModeAvoidVehicles: alerted driving with braking and obstacle
# avoidance, without traffic-light waits. Not the reckless preset.
ALERTED = 786469


def _approach_speed(remaining, radius, cruise, arrival):
    # A gradual speed request avoids crawling the entire last 3*cruise metres.
    # This is a tuning curve, not a guarantee of the engine's braking distance.
    return min(cruise, max(arrival, math.sqrt(5 * max(0, remaining - radius))))


def _bounds(**values):
    for name, (value, low, high) in values.items():
        if isinstance(value, bool) or not math.isfinite(value) or not low <= value <= high:
            raise ValueError(f"{name} must be finite and between {low} and {high}.")


class _Driver:
    def __init__(self, ctx, minimum_health):
        self.ctx, self.game, self.minimum_health = ctx, ctx.game, minimum_health
        self.identity = None
        self.owns_task = self.uncertain = False

    def observe(self):
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
        if p["entity"]["health"] < self.minimum_health or not v["driveable"] or v["upsideDown"]:
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


def wait_for_control(ctx, *, timeout=90, check_interval=.4):
    """Observe a bounded mission transition, including actual cutscene progress.

    Control returning is not Mission Passed. Call only for an expected transition;
    this resumes simulation but never dismisses a game menu or failure modal.
    """
    _bounds(timeout=(timeout, 1, 300), check_interval=(check_interval, .05, 2))
    deadline = time.monotonic() + timeout
    previous, last_cutscene = None, -1
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
            return state
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
