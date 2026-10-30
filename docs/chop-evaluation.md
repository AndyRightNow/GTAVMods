# Chop development run - 2026-10-30

Chop reached an independently inspected **Mission Passed - Chop** banner through
normal Story Mode play. Franklin drove the van, pursued D without shooting,
followed the on-foot chase, followed Chop to the boxcar, opened it with ordinary
input, and completed the capture, release and Lamar drop-off. Thor stayed disabled.
No teleport, grants, attachment edits, direct damage, mission-variable edits or
mission Skip was used. This was an assisted development run with pauses and
policy revisions, not an uninterrupted benchmark or a Terra/DeepSeek comparison.

## Time and attempts

From entering the contact at 18:18:17 UTC to the first Mission Passed capture at
19:12:45 UTC: **54m 28s wall time**, **19m 58s across 18 supervised Python runs**.
There was **one mission start, two mission failures and two checkpoint retries**.
Wall time includes paused implementation, testing and inspection. Runner time
includes failed and diagnostic segments; retry-menu watcher time and desktop
inputs are separate host assistance. Travel to the initial contact, saving and
post-completion evidence work are excluded. No user gameplay input was needed.

The parent wrote and revised policies, inspected images and recovered the two
actual game checkpoints. It also consulted [this mission guide](https://thegta.wiki/wiki/chop-mission/)
when calling the distracted dog had no effect. The close approach and ordinary
interaction then worked. The run is therefore not an unaided mission evaluation.

## Evidence and save

Paths below are relative to ignored `artifacts/gta-story-continuation`.
`chop-evaluation.json` contains run times, outcomes, image hash and save manifest.
Sources, SDK snapshots, action/state traces, screenshots and failed attempts were
also copied to `D:/GTAVMods-evidence/2026-10-31/chop-evaluation` with a hash manifest.

| Observation | Evidence |
| --- | --- |
| Intro and expected contact transition | `franklin-door-001/` |
| D escaped after incorrect red-blip wait | `chop-van-pursuit-001/mission-failed-d-got-away.png` |
| D escaped after checkpoint-policy control-flow error | `chop-target-discovery-001/mission-failed-d-got-away.png` |
| Continuous nonlethal vehicle pursuit | `chop-known-rider-001/` |
| Persistent foot pursuit to railway search | `chop-foot-pursuit-002/` |
| Following Chop and close contextual interaction | `chop-follow-dog-001/`, `chop-close-call-001/` |
| Normal boxcar opening and capture | `chop-boxcar-001/` |
| Release and final drop-off | `chop-return-drive-001/`, `chop-dropoff-001/` |
| Named Mission Passed banner | `chop-save-001/002-chop-named-result.png` |

Named result SHA-256:
`6db95204421c1ac5824936f3ceec560752db387d35674b24bf7087a1f1ed107b`.
Normal Quick Save created **SGTA50004** without replacing an earlier slot.
The save and its backup were copied outside the checkout and hash-verified:
`c61b2cfe87a48e0e9a93d2a27654ef72e26e6e0b6401756097ae8d8324c6abed`.

## Capability fixes and policy mistakes

`entities.observe_entity` now provides bounded identity-checked position,
velocity, health, ordinary occupancy, attachment parent and relative-pose reads.
Chop was visibly in the van through a scripted attachment while ordinary seat
flags stayed false. D also returned an old bike handle after dismounting; gating
that handle with `Ped.IsInVehicle()` prevents false boarding decisions.

`navigation.follow_on_foot` submits one normal persistent following task and
observes identity, player health, separation, movement and mission transitions.
It does not stop merely because the target is nearby. Its stationary/deadline
stops preserve uncertainty; callers choose contextual interaction or obstacle
recovery. The shipped helper was exercised in the rail pursuit, dog following
and final pursuit. These runs validate those scenes, not every obstacle layout.

The first failure was a policy assumption that a visible enemy must yield raw
blip color 1. D and Chop both returned color 49 in this scene despite different
rendered markers. Read entity identity and the actual objective before selecting
or waiting for a target. The second failure followed a missing `return` after
`ctx.checkpoint`, which let a recovery loop run for another minute. The skill now
explicitly distinguishes recording a checkpoint from exiting the policy.

Calling Chop from too far away was also a policy mistake. Simulated and physical
E did not resolve it; moving close enough and using ordinary Context did. This
is not evidence of a broken keyboard bridge. A later following stall occurred
at a visible boxcar interaction prompt; the correct next action was interaction,
not more pathfinding. Route disappearance during the final drive correctly
triggered braking and the normal release sequence.

## Validation

`build-foot-follow-001.log` records the passing full build/package workflow:
105 Python tests, 15 Rust tests, 346 C# scenarios, Rust formatting/Clippy, and
100% executable C# line and branch coverage for GameBridge, ModUtils and Thor.
Eight new following scenarios protect persistent movement, boarding, target
loss, stalled movement, health stop, mission task ownership, policy handoff and
uncertain-receipt behavior. Live observations above are recorded separately.
