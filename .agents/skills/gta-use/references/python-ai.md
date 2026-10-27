# Writing supervised Python game AI

## Start from a bounded segment

Write an ordinary Python file exporting `run(ctx)` (sync or async). Use `ctx.game`
for the inherited session/lease. Execute from the uv project:

```powershell
uv run --frozen gta-use run .\policy.py --process 12345 --timeout 180 --stuck-timeout 30 --check-timeout 10
uv run --frozen gta-use status '<run directory printed by the runner>'
uv run --frozen gta-use cancel '<run directory printed by the runner>'
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

## Give each segment an explicit contract

Before coding, record the starting objective and observable preconditions, target
identity, allowed controls, success predicate, progress metric, and recovery path.
Prefer one game checkpoint or tactical objective per run. A segment may contain
many actions and a reactive loop; do not split every 300 ms shot into a separate
model interaction. Keep pause/inspect as the fallback for an uncertain scene.

Use the shipped policies as concrete starting points:

- [cover_segment.py](../../../../tools/gta_use/examples/cover_segment.py): normal
  cover input, health checks, confirmed human targets, LOS, reload and short
  head-aim/fire actions. `GTA_USE_TARGETS` can carry a JSON list of handles verified
  for the current objective. Its no-visible-targets checkpoint does not prove
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
ms=350, shoot=True)` aims/fires briefly; it does not choose a target, find cover or
alter health. Firing requires LOS, but effectiveness still needs observed damage.
Do not reissue a long native navigation task each iteration if it is progressing.
Pause stops time; it does not erase a persistent native task before the next resume.

## Observe, progress, checkpoint

`ctx.check(**filters)` fetches and persists a pack, updates the observation timer,
and detects player death. `ctx.observed(pack)` does the same for a WebSocket pack.
Socket Ping/Pong and successful command receipts are not observation/progress.

`ctx.progress(message, **evidence)` resets the stall timer only for meaningful
change. Examples: distance reduces beyond a tolerance; a new road cell is reached;
cover is first acquired; confirmed target health decreases; objective stage changes.
Do not call it unconditionally in every loop, for repeated retries, or merely
because the controller is alive. Avoid metrics that permit endless circles.

`ctx.checkpoint(name, **evidence)` requires nonempty evidence; call it only when
the predicate was observed, then return. Returning without it fails. The name is
your policy's label, not a GTA retry checkpoint. Mission completion requires the
actual game's completion signal, normally a screenshot of Mission Passed.

`ctx.uncertain(reason)` raises a stop condition. Include enough context in logs to
decide the next action. Do not catch it and continue the policy. Do not suppress
`UncertainOutcome` and retry a timed-out mutation: inspect its receipt and state.

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

Read the failed run's last state, progress, stdout/stderr, events and receipts.
Classify the failure: invalid schema, stale identity, missing observation, task
not completed, physics/pathfinding mismatch, poor policy, suspended game ticks,
or transport/storage failure. Fix that cause and rerun from the observed checkpoint.
Keep failure artifacts so a successful retry does not erase the evidence.

The Repossession test showed useful limits: cover input succeeded where an NPC
cover task did not; rooftop shots needed cover-edge repositioning; hostile groups
included animals; naive pursuit and a navmesh shortcut failed; vehicle shooting
was not verified effective. Prefer the proven on-foot aim control or explicitly
test a new mechanic instead of assuming native task acceptance proves success.
