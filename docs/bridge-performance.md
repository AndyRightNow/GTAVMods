# Bridge performance and memory profiling

Use `tools/profiling` to measure the installed bridge and its Python processes
against a real, backed-up Story Mode session. All reports go to a fresh directory
outside Git. Keep the game scene, window focus, graphics settings, filters and
request rates consistent between runs. Do not run builds or other benchmarks at
the same time. Readiness of HTTP does not establish game-thread readiness.

The optimized implementation reads each bounded pipe frame asynchronously, then
dispatches its game operation on Tick. It arms the next listener when the previous
write completes. Successful/error responses are encoded once and the exact bytes
are both persisted and returned. The Python companion caches Windows DLL/signature
setup and returns already-decoded JSON directly, avoiding FastAPI's recursive
object conversion. Python observation snapshots use one JSON write instead of
many tiny writes; atomic replacement and `fsync` remain enabled.

## Normal measurements

From the repository root, with the locked Python 3.14 uv environment prepared:

```powershell
$gameProcess = Get-Process GTA5_Enhanced -ErrorAction Stop
uv run --frozen python tools/profiling/profile_live.py --process $gameProcess.Id --output C:/evidence/gta-baseline --seconds 15 --soak-blocks 8
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
  leaks over hours, script reloads or every mission. Receipt files intentionally
  grow on disk; preserve/archive them outside the game after a session.

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
