# Marriage Counseling development run - 2026-10-30

Marriage Counseling reached an independently inspected **Mission Passed -
Marriage Counseling** banner through normal Story Mode play. Michael chased the
coach, reached the canyon driveway, pulled the house down with ordinary throttle,
fought Madrazo's men, and returned home with Franklin. Thor stayed disabled.
No teleport, health/ammo/weapon grants, direct damage, mission edits or Skip was
used. Michael's normal special ability used his existing meter without refilling.
This was an assisted development run with pauses and policy revisions, not an
uninterrupted benchmark or a Terra/DeepSeek comparison.

## Time and attempts

The first observed active mission at 19:26:02 UTC to the captured result at
20:50:13 UTC took **1h 24m 11s wall time**, with **17m 40s of supervised runner
time during that interval across 38 runs**. The raw runner sum is 18m 46s because
the initial run includes 66 seconds of travel before the first active-mission
observation. There was one mission start, three actual mission failures and three
normal checkpoint retries. Saving, post-result transitions and retry-menu
watchers are excluded from those runner totals; failed and diagnostic segments
within the mission are included. Wall time includes paused investigation/builds.

No user gameplay input was needed. The parent wrote/revised policies, inspected
images, performed three Retry-menu recoveries and consulted online mission
walkthroughs while diagnosing the house pull. This was not an unaided evaluation.

## Evidence and save

Paths below are relative to ignored `artifacts/gta-story-continuation`.
`marriage-evaluation.json` records source runs, timing boundaries, failures and
save hashes. The complete selected evidence, including failed traces, was copied
to `D:/GTAVMods-evidence/2026-10-31/marriage-evaluation` with a hash manifest
(1,110 files, approximately 825 MB).

| Observation | Evidence |
| --- | --- |
| Coach chase and canyon approach | `marriage-coach-pursuit-001/`, `marriage-canyon-local-002/` |
| Ordinary throttle and full house collapse | `house-input-diagnostic-001/` |
| Michael died twice; Franklin died once | `marriage-failed-001.png`, `marriage-failed-002.png`, `marriage-failed-003.png` |
| Vehicle shots spent ammo without measured target damage | `marriage-confirmed-defense-001/`, `marriage-passenger-fire-001/` |
| Three attackers defeated from settled cover | `marriage-truck-cover-003/`, `marriage-cover-selection-001/`, `marriage-cover-selection-002/` |
| Final attacker defeated after a short flank | `marriage-final-attacker-001/` |
| Return, ending cinematic, named Mission Passed | `marriage-home-001/008-home-ending-inspect-after.png` |
| Normal phone/save UI and post-save observation | `marriage-save-001/`, `marriage-quicksave-001/`, `after-marriage-save-001/` |

Result image SHA-256:
`b27ac2244e19e70936bd8feda24a1fc62c4c1fbf74bef3e2241af15c77569d94`.
Quick Save created **SGTA50005**, preserving earlier slots. The save and its `.bak`
were hash-verified in a separate evidence directory:
`071c25a740bbd6f60c556608dafec5a86f437585ed7258e50d923a55fe036e69`.

## Fixes and limits

The SDK incorrectly treated an unavailable vehicle clip count (`null`) as empty.
`fire_from_vehicle` now distinguishes null from observed zero and permits one
bounded, target-gated burst when reserve ammunition exists. Live verification
spent ammunition after the fix; it did **not** prove accurate moving vehicle
fire. This mission's successful combat used cover and an exposed flank instead.
The final flank used one round and changed the confirmed attacker's health from
200 to 0; no direct damage command was used.

Several failures were policy mistakes: repeated reloads with unavailable clip
counts, reloading while Unarmed after vehicle exit, incorrect helper arguments,
and trying to switch characters before observing the requested character.
The new offline `python -m gta_use helpers [query]` command exposes actual SDK
signatures, imports and contracts. The skill documents null ammo, owned weapon
selection and the difference between a control transition and character identity.
Native drive-by and duck policies stopped before executing those actions; these
runs provide no live evidence for those mechanics.

The native road task stalled at the canyon entrance. Bounded local steering and
full throttle reached the nearby driveway through normal driving. Earlier short
pulls only partially collapsed the house; sustained throttle completed it.
The ad hoc task-status query in that investigation used an unverified script-task
identifier and is not evidence about engine task status.

The Mission Passed banner appeared while `missionActive` remained true and
`playerControl` was false. The policy therefore ended at its inspection deadline;
visual result evidence establishes completion, not that checkpoint label or flag.
After Continue, a normal post-mission transition finished before saving.

## Validation

`build-unknown-vehicle-ammo-001.log` records the passing build for the ammo fix:
107 Python tests, 15 Rust tests, 346 C# scenarios, formatting/Clippy, and 100%
executable C# line/branch coverage for GameBridge, ModUtils and Thor. Both unknown
clip and observed empty clip behavior are exercised through the real SDK runner
and Rust service with explicit external engine observations.

`build-helper-catalogue-002.log` adds three offline CLI discovery scenarios and
passes the complete package workflow with 110 Python tests, the same 15 Rust and
346 C# scenarios, and unchanged 100% C# coverage. No runtime files were replaced
while GTA was running.
