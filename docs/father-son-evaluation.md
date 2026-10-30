# Father/Son development run - 2026-10-30

Father/Son reached the actual **Mission Passed** screen using ordinary Story Mode
controls, Python policies and GTA player movement/driving tasks. Thor was disabled.
No teleport, health/ammo grants, mission globals, direct damage or mission Skip
was used. This was an assisted development run with pauses and policy revisions,
not an uninterrupted mission policy or an autonomous campaign benchmark.

## Time and attempts

From the first mission policy start (04:32:26 UTC) through the retained result
banner (13:35:38 UTC): **9h 03m elapsed**, **52m 47s in 61 recorded Python runs**.
Runner time includes diagnostics and observations; it is not pure gameplay time.
The remainder includes analysis, editing, approvals/tool overhead and pauses.
There were **8 actual mission failures, 7 normal checkpoint retries and 2 full
mission starts**. Policy stops, diagnostics and reload/deployment work are separate
from game failures. No user gameplay input was needed during this continuation;
parent-authored routing, scripting, UI retries and debugging were extensive.

## Evidence

Local evidence is retained outside Git under
`artifacts/gta-story-continuation/`; `STATUS.json` indexes results and source hashes.

| Observation | Evidence relative to that directory |
| --- | --- |
| Actual Mission Passed banner | `father-son-result-banner-001/003-result-banner.png` |
| Ordinary fire spent ammo and affected the marked attacker | `manual-rescue-continue-001/events.jsonl` (277 -> 276 ammo, 200 -> 171 -> 0 target health) |
| Jimmy climbs into the rear seat | `catch-jimmy-006/003-catch-jimmy-progress.png` and the next frame |
| Franklin lands on the hood | `recover-franklin-001/010-franklin-at-car-after.png` |
| Boarding completes and mission damages the engine | `rescue-boarding-transition-001/005-post-rescue-objective-inspect-after.png` |
| Normal free repair selected in the garage | `repair-confirm-001/002-menu-input-observed-after.png` |
| Continuous return drive and final scene | `father-son-return-home-001/` |

A new normal save was created in slot 3 (`SGTA50002`); prior slots were retained.
The original save/settings backup is recorded in `STATUS.json`. Failed runs and
their original source snapshots remain available; later revisions do not replace
them.

## Reusable lessons

The largest avoidable delays were repeated ineffective fire under a driving task,
manual pursuit tuning across traffic, and missing a successful scripted catch
because normal seat flags remained false. The Python AI skill now documents those
observations and their limits. The SDK's `follow_entity` extracts the persistent
moving-actor approach used for the rescues, with bounded progress, target/driver
validation, policy callbacks and mission takeover handling. It does not solve
precise moving alignment or infer mission success from distance.

The input-composition bridge change is separately verified: `aim` can retain an
existing continuous input set with `keepInputs=True`, while preserving its TTL.
That fixes input cancellation; it does not override GTA task conflicts.

The extracted following helper subsequently passed a bounded live approach to a
freshly observed moving traffic vehicle (`follow-helper-live-001`, 29.1 seconds
including ordinary car entry). The run retained source hashes, actions and world
states. Its screenshots were unavailable because GTA was not foreground; do not
present this check as visual verification or as another mission completion.
