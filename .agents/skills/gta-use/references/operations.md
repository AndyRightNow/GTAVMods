# Operating and diagnosing GTA use

## Prepare and select a session

Read [AGENTS.md](../../../../AGENTS.md) before installing or launching. The tested
installation path and runtime versions are dated facts; inspect the actual local
installation. Build with `scripts/Build.ps1`; do not deploy while GTA is running.
Back up replaced and obsolete files outside the repository with paths and hashes.
Ship Thor, ModUtils and the unified GameBridge together with packaged dependencies.
Back up/remove old `AgentBridge.dll` and `DeveloperConsole.dll` from `scripts`.

Run the packaged `Install-AgentService.ps1 -GameDirectory <game directory>` once
with GTA closed. It uses uv/Python 3.14 and writes the interpreter path. The bridge
starts the packaged Rust server invisibly, checks it every ten seconds, and reuses
it across managed reloads. Runtime installation never happens on the game thread.
Rust is the sole server; Python runs the SDK and policies. Upgrades back up and
remove the obsolete Python server, bytecode and backend selector, with a hash
manifest under the configured runtime root's `Backups` directory. See
[server setup](../../../../docs/rust-service.md) and
[measured comparisons](../../../../docs/bridge-performance.md).

Launch through Steam with the existing modded Story setup; let the user handle
sign-in or verification. Use the computer-use skill if desktop UI is needed.
With the optional [startup helper](../../../../docs/story-startup.md) installed,
Story resumes automatically. Otherwise select Story/Resume Story, never Online.
Do not treat old discovery files as
running processes; find the current `GTA5_Enhanced` PID.

Runtime files default to the checkout's ignored `artifacts/gta-use`. Run the repository installer with GTA closed to record this root in the installed `scripts/GtaUse/.runtime-root`. The SDK reads its own project's optional `.runtime-root` regardless of the working directory; use `--discovery` or `GTA_USE_DISCOVERY` for an explicit `Bridge` directory. Packaged-only installs default to `scripts/GtaUse/artifacts/gta-use`. Discovery is `<runtime>/Bridge`. `bridge-<PID>.json` identifies the
managed session and artifact directory; `service-<PID>.json` contains the loopback
URL and credential. Use `Client(process=<PID>)`; avoid dumping credential files.
`GET /v1/session` can succeed while game ticks are suspended. Follow it with a
small state query and confirm the current player is available.

## Choose an action surface

| Need | Use | Verify afterward |
| --- | --- | --- |
| One observation / screenshot | `game.state(...)`, `game.request("capture")` | Fresh session, expected fields, actual screenshot |
| Short action then inspect | `game.step(500, inputs, **filters)` | Position/health/occupancy and paused state |
| Ordered deterministic sequence | `game.batch([...])` | Completed results, not just acceptance |
| Continuous mission segment | `gta-use run policy.py ...` | Observed checkpoint and confirmed pause |
| Stream observations | `async with game.connect()` and `states(hz=...)` | Call `ctx.observed` for each used pack |
| Trusted one-off engine operation | `game.native(...)` or `game.command(...)` | Actual resulting game state |
| Reload managed scripts | `game.request("reload")` | New ready session; fresh Client |

Use `gta-use describe` for a compact capability list, `describe --command controls` for a specific schema, or `describe --control Move` for matching control names. Raw `request describe` returns the complete catalogue. Controls accept
GTA.Control names or integer IDs, explicit group and normalized value. Positive
and negative axis directions depend on the control; test a short movement before
assuming a direction. A batch can apply multiple simultaneous controls in a step,
but its steps execute sequentially. Only one batch can run at a time.

The SDK's action methods return immediate results in one request; timed actions
wait for batch completion. Ordinary `request("batch", steps=...)` returns
acceptance; query the batch ID for completion. Wall-clock
controls still require game ticks. Game-time waits require resumed simulation.
Use short actions within the Python observation deadline, not a 30-second action
with a 10-second check deadline.

`native` supports scalar arguments/returns and vector3 returns. Preserve the ABI:
`{"type":"float","value":18}` is a float even when it looks like an integer.
Consult the local SHVDNE/native contracts or authoritative definitions for new
signatures. For output pointers, use a brief C# operation or typed extension.
Do not spin, sleep or wait for a task to finish inside C# on the script thread.

## Leases, pause and reload

The supervised runner acquires its own lease and heartbeats independently of the
worker. Scripts use `ctx.game`, which inherits that lease. Completion, uncertainty,
exceptions and cancellation release the lease and request a pause. Inspect the
runner's `paused` result; HTTP availability alone does not confirm it.
A GTA pause/settings menu can suspend game ticks long enough for heartbeat
and release requests to time out, leaving `paused: null`. Close the menu
through normal UI, inspect the current state, and do not assume release. If
the old lease is expired, a fresh observation-only supervised run can acquire
and release it; inspect again for `paused: true` and `lease.active: false`.

For a manual controller, create `Client(process=..., token=<fresh UUID>)`, acquire
the lease, heartbeat faster than expiry, and release in `finally`. Do not hold a
lease while waiting indefinitely for a human/model response. Prefer `step` with
the game paused during inspection, or the supervised runner for continuous play.
Plain top-level `cancel` outside a supervised run releases the bridge-owned pause;
it is not equivalent to runner cancellation, which leaves the game paused.

Before reload, stop the current Python run and inspect its final outcome. Reload
acceptance is delivered before SHVDNE Reload is invoked. Wait for a different
ready managed session, then recreate the Client and re-register extensions.
The service process/URL normally stays the same. Reload invalidates old leases,
handles and compiled commands; never continue the old policy silently. Native
ASI changes require a full game restart.

## Diagnose from files

| Evidence | Interpretation |
| --- | --- |
| `service-<PID>-stdio.log` | Interpreter, imports and startup failures |
| `service-<PID>.jsonl` | Rotating HTTP/WebSocket/service diagnostics |
| Game `scripts/Logs/Thor-*.jsonl` | Shared mod/bridge events, exceptions, snapshots; fallback under LOCALAPPDATA/GTAVMods/Logs |
| Session batch/status/step artifacts | Accepted work, progress, completion and output |
| Run `run.json`, `script.py` | Entry script snapshot/hash, session and deadlines |
| Run `state.json`, `progress.json`, `events.jsonl` | Last observation, responsiveness, meaningful progress and checkpoint evidence |
| Run `result.json`, `stdout.log`, `stderr.log` | Confirmed outcome/pause or failure and traceback |
| New SHV/SHVDNE/DirectStorageFix logs | Actual host loading and runtime compatibility |

Inspect timestamps, PID and session IDs before drawing conclusions. Console/Trace
output is queued to files. Diagnostics use a bounded background queue; storage
failures or overload drop records and increase `status.droppedRecords` without
stopping actions. Requests/responses have no disk receipts or deduplication;
IDs only correlate replies. Reusing an ID explicitly executes again. No bridge
writer forces a durable disk flush. Batch/screenshot artifacts are retained. Archive evidence outside Git; the diagnostic
collector excludes saves/accounts and makes screenshot collection opt-in.

On an uncertain request, inspect current game state and any batch diagnostics before
deciding whether a new command is appropriate. Reconnecting never makes replay
safe automatically. If game state requests hang while HTTP works, inspect the
visible game: mission failure/save menus can stop SHVDNE ticks. Use normal Retry
UI, wait for readiness, acquire a new lease and reobserve the checkpoint. Do not
reinstall runtimes or kill the game as the first response to a modal screen.

## Record a mission test

Disable Thor for normal gameplay; identify prohibited test shortcuts up front
when proving ordinary play. The Repossession proof used no health/ammo grants,
teleports, invincibility, direct damage or mission-variable edits. Preserve the
user's save and settings; avoid saving test effects over their progress.

Capture briefing/objective, significant segment transitions and the actual
Mission Passed screen. Keep retries, script snapshots, state/progress logs and
final results with the screenshots. Restore test settings/save handling afterward.
A delivered vehicle, absent enemy, ended cutscene or `ctx.checkpoint` is evidence
for that observation only; mission completion needs the game's completion signal.
