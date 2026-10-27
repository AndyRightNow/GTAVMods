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

From a source checkout:

~~~powershell
uv run --frozen gta-use request describe --process 12345
uv run --frozen gta-use request state --process 12345 --args '{"profile":"combat","radius":60,"limit":16,"lineOfSight":"on"}'
uv run --frozen gta-use request capture --process 12345
~~~

The packaged project is scripts/GtaUse; use uv run --project <that folder> from
elsewhere. Select --process when several discovery files exist. --discovery
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
Firing requires line of sight; a disappeared/dead target ends tracking.
It does not find cover, select enemies or change health. Python decides those
policies from observed state, and should verify cover before firing.

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
Compilation is bounded to 128 attempts per managed domain and output to 1 MiB.
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

`tools/gta_use/examples/cover_segment.py` runs a continuous cover/aim/fire loop,
checks health and ammo, and pauses for uncertain cover geometry or blocked sight.
It uses actual combat or explicit `GTA_USE_TARGETS` handles, excludes animals, and
reports an observation checkpoint rather than claiming a mission passed. A
normal cover input worked in the live mission where the NPC cover task did not;
moving along a cover edge was necessary for rooftop sightlines.

`tools/gta_use/examples/drive_to.py` accepts a JSON `GTA_USE_DESTINATION` with x/y/z
coordinates and checks occupancy, condition, position and route progress. It is
a starting policy, not a promise that GTA's pathfinder can navigate every chase
or entrance. New road cells count as progress to allow detours; revisiting the
same cells indefinitely does not keep the watchdog alive.

Run either with `uv run gta-use run <script> --process <PID> --timeout 180
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
