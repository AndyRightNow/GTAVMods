# Gameplay tuning and test review, 2026-10-21

Gameplay calibration now lives in [`Thor/Tuning`](../Thor/Tuning/README.md).
Production and player integration scenarios use the same composition entry point;
each scenario can supply settings before the real components are constructed.
The extracted defaults retain their previous values. The terrain recovery timer
also has a behavior fix described below.

The throw test no longer copies seventeen animation/delay combinations. Two
cases supply a positive delay and zero delay, observe the hammer while the host
wait is pending, then allow the host to resume late. They require the selected
clip's delay, the expected animation request, and exactly one release after the
wait completes. The separate loading-failure scenario checks its timeout before
windup, including signed clock rollover. Actual animation-to-release calibration
still requires the game.

Other scenarios use supplied distance bands, damage, health, power fractions,
timeouts, volumes, camera framing and effect settings. Assertions protect
observable relationships and boundary behavior, including translated camera/trail
positions, proportional stored power, configured expiry and asynchronous catch
confirmation. They do not assert a default camera-preset table, intermediate
health values copied from the implementation, or a trail tessellation index.
Pure geometry tests also require preserved vertices, and diagnostic tests require
a stable output bound as oversized inputs grow.

Three xhigh subagents audited the tests and cross-reviewed each other's changes.
The review corrected a wraparound setup whose windup consumed the clock boundary,
camera observations coupled to default preset weights, default-dependent rotation
convergence, overly broad power/trail assertions, a log-loss comparison that used
the same counter twice, and a geometry assertion that could accept no vertices.
The policy and feature organization remain in `AGENTS.md` and `ThorTest/README.md`.

The recovery scenario exposed two actual timer bugs: casting a random fraction
before multiplication always removed its jitter, and checking the clock's exact
modulo could miss an interval between frames. Recovery now measures elapsed time
from entry/refresh and applies jitter before truncation. The scenario crosses
deadlines on irregular frames, verifies the jitter delays the next change,
checks bounded motion and confirms manual recall overrides drift. This does not
establish that GTA terrain collision is fixed.

## Focused fault checks

The complete 289-case baseline passed before fault injection. Each change below
was applied separately to an isolated source copy, compiled successfully and was
rejected by its relevant player scenario. Source bytes were restored after each
check. These nine checks are evidence for specific assertions, not a full mutation
score or a proof that every possible fault is detected.

| Injected fault | Observable expectation that rejected it |
| --- | --- |
| Skip the throw wait | The hammer cannot release before the host completes the wait. |
| Replace the supplied throw delay with a fixed delay | The selected clip controls the requested wait. |
| Render the trail from the world origin | The trail starts at and translates with the observed hammer. |
| Ignore configured trail detail | Denser settings produce more distinct trail locations. |
| Force lightning sway to zero | Enabled sway produces lateral detail; zero sway stays straight. |
| Refresh recovery only at exact clock multiples | The first frame crossing the deadline starts bounded drift. |
| Truncate the jitter sample before scaling it | Nonzero jitter postpones the next direction change. |
| Omit the hammer position from camera framing | The view keeps the supplied offset when the hammer moves. |
| Substitute a wrong stored-power fraction | Player health and movement match the supplied fraction. |

The shared build still requires 100% measured executable line and branch
coverage on all four mod assemblies, with no new exclusions and the same 146
audited primitive bindings. The source and catalogue contain 159 test methods
executing 289 cases: 218 player integration cases and 71 pure helper cases.
The catalogue excludes PowerShell script tests. Rendering, physics, audible
output and the suitability of calibrated defaults remain live-game checks.
