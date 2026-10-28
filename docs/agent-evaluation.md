# GTA use agent evaluations

## Comparison: DeepSeek 1017 Max and GPT-6.1 Terra Max

**Neither model completed Repossession.** DeepSeek reached the pursuit but needed
repeated host intervention; Terra handled its own recovery after setup, stayed in
cover during its combat sequence, and stopped earlier at an unresolved firing
angle. Neither trial has a Mission Passed screenshot.

| Measure | DeepSeek-V4.1-Flash 1017 / Max | GPT-6.1 Terra / Max |
| --- | --- | --- |
| Furthest verified stage | Garage combat, then unsuccessful bike pursuit | Garage combat; one confirmed attacker defeated |
| First supervised run to last run ending | 2h 14m 7s | 2h 17m 34s |
| Sum of supervised runner elapsed time | 28m 5s | 16m 24s |
| Supervised runs | 98 | 54 |
| Runner checkpoint / failed states | 53 / 45 | 34 / 20 |
| Actual GTA checkpoint retries | 4, operated by host | 0; stopped before a mission-failure retry |
| Confirmed pause / unknown pause results | 92 / 6 | 50 / 4 |
| Host setup | Launch, backups, interpreter/PID and configuration | Updated installation, fresh Story session, paused handoff, Thor disabled and backups |
| Host gameplay help after handoff | Modal recovery/retries, focus handling, navigation and API guidance, combat correction | None recorded; the task handled its own menus, expired leases and settings restoration |

Times were recomputed from retained `run.json.started` and
`result.json.elapsedSeconds`, using first start to final end rather than summing
policy durations. The DeepSeek window was 2026-10-27 22:15:48 to 2026-10-28
00:29:55; Terra was 2026-10-28 00:55:49 to 03:13:22, all Asia/Shanghai. These
windows include observation and cleanup policies. Runner time includes I/O and
waiting; it is not unpaused simulation time. Setup, initial preparation and final
report writing lie outside these windows. The remaining time includes reasoning,
tool interactions, UI recovery and gaps; no token/cost measurement was available.

Terra used fewer scripts and less runner time, but nearly the same wall time and
reached less of the mission. Zero retries is not evidence of higher mission
success when the attempt stopped at an earlier blocker. Terra's original skill
already included changes learned from DeepSeek; their harnesses, assistance and
stopping conditions also differed. This is a single assisted-workflow comparison,
not an isolated model ranking or measured before/after improvement.

Terra left Franklin at 191 health and Lamar at 418, previously observed at 500.
The cause of Lamar's damage is unknown. One early burst preceded its companion
line-of-fire check, so the later guard does not establish the safety of that shot.
It restored Autosave and Pause Game On Focus Loss to On and verified both save
hashes unchanged. The host independently rechecked the hashes after the run.

The retained Terra trace and reproducible comparison metrics are outside Git at
`C:\Users\Helen\Documents\Codex\2026-10-28\agent-comparison`. Its review-only
patch was inspected before applying the narrow documentation and policy changes.

## Changes from the Terra findings

- `aim` now returns constant-space frame counts and the last observed LOS,
  aim-target handle and requested Attack state. A snapshot taken after releasing
  aim can miss what happened during the action; counters preserve that evidence
  without per-frame file writes or retained samples.
- Optional `requireAimTarget` withholds Attack when the engine reports another
  entity or an unknown/stale aim target. Default behavior is preserved. Matching
  aim is not proof of a clear muzzle path, friendly-fire safety or damage.
- The cover example requires an explicit companion list, checks nearby humans
  and companion/blip positions conservatively, uses the gate, records ammo and
  target health across the whole aim interval, and stops on fully withheld fire.
  Its 2D clearance is an example filter, not a general combat or cover planner.
- Skill references clarify loaded versus playing cutscenes, menu-induced lease
  timeouts, changing combat flags, incomplete nearby scans and whole-interval
  shot evidence. Repeating a blocked angle is not useful progress.

The full build/test/package workflow passed **331 C#**, **13 Python** and
**8 Rust** cases. C# executable line/branch coverage stayed at 100%: GameBridge
1076/1076 and 595/595; ModUtils 274/274 and 90/90; Thor 1496/1496 and 610/610.
The added scenarios verify fire withholding for a mismatched/unknown/stale aim
target, normal fire when both checks pass, LOS overriding a matching target,
control release, bounded telemetry, companion/unknown-human obstruction,
missing-companion recovery and interval-wide evidence. They use explicit engine
observations, not simulated successful gunfire.

After a normal game exit, all 178 packaged files were deployed with backups and
hash verification, followed by a fresh Story launch. A live unarmed check recorded
24 aim-only frames and 26 frames with `shoot=true, requireAimTarget=true`; both
reported unknown aim targets, no LOS and zero Attack requests. Health remained
200 and the runner confirmed pause. This validates deployed feedback and the
combined withholding path in that scene. The positive matching-target fire path
is covered by integration scenarios, not by this live check. The new policy has
not completed a mission or established reliable armed cover fire.

A separate 700 ms aim-only diagnostic in Terra's final firefight, before
deployment, preserved 191 health and 138 total pistol ammo. Its post-action
snapshot returned no aim target, illustrating why it cannot reconstruct the
per-frame aim decision. Neither follow-up is counted as part of Terra's trial.
The follow-up game process exited normally. Both save hashes remain unchanged;
all deployed files match their manifest. Final graphics comparison found only
the window-mode value differed from the pretrial backup. With GTA closed, that
value was restored and the complete graphics file again matched the original
hash. The cause of that window-mode change was not established.

## DeepSeek 1017 Max trial

Evaluated on 2026-10-27–28 with DeepSeek Harness, DeepSeek-V4.1-Flash build **1017**,
reasoning **Max**, Workspace Write access, GTA V Enhanced **1.0.1158.16** and the
Rust-only bridge. Python used the prepared **3.14.9** environment.

**Result: Repossession was not completed.** DeepSeek reached the briefing,
destination, garage search and combat, then repeatedly failed the bike-recovery
and pursuit sequence. The host ended the trial after four checkpoint retries and
continued navigation/selection mistakes. This skill/bridge combination did not
produce reliable mission autonomy for this model in this trial. The earlier
Repossession proof in the API guide was produced separately and is not evidence
of DeepSeek success.

## Method and assistance

DeepSeek received the repository skill and permission to inspect the SDK and
write supervised Python policies. It was told not to read the previous solved
mission scripts, use gameplay cheats, alter mission state, or overwrite saves.
It owned gameplay commands; the host read evidence and changed production code
independently. The save/profile and graphics configuration were backed up before
launch. Gameplay evidence remains outside Git.

This is an iterative usability test, not a controlled model benchmark. The host
initially requested a plan and skeleton while arranging launch, contributing to
the amount of preparation. DeepSeek spent 62 steps and 10m36s on that initial
phase and produced about 1,500 lines of plans/helpers before live play. Those
figures should not be attributed solely to the skill.

The retained trace contains **98 completed supervised runs**: 53 runner checkpoint
states and 45 failures, with **103 screenshots**. The checkpoint states include
inspection and partial objectives, not mission successes. Combined runner elapsed
time was 1,685 seconds (28.1 minutes), excluding model reasoning, tool interactions
and host setup/recovery. Ninety-two final results confirmed pause; six reported
unknown pause after blocked game-thread requests. The host verified no active
lease, completed batch and `paused:true` before taking over for cleanup.

The host launched through Steam, supplied the verified PID, resolved the autosave
modal without accepting an overwrite, temporarily disabled Pause Game On Focus
Loss, and restored GTA foreground after inspecting the harness. An unintended
delayed Insert input caused a managed reload, requiring Thor to be disabled again.
The host also supplied general routing guidance from the revised skill after
repeated straight-line attempts at buildings. These are assisted observations.

## Findings and changes

| Observed friction | Change or finding |
| --- | --- |
| `uv run` attempted to modify an inaccessible environment despite an existing prepared interpreter. | Skill and API guide now start with `.python-path` and `python -m gta_use`; setup is conditional. No permissions were weakened. |
| Capability discovery dumped the entire control enum. | `describe` prints operation/command/section names and a control count. `--command` selects one real schema; `--control` filters names. The original complete request remains available. |
| Basic inspection required inline JSON quoting and several tool interactions. | `inspect` collects status and the original filtered world pack, optionally captures, and can write a fresh JSON evidence file. Sections, radius, entity/blip limits, types and LOS are CLI arguments. |
| Repeated broad documentation reads and a large helper framework delayed first play. | Skill uses progressive disclosure and starts with a small runnable observation policy. It points to the existing runner's logs, timeouts and evidence methods. |
| Searching native hash names in SHVDNE XML did not find C# methods; cross-references obscured overload documentation. | Python guide shows exact member selection and typed movement APIs. |
| Guessed `new Vehicle(handle)` failed to compile against the pinned Enhanced API. | Guide points to `Entity.FromHandle`, a checked type cast, existence and role validation. DeepSeek also diagnosed and corrected this compile error itself. |
| An accepted straight-line task stopped at walls; repeating guessed routes wasted runs. | Guide distinguishes navmesh routing, route-result queries, observed movement, exterior waypoints and actual entrances. A navmesh retry subsequently reached the mission contact. |
| A new movement policy waited for vehicle entry while the simulation was still paused. | Guide explicitly says to resume before movement tasks. DeepSeek diagnosed and corrected this itself. |
| A cutscene advanced while the policy reported no progress, tripping the stall watchdog. | Guide includes advancing cutscene time as meaningful progress during a bounded wait. DeepSeek's corrected wait completed. |
| Following a moving companion kept roughly constant separation and falsely tripped the stall check. | Guide distinguishes world movement from distance-to-target progress; DeepSeek revised the loop to follow the moving blip continuously. |
| Helper keyword collisions (`name`, then `screenshot`) failed otherwise useful scripts. | These were policy implementation errors, reinforcing the recommendation to use the shipped context before building wrappers. They are retained in the failed-run evidence. |
| Some policy checkpoint names claimed vehicle entry while their evidence contained `entered=false`. | Host review rejected those labels as success evidence. The supervisor enforces evidence presence, not the truth of arbitrary mission predicates; the policy must enforce its actual postcondition. |
| HTTP session discovery worked while modal UI suspended game ticks. | Skill separates companion readiness from game-thread readiness and documents visible-UI recovery after one bounded failure. |
| The old installed SDK lacked the newly added CLI commands. | Evaluation used the source interpreter recorded by the build. Installed SDK updates require deployment; changing the skill alone does not update it. |
| Repository instructions still called individual action requests/results durable files. | Corrected `AGENTS.md` to distinguish ephemeral actions from bounded file diagnostics and retained batch/policy evidence. |
| A numeric vehicle model was mistaken for the mission bike; the selected Bison pickup was trapped while the actual biker escaped. | Entity packs now add `modelName` from the pinned SDK's ped/vehicle enums, reusing the existing model hash without extra native queries. Unknown/custom models remain null; names do not establish mission roles. |

The compact capability response was **358 bytes** versus **10,069 bytes** for the
same full single-line JSON catalogue: **96.4% less agent-visible output**. This is a
discovery/output improvement, not a measurement of transport latency or model
success rate. Client-side capability filtering still reads the full catalogue;
world-state filters actually bound the requested game work.

Across 1,696 recorded observations with mixed filters, game-side state capture
was 1.05 ms median, 2.70 ms p95 and 15.41 ms maximum. This excludes HTTP, tool and
model overhead and is not a controlled latency benchmark. These observations
point to script fragmentation and policy decisions as major costs in this trial;
they do not establish a before/after improvement in model success or total time.

## Automated validation

The full build/test/package workflow passed **330 C#**, **8 Rust** and **8 Python**
cases, plus coverage, installer and diagnostic guardrails. Executable C# coverage
remains 100% of lines and branches: GameBridge 1060/1060 and 577/577; ModUtils
274/274 and 90/90; Thor 1496/1496 and 610/610.

The external CLI scenario drives the real Rust service and cooperating managed
bridge with explicit external game/device observations. It checks compact
discovery, command/control selection, rejection of invented commands, filtered
nearby/navigation results, optional capture, no lease acquisition for inspection,
evidence preservation, service restart and managed reload. A transient Windows
sharing violation found during the build is now retried by the PowerShell batch
status reader within its existing deadline, without replaying the accepted action.
Both a temporarily absent status file and an exclusively locked file are covered.

The skill passed its validator. Live compact `describe` and filtered `inspect`
also succeeded against the active GTA session without taking the policy's lease.
After deployment and a fresh Steam launch, the installed SDK repeated both checks.
The pack identified Franklin, Buffalo2, Crow and nearby pedestrian models by
name, while unsupported object models remained null. Story resumed at Franklin's
original house, the Rust service started automatically, and SHVDNE logged both
scripts starting in the new session. The host disabled Thor, captured the scene,
and confirmed an idle scheduler, released lease, paused simulation and zero
dropped diagnostics before handing sole control to the comparison task.

## Remaining limits

The world pack exposes structured observations, not arbitrary mission HUD text,
private script variables, a universal cover finder or a general route planner.
Screenshots still resolve objectives and ambiguous geometry. A healthy, driveable
vehicle may be trapped inside a showroom; command acceptance and engine flags
alone do not establish a usable route. Campaign-wide autonomy requires broader
mission testing than this run.

The first mission attempt ended with **Mission Failed — Lamar was abandoned**.
The host operated Retry through the visible game UI. When ticks resumed, lease
expiry paused the game during its black transition; a new supervised controller
needed to resume before judging whether checkpoint loading had stalled. The
companion can serve diagnostics during these modals, but it currently cannot
operate their desktop input or capture them through the game-thread endpoint.

Host review also found a combat policy that treated all nearby humans except
Lamar as targets, approached invisible targets after unsuccessful cover taps,
used a health threshold of 90, and treated target disappearance as death. These
contradict the skill's existing confirmed-target and cover-first guidance. The
host requested cancellation and stricter predicates, pointing back to the shipped
cover example. The first run had already stalled; the next approach and firing
segments completed before that feedback took effect. Their nine reported kills
and 150 remaining player health do **not** prove cover-first combat or correct
target selection. This is a model-compliance failure, not missing native access.
The firing log records 24 bursts: 16 with `inCover=false` and eight with
`inCover=true`. DeepSeek wrote a stricter replacement policy afterward; writing it
alone does not establish that it works in combat.

The second failure was **Mission Failed — The biker escaped**. After the fight,
DeepSeek entered a Bison pickup instead of pursuing the moving mission target,
then spent its bounded driving/reversing attempts in the trapped vehicle. The
host operated Retry again. This exposed both an observation usability gap
(numeric-only vehicle identification) and a policy error (not verifying the
vehicle's role and useful movement before continuing).

A further pursuit retry failed with the same biker-escape result. Its policy
declared the target missing when it crossed a hardcoded 500 m selection bound;
the saved state still contained that blip at 504 m. The host identified this
from the evidence and recommended tracking the confirmed blip/entity identity
and fresh position. The world-state guide now includes this general distinction
between selection filters and identity. Walking to the moving bike's stale
position and repeated short setup segments also consumed its escape window.
Another biker-escape failure occurred during car entry, before the newly selected
continuous chase task could run. The host supplied the actual local
`TaskInvoker.VehicleChase(Ped)` documentation; this is API assistance, not live
validation of that task. The guide distinguishes a moving-target task from
repeated static routes and keeps ordinary target motion within a bounded policy.

## Cleanup and comparison handoff

After the controller stopped, releasing the simulation pause revealed another
biker-escape failure. The host did not retry it. The normal mission-exit and quit
confirmations repeatedly failed to accept desktop input, so the host terminated
that verified GTA process before deployment. This recovery is not a clean-exit
verification.

Both Story save files still matched their pretrial SHA-256 hashes. With GTA
closed, the host restored the original graphics settings byte-for-byte; the only
difference had been Pause Game On Focus Loss. Autosave had been disabled after
declining an overwrite and remained a temporary test setting through the handoff;
Terra subsequently restored it to On. No profile/account configuration was replaced.

The rebuilt package was deployed with all 178 installed files hash-verified and
the replaced files backed up outside Git. A separate built-in Codex task was
created for GPT-6.1 Terra with Max reasoning. It must use fresh policies without
reading DeepSeek's artifacts or the previous solved mission. It receives the
revised skill and bridge, so this is a comparison of assisted workflows, not an
isolated model comparison under identical instructions. Its completed outcome
and the follow-up changes are recorded above.

The DeepSeek trace was archived outside Git at
`C:\Users\Helen\Documents\Codex\2026-10-27\deepseek-1017-eval-20261027-132223\trace`.
It retains 1,066 files (about 462 MiB), excluding Python caches. The host's report
separates verified observations from DeepSeek's own final notes: the host made
the production changes, the graphics setting was restored after those notes were
written, and a persistent vehicle task alone was not proven to cause blocked ticks.
