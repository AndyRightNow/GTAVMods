# Bridge performance and memory profiling

Use `tools/profiling` to measure the installed bridge and its Python processes
against a real, backed-up Story Mode session. All reports go to a fresh directory
outside Git. Keep the game scene, window focus, graphics settings, filters and
request rates consistent between runs. Do not run builds or other benchmarks at
the same time. Readiness of HTTP does not establish game-thread readiness.

The optimized implementation reads each bounded pipe frame asynchronously, then
dispatches its game operation on Tick. It arms the next listener when the previous
write completes. Successful/error responses are encoded once and returned without
disk receipts. The Python companion caches Windows DLL/signature
setup and returns already-decoded JSON directly, avoiding FastAPI's recursive
object conversion. Python observation snapshots use one JSON write instead of
many tiny writes; atomic replacement preserves consistent visibility without a
forced durable flush. See the [latest action measurements](#ephemeral-action-optimization-2026-10-27);
earlier sections retain historical results from the receipt-based implementation.

## Normal measurements

From the repository root, with the locked Python 3.14 uv environment prepared:

```powershell
$gameProcess = Get-Process GTA5_Enhanced -ErrorAction Stop
uv run --frozen python tools/profiling/profile_live.py --process $gameProcess.Id --output C:/evidence/gta-baseline --seconds 15 --soak-blocks 8
uv run --frozen python tools/profiling/profile_actions.py --process $gameProcess.Id --output C:/evidence/actions.json --samples 300
uv run --frozen python tools/profiling/profile_policy.py run tools/profiling/policy_workload.py --process $gameProcess.Id --output C:/evidence/policy-baseline --timeout 180 --stuck-timeout 120 --check-timeout 10
```

The live benchmark disables Thor, pauses the game, registers a temporary
`profile.runtime` section, then measures idle, saturated HTTP reads, 10 Hz minimal
and 5 Hz combat WebSocket streams, repeated HTTP soak blocks and cooldown. It
leaves the game paused. Reload or exit to remove the temporary section.
The workload uses player-only, combat and full profiles with explicit query
limits; full includes inventory, navigation, rays and entity LOS. The policy
workload performs 100 observations through the actual supervisor and worker.
Its checkpoint means only that those observations completed.

`report.json` records latency percentiles, capture time, payload size, query cost,
AppDomain allocation/collection counters and before/after process counters.
`processes.jsonl` samples process private bytes, working set, handles and CPU once
per second. The policy's `profile.json` includes both actual Python interpreters;
on Windows a venv launcher is not necessarily the supervisor process.

Interpret the counters carefully:

- CPU percentages use **one logical core** as 100%. GTA CPU and private bytes
  include the entire game, renderer and mods. They do not isolate bridge cost.
- AppDomain allocation is cumulative managed allocation, not retained memory;
  it includes other scripts in that domain. Compare idle-subtracted bytes per
  request, post-GC heap, repeated load blocks and cooldown. The explicit GC occurs
  after timed throughput phases and is not production behavior.
- WebSocket timings are arrival intervals, not request latency. Report actual
  observations delivered as well as the requested frequency.
- Saturated tests can consume more CPU after an optimization because they perform
  more work. Compare CPU seconds per request and fixed-rate streams as well.
- A short stable-memory soak is evidence for that workload, not proof against
  leaks over hours, script reloads or every mission. Batch, screenshot and policy
  evidence files accumulate on disk; archive them outside the game after a session.

## CPU and allocation attribution

Run instrumentation separately. `cProfile` changes execution cost, and
`tracemalloc` tracks Python allocations rather than all native process memory.
For a policy, set `GTA_USE_PROFILE=1` before the policy command above and remove
the environment variable afterward. The worker writes `worker.prof`,
`worker-profile.txt`, `allocation-growth.txt` and traced current/peak memory.

To profile the service while preserving its auto-start watchdog:

1. Pause the game and finish all active jobs. Launch `profile_service.py` with
   the prepared interpreter, discovery directory, game PID, fresh output directory
   and `--wait-for-start`. Use `Start-Process -WindowStyle Hidden` with stdout/stderr
   redirected to evidence files. Do not modify installed game DLLs while running.
2. Wait for the output's `READY` file. It contains the actual profiler PID.
   Read `service-<gamePID>.json` locally without printing its bearer token; stop
   only its `serviceProcess`, then immediately create the profiler's `START` file.
   Verify that discovery now names the PID in `READY` before sending workloads.
   If the watchdog won the race, retain that failed attempt and try a fresh run.
3. Run representative reads/streams for at least 60 seconds. These timings are
   instrumented and must not be reported as normal latency. The profiler takes
   its warm snapshot after 30 seconds and logs traced memory every 10 seconds.
4. Create `STOP` to request graceful shutdown. Wait for `service.prof`,
   `service.txt`, `warm.snapshot`, `final.snapshot`, `memory.jsonl` and
   `allocation-growth.txt`. Verify the ordinary companion auto-starts afterward.

Python 3.14 uses one interpreter-wide monitoring ID for `cProfile`; do not nest
another profiler around pipe workers. The saved statistics include call counts
and blocking waits. Large cumulative wall time in socket/pipe waits is not CPU
utilization; use Windows process counters for that comparison. Inspect a profile
with `uv run --frozen python -m pstats <path-to-service.prof>`.

Methodology references: [Python profiling](https://docs.python.org/3.14/library/profile.html),
[tracemalloc](https://docs.python.org/3.14/library/tracemalloc.html),
[AppDomain allocations](https://learn.microsoft.com/en-us/dotnet/api/system.appdomain.monitoringtotalallocatedmemorysize?view=netframework-4.8.1),
[Windows memory counters](https://learn.microsoft.com/en-us/windows/win32/api/psapi/ns-psapi-process_memory_counters_ex),
and [process CPU time](https://learn.microsoft.com/en-us/windows/win32/api/processthreadsapi/nf-processthreadsapi-getprocesstimes).

## Live results, 2026-10-27

Baseline: commit `591cd9b`, Enhanced 1.0.1158.16, Python 3.14.9, i5-12500H /
RTX 3050 Laptop, 2560x1440 borderless and unchanged graphics settings. Both builds
observed Franklin at his original house with Thor disabled and simulation paused.
Each timed phase ran for 15 seconds; each run included eight 15-second minimal
read soak blocks. These are single-machine measurements, not service guarantees.

| Normal workload | Baseline | Optimized |
| --- | ---: | ---: |
| HTTP player-only mean / p95 | 69.40 / 71.69 ms | 41.64 / 43.31 ms |
| HTTP player-only reads in 15 seconds | 216 | 360 |
| HTTP player-only service CPU per read | 8.61 ms | 2.78 ms |
| HTTP player-only managed allocation per read, including background | 199.93 KiB | 142.68 KiB |
| HTTP full mean / p95 | 72.66 / 89.82 ms | 40.98 / 44.43 ms |
| HTTP full service CPU per read | 15.17 ms | 4.95 ms |
| HTTP full managed allocation per read, including background | 3287.63 KiB | 2026.55 KiB |
| Requested 10 Hz player stream: observations / mean interval | 139 / 108.27 ms | 139 / 108.90 ms |
| Requested 10 Hz player stream: service CPU, one core | 10.41% | 2.57% |

The full query returned 48 entities, 48 LOS tests, five rays, 114 inventory checks
and 15 blips in both runs. Ambient populations still differed: the combat query
returned 21 peds in the baseline and seven in the final run. Its latency fell
from 69.25 to 41.57 ms, but its allocation/capture differences are not a controlled
algorithm comparison. Player-only requests are the clearest transport comparison.
World capture cost remains filter-dependent. Requested stream Hz is a maximum;
the actual player stream delivered about 9.2 observations per second.

The final two-minute soak completed **2,880 reads**. Service private memory stayed
between 48.87 and 48.94 MiB and returned to 48.87 MiB after cooldown; handles stayed
at 241 during reads and 240 afterward. The baseline stayed at 48.60–48.67 MiB.
The final post-GC managed heap was 1.97 MiB with 1.41 MiB surviving in the monitored
AppDomain (baseline 2.07 / 1.40 MiB). This shows bounded memory for this short
workload; it does not establish long-session leak freedom. The final session had
also performed the retained partial run before the complete repeat.

The normal supervised Python workload completed 100 checks in 10.04 seconds:
46.60 ms mean / 59.14 ms p95, versus 65.29 / 99.88 ms previously. Worker CPU was
8.09% of one core and supervisor CPU 0.78%; worker private memory grew by 1 MiB
to 30.09 MiB, while the supervisor stayed at 29.22 MiB. Handles stayed at 164 / 201.
Worker CPU varied across short runs (8.86% baseline, 9.96% intermediate), so the
data supports a latency improvement rather than a substantial worker CPU claim.
Separate worker instrumentation reduced recorded calls from 419,103 to 207,971
for 100 checks after replacing chunked JSON writes. The optimized trace peaked
at 0.73 MiB of traced Python allocations, with 0.15 MiB remaining after GC;
durable file flushes and atomic replacements still occurred.

The separate service trace confirmed the intended hot paths disappeared:
Windows API setup ran once (834 times in the baseline trace), and
`jsonable_encoder` fell from 88,585 recursive calls to 63 small metadata calls.
The runs had different durations and throughput, so total profiler times are not
a speed comparison. Instrumented traced memory settled near 30.5 MiB, with a
53.6 MiB peak and about 20 MiB of tracer overhead; the final snapshot did not
show a growing retained response cache. Those values are distinct from normal
process private memory. After profiling, the normal companion restarted
automatically. A final live smoke test passed a 500 ms advance/pause, capture,
C# file output and managed reload with a new session and the same service.

Raw reports and profiles are outside Git at
`C:\Users\Helen\Documents\Codex\2026-10-27\gta-use-performance`:
`baseline`, `final-complete`, `policy-baseline`, `policy-final`,
`service-baseline-valid`, `service-final`, and `policy-json-profiled`.
`methodology.json` identifies builds, processes and excluded instrumentation
attempts. `final` is a retained partial run: a pause menu interrupted its fifth
soak block and stopped managed ticks. Clicking the menu's Back control recovered
the game; the entire benchmark then passed in `final-complete`. No failed or
uncertain action was automatically replayed.

## Rust versus FastAPI, 2026-10-27

The optional Axum 0.8.9 / Tokio 1.54.1 companion was built with Rust 1.99.1,
release optimization, thin LTO and a static runtime. Its executable is 1,478,656
bytes (1.41 MiB). Both backends used the same uv-managed Python 3.14.9 SDK,
game bridge, durable receipts and logging. Hardware and graphics settings match
the preceding measurements. No headless or rendering changes were made.

The final pair below used the **same paused Story session**, with Rust followed
by FastAPI. Each phase ran for 15 seconds; each soak comprised eight 15-second
blocks. Memory and CPU figures describe the **companion process**, excluding
GTA, the benchmark client and Python policy processes.

| Measurement | FastAPI | Rust / Axum |
| --- | ---: | ---: |
| Private memory during two-minute soak | 49.28–49.38 MiB | 4.25–4.64 MiB |
| Working set during soak | 85.33–85.39 MiB | 32.77–32.95 MiB |
| HTTP player-only mean / p95 | 41.84 / 44.46 ms | 44.89 / 55.55 ms |
| HTTP player-only service CPU per read | 3.75 ms | 1.68 ms |
| HTTP combat mean / p95 | 45.51 / 56.27 ms | 46.29 / 56.84 ms |
| HTTP full mean / p95 | 47.47 / 56.10 ms | 41.07 / 44.45 ms |
| HTTP full service CPU per read | 4.92 ms | 2.61 ms |
| Requested 10 Hz player stream: observations | 139 | 139 |
| Player stream: mean arrival interval | 108.86 ms | 108.50 ms |
| Player stream: service CPU, one core | 3.79% | 1.65% |
| Two-minute soak completed reads | 2,772 | 2,618 |
| HTTP-only session discovery mean / p95 | 1.73 / 2.81 ms | 0.95 / 1.65 ms |

Rust saved about **45 MiB of private memory**, approximately 91%, and 55% of
service CPU per player-only read in this pair. Working set fell about 61%; it is
a different metric and includes shared resident pages. The latency result is
mixed: Rust's minimal-state mean was 7.3% slower, while full-state reads were
faster in this pair. An earlier FastAPI baseline measured 41.66 ms minimal and
40.64 ms full, with 5.21 / 5.28 ms service CPU per read. That variation prevents
a broad claim that Rust makes full-state capture faster. The HTTP-only check
performed 500 warmed `GET /v1/session` reads without game-thread dispatch; it
isolates a small part of the path, not end-to-end game control latency.

The paired full queries had identical cost counts: 627 nearby candidates,
48 returned entities/LOS tests, five rays, 114 inventory checks and 15 blips.
Combat returned 14 peds from 608 candidates in both runs. Minimal/combat/full
payloads were approximately 1.34 / 9.27 / 27.83 KiB. Game-side capture itself
averaged 0.26 / 2.05 / 3.49 ms with Rust and 0.31 / 2.69 / 4.30 ms with FastAPI;
engine scheduling and background load still affect these sequential samples.
Neither backend changes the game-thread execution model. Requested stream Hz
remains a ceiling; the player streams delivered about 9.2 observations/second.

Rust's soak handles stayed at 183–184 and fell to 182 at cooldown. Private memory
settled at 4.60 MiB after cooldown, following modest allocator growth during the
soak. FastAPI handles ranged from 235–244 and fell to 234, with 49.28 MiB private
memory afterward. These short runs do not establish long-session leak freedom.

Both supervised Python policies completed 100 observations in 10.04 seconds.
Mean / p95 observation latency was 45.66 / 62.13 ms through Rust and
50.37 / 65.00 ms through FastAPI. The Python worker still used about 29.6 MiB
private memory and the supervisor about 28–29 MiB with either backend. Worker
CPU was 8.56% / 7.01% of one core (Rust / FastAPI), and supervisor CPU was
1.25% / 1.56%; these short samples do not support a worker CPU improvement claim.
Rust replaces the resident HTTP interpreter, not the Python AI runtime.

### Implementation and verification

The final server uses two Tokio executor threads, bounded queues and complete
pipe-exchange ownership across caller cancellation. Accepted TCP connections use
TCP_NODELAY. Busy-pipe acquisition waits for Windows availability on a bounded
blocking worker instead of sleeping for a fixed 10 ms between attempts. Earlier
Rust builds are retained separately: minimal means were 52.11 ms initially and
47.03 ms with TCP_NODELAY alone. Those runs required cold game restarts, so they
are development evidence rather than a controlled attribution of each change.

The complete build passed 324 C# scenarios, 17 Python scenarios and six Rust
HTTP/WebSocket/pipe scenarios, rustfmt and Clippy. The strict C# line/branch
coverage gate remains 100%. Windows MSVC CI also passed. Live final-binary checks
verified automatic startup, observed movement from 500 ms input, pause, capture,
C# file output, managed reload retaining the service PID/URL, a Python checkpoint
job, parallel-job rejection and cancellation of a CPU-bound policy. The earlier
complete Repossession mission was not replayed for this backend comparison.

FastAPI remains the default and provides generated interactive API documentation
and a single Python service stack to maintain. Rust is selectable with
`Install-AgentService.ps1 -Backend Rust`, retains the same Python SDK/policies,
and provides the lower companion footprint measured here. It adds a Rust build
toolchain and a second HTTP implementation to maintain; its OpenAPI document is
compact and it has no Swagger UI. See [backend setup](rust-service.md).

Raw evidence is outside Git at
`C:\Users\Helen\Documents\Codex\2026-10-27\gta-use-rust-comparison`:
`rust-wait` and `fastapi-after` contain the final pair; `fastapi-before`, `rust`
and `rust-final` retain the earlier runs. `policy-rust-wait`,
`policy-fastapi-after`, `metadata-*.json`, `summary.json` and `methodology.json`
record process counters, exact binary hashes and phase results. Final controls
and reload evidence are in `live-rust-wait-complete` and `reload-rust-wait`.
`live-rust-wait` retains an initial harness failure: it tried to start a new job
after the previous result was written but before its supervisor exited. The
server correctly returned 409; the revised harness waits after this confirmed
rejection. Earlier partial-axis movement checks are also retained; full-axis
input produced observed movement. No uncertain mutation was blindly replayed.


## Ephemeral action optimization (2026-10-27)

The comparisons above measured the earlier receipt-based bridge. The current
bridge removes request/response artifacts and all forced durable flushes. File
diagnostics move to a bounded background writer; failures do not stop game
commands. Async pipe I/O rearms the next listener without waiting for another
script tick. Only game dispatch needs the tick. Python `action`, `native` and
`command` return immediate results in one RPC instead of creating and polling a
batch. Timed actions still use the sequence scheduler. Reusing an ID explicitly
executes again; clients never retry a submitted request automatically.

### Final paired results

The final package ran in GTA Enhanced 1.0.1158.16, PID 75248, using one paused
Story session for Rust then FastAPI. Thor was disabled. Each action phase used
five warmups and 300 measured calls; state phases lasted five seconds and each
soak comprised 24 five-second blocks. No builds or test suites ran during these
measurements. Steam offline mode allowed the unchanged executable to launch
despite a queued update. No graphics or headless changes were made.

| Measurement | FastAPI | Rust / Axum |
| --- | ---: | ---: |
| Player-state HTTP mean / p95, 300 calls | 13.86 / 15.08 ms | 13.85 / 14.65 ms |
| Direct native HTTP mean / p95 | 13.98 / 15.02 ms | 13.88 / 14.51 ms |
| Direct native WebSocket mean / p95 | 13.89 / 14.58 ms | 13.89 / 14.55 ms |
| Same native through batch polling, mean / p95 | 71.13 / 102.50 ms | 55.69 / 59.08 ms |
| Service CPU per direct HTTP native | 3.96 ms | 1.09 ms |
| Service CPU per direct WebSocket native | 1.77 ms | 1.04 ms |
| Soak private memory | 48.73–48.84 MiB | 4.29–4.34 MiB |
| Soak working set | 84.86–84.93 MiB | 32.54–32.58 MiB |
| Soak handles | 231–241 | 171–172 |
| Two-minute soak completed reads | 8,629 | 8,646 |

The direct SDK path was **4.0× faster with Rust and 5.1× with FastAPI** than
sending the identical native through the batch path in these runs. This is a
comparison of the two paths in the new build, not an old-build action benchmark.
Player-state latency fell about **67–69%** from the earlier 42–45 ms runs, which
used another game process. That historical comparison does not isolate each
optimization. The remaining approximately 14 ms round trip is largely script-tick
scheduling; GTA calls still execute exclusively on the script thread. The native
used here, `GET_FRAME_COUNT`, freezes while paused: its recorded zero strides are
not evidence of script cadence or engine FPS.

The five-second minimal/combat/full HTTP phases averaged 13.87 / 13.95 / 25.13 ms
through FastAPI and 13.83 / 13.61 / 13.65 ms through Rust. Query costs matched:
combat returned 15 peds/LOS tests from 607 candidates; full returned 48 entities
and LOS tests from 627 candidates, five rays, 114 inventory checks and 15 blips.
Full capture itself averaged 3.87 / 3.37 ms (FastAPI / Rust). These short sequential
phases are scene-specific, and a little extra host work can miss the next tick;
they do not establish universal full-state latency. At a requested 10 Hz, minimal
streams delivered 48 / 47 observations in about 5.2 seconds; these intervals are
intentional stream pacing, not command latency.

Post-GC managed heaps were 2.01 / 1.96 MiB (FastAPI / Rust), with 1.45 MiB surviving
in the monitored AppDomain. Private service memory at cooldown was 48.73 / 4.34 MiB.
Python policies each completed 100 checks in 10.04 seconds; observation plus
snapshot publication averaged 21.63 / 20.00 ms, with p95 33.77 / 34.01 ms. Their
workers still used about 30 MiB and supervisors about 29 MiB each. Rust saves the
HTTP interpreter's resident cost; it does not replace the Python policy runtime.
These are short workload measurements, not a long-session leak guarantee.

### Verification and evidence

The complete build passed **329 C# scenarios, 17 Python scenarios and seven Rust
scenarios**, including actual external HTTP/WS/pipe clients, stalled storage,
repeated correlation IDs, lease authorization, consecutive-tick controls and
reload/abort races. The strict C# gate remains 100%: GameBridge 1059/1059 lines,
579/579 branches; ModUtils 274/274, 90/90; Thor 1496/1496, 610/610. No gate or
primitive exclusion was weakened.

Live final-binary checks passed 500 ms movement (0.48 m observed), pause, capture,
C# output, a Python checkpoint job, concurrent-job rejection and cancellation of
a busy worker. Reload through both backends changed the managed session while
retaining each companion PID/URL. Both soaks completed with zero dropped records
and no request/response receipt files. The earlier full Repossession mission was
not replayed for this performance change.

Raw evidence is outside Git at
`C:\Users\Helen\Documents\Codex\2026-10-27\gta-use-ephemeral-performance`.
`rust-actions-final.json`, `fastapi-actions-final.json`, `rust-final`,
`fastapi-final`, `policy-rust-final`, `policy-fastapi-final`, `summary-final.json`
and `methodology-final.json` identify final measurements and binary hashes.
`controls-rust-final`, `reload-rust-final` and `reload-fastapi-final` retain live
verification. `fastapi-actions.json` is excluded because it overlapped a build.
`reload/smoke-before-reload.json` preserves a failed development run: Windows
temporarily locked a batch status file. The final Rust server retries only that
artifact read, covered by a real Windows file-lock scenario; it never resubmits
the game action. The final rerun passed.
