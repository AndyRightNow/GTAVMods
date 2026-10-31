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

A character-switch request must be followed by the requested player model/identity
observation. `wait_for_control` can return the old character while switching is
mission-locked; control availability alone does not establish a successful switch.
Stop after a bounded no-effect attempt and inspect the current mission prompt.

`ctx.checkpoint(...)` records evidence; it does **not** exit the function or pause.
Use `return` immediately afterward when it ends a segment, especially inside a
loop. The supervisor pauses only after the function exits. Accidentally continuing
a loop after a checkpoint can let a timed target escape while producing repeated
captures. `ctx.uncertain(...)` raises and stops; it has different control flow.

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

For an explicitly requested uninterrupted mission, compose the verified phases
inside one `run(ctx)` and reserve `ctx.checkpoint` for the final result. Helpers
that return with simulation running can transition directly to the next phase.
Keep stage deadlines and failure stops: a failed run remains a failed attempt,
not a partial success to concatenate with a later checkpoint run. Record source
hashes, phase times, action logs and the final Mission Passed image. Count game
retries, policy restarts and host interventions separately.

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
- [repossession.py](../../../../tools/gta_use/examples/repossession.py): a
  single-invocation policy now composed from the reusable SDK helpers below.
  Its pre-extraction revision completed Repossession in 17m 4s without an
  intermediate pause, retry or host input. Its route anchors and tuning are
  specific to the tested original-house save and mission; use its phase/observation
  pattern, not its coordinates, for another mission. Read the
  [replay conditions and evidence](../../../../docs/continuous-repossession.md)
  before running it. It disables Thor and drives through live Story gameplay.

Inspect current state before reusing a policy; do not reuse old target
handles, coordinates or tuning blindly. Resolve a known model name with
`game.native("GET_HASH_KEY", "bagger", returns="int")` (using the relevant name)
and compare it with the freshly observed model. Do not guess numeric hashes;
a valid but wrong hash silently prevents an objective transition. Extend a policy only after its missing
observation/behavior is understood. The SDK is in
[client.py](../../../../tools/gta_use/client.py), the context in
[segment.py](../../../../tools/gta_use/segment.py), and supervision in
[__main__.py](../../../../tools/gta_use/__main__.py).

## Visible in-game cursor interfaces

`from gta_use.ui import click_at` provides `click_at(ctx, x, y)` for a freshly
observed game UI target. Coordinates are fractions of the full game image,
not pixels. Resume first; the helper verifies the cursor position, then sends
one bounded left click. `button="right"` uses the normal cursor cancel control.
It withholds a click if positioning fails, the observed cursor differs, or the
pause menu opens. Uncertain receipts stop without replay. Inspect the resulting
UI: `outcome="input-submitted"` does not mean the button or mission succeeded.

This helper does not focus an interface or control the operating system. During
Friend Request, long tool gaps let the idle camera hide the fictional computer
screen even while simulation was paused. A short normal W tap restored that
specific screen; do not generalize that key to other interfaces. One continuous
visual Python loop closed 14 observed pop-up X buttons in 13.4 seconds. The
antivirus then required distinct Scan Computer and Exterminate clicks; waiting
for player control after Scan alone stalled. Interpret visible prompts rather
than assuming every non-controllable scene will finish automatically.

The extracted helper also positioned the cursor correctly over Michael's phone,
but its CursorAccept input did not open Contacts. Normal Enter did. Phone list
navigation used arrow keys and Enter; do not retry cursor clicks merely because
the input receipt succeeded. Cursor-driven computer interfaces and phone menus
use different controls. After the mission, the extracted helper successfully
opened an observed article link and closed GTA's fictional browser, with
before/after screenshots and cursor readback retained. Right-click behavior is
covered by automated scenarios but has not yet been verified live.

## Physical key fallback

Use ordinary controls/tasks first. If a verified interaction ignores simulated
control values, use `ctx.game.hold_key("H", 2500)` after resuming. This holds one
actual key for 1..5000 ms in foreground GTA under the current controller lease.
The Rust service releases on the deadline, focus loss, failed lease verification
or normal shutdown; Python cancellation does not leave an indefinite hold. Fatal
service/OS termination cannot guarantee key release. Chords and overlapping holds
are rejected, and an already held user key is preserved.

Inspect the game after delivery: a released-key receipt is not objective success.
On an uncertain response, never automatically replay. Keep the hold shorter than
the runner observation deadline. This is not a pause-menu recovery API: lease
validation needs game ticks, which save/failure menus may suspend. Use normal
computer-use UI there, then acquire a fresh supervised lease after returning.

Pulling Favors exposed this distinction: injected H-bound controls read as pressed
but did not release the tow hook. One actual 2.5-second H hold released it and
produced Mission Passed. The engine's internal reason is unverified. For towing,
verify the hooked entity during travel, approach a gate through its opening, and
stop on the actual drop-off prompt rather than forcing a coordinate past a wall.
Do not replace the interaction with attachment/detachment natives.

### Scripted lenses and physical mouse input

Camera glasses and similar mission interfaces can ignore injected Look/Attack
controls. Use `ctx.game.move_mouse(dx, dy)` for a small relative movement, and
`ctx.game.hold_mouse("left", 100)` for one shutter press without pointer movement.
These require resumed foreground GTA and the current lease. They share the
keyboard's bounded hold, focus/lease checks and automatic release. Inspect the
result; never replay an uncertain request. Delta values are signed integers
(-2048..2048), positive right/down, not absolute screenshot coordinates. Camera
sensitivity changes the visible effect. Ordinary UI `click_at` is a different
control path and should not be used to aim a raw-input camera.

Read `state['sections']['camera']['rendered']` for the actual displayed position,
rotation and FOV. The original camera fields remain the gameplay camera and can
stay unchanged while a scripted lens turns. Do not use that stale view to judge
whether a mouse action worked. Camera input does not prove the mission accepted
a photo: inspect the feedback/objective before advancing.

## Compose reusable SDK operations

Look up the shipped signature before composing a helper:

```powershell
& $gtaPython -m gta_use helpers
& $gtaPython -m gta_use helpers navigation.navigate_to
& $gtaPython -m gta_use helpers combat
```

The query prints imports, signatures and docstrings without connecting to GTA.
Do not transfer argument names between helpers: `navigate_to` uses `radius` and
`move_blend_ratio`; `drive_to` uses `radius`, `speed` and `arrival_speed`.
A synchronous `Client` must be closed in `finally`; it is not a context manager.
Inside a supervised policy, use the runner-owned `ctx.game`.


Prefer these bounded helpers to copying native/C# loops into each mission. They
use the current `ctx` lease and observations, never start another controller, and
do not checkpoint or pause on success. Policy code chooses objectives, targets,
routes and recovery tactics. A lost action receipt propagates without replay.

| Operation | Contract |
| --- | --- |
| `vehicles.acquire_vehicle(ctx, desired=None, passenger=False, companion=None)` | Ordinary entry/carjacking; verifies the actual driver seat. `desired` and `companion` are observed `{handle, model}` entities. Prefers ordinary road cars/bikes/vans, then stationary vehicles and distance; `preferred_classes` customizes ranking and `desired` selects a required vehicle. Accepts a passenger seat already occupied by the identified companion. Stops if a different vehicle must first be exited. |
| `vehicles.leave_vehicle(ctx)` | Brakes when driving, requests ordinary exit without automatic blocked-door warping, waits for an on-foot observation. |
| `vehicles.recover_upright(ctx)` | Bounded ordinary steering, followed by observed wheels-on-ground; never rotates or teleports the car. |
| `navigation.follow_on_foot(ctx, target, on_tick=...)` | One persistent normal task toward an observed actor; checks current identities, health, route movement and bounded separation. Returns on cutscene, target death/boarding or a caller predicate. Use for companion following or a confirmed on-foot pursuit; a stationary target/obstacle stops for inspection. |
| `navigation.climb_toward(ctx, facing)` | One natural turn and normal climb at a caller-observed nearby edge, including a stationary vehicle/roof approach. Returns `climbed`, `landed`, `no-effect` or `transition`; inspect the final position. It does not move to the facing point or prove route arrival. |
| `navigation.climb_ladder(ctx, facing, direction="up")` | Mount a visible nearby ladder and supply bounded ordinary movement input until a stable exit with signed height progress. Use `direction="down"` to descend; omit `facing` only when already attached. Stops on stalls, identity/health changes or unknown outcomes; preserves mission takeover. |
| `combat.melee_burst(ctx, target)` | One unarmed Aim/light-punch exchange with fresh identity, range, facing and health checks. Reports observed damage; returns immediately on mission takeover. Caller owns bounded retries, defense and recovery. |
| `combat.take_cover(ctx)` | One cover tap, then waits for settled cover; does not toggle during entry. Requires an observed suitable cover location. |
| `combat.fire_from_cover(ctx, target, allies=...)` | One aim-only peek, current muzzle/companion geometry check and gated burst. Reports `not-ready`, `withheld` or `fired`, ammo and observed target-health change. Policy owns reload, healing waits, angle recovery and enemy selection. |
| `combat.fire_from_vehicle(ctx, target)` | One bounded target-gated burst from the current seat without replacing the driving task. Rechecks target identity/range, observes ammo spent and damage; withholds during phone calls or without usable ammunition. |
| `combat.muzzle_clear(game, target, allies=...)` | Current 3D muzzle ray and ally clearance for custom firing policies; aim/peek first. Supply every identified companion. |
| `driving.intercept_vehicle(ctx, rider, vehicle, rider_model=..., vehicle_model=..., waypoints=...)` | Persistent interception route then moving pursuit; defaults to chase. `engagement="ram"` explicitly opts into ordinary collision damage. Rider separation is an observation, not mission completion. |
| `driving.follow_entity(ctx, target)` | One persistent normal vehicle-follow task toward a moving ped, object or vehicle. Validates observed `{handle, model}`, driver identity and recent gap closure. Returns `near-target`, `policy`, `target-dead` or `transition`; proximity does not establish boarding. |
| `driving.wait_for_control(ctx, stable_for=2)` | Requires control to remain available for the requested interval, restarting that interval if a cutscene returns. |
| `ctx.capture("stage-name", state=state)` | Retains a numbered screenshot and evidence in the run directory. `allow_unavailable=True` may skip only a foreground-window rejection, never uncertain transport. |

```python
from gta_use.vehicles import acquire_vehicle, leave_vehicle
from gta_use.combat import take_cover, fire_from_cover

# Supply target/companions only after identifying them in fresh mission state.
car = acquire_vehicle(ctx, passenger=True, companion=companion_entity)
# ... drive to the observed mission approach ...
leave_vehicle(ctx)
# ... navigate to an observed cover location ...
take_cover(ctx, minimum_health=120)
burst = fire_from_cover(ctx, target_entity, allies=[companion_entity])
if burst["outcome"] == "fired" and burst["damaged"]:
    ctx.log("tactical-outcome", target=burst["target"], health=burst["after"])
```

When walking into an observed mission entrance/contact, use
`navigate_to(..., allow_transition=True)` to accept the expected control takeover.
Check `result.get("outcome") == "transition"` and call `wait_for_control` before
reading the next objective. The result has `arrived=False`: a cutscene does not
prove arrival or mission completion. The default still stops on control loss;
menus, missing/changed players, health failures and vehicle entry remain stops.

Vehicle entry uses native timeout `-1`; Python owns the deadline. A finite native
entry timeout can warp the ped even without an explicit warp flag. Do not restore
that fallback to make a blocked approach look successful. Exit also disables the
engine's default blocked-door warp.

Target/companion identities, cover placement and tuning remain the caller's
responsibility. An unknown human can still move into a later shot; current muzzle
clearance is not a promise about the whole burst. Helpers use narrow state filters
and combine candidate eligibility or pursuit target reads into one immediate C#
request. The bridge caches repeated source within its 128-compilation session
budget; they do not add a durable request store or a second scheduler.


### Unavailable ammunition observations

`weapons.ammoInClip: null` means the engine did not provide a clip count;
it does not mean zero. In Marriage Counseling the holstered vehicle pistol
reported null while total ammo was 450. A policy that used `if not ammoInClip`
repeated reloads without shooting and exposed the player to incoming fire.
Reload only for an observed zero, with a bounded wait and fresh observation.
`fire_from_vehicle` permits one target-gated burst when clip count is unknown
and total ammunition is present; assess ammo spent and damage before repeating.
An unknown clip does not establish that shooting will work.
After leaving a vehicle, inspect the selected weapon again: the game may select
Unarmed. If necessary, select a verified already-owned weapon, then observe the
new loadout. Reload cannot turn Unarmed into a firearm. Do not grant weapons or
ammunition to satisfy a combat precondition.

## Scripted boarding and vehicle task conflicts

Use `entities.observe_entity(game, observed_target, relative_to=observed_vehicle)`
for one compact, read-only target/relative-pose observation. It validates both
handle/model identities and returns `None` if either no longer matches. The
result includes position, velocity, health, `inVehicle`, `vehicle` (ordinary seated occupancy),
`attachedTo` (direct engine parent) and `relative` (vehicle-local x/y/z). Without
`relative_to`, `relative` is None. This does not resume or alter the game; callers
still own current-objective interpretation and bounded observation policies.

In Chop, the dog visibly sat in the van while `vehicle` stayed 0;
`attachedTo` identified the van throughout normal driving. A boarding predicate
that required every companion to occupy a seat caused an unnecessary policy stop.
D's foot-pursuit phase also returned the previous bike from
`GET_VEHICLE_PED_IS_IN(..., false)` after the observed dismount. The helper gates
that handle with `Ped.IsInVehicle()` so a stale vehicle handle cannot end foot
following as a false boarding event. Keep raw native returns distinct from
confirmed current occupancy.

Check actual attachment and travel-relative pose when the seat flag conflicts
with the scene. Never attach or warp a companion to manufacture boarding.


During Father/Son, Jimmy was visibly riding in the player car while
`GET_VEHICLE_PED_IS_IN` and `IsInVehicle` still returned false. Franklin also
landed on the hood before ordinary seated occupancy became true. Combine the
current objective, vehicle-relative pose over several observations and a
screenshot at ambiguous transitions. Once the actor travels with the player car,
stop targeting that actor with a driving task and inspect the next objective.
Proximity alone is not proof of a completed rescue.

Use `follow_entity` when the target is an observed actor on a trailer or otherwise
not seated in a target vehicle. Its `min_distance` is an integer native request;
`arrival_radius` is the measured 3D gap at which the helper returns. Neither
promises a precise offset. For exact alignment, approach on the road first, then
use a bounded local steering controller after checking obstacles. Do not send a
road pathfinder repeatedly toward an inaccessible garage center; inspect the
entrance and align at low speed.

In the Father/Son shooting phase, `TASK_VEHICLE_CHASE` suppressed ordinary player
vehicle fire: the same input spent ammo after clearing the driving task but spent
none while chasing. `TASK_DRIVE_BY` beneath the driving task also had no effect
there. Prior Repossession firing evidence remains valid for that scene. After a
bounded no-ammo/no-damage burst, inspect task interference before repeating it.
Use ordinary sustained steering/throttle plus aim/fire when simultaneous AI
vehicle tasks block player shooting. `keepInputs=True` preserves held controls,
but cannot make a game task permit firing. Track ammo across **every** observation,
including increases from mission replenishment; accepted attack frames are not
shots fired.

Captures block the Python feedback loop: avoid taking screenshots during a sharp
manual turn with steering held. Measured analog steering had a 0.25 deadzone in
this setup; use observed input feedback when calibrating small corrections,
rather than increasing control duration to compensate for zero effective input.

Menu controls can use `clock="wall"` and group 2 for `FrontendAccept` /
`FrontendCancel` (verified in Los Santos Customs). The save-slot menu suspends
SHVDNE; after one timeout use the visible menu. A supervisor pause or lease expiry
can leave the returning game frozen. Reacquire through a fresh supervised run
and resume before waiting for gameplay; an expired token cannot resume it.

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
Before a timed pursuit, plan the departure as well as vehicle entry. A correct
heading can still leave the car against stairs or on a sidewalk. Verify the
stopped position and physical clearance; choose a turn toward the observed road.

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
then stop and leave for the final entrance. A passenger seat occupied by the
verified mission companion already satisfies companion boarding; do not reject
that vehicle merely because the seat is not free. Walk when needed to reach a vehicle,
enter a building, or satisfy an on-foot objective; record the concrete reason
when no usable vehicle or drivable route is available. A failed walking route is
also a reason to reconsider transport, not just generate more foot waypoints.
Handle expected calls and objective changes within the policy rather than
starting a new model interaction for every brief input.

## Continuous driving and pursuit

When implementing feedback steering or sustained movement, use
`ctx.game.set_inputs([{"control": "VehicleAccelerate", "value": .5}], ttl_ms=1000)`
after resuming. It returns immediately and holds input every frame while state
requests run. Each update replaces the complete set; include steering and brake
values in the same update. Short timed bursts followed by observations leave
input gaps and can lose acceleration or steering authority at speed.

Renew before the independent wall-time TTL (50–5000 ms) or the bridge releases
input and pauses. Supervisor heartbeats do not renew this deadline. Choose it
above the measured observation-loop latency, and treat expiry as uncertainty.
`release_inputs()` releases the set without pausing; pause/cancel/lease loss,
reload and timed controls/wait/aim also clear it. For simultaneous continuous
driving and a short aim burst, pass `keepInputs=True` to `aim`. The original
input TTL remains in force and expiry stops aiming as well as driving; renew
before the burst and include its wall time in the renewal budget. The default
`aim` behavior still clears held input. A timed throttle/steering burst followed
by state requests leaves gaps, even when aiming is composed into that burst. `LiveClient` exposes the same
async methods. Keep the policy bounded and verify motion; held input is not a
pathfinder or obstacle-avoidance guarantee. Inspect `status.inputs` if unsure
whether a set is still active.

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
The helper brakes and clears its driving task on ordinary completion; repeatedly
calling it with short timeouts to pursue a moving point sacrifices momentum.
Use a persistent pursuit task or a deliberate road-interception phase, then
switch to the freshly observed target. Road distance and interception geometry
matter when direct chasing takes an unproductive route. At freeway interchanges,
3D distance alone does not prove the vehicles share a road. Check elevation,
heading and the actual ramp before switching from road pathfinding to local
relative steering; a target directly above another road is not reachable by
steering toward its coordinates. Keep route recovery bounded and inspect the
next real checkpoint after a lost-target failure.

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
scenario-dependent. In the 2026-10-29 Repossession recovery, a native chase plus
two gated 1.1-second rider bursts stopped the rider in under 20 seconds; the
first requested no Attack frames, the second requested ten and observed health
100 to 0. This validates that chase/aim combination, not arbitrary high-speed
accuracy. `fire_from_vehicle` extracts it with fresh range, identity and ammo
observations. Use it in a pursuit callback, with a bounded no-shot allowance:
two windows without ammo expenditure require diagnosis or another verified
tactic. Neither aim matches nor incidental damage establish successful fire;
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

In a Repossession test, a long-range driving task kept weapon readiness false;
clearing that task restored readiness but did not establish accurate moving fire.
Do not assume arbitrary driving and firing tasks cooperate. Where the objective
allows physical interception, a verified vehicle mission can be an alternative:
`TASK_VEHICLE_MISSION` with mission type `2` (Ram) succeeded in a pursuit test
through ordinary collision physics. Verify the native signature and target
identity, then observe rider state and the required vehicle's condition. This is
an optional tactic, unsuitable when the target must remain unhurt or undamaged.


## Run and climb short access routes

For a moving companion or a confirmed on-foot pursuit, use
`follow_on_foot(ctx, target, timeout=180, stall_timeout=8, max_gap=120, on_tick=...)`.
The target is a fresh `{handle, model}` identity. The default offset is two metres
behind it, move blend ratio 3, and stopping range 2. The helper follows continuously;
being close alone does not complete it. `on_tick(ctx, state, target)` can return
True for an observed objective change. It must not replace the owned follow task.
Use a smaller walking/running blend when following a slower companion.

The helper returns `policy`, `transition`, `target-boarded` or `target-dead` with
observations, never Mission Passed. It stops on stationary/blocked movement,
excessive gap, identity/health/water failures or an uncertain receipt. For a
stationary interaction, a policy can return after several close, stationary target
observations rather than letting the movement watchdog expire. Inspect geometry
and use `navigate_to`/`climb_toward` after an obstacle stop; this following task does
not promise to solve arbitrary fences. It preserves mission-owned tasks at a
control takeover and leaves successful simulation running for the next phase.

Live in Chop: it followed D from the bus depot to the railway-search transition,
with Franklin and D both at 200 health. This verifies that route, not every path.


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

For an observed vehicle-to-roof access route, use `climb_toward` from the
outside of the ledge. Inspect vertical collision/roof geometry when the next
edge is unclear; walking farther underneath an overhang can remove the reachable
edge. In Complications, normal truck-rack and roof climbs worked, while a farther
approach under the roof produced a jump back to ground. The helper observes
animation and two settled samples; `landed` can be an ordinary jump, and neither
landing outcome proves the intended destination was reached. Keep approach and
landing decisions in the mission policy.

For an active unarmed fistfight, use `melee_burst` after identifying the opponent.
It does not aim-lock or turn toward an arbitrary ped. Reobserve before each
exchange, stop on a bounded no-damage budget, and preserve mission ending tasks
when it returns `transition`. Normal light-punch input worked in the Complications
Simeon fight; the optional gardener stealth takedown did not animate in the tested
setup, so do not assume this helper solves every contextual takedown. Two early
punch windows in the active fight had no damage before later punches connected.

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
its coordinates do not identify the accessible doorway. Choose the observed
entrance before routing around the building. At Lester's first visit, navigation
stopped at the front door and a short camera shot returned to controllable play
without setting `missionActive`. Waiting there did not advance anything. A bounded
ordinary forward walk opened the door; navigation inside then reached Lester and
started the actual mission cutscene. This does not establish why the earlier
native task stopped or mean every scripted camera is a mission takeover.

For a visibly reachable doorway, try one short normal movement interval after
checking the current camera/facing, then inspect actual position and the scene.
`hold_key("W", ...)` moves relative to the current view; backing away can turn the
camera, so blindly following S with W need not retrace the path. Do not unlock
objects or clear a newly installed scene task to force entry. Use another route
only when observed geometry supports it. Keep automatic warping out of ordinary-
play policies when choosing navigation overloads.

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
Poll the transition within a finite entry deadline instead of tapping Cover
again after a fixed short sleep; a second tap can cancel entry or leave cover.
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

### Marker height and vehicle-exit recovery

Blip positions are not necessarily standing-ped destinations. A ground-level
marker can be roughly one metre below the player's entity origin. Navigation
compares full 3D distance; inspect ground height and the observed standing offset
before using a tight arrival radius. Preserve the intended level on stairs,
bridges and roofs. At Franklin's Chop contact, a ground-checked short approach
around the parked truck triggered the cutscene; the direct marker route stalled.

A supervisor timeout can pause the world while leaving a persistent native task
installed. Reobserve occupancy before choosing the next action. If an old vehicle
entry completes after resume, use or exit the actual vehicle; do not assume the
new target was acquired. Replace only a task the policy owns, and preserve
mission tasks on a control transition.

### Raw markers and contextual following stops

`navigation.gpsTargets` describes routed/waypoint targets, not every mission goal.
After the Friend Request clothing purchase, the yellow Lifeinvader marker was
present in `navigation.blips` with `routed=False` while the player was on foot.
An empty GPS list did not mean the mission had stalled. Check the visible objective
and full bounded blip set (`blipLimit=256` if truncated) before waiting or retrying;
select a target from that evidence, not from color alone.

A rendered marker's apparent color need not equal `navigation.blips[].color`.
During Chop, D and Chop both returned raw color 49 despite different visible
markers. Identify the current objective and observed entity/model before waiting
for an assumed color. Do not treat every loaded mission ped as an enemy.

When following stops at a stationary companion, inspect the current prompt and
geometry before retrying navigation. Chop required a close contextual interaction
at the distracted dog and ordinary Context at the boxcar. A delivered key from
outside the interaction range did not advance the objective; that alone did not
show a bridge input defect. See the [Chop evaluation](../../../../docs/chop-evaluation.md)
for the failed traces, live helper verification and assistance limits.

### Ladders and trustworthy roof waypoints

`player.climbingLadder` is separate from `climbing`, `inAir` and `falling`. All
three ordinary flags can be false while the player is still attached to a ladder.
`navigate_to` preserves that attachment and stops for an explicit ladder decision;
use `climb_ladder(ctx, direction="down")` to continue an observed descent. Before
mounting, approach the visible ladder end and supply a nearby facing point. A
successful native mount can leave the player stationary until movement is held.
The helper supplies that input, bounds time and progress, and waits for the exit
animation and position to settle. Its compatibility fallback uses a native read
when an older installed bridge lacks the new field.

Do not turn every sampled movement position into a return waypoint. A sample may
be on a rung, midway through a dismount, or over a parapet; a vertical ray can hit
a roof appliance instead of the walking surface. Record settled landings and
identify the connecting stairs, ladder or climb. For a short roof route, ordinary
navmesh walking can go around large equipment; reserve direct routing for an
observed clear crossing. A visible AC unit can also be a normal stepping climb,
so inspect the whole structure rather than repeatedly attempting a nonexistent
ladder. These distinctions resolved the Casing rooftop return without changing
position, health, mission state or geometry.
