# One-script Repossession replay — 2026-10-29

**Mission Passed: Repossession** was visually verified in a complete run from
Franklin's original-house save. One supervised Python invocation took **1,023.92 s
(17m 4s)**, including the contact call, travel to Simeon, normal cutscenes and
ending evidence collection. There were **zero intermediate policy pauses, game
checkpoint retries, script reloads or host inputs** during this passing attempt.
The supervisor paused after the final evidence checkpoint.

The [original example at ff99490](https://github.com/AndyRightNow/GTAVMods/blob/ff994908bc2d3749e754756657ec6cf40c9f9c32/tools/gta_use/examples/repossession.py)
is byte-identical to that run's snapshotted source. The current
[shipped example](../tools/gta_use/examples/repossession.py) extracts reusable
vehicle, combat and interception operations into the SDK; the historical timings
and hashes below identify the original run. It is a developed, mission-specific policy,
not a fresh blind model evaluation. Earlier failed development attempts are
accounted for below; this result does not establish repeatability or campaign-wide
autonomy.

## Run conditions

The installation was GTA V Enhanced **1.0.1158.16**, SHVDNE **1.1.0.6**, the Rust
service and Python **3.14.9**. The starting save was the separately installed
Franklin and Lamar **1.6%** save documented in AGENTS.md, with Franklin at about
`(-14.38, -1438.51, 31.11)`. The host loaded that original save before starting the
policy. Saves/settings were backed up separately. Autosave was already Off;
Pause Game On Focus Loss was temporarily Off for the test. Thor was disabled by
the policy before ordinary gameplay.

From an equivalent prepared, observed Story session, use the current game PID:

```powershell
$gtaPython = (Get-Content -LiteralPath .python-path -Raw).Trim()
$gameProcess = Get-Process GTA5_Enhanced -ErrorAction Stop
& $gtaPython -m gta_use run tools/gta_use/examples/repossession.py `
    --process $gameProcess.Id --timeout 1800 --stuck-timeout 100 `
    --check-timeout 12 --output artifacts/repossession-replay-new
```

The output directory must be fresh. This drives the actual game; it does not load
the starting save or restore settings for the caller. Follow AGENTS.md and the
gta-use skill for backups and cleanup. Route anchors, vehicle/companion models,
cover points and turn tuning belong to this mission. They are not universal
navigation instructions. Failure stops remain intentional; do not concatenate a
later checkpoint recovery and label it an uninterrupted pass.

## Timing and outcomes

The run began **06:38:11 UTC**, reached the mission briefing at **06:40:58**, and
ended at **06:55:15**. The inspected Mission Passed image was captured at
**06:55:08**. Stage times are wall-clock durations, including bridge I/O and
observations, not measured simulation time.

| Phase | Time | Observed outcome |
| --- | ---: | --- |
| House, contact call and Simeon approach | 166.22 s | Original save; driver seat reached (entry caveat below), garage reverse-out |
| Briefing and control return | 128.30 s | Cutscene advanced without a pause |
| Beach drive, parking and exit | 198.03 s | Lamar boarded; car stopped facing the departure street |
| Follow Lamar and entry fence | 64.37 s | `navigate_to` climbed and reached the far side |
| Garages, cutscene and pistol pickup | 58.66 s | Mission weapon acquired normally |
| Cover combat | 63.68 s | 12 firing intervals; 11 measured target-health reductions |
| Follow, exit fence and pursuit-car entry | 34.48 s | Normal climb and driver entry |
| Moving pursuit through rider separation | 92.08 s | Rider health reached zero at 5.62 m separation |
| Stop, exit and recover marked Bagger | 13.98 s | Correct mission bike verified |
| Bike return | 150.50 s | Delivery triggered the ending |
| Ending and evidence collection | 51.99 s | Mission Passed captured; final checkpoint |

The 1,574 logged policy action requests include observation natives/C# and batches;
they are not 1,574 distinct gameplay inputs. Franklin finished at **182 health**.
No explicit teleport, direct damage, health change, weapon/ammo grant, lock change,
mission global change or time manipulation was requested. Subsequent helper
extraction found that the original vehicle-entry task's finite 25-second engine
timeout can warp a ped. Its first entry took about 25.8 seconds, so the earlier
claim of entirely ordinary entry is **not verified**. The SDK now passes `-1`
to disable that engine timeout and enforces a separate Python deadline, while
logging approach observations. Other movement, cover, aiming, firing and climbing
used normal game tasks and controls.

Pursuit used a persistent road interception followed by `TASK_VEHICLE_MISSION`
type 2 (Ram), allowing ordinary game collision physics. It did not prove accurate
moving vehicle shooting. The required bike remained usable. The destination was
about **1,905 m straight-line distance** when recovered; that is not a measured GPS
route length. The prior assisted Terra continuation spent about 20 minutes in
return policies after a 10,136 m GPS route. This run's shorter chase/return and
absence of model handoffs are useful workflow results, not a controlled speed
benchmark on identical starting conditions.

## Evidence

Local evidence is retained outside version control under
`artifacts/single-run-repossession/run-010/`:

- `run.json`, `script.py`, `events.jsonl`, `state.json`, `result.json` and
  `trace-audit.json` record the source, stages, actions and terminal result.
- `004-departure-parking.png`, `006-entry-fence-crossed.png`, combat captures,
  `018-exit-fence-crossed.png`, `020-return-mission-bike.png` and ending captures
  preserve the process.
- **`060-ending.png`** shows Mission Passed: Repossession. SHA-256:
  `8cc176290b186f1c2839d9790585383e1c1e350c31ae996918b01df7c3e83b11`.

Source SHA-256 hashes recorded before gameplay:

| Source | SHA-256 |
| --- | --- |
| Mission policy | `2d0d5ac8ae143addfd273485d31654bfac1701438e6414fa22759d04c111a5b3` |
| `gta_use/driving.py` | `c5ff1d3237910f5f59a3b19085db3cd9a492384c21ce80ff70218022feefb9d8` |
| `gta_use/navigation.py` | `b3cce8311ba30c48597e498396d90cfafdf04ef70da46f75a36cde75967b98cc` |

The policy rejects pause/step/reload commands and records its cooperating helper
hashes. The host only read logs and saved screenshots while it ran; no script
edits or additional controller were used mid-run. The final checkpoint alone is
not proof of a pass: the host separately inspected the actual Mission Passed image.

## Development cost and fixes

This passing run was the **seventh actual full mission attempt**. Before it there
were six failed full attempts, three pre-mission/setup failures, and **29 local
or checkpoint diagnostics**. The diagnostics involved **ten actual GTA checkpoint
Retry operations**; original-save resets are separate. Diagnostic 025 completed
the mission from its pursuit checkpoint, and is explicitly excluded from the
full-mission result. There was one game-process restart to deploy the physical
probe fix. Save loading also reinitialized managed scripts; it was not an in-run
reload. Failed traces remain retained.

From the first development run at 03:20:47 UTC to the passing run's end was about
**3h 34m 28s**, with **1h 51m 29s** of summed runner time. That includes policy
development, diagnostics, original-save resets, host investigation and pauses
between attempts. Earlier Terra/DeepSeek evaluation time is additional and is
reported separately in [agent evaluations](agent-evaluation.md).

The reusable corrections were physical rays that include collidable see-through
fences; waiting for settled cover before peeking/firing; checking actual ammo and
target damage; keeping pursuit continuous instead of repeatedly invoking a
stop-on-arrival driver; and verifying parking position/clearance as well as
heading. The failed forward-only turn achieved the heading but trapped the car
under stairs. A bounded ordinary three-point turn was verified locally and then
worked in the complete replay. Mission-specific route/tactical choices remain in
Python rather than the bridge.

## Validation and cleanup

The full build/test/package workflow passed after adding the exact passing policy:
**336 C# scenarios, 41 Python scenarios and 8 Rust tests**, plus the installer,
diagnostic and coverage guardrails. Executable line and branch coverage remains
100% for GameBridge (1110 lines / 613 branches), ModUtils (274 / 90) and Thor
(1496 / 610). The skill validator and whitespace checks also passed. The package
includes this report and the passing policy.

GTA exited through its normal Quit command after evidence collection. Autosave
remained Off, as it was before this replay. The graphics menu refused to apply
the restored focus-loss setting because of its video-memory limit, so the original
`settings.xml` was restored from the verified backup with the game closed. Its
complete hash and both Story save hashes match their pre-test values. No profile
or account files were replaced. Local `cleanup.json` records those checks beside
`build-continuous-mission.log` under `artifacts/single-run-repossession/`.

## SDK extraction and campaign continuation — 2026-10-29

Reusable vehicle access/exit/roll recovery, settled-cover entry, muzzle-safe
bursts, continuous interception, stable control-return waiting and retained
screenshots now live in the SDK. The mission owns its route anchors, enemy
identification and tactics. Candidate eligibility and pursuit target observations
are batched into single C# requests rather than several native round trips.

Live extraction testing found three problems before the complete replay: the
finite entry timeout could warp the player; the no-warp entry task could not
path from the house doorstep; and two generated C# snippets needed the correct
leave-vehicle enum and an unambiguous bone placeholder. These are fixed. A
short driveway approach followed by native timeout `-1` reached the driver seat
in about six seconds, with continuous approach positions and before/after images.
The ordinary approach plus entry diagnostic took 14.32 seconds. Engine timeouts
are no longer used to manufacture entry success.

`artifacts/sdk-helper-extraction/run-002` then played continuously from the garage
car through the briefing, drive, both fences, garages and cover combat. The
cover helper observed eleven target-health reductions to zero, twelve rounds
used and player health 191. The run failed after **984.67 seconds**, because
the four-minute Ram pursuit did not stop the rider. This is a failed uninterrupted
attempt; it is not counted as an uninterrupted pass. A window-focus restoration
occurred, but no host gameplay input or policy edit occurred during that run.

A separate recovery policy continued the same mission without a GTA checkpoint
retry. Native chase plus two gated 1.1-second pistol intervals stopped the rider
in under twenty seconds. The first interval requested zero Attack frames; the
second requested ten and observed rider health 100 to 0. The ordinary bike
return took 186.65 seconds. Recovery and ending evidence took **266.15 seconds**;
combined runner time was **1,250.82 seconds**, with **one policy recovery**, zero
GTA retries and final health 191. `combat.fire_from_vehicle` extracts the tested
chase/fire pattern with additional range, identity and ammo checks; the example
now uses that helper with a two-window no-shot limit. That final source has not
yet completed an uninterrupted replay.

Mission Passed was independently inspected in
`artifacts/gta-story-campaign/repossession-recovery-001/host-ending-001.png`
(SHA-256 `ebed44b19129c1dffac136d94dd9cd3eff57fc1baca21307222b1a242f2e2efd`).
The bridge correctly rejected captures while another window was foreground;
the retained image came from the desktop window-capture tool. Null screenshot
fields in the policy result are not completion evidence. Both runs, failures,
source hashes and structured traces remain outside Git.

The campaign continues from a new manual slot `SGTA50000`. The original
`SGTA50015` and backup hashes remain unchanged. Autosave stays Off; focus-loss
pausing is temporarily disabled for supervised play and must be restored at
the end of testing. This ongoing campaign is separate from the earlier cleaned-up
single-run evaluation.

The extraction build/test/package workflow passed **336 C# scenarios, 67 Python
scenarios and 8 Rust tests**, plus the same installer, diagnostic and coverage
guardrails. C# executable line and branch coverage remains 100% with the counts
above. Skill validation and whitespace checks passed. No DLL deployment was
needed for these Python helpers; the existing game process imports the SDK.
