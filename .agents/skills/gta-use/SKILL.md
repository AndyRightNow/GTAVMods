---
name: gta-use
description: Operate and diagnose GTA V Enhanced Story Mode through the GTA use bridge. Use for external reload and console commands, filtered world observations, timed controls, screenshots, and supervised Python game AI or mission testing. Does not apply to GTA Online or unrelated desktop automation.
---

# GTA use

Use filtered JSON observations and supervised Python policies for continuous
segments. Use screenshots for objective text, menus and uncertain geometry.
Command acceptance is not an observed gameplay outcome.

## Start playing without rediscovering the installation

For an already prepared installation, reuse its recorded Python 3.14 interpreter.
Build/install uses uv; playing does not need another sync or a new environment.
From the repository root (or `<game>/scripts/GtaUse` for an installed package):

```powershell
$gtaPython = (Get-Content -LiteralPath .python-path -Raw).Trim()
$gameProcess = Get-Process GTA5_Enhanced -ErrorAction Stop
& $gtaPython -m gta_use describe --process $gameProcess.Id
& $gtaPython -m gta_use inspect --process $gameProcess.Id --sections player navigation weapons --capture --output artifacts/initial-world.json
```

Use a fresh output path in an existing directory. `inspect` saves the original
pack under `state`, bridge status under `status`, and the image path under
`capture.screenshot`. Open that image; capture requires GTA visible/foreground.
`describe --command controls` returns one command schema;
`describe --control Move` returns matching control names. `inspect --blip-limit 256` checks all bounded navigation markers
once when `navigation.truncated` is true; radius, entity limit, type and LOS filters
are also CLI options. The raw
`request describe` remains available for the complete catalogue.

If `.python-path` is missing or its interpreter no longer works, read
[operations](references/operations.md) for setup. Do not run `uv sync` repeatedly
against a read-only environment. Process enumeration can be restricted in an
agent sandbox; an empty list there does not prove GTA exited. Use the host's
verified PID and normal scoped execution approval when required. Never dump
`service-<PID>.json`, which contains a bearer credential.

For mission scripting, read [Python AI](references/python-ai.md) and consult
[world state](references/world-state.md) for the fields your policy consumes.
Start by running the small observation example in Python AI. Then implement the
next observed objective. The runner already supplies logs, state files, leases,
heartbeats, checkpoints and timeouts; use `ctx.directory` and `ctx.log` instead
of building another framework before the first runnable segment.

## Observe, act, verify

1. Confirm a fresh player and the current objective. Disable Thor once for normal
   mission play: `game.command("Thor.Thor.Current.SetEnabled(false);")`. Use the
   backed-up Story save; preserve settings and progress.
2. Unknown scene: `game.step(500, inputs, **filters)` advances, pauses and observes.
   Known segment: write `run(ctx)` and use `gta-use run` with finite overall,
   stall and observation deadlines. Resume once, react until a verified objective
   or uncertainty, and inspect the runner's final `paused` result.
3. Verify actual position progress, vehicle occupancy, acquired cover, target
   damage or objective transition. Revalidate handles after time advances.
   A hostile relationship alone does not identify a mission enemy. For blocked
   fire, inspect aim feedback and companion clearance; see Python AI before
   repeating the same firing angle.
4. On failure, inspect `result.json`, `state.json`, `events.jsonl` and stderr.
   Correct the cause and recover the game's real checkpoint before a new run.
   A policy checkpoint label does not create a GTA checkpoint or prove success.
5. Capture significant transitions and the actual Mission Passed screen. Preserve
   failed attempts and script/log evidence outside Git; report assistance and
   unverified mechanics as well as successes.

## If HTTP works but the game does not answer

`/v1/session` is served outside the game; state and capture need SHVDNE ticks.
A pause menu, focus loss or mission-failure modal can suspend those ticks. After
one bounded state failure, inspect the visible game instead of spending minutes
retrying the same request. Ordinary desktop input may be needed to resume/retry.
Reobserve before acting; never blindly replay an uncertain mutation. The bridge's
simulation pause is different from the game's pause menu.

When switching to a separate harness interrupts gameplay, record and temporarily
disable Settings > Graphics > Pause Game On Focus Loss; restore it afterward.
Follow each modal's displayed shortcuts, not its hover highlight. On the autosave
overwrite alert, Esc means No and Enter means Yes. Declining may open a separate
disable-autosave confirmation. Verify each transition before sending another key.

## Boundaries

- Story Mode only. Close GTA before replacing DLLs/plugins. For setup, runtime
  changes or save handling, read [AGENTS.md](../../../AGENTS.md) first.
- One controller per game. Stop an existing policy and confirm pause before a
  handoff. No detached workers or stall resets for unchanged observations.
- General native/C# access is trusted code. Keep C# brief and nonblocking; Python
  owns long logic. Preserve native argument types. See the task lookup guidance
  in [Python AI](references/python-ai.md) before inventing a native signature.
- Lease expiry acts on the next game tick; it cannot preempt a blocked script
  thread. Pause does not erase persistent native tasks. Reload invalidates
  handles, leases and dynamic registrations.

## Read more only when needed

- Connect, reload, recover or diagnose: [operations](references/operations.md).
- Field meanings, filters and costs: [world state](references/world-state.md);
  [full state contract](../../../docs/world-state.md) for exact fields.
- Write or revise a continuous policy: [Python AI](references/python-ai.md).
- Install the Rust service: [server setup](../../../docs/rust-service.md).
- Startup menus: [Story startup helper](../../../docs/story-startup.md).
- Measure performance: [profiling](../../../docs/bridge-performance.md).
- Protocol, extensions or historical validation: [full API guide](../../../docs/gta-use.md).

Extend the existing transport/scheduler with namespaced commands or state sections.
Keep observations bounded and free of gameplay side effects. Production changes
follow the repository's scenario tests and 100% C# coverage gate. Prior Repossession
verification is evidence for those controls, not universal mission coverage.
