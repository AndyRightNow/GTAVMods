# Casing the Jewel Store development run - 2026-10-31

Casing the Jewel Store reached an independently inspected **Mission Passed -
Casing the Jewel Store** banner through normal Story Mode play. Michael drove
with Lester, photographed the shop, spoke to the clerk, traversed the construction
site and roofs, photographed the ventilation, returned to the factory and chose
the Smart approach. Eddie Toh, Gustavo Mota and Paige Harris were selected through
the displayed planning interface. Thor stayed disabled. No teleport, grants,
invincibility, mission-variable edits or Skip was used.

This was an assisted development run with many policy revisions and pauses,
not an uninterrupted benchmark or an unaided model evaluation.

## Time and attempts

The two mission starts had **zero actual Mission Failed events and zero checkpoint
retries**. The first attempt was deliberately ended to deploy relative mouse input
and rendered-camera observations. A new game process restarted the mission from
the preceding normal save; that is one runtime restart and a second full mission
start, not a checkpoint retry.

| Interval | Wall time | Supervised runner time | Policy runs |
| --- | --- | --- | --- |
| First active attempt, before deployment shutdown | 45m 3s | 9m 30s | 18 |
| Restart/deployment gap to second active attempt | 24m 7s | Excluded from mission runner totals | - |
| Completed attempt | 2h 21m 57s | 29m 0s | 79 |
| First active attempt to named completion | 3h 31m 7s | 38m 30s | 97 |

The first active observation was 2026-10-30 23:25:43.566722 UTC. The completed
attempt began at 2026-10-31 00:34:53.775161 UTC; the named result capture was
02:56:50.754686 UTC. Runner totals are clipped to these boundaries. The completed
attempt's untrimmed runner sum is 33m 22s because the starting run also includes
approach travel. Eighteen policies failed during the completed interval; these
were watchdog, validation or uncertain-outcome stops, not game retries.

The parent authored/revised policies, interpreted images, consulted public
mission walkthroughs and operated the normal save menu. No user supplied gameplay
input and no other controller operated concurrently. Wall time includes paused
investigation, model/tool latency and validation work. This remains far slower
than a competent continuous policy and does not meet the earlier uninterrupted
mission benchmark.

## Evidence and save

Paths are relative to ignored `artifacts/gta-story-continuation`.
`casing-evaluation.json` records exact boundaries and per-run durations.
The first attempt is retained under
`D:/GTAVMods-evidence/2026-10-31/casing-camera-blocker-attempt-1`.
The completed attempt archive is
`D:/GTAVMods-evidence/2026-10-31/casing-completed-evaluation`: 2,319 files,
1,788,545,724 bytes, with SHA-256 manifest, source snapshots, action/state traces,
failed policies and screenshots. Saves have a separate backup.

| Observation | Evidence |
| --- | --- |
| Accepted shop photograph, then clerk objective | `casing-keypad-photo-001/003-keypad-feedback-0.png`, `009-keypad-frame-observed-after.png` |
| Visible AC unit used as roof access | `vantage-ac-climb-002/`, `vantage-hut-climb-001/`, `vantage-position-001/` |
| Accepted ventilation photograph | `vent-photo-004/008-vent-frame-inspect-after.png` |
| Actual ladder descent and settled landing | `ladder-return-navmesh-002/`, `construction-ladder-helper-001/` |
| Return to Lester and normal drive to factory | `casing-stair-return-004/` |
| Approach and crew selected through visible UI | `heist-smart-*`, `heist-driver-select-001/`, `heist-gunman-select-001/`, `heist-hacker-select-001/` |
| Named Mission Passed | `heist-plan-confirm-001/014-menu-followup-12.png` |
| Normal separate save | `casing-save-phone-001/`, `casing-save-left-001/`, `casing-save-open-001/`, `after-casing-save-001.json` |

Result image SHA-256:
`df1c1c88c18d3f39956b2367eccf0a8f8ea411fa4ef01fc6bc7597aeb2a0d4e5`.
Quick Save created **SGTA50007**, preserving previous slots. Its 566,568 bytes and
matching `.bak` were backed up and hash-verified outside the repo:
`1c5a9d48dbe25c4911ccccc10755f6b04a9b2fd7ecb578f6823095a8584bd65f`.

## Reusable changes and remaining limits

Relative mouse input and rendered-camera observations enabled both required
photographs. A submitted photo still needs visible mission feedback; an accepted
input receipt is insufficient. The ventilation photo initially targeted a small
AC unit before the correct large vent box was identified.

The player state now exposes `climbingLadder`, using the native task-active query
used by SHVDNE's own ladder property. The live player could be attached while
`climbing` and `inAir` were both false. Navigation now stops without clearing that
animation instead of declaring arrival on a rung. The general `climb_ladder`
helper mounts once, applies bounded ordinary up/down controls while attached,
requires signed height progress and waits for a stable dismount. It guards health,
identity, mission takeover and overall/stall deadlines. Older bridges use a native
query fallback without compiling C# for each observation.

The initial extracted helper descended a construction ladder live in 6.30 seconds,
with a 2.54 m height change and an independently inspected standing-at-bottom
image. The final native/state-field revision still needs its separate live check;
the earlier run is not proof of that exact source revision. Automated scenarios
cover ascent/descent, false exit heights, failed mounts, stalls, uncertain receipts,
identity/health changes, mission takeover and preservation of ladder attachment.

Several delays were policy mistakes: ray hits on rooftop equipment were treated
as floor heights; long navmesh routes reversed completed climbs; rung positions
were used as return waypoints; wall cracks were misread as a ladder. The skill now
explains these distinctions and favors observed standing positions, explicit
landings and ordinary routes around equipment. Native navigation is not a general
parkour planner. Screenshots remain necessary for mission text and ambiguous
geometry. The physical-mouse heartbeat under concurrent capture remains a recorded
follow-up; it is not claimed fixed here.

The full build/package workflow passed 136 Python scenarios, 20 Rust tests and
348 C# integration tests, with 100% executable line and branch coverage for
GameBridge, ModUtils and Thor. The four changed installed files were backed up
with hashes and deployed only after GTA exited.
