---
name: gta-use
description: Operate and diagnose GTA V Enhanced Story Mode through this repository's GTA use bridge. Use for external reload or C# commands, structured world observations, timed controls, screenshots, mission testing, and supervised Python game AI scripts or behavior trees. Includes continuous checkpoint policies and pause/inspect recovery. Does not apply to GTA Online or unrelated desktop automation.
---

# GTA use

Treat the game as an observed world with explicit actions. Prefer filtered JSON
observations and supervised Python policies; use screenshots for mission text,
menus and ambiguous geometry. Do not infer success from command acceptance.

## Read the right reference

- Skip startup screens and resume Story on launch: [startup helper](../../../docs/story-startup.md).
- Start, connect, reload, diagnose or recover: [operations](references/operations.md).
- Choose observations for walking, driving or combat: [world state](references/world-state.md).
- Write a one-off policy, behavior tree or mission segment: [Python AI](references/python-ai.md).
- Select the optional Rust companion: [backend setup](../../../docs/rust-service.md).
- Measure latency, CPU, allocations or memory: [profiling guide](../../../docs/bridge-performance.md).
- Exact API/limits and live evidence: [GTA use guide](../../../docs/gta-use.md).
- Exact state fields/filters: [state contract](../../../docs/world-state.md).
- Build, installation and save preservation: [repository instructions](../../../AGENTS.md).

Paths in commands below are relative to the repository root. From a packaged
installation, use `uv run --project <game>/scripts/GtaUse --frozen ...` instead.
Use Python 3.14 and the locked uv project. A prepared bridge starts the selected companion
automatically; do not start competing servers.

## Operating loop

1. Find the actual `GTA5_Enhanced` process and select its PID explicitly. Read
   discovery through the SDK, run `describe`, and obtain a small state pack.
   Inspect session/player availability before acting. Readiness of HTTP alone
   does not prove the game thread is ticking.
2. Identify the current objective from observations and, when needed, a fresh
   screenshot. For ordinary mission tests, disable Thor through
   `game.command("Thor.Thor.Current.SetEnabled(false);")`. Use a backed-up Story
   save and preserve the user's settings and progress.
3. Choose the control mode. For an uncertain scene, `game.step(500, inputs, ... )`
   advances, pauses and observes. For a known segment, write `run(ctx)` and use
   `gta-use run` with overall, stall and observation deadlines. Resume once and
   react continuously until a verified checkpoint or uncertainty.
4. Check independent outcomes: vehicle occupancy, position progress, acquired
   cover, target damage, objective transition. Reobserve handles after time
   advances; missing entities and hostile relationship numbers are not sufficient
   evidence of success or target identity.
5. On failure, inspect `result.json`, `state.json`, `events.jsonl`, stderr and
   bridge diagnostics and current game state. Recover the actual game checkpoint, change the policy, then
   rerun from fresh observations. Never blindly replay an uncertain mutation.
6. Preserve screenshots and script/log evidence outside Git. Report what was
   actually observed, including retries and unverified mechanics. A script's
   checkpoint label does not mean GTA saved a checkpoint or passed the mission.

## First connection

```powershell
$gameProcess = Get-Process GTA5_Enhanced -ErrorAction Stop
uv run --frozen gta-use request describe --process $gameProcess.Id
uv run --frozen gta-use request state --process $gameProcess.Id --args '{"sections":["player","weapons"]}'
uv run --frozen gta-use request capture --process $gameProcess.Id
```

Keep bearer tokens out of tool output, commits and shared evidence. The SDK reads
the service credential locally. Use the returned screenshot path to inspect the
actual image; capture requires the GTA window visible and in the foreground.

## Boundaries that matter

- Story Mode only. Close GTA before replacing DLLs or native plugins. The single
  managed module is `GameBridge.dll`; remove backed-up obsolete AgentBridge and
  DeveloperConsole DLLs during upgrades to avoid duplicate scripts.
- General native/C# access is trusted code, not a sandbox. Keep C# short and
  nonblocking; run long logic in supervised Python. Do not invent unsupported
  native signatures or pass integer-looking floats without explicit types.
- A lease pauses/cancels bridge actions on expiry when the game next ticks.
  It cannot preempt a blocked game thread. A timeout or `paused: null` requires
  inspection and possibly ordinary desktop input for a modal Retry screen.
- Pause is not an undo operation. A submitted native task may continue when time
  resumes. Understand outstanding tasks before switching controllers.
- Preserve one controller per game. Do not launch detached workers, reset stall
  timers for unchanged observations, or silently resume an expired/reloaded run.

## Extending the system

Discover operations and section schemas with `describe` before creating a new
shortcut. Reusable game actions belong in `GameBridge/Agent`; engine mappings and
observations belong in `GameBridge` under `GTAVMods.Game`. Python policy stays in
`tools/gta_use` or a recorded one-off script. Register namespaced commands or state
sections for scenario-specific needs; use the existing transport and scheduler.
Keep state providers bounded and free of gameplay side effects. Follow the
repository's player-scenario tests and 100% coverage gate for production changes.

The full Repossession mission was verified on 2026-10-27. That evidence supports
the documented controls, not a claim of universal mission or campaign coverage.
