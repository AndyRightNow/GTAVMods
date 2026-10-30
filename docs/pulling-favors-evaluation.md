# Pulling Favors development run - 2026-10-30

Pulling Favors reached an independently inspected **Mission Passed** banner via
normal Story Mode travel, tow-truck controls and one physical H hold. Thor was
disabled. No teleport, health/ammo grants, mission-variable changes, forced
attachment/detachment or mission Skip was used. This was a parent-assisted
development run with pauses and policy revisions, not an uninterrupted benchmark.

## Time and attempts

From starting travel to Tonya at 16:43:22 UTC through the final captured result at
17:42:17 UTC: **58m 55s wall time**, **17m 8s across 18 supervised Python runs**.
There was **one mission start, zero mission failures and zero checkpoint retries**.
Runner time includes observation and diagnostic segments. Host work included
routing, screenshot inspection, gate corrections, input investigation and code
changes. No user gameplay input was required during this mission. Saving,
packaging and deployment afterward are excluded from these mission times.

## Retained evidence

All paths below are relative to the ignored `artifacts/gta-story-continuation`.
`pulling-favors-evaluation.json` records the run list, timestamps and save backup.
Original entry scripts, SDK source snapshots, actions, observations, screenshots
and unsuccessful attempts remain available.

| Observation | Evidence |
| --- | --- |
| Mission briefing | `contact-intro-001/` |
| Ordinary vehicle entry and tow-truck acquisition | `pulling-favors-truck-001/` |
| Reverse alignment physically hooks the marked car | `tow-hook-001/` |
| Towed-car identity and health preserved during return | `tow-return-001/` |
| Gate opening reached through bounded ordinary steering | `tow-yard-gate-001/` |
| H-bound simulated controls read as pressed without releasing hook | `tow-release-005/` |
| Actual 2.5-second H hold releases the car | `tow-physical-hold-001/events.jsonl` |
| Actual Mission Passed banner | `tow-physical-hold-001/010-pulling-favors-result-inspect-after.png` |

Mission Passed screenshot SHA-256:
`3e79cf19c9531fd5534fe5b4c0ec1f7deaee7fe8218da9b9ca31517619dd7aa0`.
Normal Quick Save created `SGTA50003`; previous slots were preserved and the new
save was separately copied and hash-verified outside the repository.

## Capability and policy findings

Simulated H-bound controls reported pressed values, but repeated bounded attempts
never released the car. Actual keyboard H released it on the first live hold and
produced Mission Passed. The engine's internal reason is not established. The
reusable fix is a leased, bounded Rust keyboard endpoint with Python
`game.hold_key`, foreground checks and release on deadline/focus/lease loss.
The mission test invoked this same implementation using a temporary CLI because
the installed runtime could not be replaced while GTA was running. That test CLI
is excluded from production; HTTP endpoint verification is recorded separately.

The road-driving task stalled at the impound gate. A low-speed approach through
the observed opening worked; steering directly toward a final marker was a policy
error. The final truck position already enabled the drop-off prompt even though
the chosen coordinate lay beyond a wall. Stop on observed objective eligibility
instead of forcing exact marker proximity.

Entering Tonya's contact cutscene also exposed an overly strict navigation stop.
`navigate_to(..., allow_transition=True)` can now return an explicit transition
without clearing mission-owned tasks or claiming arrival. The default remains
conservative; missing/changed player, menus, vehicle entry and health/water risks
still stop. Automated scenarios cover these boundaries; the new opt-in behavior
requires separate live contact validation.
