# Writing supervised Python game AI

## Start from a bounded segment

Write an ordinary Python file exporting `run(ctx)` (sync or async). Use `ctx.game`
for the inherited session/lease. Use the prepared interpreter from `.python-path` (see the skill entrypoint):

```powershell
& $gtaPython -m gta_use run .\policy.py --process 12345 --timeout 180 --stuck-timeout 30 --check-timeout 10
& $gtaPython -m gta_use status '<run directory printed by the runner>'
& $gtaPython -m gta_use cancel '<run directory printed by the runner>'
```

The supervisor owns the lease and heartbeat; a separate worker executes the file.
It terminates stuck workers and releases the lease on completion or failure. Keep
the overall deadline finite even for a long behavior tree. `--output` must name
a fresh directory. Imported helpers remain imports from the original script
directory; snapshot/version them separately with the entry script for reproducibility.

Use this harmless observation segment to check the environment before gameplay:

```python
def run(ctx):
    state = ctx.check(sections=["player", "weapons"])
    player = state["sections"].get("player")
    if player is None:
        ctx.uncertain("Player is not available; inspect loading or a transition.")
    entity = player["entity"]
    ctx.checkpoint("player-inspected", position=entity["position"],
                   health=entity["health"])
```

This confirms an observation, not mission progress. Runner completion requests
pause even if the script did not advance simulation. Check `result.json` for
confirmed pause before handing control to another agent.
The next movement segment must resume before submitting or waiting for a task;
starting a policy or accepting a native task does not unpause simulation.

## Give each segment an explicit contract

Before coding, record the starting objective and observable preconditions, target
identity, allowed controls, success predicate, progress metric, and recovery path.
Prefer one game checkpoint or tactical objective per run. A segment may contain
many actions and a reactive loop; do not split every 300 ms shot into a separate
model interaction. Keep pause/inspect as the fallback for an uncertain scene.

Use the shipped policies as concrete starting points:

- [cover_segment.py](../../../../tools/gta_use/examples/cover_segment.py): normal
  cover input, health checks, confirmed human targets, an aim-only peek, reload
  and gated head-aim/fire actions. `GTA_USE_TARGETS` can carry a JSON list of handles verified
  for the current objective. Set `GTA_USE_ALLIES` to the currently identified
  companion handles (or [] after checking none are present). It withholds fire
  for possible friendly obstruction and uses `requireAimTarget=True`.
  Its conservative 2D companion filter can reject a clear shot above a crouched
  companion; inspect actual 3D muzzle geometry before refining that filter.
  Its no-visible-targets checkpoint does not prove
  mission completion. Its health thresholds are an example policy, not universal
  tuning. It deliberately stops when cover geometry or firing angle is uncertain.
- [drive_to.py](../../../../tools/gta_use/examples/drive_to.py): observed driver
  occupancy, normal long-range driving task, vehicle condition, coordinate/road
  progress and bounded arrival. `GTA_USE_DESTINATION` is JSON with finite x/y/z.
  It is not a pursuit solver or guarantee of pathfinder success at gates/entrances.

Inspect current state before reusing either policy; do not reuse old target
handles, coordinates or tuning blindly. Extend a policy only after its missing
observation/behavior is understood. The SDK is in
[client.py](../../../../tools/gta_use/client.py), the context in
[segment.py](../../../../tools/gta_use/segment.py), and supervision in
[__main__.py](../../../../tools/gta_use/__main__.py).


## Identify the mission route, then choose transport

Identify the objective and intended access route before choosing transport. A
short run through an alley or over a fence may be necessary to reach the relevant
bike or car. Read fresh objective/companion/vehicle evidence and inspect a
screenshot when the route is uncertain. Do not optimize a list of nearby cars
before establishing where the current mission actually wants the player to go.

Always prioritize vehicles for reaching a destination. Observe nearby vehicles
and choose a suitable accessible car or bike; normal in-game carjacking is
allowed. Do not walk back to a personal vehicle when a closer suitable one can
be acquired. Mission-required vehicles, companions and current terrain still
constrain the choice. A model name or driveable flag alone does not establish
that a vehicle can leave its location.

Check entry eligibility before moving. Driveability and door/driver access are
separate: reject `CannotEnter` / `PlayerCannotEnter` for ordinary play, but do not
exclude `CanBeBrokenInto` or an occupied seat merely because the vehicle is not
unlocked and empty. Ordinary break-in/carjacking is allowed when its specific lock
state permits it; inspect `GTA.VehicleLockStatus` in the pinned XML for conditional
states. Estimate approach, entry and road-access time instead of selecting only
unlocked cars or choosing solely by straight-line distance. Verify actual entry
with a bounded attempt and try another eligible candidate if it fails; never
warp or change locks to manufacture success.

For a moving vehicle pursuit, cancel the previous on-foot follow task when the
objective changes. On-foot motion should terminate at the selected acquisition
vehicle, not chase the rider. Recheck the player's driver seat and target rider's
current vehicle before submitting a chase, and monitor both during the segment.

Keep acquisition, driving and arrival in a continuous bounded policy when the
scene is understood: approach the vehicle locally, use normal entry/carjacking,
verify the player is actually driving, submit a driving task, monitor progress,
then stop and leave for the final entrance. Walk when needed to reach a vehicle,
enter a building, or satisfy an on-foot objective; record the concrete reason
when no usable vehicle or drivable route is available. A failed walking route is
also a reason to reconsider transport, not just generate more foot waypoints.
Handle expected calls and objective changes within the policy rather than
starting a new model interaction for every brief input.

## Continuous driving and pursuit

Use `gta_use.driving.drive_to(ctx, destination)` for an observed road destination.
Its default speed is 28 m/s and its alerted driving style (`786469`) goes around
traffic and ignores lights while retaining braking. Slow down deliberately for
terrain, fragile vehicles or passengers; do not default to obeying traffic lights
in a time-sensitive mission. Speed is a request to GTA's pathfinder, not a promise
of safe cornering. It slows to 6 m/s near the goal and requires both distance
within 5 m and speed at most 1.5 m/s. Tune those parameters from the scene.

It submits one normal driving task, observes only player/vehicle at 2.5 Hz, and
leaves the game running on return. Reaching an intermediate distance is not a
reason to stop for the model. `on_tick(ctx, state)` may return True for a policy
transition; callbacks must not replace the helper's driving task. A 12-second
motion stall stops with evidence rather than resubmitting the same route. Select
an accessible road/entrance waypoint after inspecting the obstruction; do not
increase every timeout or force a straight-line path through unknown geometry.

```python
from gta_use.driving import drive_to, wait_for_control

result = drive_to(ctx, destination, timeout=600)
if result["outcome"] == "transition":
    state = wait_for_control(ctx, timeout=90)
# Continue the observed objective in this same policy. Capture the actual result;
# control returning, a cleared mission flag, or arrival alone is not Mission Passed.
```

`drive_to` returns `arrived`, `transition` (mission control/cutscene), or `policy`.
It does not clear a mission's new task during a cutscene. `wait_for_control`
observes actual cutscene-time progression, allows the mission flag to clear,
and keeps checking through the control-return phase. Give the enclosing runner
an overall budget that includes the transition, instead of applying a short
approach timeout to the ending cutscene.

For an identified moving rider use `pursue_vehicle(ctx, rider, vehicle,
rider_model=..., vehicle_model=..., on_tick=engage)`. This checks both entity
identities, driver occupancy and health, uses one native moving-target chase,
and continues through engagement range. The default desired separation is 8 m;
it is a task request, not enforced collision clearance. Do not treat reaching
25 m as a completed objective and hand back to the model while the rider escapes.
`engage(ctx, state, target)` runs inside the same continuous loop; use bounded
shooting/repositioning actions and return True only for a deliberate transition.
It must not replace the driving task, hide uncertain receipts or loop indefinitely.
The helper ends when the rider leaves the vehicle, control changes, or a policy
predicate succeeds. None of those proves a mission pass.

Closing distance or actual target-health loss resets the pursuit's 25-second
no-effect budget. Road travel, repeated aim matches and requested Attack do not.
Keep the overall chase deadline finite (180 s default); ordinary target movement
belongs inside the controller. Use target identity rather than a selection-radius
cutoff. The helper uses small native target queries and a player/vehicle pack;
add expensive nearby/LOS/probe observations only at engagement cadence.

The general `aim` command now adds VehicleAim/VehicleAttack when the player is
seated. Use `warmupMs=1000, ms=2200` as a bounded starting interval to keep aim
held through preparation before firing; the total includes warm-up. Ped targets
use their bone; vehicles/props use their origin plus optional `height` in world-Z
metres. Choose an observed visible point, for example `height=1.5` on the tested
car. For a rider, target the ped's bone; aiming at the bike while the engine
reports the rider correctly withholds fire. Check `session.phoneCall` and the
selected weapon first: an incoming call can interrupt vehicle weapon use. Handle
mission calls according to the objective rather than blindly cancelling them.
Keep `requireAimTarget=True` for target confirmation. Inspect `describe --command aim`
before using these parameters on an older installation. This is normal control
input, not forced damage. One stationary-car test hit spent a round and reduced
entity health from 1000 to 974 while body/engine health remained 1000. A later
ped-bone interval during pursuit spent three rounds and reduced rider health
from 200 to 110, but the car nearly stopped. Sustained accuracy at speed remains
unverified. Neither aim matches nor incidental damage establish successful fire;
correlate requested Attack, ammo and the intended target's health.

Vehicle shooting has different timing from on-foot `aim`. The latter's aim and
Attack receipts do not prove vehicle weapon discharge. Preserve enough continuous
time for equipping/aiming and measure ammo spent AND the intended target's health
across the interval. A shot into the bike is not a hit on the rider. After two
bounded permitted windows with no ammo change, inspect the active vehicle weapon,
controls and task interaction or switch a verified tactic; do not repeat dozens
of half-second trials. A three-second TASK_DRIVE_BY diagnostic became active but spent no ammo; do not
substitute it for verified player input. Any new native firing task requires live validation and
explicit cancellation when the target or firing geometry changes.


## Run and climb short access routes

Use `gta_use.navigation.navigate_to(ctx, destination, ...)` for a fixed on-foot
destination with optional `waypoints=[{x,y,z}, ...]`. Supply freshly observed
approach/far-side points; the helper does not infer mission objectives. For a
fence, choose a reachable approach and a landing point beyond the fence by more
than the arrival radius. A destination on another floor needs a stair/ladder/edge
route, not a matching horizontal coordinate.

```python
from gta_use.navigation import navigate_to

# Inside run(ctx), after identifying destination from current observations:
arrival = navigate_to(ctx, destination, waypoints=approach_points,
                      parkour=True, timeout=60, max_climbs=3)
ctx.checkpoint("access-route-reached", **arrival)
```

Default `route_mode="navmesh"` lets GTA route around obstacles. For an observed
short fence crossing where that detour is undesirable, use `route_mode="direct"`
with an approach and landing point across the panel, away from a gate hinge.
Every direct leg is limited to 30 m. It uses normal straight movement and the
same bounded climb recovery; shuffling without getting closer cannot reset the
stall timer. A three-ray fan runs only on a stall; if it misses, a lower fan
checks for low fences (at most six rays per recovery). If both miss, inspect
and select a clearer crossing instead of repeating the same failed waypoint.
Direct mode does not plan around buildings, water or cliffs; inspect the landing.
It resumes once, submits the selected movement task with no timeout warp, and
polls only player state. On a stall it reads the route result and bounded probes. A nearby static obstacle may trigger a short approach, natural turn and
normal GTA climb/vault task, followed by observed landing and resumed routing.
Task acceptance, an animation or an attempted climb is not arrival. It stops on
bounded unsuccessful attempts, health/control/identity changes or uncertain I/O;
no blind replay, forced movement, lock change or teleport is used. No C# compilation
is needed. Successful return leaves the game running for the next policy action;
the enclosing runner pauses when the segment ends.

Defaults: `radius=.75`, `stall_timeout=2.5`, `climb_timeout=7`,
`minimum_health=100`, `move_blend_ratio=2.5`, `check_interval=.2` seconds for
polling. `parkour=False` stops at a stall; adjust limits deliberately for the scene.
This is bounded recovery using GTA's own climb detection, not a guarantee that
any fence, wall, rooftop, ladder or gap can be traversed. Inspect and change the
route after failure. For a standalone run use
[navigate_to.py](../../../../tools/gta_use/examples/navigate_to.py) with JSON
`GTA_USE_DESTINATION` and optional `GTA_USE_WAYPOINTS`.

## Find a task without guessing its ABI

For ordinary movement/vehicle tasks, the local SHVDNE XML documents **C# API
names**, not all native hash names. Search `.deps/shvdne/ScriptHookVDotNet3.xml`
for `M:GTA.TaskInvoker.FollowNavMeshTo`, `M:GTA.TaskInvoker.EnterVehicle`,
`M:GTA.TaskInvoker.DriveTo` or `T:GTA.VehicleDrivingFlags`. Read the matching
overload's parameters and use a short `game.command` with the typed SHVDNE API.
Read the actual `<member name="...">` entry, not the first `<see cref="...">`
search hit. For example, PowerShell can select an overload without dumping the XML:

```powershell
$api = [xml](Get-Content .deps/shvdne/ScriptHookVDotNet3.xml -Raw)
$api.doc.members.member | Where-Object { $_.name.StartsWith('M:GTA.TaskInvoker.FollowNavMeshTo(') } | Select-Object -First 1 -ExpandProperty OuterXml
```

Do not assume that an absent `TASK_*` XML search means no movement API exists.
For raw natives, verify the upstream signature before using `game.native`.
Convert observed handles with `Entity.FromHandle(handle) as Vehicle` (or `as Ped`),
then check null, existence and current role. Do not guess public `new Vehicle(handle)`
or `new Ped(handle)` constructors; they are unavailable in the pinned API.
XML can mention nonpublic constants such as `TaskInvoker.DefaultNavmeshFinalHeading`;
omit optional arguments or inspect the loaded public overload and its defaults
instead of assuming every XML symbol is callable.

Submit a navigation task once per goal, then observe progress. The in-game C#
compiler has a 128-compilation-attempt budget per managed session. Successfully
compiled identical source is reused in current builds, but every explicit call
still executes against fresh state; no action result is cached. Older deployments
compile every call. Failed compilation attempts consume the budget; runtime
failures do not require recompilation of successfully compiled source.

State loops belong in Python and should use filtered packs or `game.native` for
simple queries. For repeated compound observations, register one bounded read-only
provider and poll its section. For a recurring batch guard with changing entity
handles, register a parameterized namespaced command once through
`GTAVMods.Agent.BridgeScript.Active.Commands` and pass handles as arguments.
Do not interpolate changing coordinates/handles into new C# on each loop iteration;
source reuse cannot prevent that from exhausting the budget. Keep an explicit
reload/revalidation recovery path, and disable Thor again after reload for normal
mission testing. Reload can change gameplay state, so record any resulting effects.
A long route can continue while Python checks health, position and the goal.
Avoid clear/reissue loops when the same task is making progress.
For a moving vehicle target, also inspect `M:GTA.TaskInvoker.VehicleChase(GTA.Ped)`.
Its contract requires both peds in vehicles. A chase task tracks a moving target;
repeated routes to its old coordinates can waste acceleration and pathfinding.
Expected target motion should update or transition the current policy, with
bounded recovery, rather than stop the run after every small displacement.

A straight-line task does not route around a building. Use navmesh movement for
walkable routes, then inspect `PED.GetNavMeshRouteResult()` when position stalls:
`TaskNotFound`, `RouteNotYetTried`, `RouteNotFound` and `RouteFound` distinguish
missing tasks, pending searches and path results. A found route still needs
observed movement. Repeating the same blocked goal is not recovery. Inspect the
entrance and probe directions, choose a reachable exterior waypoint, or use normal
controls for a short verified passage. A mission blip can be inside a building;
its coordinates do not identify the accessible doorway. Keep automatic warping
out of ordinary-play policies when choosing navigation overloads.

The runner already provides `ctx.directory`, `ctx.log(event, **data)`,
`ctx.check`, `ctx.progress`, `ctx.checkpoint` and `ctx.uncertain`. Run the small
observation example before adding navigation/combat. Add helpers only when an
actual segment repeats that logic. `ctx.check` terminates the current run on
observed player death; recovery starts a new run after the real Retry flow.
## Reactive loop / behavior tree design

Use a priority selector with persistent node state. Each tick consumes one fresh
pack, selects one action, and returns to observation. Keep per-node start time,
target identity and expected outcome so tasks are not resubmitted every frame.

```text
If player missing, session changed, or objective unknown -> stop and inspect
If player dead or vehicle unrecoverable -> stop; recover real mission checkpoint
If health critical -> preserve life: verified cover/retreat; bounded recovery check
If exposed to confirmed enemies -> acquire a reachable cover point; verify inCover
If reloading / clip empty -> wait / request reload; verify ammo changes
If a confirmed enemy is visible from cover -> short aim/fire; verify target damage
If enemies remain but firing is blocked -> inspect/reposition along verified cover
If route/objective is known -> advance bounded movement; verify position progress
If the observed success predicate holds -> checkpoint with evidence; return
Otherwise -> uncertainty with state/screenshot; no blind action repetition
```

This is a policy design, not an installed `find_closest_cover` command. There is
no universal cover finder in the current API. Build candidate cover from actual
geometry/probes or a scene inspection, test reachability with bounded movement,
then verify cover acquisition. Do not substitute rushing toward the enemy.

Keep movement, combat and driving nodes general: accept goals/predicates and
tuning data rather than hardcoding one mission's coordinates into the bridge.
Use explicit transitions such as moving -> arriving -> stopped, exposed ->
entering cover -> in cover -> aiming. Read back the transition before the next
dependent action. Keep Lua/C#/native loops off the game thread; Python owns policy.

`game.action("resume")` once begins continuous play. Timed controls and aim actions
are frame-driven by the bridge. `game.action("aim", entity=handle, bone=31086,
ms=350, shoot=True, requireAimTarget=True)` aims/fires briefly;
it does not choose a target, find cover or
alter health. Firing requires LOS; the optional gate also requires the engine
aim-target handle to match. Use the returned frame counts and `last` observation
to distinguish an obstruction, unknown aim and requested Attack. Requested Attack
is not proof of a shot. Measure ammo/health across the whole interval, and inspect
a different protected angle when fire is withheld or damage remains unchanged.
Do not reissue a long native navigation task each iteration if it is progressing.
Pause stops time; it does not erase a persistent native task before the next resume.
`cutsceneActive` can describe a loaded cutscene before playback starts; use
`cutscenePlaying` and advancing `cutsceneTime` to establish playback. If time
does not advance, inspect focus and the visible UI before retrying. Record any
temporary focus-loss setting change and restore the original value afterward.

## Shoot from cover by observing the peek transition

Tucked-position LOS can be blocked by the player's own cover. Do not require it
before trying a bounded **aim-only** peek at a confirmed target. Use normal Cover
input, allow `player.goingIntoCover` to finish, and verify `player.inCover`.
Hold aim without firing (the example uses 800 ms), then refresh target identity,
health, ammunition and companion positions before a gated firing interval.
The duration is an example, not proof that a particular animation has completed.

Observe `player.aimingFromCover`, `player.coverFacingLeft`, `weapons.freeAiming`
and `weapons.readyToShoot` during the action, using state requests alongside an
accepted batch or a stream. Aim results retain `inCoverFrames`,
`aimingFromCoverFrames` and `weaponReadyFrames` plus their last observations.
Pause can reset transient aim/readiness flags while freezing the aiming pose;
a paused false value does not prove that the player never aimed from cover.
Older deployments can query the corresponding GTA natives or register a bounded
read-only provider through `((GTAVMods.Game.AgentWorld)CONTEXT.World).StatePack`.

Verify companion clearance separately. A 2D corridor is a conservative first
filter; it cannot distinguish a crouched companion below the muzzle from an
obstruction at gun height. For ambiguous geometry, inspect fresh 3D positions
and a ray from the actual weapon muzzle to the target bone, ignoring the player.
Check the ray completed and which entity it hit first. A missing muzzle bone,
incomplete ray or unknown companion position is unresolved, not a clear shot.
Recheck when either entity moves; a ray sample is not a future hit guarantee.

Then fire a bounded interval with `requireAimTarget=True` and measure whole-
interval ammo and target health. Requested Attack frames can precede a shot or
occur during a transition. One short interval with no ammo spent does not prove
that cover firing is unsupported. Continue a bounded verified transition or
change the protected angle based on evidence; do not abandon cover solely because
the initial tucked LOS was false. Log waiting as waiting, not as progress.

## Observe, progress, checkpoint

`ctx.check(**filters)` fetches and persists a pack, updates the observation timer,
and detects player death. `ctx.observed(pack)` does the same for a WebSocket pack.
Socket Ping/Pong and successful command receipts are not observation/progress.

`ctx.progress(message, **evidence)` resets the stall timer only for meaningful
change. Examples: distance reduces beyond a tolerance; a new road cell is reached;
cover is first acquired; confirmed target health decreases; objective stage changes;
cutscene time advances during a bounded wait. A long cutscene can exceed the stall
timeout even while observations keep arriving; report its actual time progression.
When following a moving companion, distance can stay constant during useful
movement; track actual world positions or new route cells as well as separation.
Do not call it unconditionally in every loop, for repeated retries, or merely
because the controller is alive. Avoid metrics that permit endless circles.

`ctx.checkpoint(name, **evidence)` requires nonempty evidence; call it only when
the predicate was observed, then return. Returning without it fails. The name is
your policy's label, not a GTA retry checkpoint. Mission completion requires the
actual game's completion signal, normally a screenshot of Mission Passed.

`ctx.uncertain(reason)` raises a stop condition. Include enough context in logs to
decide the next action. Do not catch it and continue the policy. Do not suppress
`UncertainOutcome` and retry a timed-out mutation: inspect current game state and available diagnostics.

## Async policies and timing

For a streaming decision loop, use one async client and feed observations back
to the supervisor context:

```python
async def run(ctx):
    async with ctx.game.connect() as game:
        async for state in game.states(hz=5, sections=["player", "weapons"]):
            ctx.observed(state)
            player = state["sections"].get("player")
            if player is None:
                ctx.uncertain("Player unavailable during stream.")
            ctx.checkpoint("stream-inspected", position=player["entity"]["position"])
            return
```

This is a connectivity example; replace the predicate with a real policy for
gameplay. The receiver keeps the latest pending state, not every historical frame.
Correlated requests may coexist with the stream, but the action scheduler still
allows one batch at a time. Do not run synchronous `ctx.game` I/O on the async
event loop while expecting it to service a stream; use the async client's methods.

Match action length and observation cadence to risk. Health-sensitive combat
needs frequent checks; a known drive can observe less frequently. The worker must
check before `checkTimeout` and demonstrate meaningful progress before
`stuckTimeout`; supervisor heartbeats do not extend either. Use monotonic time for
local node deadlines, game-time actions for simulation, and wall-time deadlines
for detecting stalls. A suspended game timer must not disable all timeout paths.

## Iterate from evidence

Read the failed run's last state, progress, stdout/stderr, events and current game state.
Classify the failure: invalid schema, stale identity, missing observation, task
not completed, physics/pathfinding mismatch, poor policy, suspended game ticks,
or transport/storage failure. Fix that cause and rerun from the observed checkpoint.
Keep failure artifacts so a successful retry does not erase the evidence.

The Repossession test showed useful limits: cover input succeeded where an NPC
cover task did not; rooftop shots needed cover-edge repositioning; hostile groups
included animals; naive pursuit and a navmesh shortcut failed. Later isolated
tests verified vehicle-seat fire and a low-fence climb, with evidence limits in
the evaluation report. Verify each mechanic in the current scene instead of
assuming native task acceptance proves success.
