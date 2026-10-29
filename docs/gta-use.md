# GTA use

GTA use exposes **GTA V Enhanced Story Mode** through an automatically started
**[Rust/Axum HTTP/WebSocket service](rust-service.md)**, a Python 3.14 SDK and an in-game action scheduler.
[uv](https://docs.astral.sh/uv/) prepares the locked Python environment. The mod
itself continues to run on SHVDNE's .NET Framework 4.8 host.

The old F8 console is removed. The upstream SHVDNE F4 console and Insert shortcut
remain separate runtime features. External C# statements, reload, native actions,
screenshots and structured observations replace the mod console.

For an agent workflow, use the repository's
[gta-use skill](../.agents/skills/gta-use/SKILL.md), including its operations,
state-selection and Python policy references.

## Runtime structure

`GameBridge.dll` is the single managed bridge. `GTAVMods.Game` implements typed
engine contracts and state observations; `GTAVMods.Agent` implements the action
scheduler, commands, diagnostics, pipe host and companion supervision. Shared file
diagnostics live in `GameBridge/Diagnostics`, retaining the `ADModUtils` public
names for source compatibility. ModUtils and Thor depend on GameBridge; the
bridge has no dependency on either mod or on NAudio. This removes the former
AgentBridge-to-ModUtils-to-GameBridge dependency chain.

The Rust server runs in a separate process; SHVDNE hosts .NET Framework
4.8 and game calls must run on its script thread. This separation lets
HTTP diagnostics and Python watchdogs respond when the game stops ticking.
There is one managed bridge module and one public HTTP/WebSocket service;
Python policies do not access the private pipe. Coverage still enforces the
engine namespace boundary inside the combined assembly.

## Install and automatic startup

Build with the .NET 10 SDK, Rust, uv and .NET Framework 4.8:

~~~powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Build.ps1
# Optional explicit tool locations:
# ... -DotNet 'C:\path\dotnet.exe' -Uv 'C:\path\uv.exe' -Cargo 'C:\path\cargo.exe'
~~~

Close GTA, back up replaced files, and merge the package's scripts directory into
the game root. GameBridge.dll now contains both the agent scheduler and game
adapters. Back up and remove obsolete scripts/DeveloperConsole.dll and
scripts/AgentBridge.dll; leaving AgentBridge.dll loads a duplicate script.
Prepare the packaged service once, with GTA closed:

~~~powershell
.\Install-AgentService.ps1 -GameDirectory 'D:\SteamLibrary\steamapps\common\Grand Theft Auto V Enhanced'
~~~

The installer runs uv sync --frozen with Python 3.14 and records the resolved
interpreter in scripts/GtaUse/.python-path. UV_PROJECT_ENVIRONMENT can select a
different environment location. This supports environments outside a sandboxed
workspace; the interpreter must have ordinary user access to GTA's named pipe.
Do not change Windows security labels or weaken the pipe ACL to fix an environment.

Rust is the only server. The installer requires `scripts/GtaUse/gta-use-server.exe`
and backs up/removes the obsolete Python server, its bytecode and backend selector
from older packages. Backups and a SHA-256 manifest are kept under
`%LOCALAPPDATA%/GTAVMods/Backups`. Python remains available for the SDK and policies;
`uv sync --frozen` removes the old server dependencies from this dedicated environment.

The bridge starts the prepared service invisibly, checks it every ten seconds,
reuses it across script reloads, and restarts it after an exit. It never downloads
dependencies from the game thread. A missing environment produces a file diagnostic
with the preparation command. The companion exits when the game process exits.

Discovery files are in %LOCALAPPDATA%\GTAVMods\Bridge:

- bridge-<game PID>.json: current managed session, private pipe and session directory.
- service-<game PID>.json: loopback URL, bearer token and companion process.
- service-<game PID>.jsonl: rotating service diagnostics.
- service-<game PID>-stdio.log: Rust startup failures and panic diagnostics.

The service binds only 127.0.0.1, uses a fresh bearer token, and rejects Origin
headers. The private named pipe grants the current Windows user access and denies
network logons. Commands run with the user's game privileges; this is a trusted
development interface, not a Python/C# sandbox.

## Python command utility

For an already built checkout or installed `scripts/GtaUse` directory, reuse the
uv-prepared interpreter recorded in `.python-path`. This does not sync packages
or write to a read-only `.venv` just to play:

~~~powershell
$gtaPython = (Get-Content -LiteralPath .python-path -Raw).Trim()
& $gtaPython -m gta_use describe --process 12345
& $gtaPython -m gta_use describe --process 12345 --command controls
& $gtaPython -m gta_use describe --process 12345 --control Move
& $gtaPython -m gta_use inspect --process 12345 --sections player navigation weapons --capture --output initial-world.json
~~~

`describe` lists installed operations, command names and sections without the
entire control enum. `--command` selects the actual schema; `--control` searches
control names without case sensitivity. `inspect` reads bridge status and a world
pack without acquiring a lease or changing gameplay. `--sections` replaces profile
defaults; `--profile` selects a named preset. Optional `--capture` saves a PNG of
the foreground game. `--output` saves full JSON to a new file and prints only its
path; it refuses to overwrite existing evidence. The output contains `status`,
`state` (the unchanged pack), and optional `capture.screenshot`.

`--radius`, `--limit`, `--types ped vehicle object`, `--blip-limit` and
`--line-of-sight on|off` expose the corresponding state filters without inline
JSON. If navigation reports `truncated`, inspect `--blip-limit 256` once before
concluding that a mission marker is absent; keep frequent queries bounded.

When operating a separate harness on the same desktop, GTA's **Settings >
Graphics > Pause Game On Focus Loss** can interrupt the bridge as soon as focus
changes. Record its original value, turn it off for the supervised test if needed,
and restore it afterward. This does not dismiss existing pause menus or alerts.
On an autosave overwrite alert, follow the displayed shortcuts: Esc means No;
Enter means Yes. A highlighted/hovered No button does not change Enter's meaning.

Raw requests remain available for all filters, e.g.
`request state --args '{"profile":"combat","radius":60,"limit":16,"lineOfSight":"on"}'`,
and `request describe` returns the complete catalogue. An intentionally managed
source environment can still use `uv run --frozen`; `--no-sync` reuses it without
installation. A missing `.python-path` means build/setup is needed, not a reason
to repeatedly rebuild an environment during a mission.

Select --process when several discovery files exist. --discovery
selects another discovery directory. The bridge starts the packaged Rust server
automatically; the Python command utility does not host a server.

For direct HTTP clients, read the discovery file and send
Authorization: Bearer <token>. OpenAPI is available at /openapi.json.

| Endpoint | Behavior |
| --- | --- |
| GET /v1/session | Current session/discovery, even when the game is unresponsive |
| POST /v1/request | Versioned command envelope and correlated response |
| GET /v1/batches/{id} | Per-batch status/results from files |
| GET /v1/files/{relative-path} | Session artifacts; paths cannot escape the session |
| POST /v1/jobs | Start a supervised local Python script; returns job ID |
| GET /v1/jobs/{id} | Checkpoint/progress/final result |
| DELETE /v1/jobs/{id} | Request cancellation |
| WS /v1/ws | Correlated command responses and bounded state subscriptions |

HTTP and WebSocket requests share the same scheduler, IDs, validation and response
contract. A stale session is rejected. No transport reconnect automatically repeats a
mutation. A timeout means inspect observed game state first.

## Python segments and checkpoints

Write ordinary Python functions, async functions, behavior trees or reactive loops.
The script entry point is run(ctx). Run a segment until a concrete observed
checkpoint; retry it only after inspecting failure evidence and restoring the
game's checkpoint.

~~~python
# inspect_player.py
async def run(ctx):
    async with ctx.game.connect() as game:
        async for state in game.states(hz=5, sections=["player", "weapons"]):
            ctx.observed(state)  # persists state and marks the decision loop alive
            player = state["sections"]["player"]["entity"]
            if player["dead"]:
                ctx.uncertain("Player died; retry the in-game checkpoint.")
            ctx.checkpoint("player-inspected", position=player["position"],
                           health=player["health"])
            return
~~~

~~~powershell
uv run --frozen gta-use run .\segment.py --process 12345 --timeout 180 --stuck-timeout 30 --check-timeout 10
uv run --frozen gta-use status '<run directory>'
uv run --frozen gta-use cancel '<run directory>'
~~~

POST /v1/jobs accepts script (absolute local Python path), timeout, stuckTimeout
and checkTimeout. The command utility and HTTP job endpoint run the same supervisor.

The supervisor and script are separate processes. A CPU-bound/infinite script can
be terminated at the deadline. ctx.check(...) requests and persists a state pack;
ctx.observed(state) accepts a streamed pack. Both mark the loop responsive.
ctx.progress(message, **evidence) marks **meaningful observed progress**; repeated
unchanged observations do not reset the stall deadline. ctx.checkpoint(name,
**evidence) records the segment's success predicate. Returning without a checkpoint
is a failure. An application checkpoint label is the script author's observed
predicate; it does not assert GTA created a retry checkpoint or passed a mission.

The supervisor maintains a five-second game-side lease. Loss of heartbeats
cancels the active batch and pauses simulation when the bridge next ticks.
A dead supervisor cannot leave an indefinite control stream. Expired tokens
cannot resume play. Normal completion, uncertainty, cancellation and script
failure also cancel current actions and leave the game paused for inspection.

The run directory contains the exact script snapshot and SHA-256, run.json,
progress.json, state.json, events.jsonl, stdout.log, stderr.log and result.json.
Imported helper modules remain normal Python imports from the script's original
directory; snapshot/version them separately when reproducibility requires it.
Do not spawn detached child controllers from a segment.

Compose SDK helpers before writing another native loop. `gta_use.vehicles`
provides `acquire_vehicle`, `leave_vehicle` and bounded `recover_upright`;
`gta_use.combat` provides `take_cover`, `muzzle_clear` and `fire_from_cover`.
`gta_use.driving.intercept_vehicle` keeps an observed road interception and chase
continuous; `engagement="ram"` explicitly opts into physical interception.
`wait_for_control(stable_for=2)` waits for stable control after a cutscene, and
`ctx.capture("stage", state=state)` retains numbered screenshots with evidence.
These operations share the current supervisor and lease, verify observed outcomes
and leave simulation running on success. Vehicle/target/companion arguments use
fresh `{handle, model}` identities. Entry uses native timeout `-1` and a Python
deadline; it does not warp into an inaccessible car. Approach garage entrances
with the navigation helper when required.

Long decisions and recovery limits stay in Python. Candidate access checks,
pursuit target reads and muzzle geometry are gathered in immediate C# commands
on the existing bridge, reducing round trips without adding a server or scheduler.
Mission routes, target selection and tactics stay in the policy. See the
[Python AI guide](../.agents/skills/gta-use/references/python-ai.md) for parameters
and the [Repossession replay](continuous-repossession.md) for live evidence and
its mission-specific limits.

These are distinct checks: socket liveness, script observations, meaningful
progress and overall deadline. WebSocket Ping/Pong does not prove mission progress.
If SHVDNE stops ticking in a menu/failure screen, the watchdog cannot run until
ticks resume; the supervisor records an unconfirmed pause instead of claiming
success. Desktop input may be needed to choose the game's Retry option.

## General controls and observations

~~~python
from gta_use import Client

game = Client(process=12345)
state = game.step(500, [{"control": "MoveUpDown", "value": -1}],
                  sections=["player", "nearby"], radius=30, limit=12)
game.command("Thor.Thor.Current.SetEnabled(false);")
game.native("TASK_SEEK_COVER_FROM_PED", player_handle, enemy_handle, 5000, False)
game.action("aim", entity=enemy_handle, bone=31086, ms=300, shoot=True)
game.close()
~~~

step resumes, applies controls for the specified game duration, pauses, then reads
state. For continuous play, resume once and use actions/subscriptions until the
segment ends. Controls are submitted every frame and release automatically.
Durations are frame-granular, 1–30000 ms. Wall-clock controls can be used for menus,
but they still need SHVDNE ticks. Simulation stalls fail after a bounded wait.

aim tracks the specified ped bone every frame using observed camera rotation.
For vehicles and props it tracks the entity origin instead. Optional `height`
adds a world-Z offset (-20..20 metres) to either target; choose a visible point
instead of assuming an entity origin is above the road or an obstruction.
Vehicle occupants also receive VehicleAim/VehicleAttack inputs. `warmupMs`
(default 0, at most `ms`) keeps aim held without firing during the first part of
the same action; `ms` includes that interval. For example,
`game.action("aim", entity=car, height=1.5, warmupMs=1000, ms=2200,
shoot=True, requireAimTarget=True)` provides continuous preparation and gated
fire. Observe ammo and entity health; vehicle body/engine health alone can miss
a bullet hit. The interval is tuning, not a guarantee of readiness or a hit.
Check `session.phoneCall` and the selected weapon first: an incoming call can
interrupt vehicle weapon use. Handle mission calls according to the objective.
For a rider, target the ped's bone rather than the bike's origin; an aim match
on the rider does not satisfy a fire gate aimed at the bike.
Firing requires line of sight; a disappeared/dead target ends tracking.
It does not find cover, select enemies or change health. Python decides those
policies from observed state, and should verify cover before firing.

Set `requireAimTarget=True` to additionally withhold Attack unless the game's
current aim-target handle matches the requested entity. A different, missing or
stale aim target withholds fire. The default is false for compatibility. Cover
animations or free-aim transitions can yield unknown observations. Start with a
bounded aim-only peek at a confirmed target: tucked-position LOS may be blocked
by the player's own cover. Observe the transition before deciding to reposition.

The result keeps `entity`, `targetAvailable` and `timing`, and adds `frames`,
`lineOfSightFrames`, `aimTargetMatchFrames`, `unknownAimTargetFrames`,
`attackRequestedFrames`, `inCoverFrames`, `aimingFromCoverFrames`,
`weaponReadyFrames` and `last` (targetAvailable, lineOfSight, aimTarget,
attackRequested, inCover, aimingFromCover, weaponReady). These are bounded counters and the last frame observation;
there is no per-frame file output or retained sample list. Unknown LOS/aim data
is null, including when a target becomes unavailable. An Attack request is not
an observed shot or hit, and matching aim does not prove companion clearance or
a clear muzzle path. Observe ammo and target health before and after the entire
aim/fire interval. The cover policy example records both and checks companion
and unknown-human positions before using the optional gate.
Cover/readiness counters preserve the action's observed transitions when pause
clears transient state flags. Readiness is telemetry, not an additional firing
gate. A short Attack interval can finish before a shot occurs; inspect the
transition and ammo before concluding that firing is unsupported.

native accepts a known GTA.Native.Hash name or 0x hexadecimal hash, a scalar args
array, and returns: void, bool, int, uint, float, string or vector3. Integer-looking
floating-point native arguments must be explicit, e.g. {"type":"float","value":2}.
Typed int/uint/long/ulong arguments are also supported. Output-pointer and more
complex native contracts can use a C# command or a registered extension.

Engine task submission does not prove completion. Check occupancy before driving,
position/path progress after movement, target health after shooting, and mission
signals after a checkpoint action. Entity handles can disappear or be reused.

[World-state packs](world-state.md) provide JSON sections for player, vehicle,
weapons, inventory, nearby entities, navigation, ray probes and camera. Omitted
sections do no work; optional line-of-sight and ray tests have explicit costs.
Use observations to make most decisions and screenshots for HUD instructions,
menus or uncertain scenes. The generic snapshot does not claim to expose every
mission's private variables.

## WebSocket messages

~~~json
{"type":"subscribe","session":"<session ID>","hz":5,"query":{"sections":["player","weapons","nearby"],"types":["ped"],"radius":60,"limit":16,"lineOfSight":"on"}}
~~~

The server acknowledges the subscription, then sends state messages with session,
sequence, ok and result. The requested frequency is an upper bound (0 < hz <= 10);
game-thread latency and observation cost can reduce it. Batch transitions are
pushed separately. Re-subscribing replaces the previous subscription.

Commands use type=request plus the normal version/id/session/op envelope.
Responses use type=response and the same request ID. The Python receiver
correlates concurrent requests and retains only the latest pending observation.
Slow readers are disconnected after a bounded send wait; queues are bounded.
Screenshots and large historical files use HTTP rather than blocking the live
control stream. After disconnect/reload, inspect game state and create a fresh
Client; action replay is never automatic.

## Extensibility and ephemeral execution

Top-level operations: describe, status, state, observe, capture, action, batch, cancel,
reload and lease. Batch commands: pause, resume, wait, controls, aim, native,
state, observe, capture, command and registered extensions.

A batch contains at most 128 steps. Every step is prepared/validated before any
action starts; one batch owns the action scheduler at a time. Runtime failure
stops subsequent steps and pauses the game. An explicit cancel outside a supervised
run releases the bridge-owned pause; segment cancellation uses lease release and
keeps the game paused.

C# commands run on the script thread, with GTA, GTA.Math and GTA.Native imports
and P/PED, PLAYER, V, POS aliases. Return structured values or CONTEXT.Print(value).
Vectors/quaternions become numeric fields; game objects become handles without
traversing native-backed properties. Console/Trace output goes to files.
Compilation is bounded to 128 attempts per managed session and output to 1 MiB.
Successfully compiled identical source reuses its executable delegate within that
session, avoiding repeated compiler work and assembly allocation. Every explicit
submission still executes again against the current world and records fresh
output; no action results or failures are replayed. Reuse remains available after
the compilation budget is exhausted. Invalid source consumes an attempt; changing
the source requires another attempt. The cache retains at most 128 successful
source/delegate pairs and is discarded with the bridge session.
Prefer filtered state/native queries for polling, or register a bounded provider
or parameterized command once for repeated compound work. Interpolating changing
handles or coordinates into C# on every iteration still consumes new attempts.
Keep arbitrary C# short and nonblocking: it cannot safely be preempted.

Mods can register namespaced CommandDefinition operations on
BridgeScript.Active.Commands. Preparation validates arguments and returns an
IAgentStep; Run executes each tick, batch results are logged asynchronously, and Cancel releases
owned resources. StatePack.Register adds named state sections. describe discovers
both extensions, so future controls need no transport redesign.

The private pipe uses length-prefixed UTF-8 JSON, with a 1 MiB request bound and
4 MiB serialized response bound. Large programs stay in Python files. Requests
and responses are ephemeral: IDs correlate responses, with no persisted receipts,
deduplication, recovery or replay. Explicitly submitting the same ID again executes
again. A missing response means inspect the current game state before choosing
another action; clients never automatically replay a submitted command.

Pipe connection, reads, writes and listener rearming run asynchronously. Only
game operations wait for a script tick. The `action` operation accepts a one-item
`steps` array. An `ImmediateStep` returns `{completed:true,value:...}` directly;
a timed/custom step returns `{completed:false,batch:...}` and uses the existing
scheduler. The SDK handles both through `action()`, `native()` and `command()`.
Immediate actions respect the lease and cannot interrupt an active batch.

Batch definitions, step results, screenshots and console output remain external
artifacts. JSONL and batch/output files use an ordered background writer bounded
to 2 Mi UTF-16 characters (about 4 MiB of text). It drops diagnostics on overload
or storage failure, reports `status.droppedRecords`, and never blocks or rejects
a game action for log durability. Files are atomically published for readers;
none of the C#, Python or Rust bridge writers force data to physical storage.
Queued diagnostics can be lost on crash/reload. Each batch has its own
`batch-<id>-status.json` for polling; `batch-status.json` describes the latest batch.
The service log rotates at 5 MiB with nine backups; gameplay JSONL retains its
existing rotation policy. Historical batch/screenshot artifacts remain until
archived or deleted by the operator.

reload delivers acceptance to the companion before invoking
SHVDNE's public Reload(). Confirm a new ready managed session afterward.
A full game restart is still required for native ASI plugins. Reload invalidates
old leases and compiled commands; a previous Python controller must not silently
continue into the new session.

Use a backed-up Story save, disable Thor for ordinary mission tests, and restore
test settings afterward. Automated tests cover engine contracts and failure
handling; actual mission completion requires live gameplay and Mission Passed
evidence.

## Python policy examples

`gta_use.navigation.climb_toward(ctx, facing)` attempts one normal climb from
an observed edge approach, including stationary vehicle/roof surfaces. It turns
naturally and waits for animation followed by two settled observations. Outcomes
distinguish `climbed`, an ordinary jump (`landed`), `no-effect` and mission
`transition`; none establishes route arrival. Inspect final position/height before
another action. It never forces a pose or cancels an unfinished climb.

`gta_use.combat.melee_burst(ctx, target)` performs one unarmed Aim/light-punch
exchange against a caller-identified nearby ped. Fresh observations gate identity,
facing, distance, selected weapon and player health. It reports actual target
health change and stops input when a mission takes control. Callers choose bounded
no-damage retries and defensive tactics. It neither changes weapons nor applies
damage directly. This sequence worked in Complications' required Simeon fight;
the optional gardener stealth takedown remains unverified.

`gta_use.navigation.navigate_to(ctx, destination, waypoints=(), parkour=True)`
provides bounded on-foot access routing using the existing native bridge. It
starts a normal navmesh run, checks player progress and inspects a narrow three-ray obstacle
fan only after a stall. If the upper fan misses, a lower fan checks for low
fences: at most six probes per recovery, none during ordinary movement.
A nearby static obstacle can trigger an approach,
natural turn and GTA climb/vault task; the helper then observes landing and
resumes the route. It uses no C# compilation or timeout warp. Repeated failed
climbs, state/control changes and uncertain receipts stop for inspection.
Arrival requires a settled player within a 3D radius (default 0.75 m); choose
far-side waypoints beyond that radius when crossing a fence. It cannot discover
every mission route or guarantee arbitrary walls, ladders and gaps are traversable.
Success returns arrival evidence and leaves simulation running, so a policy can
continue to normal bike/car entry. The segment runner pauses on completion.

Default `route_mode="navmesh"` allows GTA to take a detour. For an explicitly
observed fence shortcut, `route_mode="direct"` uses straight movement and the
same bounded climb recovery, with each route leg limited to 30 m. Position
oscillation cannot postpone recovery when it makes no progress toward the goal.
Choose a crossing through the panel and a visible landing, away from moving gate
hinges; this mode does not plan around buildings, water or cliffs.

Use `tools/gta_use/examples/navigate_to.py` with JSON `GTA_USE_DESTINATION` and
optional `GTA_USE_WAYPOINTS` for a standalone route. Configurable bounds include
`timeout=60`, `stall_timeout=2.5`, `max_climbs=3`, `climb_timeout=7`,
`minimum_health=100`, `move_blend_ratio=2.5` and `check_interval=.2`.
State polling reads only the player; no nearby enumeration, inventory scan or
LOS fan is needed during unobstructed movement. Vehicle-first travel still allows
short on-foot fence/entrance access to the actual mission vehicle.

Native contracts: [navmesh task](https://github.com/citizenfx/natives/blob/master/TASK/TaskFollowNavMeshToCoord.md),
[short straight approach](https://github.com/citizenfx/natives/blob/master/TASK/TaskGoStraightToCoord.md),
[natural turn](https://github.com/citizenfx/natives/blob/master/TASK/TaskTurnPedToFaceCoord.md)
and [climb task](https://github.com/citizenfx/natives/blob/master/TASK/TaskClimb.md).
The navmesh final-heading sentinel is the pinned SHVDNE overload's metadata
default; the native timeout is explicitly -1, and water entry is disabled.

`tools/gta_use/examples/cover_segment.py` runs a continuous cover/aim/fire loop,
checks health and ammo, and pauses for uncertain cover geometry or blocked sight.
It uses actual combat or explicit `GTA_USE_TARGETS` handles, excludes animals, and
requires `GTA_USE_ALLIES` with identified companion handles (or [] after checking
that none are present). It conservatively checks companion/unknown-human positions
and enables the aim-target gate. It reports an observation checkpoint rather than
claiming a mission passed. A
normal cover input worked in the live mission where the NPC cover task did not;
moving along a cover edge was necessary for rooftop sightlines.

`gta_use.driving.drive_to(ctx, destination)` keeps a road trip in one continuous
policy. The default alerted mode (`786469`) avoids traffic and ignores lights
while retaining braking. It requests 28 m/s, slows to 6 m/s near the goal and
requires distance <=5 m and speed <=1.5 m/s before reporting arrival. Tune speed
and radius for the vehicle and scene. Player/vehicle observations run at 2.5 Hz;
no C# compilation or repeated route submission is needed. A 12-second motion
stall or a finite overall deadline stops with evidence. New road cells allow
detours; bounded cell tracking prevents endless revisits from resetting progress.
The callback `on_tick(ctx, state)` can request a policy transition without a model
handoff. It must not replace the helper's driving task.

`pursue_vehicle(ctx, rider, vehicle, rider_model=..., vehicle_model=...)` uses a
moving-target task and checks the identified rider continuously. Desired separation
defaults to 8 m. Its `on_tick(ctx, state, target)` supports bounded engagement
inside the chase; it does not stop simply for reaching shooting distance. Closing
or target-health reduction counts as progress; constant-gap road travel does not.
It stops after 25 seconds without effect or 180 seconds overall by default.
Rider exit/death returns a transition, not a mission-completion claim. Vehicle
shooting remains subject to live verification; Attack receipts alone prove nothing.

Both helpers return evidence and leave simulation running for the next action.
`drive_to` returns `transition` if mission control or a cutscene takes over and
preserves the mission's tasks. `wait_for_control(ctx, timeout=90)` then observes
cutscene playback and control return, allowing the mission flag to clear. A
Mission Passed screenshot is still required. These helpers do not guarantee
pathfinder success at arbitrary entrances or select the mission's intended route.
The example `tools/gta_use/examples/drive_to.py` reads JSON `GTA_USE_DESTINATION`.

Native contracts: [driving flags](https://github.com/citizenfx/natives/blob/master/TASK/SetDriveTaskDrivingStyle.md),
[long-range driving](https://github.com/citizenfx/natives/blob/master/TASK/TaskVehicleDriveToCoordLongrange.md),
[cruise speed](https://github.com/citizenfx/natives/blob/master/TASK/SetDriveTaskCruiseSpeed.md),
and [vehicle chase](https://github.com/citizenfx/natives/blob/master/TASK/TaskVehicleChase.md).

Run either with the prepared interpreter: `python -m gta_use run <script> --process <PID> --timeout 180
--stuck-timeout 30 --check-timeout 10`. Both files are included in the package's
`scripts/GtaUse/tools/gta_use/examples` directory. The run snapshot preserves the
entry script; imported local helper files remain the caller's responsibility to
version alongside it.

GTA can stop managed ticks on mission-failure menus. The HTTP service remains
available, but game commands time out and a watchdog cannot confirm a pause
until ticks resume. Use the normal Retry UI, then reacquire the expired lease
and inspect readiness. Releasing a lease intentionally pauses; resume only after
release when proceeding without a supervisor. Do not automatically replay an
action whose outcome is uncertain.

## Live verification: Complications, 2026-10-29

Complications reached an independently inspected **Mission Passed** screen using
normal movement, truck/roof climbs, quiet interior navigation, vehicle entry and
driving, a physical storefront ram and unarmed punches. The run used supervised
segments with parent inspection and policy revisions, not one uninterrupted
script. Four actual checkpoint retries occurred during optional gardener attempts;
the successful route bypassed him quietly. Walkthrough assistance identified that
optional objective and the truck/roof/window route. No teleport, timeout warp,
health/ammo/weapon grant, direct damage, mission-global edit or mission Skip was used.

Local evidence is indexed in `artifacts/gta-story-campaign/complications-completion.json`:
39 retained segment results total 1,107.53 runner seconds, including observation
segments and a phone-menu attempt; this is not elapsed wall time or a continuous
completion time. The named Mission Passed image is
`complications-passed-001/002-complications-passed-details-after.png`, SHA-256
`f156bcc8cc3088442ea3978289b8319d4f704d115571d3a0091b011aa9f3c4ca`.
Progress was saved to a new slot, preserving the earlier saves. The extracted
`climb_toward` helper subsequently passed a separate live parked-car climb;
`melee_burst` has transport/scenario coverage but its extracted implementation
has not yet been used to complete another live fistfight.

## Live verification: Repossession, 2026-10-27

Completed the full Story mission on GTA V Enhanced 1.0.1158.16 with Thor disabled:
Simeon's briefing, the drive to Vespucci, gate and garage search, cover combat,
motorcycle pursuit/recovery, losing the police, delivery and the ending cutscene.
The game displayed **Mission Passed — Repossession**. No teleport, invincibility,
health/ammo grants, direct entity damage or mission-variable edits were used.
Movement used ordinary game tasks and timed controls; shooting used the aim/fire
control loop. This verifies one complete mission, not the entire campaign.

Python segments ran continuously between observed checkpoints, with progress and
health checks. The chase required retries: direct pursuit fell behind, and a
navmesh shortcut stalled. A road interception followed by normal vehicle chase,
braking, leaving the car and shooting on foot succeeded. The player recovered
the bike, lost the wanted level through normal driving, and completed delivery.
NPC vehicle shooting did not produce observed damage, so it is not a verified
player control. The shipped examples are reusable starting policies, not a
one-command solver for every mission.

The combat test exposed that hostile relationship groups can include animals;
the state pack now reports `ped.human`, and the example excludes nonhuman peds.
It also reports `ped.vehicle` for tracking riders. Cover was verified before
firing; moving along a cover edge was necessary to expose rooftop sightlines.
Mission instructions and final success still required screenshots. Generic
natives do not expose every mission's private objective text or script state.

Live infrastructure checks confirmed HTTP control, correlated WebSocket requests
and a 5 Hz filtered stream, external managed reload with a new session and the
same service process/URL, automatic replacement of a terminated service, and
termination of a CPU-bound Python worker with a confirmed pause in about four
seconds. Modal save/failure screens can suspend managed ticks: the service stays
available, but commands and pause confirmation must wait for normal UI recovery.

The local evidence bundle is outside Git at
`C:\Users\Helen\Documents\Codex\2026-10-27\gta-use-mission-proof`.
It contains ten unmodified screenshots (`01-briefing.png` through
`10-mission-passed.png`), mission session requests/results, Python run snapshots,
progress logs, and transport/recovery results. Failed attempts are retained.
Saves, bearer credentials and gameplay evidence are not shipped in the package.

The validation build passed 321 C# scenarios and 17 Python scenarios. Its strict
coverage gate measured 100% executable lines and branches in the bridge,
ModUtils and Thor. That gate does not measure GTA physics, rendering
or all possible mission mechanics; the live results above are separate evidence.

After consolidating the managed projects into GameBridge.dll, a fresh Story
process verified automatic companion startup, one bridge script, filtered state,
a 500 ms advance/pause, C# evaluation and file output, and managed reload with
the same companion process. Both Python examples in the repository skill ran
unchanged to observed checkpoints with confirmed pause. The strict build still
passed all 321 C# and 17 Python scenarios: GameBridge measured 1005/1005 lines and
556/556 branches, ModUtils 274/274 and 90/90, Thor 1496/1496 and 610/610.
This was a startup/reload smoke test; the full mission evidence above predates
the assembly consolidation. Its local follow-up evidence is under
`C:\Users\Helen\Documents\Codex\2026-10-27\gta-use-20261027-044636\unified-bridge-smoke`.
