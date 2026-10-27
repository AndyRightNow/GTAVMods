"""Read-only profiling workload for gta-use run; no gameplay completion claim.

Set GTA_USE_PROFILE=1 for cProfile/tracemalloc. Keep its instrumented results
separate from ordinary CPU/latency measurements. Uses the normal lease/runner.
"""
import cProfile
import gc
import io
import json
import os
import pstats
import time
import tracemalloc
from profile_live import Process, distribution


def run(ctx):
    instrumented = os.environ.get("GTA_USE_PROFILE") == "1"
    process = Process(os.getpid())
    # A Windows venv redirector can be the OS parent; the wrapper records the
    # actual Python supervisor, not that idle launcher process.
    supervisor = Process(int(os.environ["GTA_USE_PROFILE_SUPERVISOR"]))
    profile = cProfile.Profile()
    for _ in range(5):
        ctx.check(sections=["player", "weapons"])
    if instrumented:
        tracemalloc.start(10)
        baseline = tracemalloc.take_snapshot()
        profile.enable()
    start = time.monotonic()
    before = process.read()
    supervisor_before = supervisor.read()
    latencies = []
    try:
        for _ in range(100):
            tick = time.monotonic()
            ctx.check(sections=["player", "weapons"])
            latencies.append((time.monotonic() - tick) * 1000)
            time.sleep(max(0, .1 - (time.monotonic() - tick)))
        after = process.read()
        supervisor_after = supervisor.read()
        elapsed = time.monotonic() - start
        result = dict(instrumented=instrumented, elapsed=elapsed, latencyMs=distribution(latencies),
                      before=before, after=after, cpuCorePercent=100 * (after["cpuSeconds"] - before["cpuSeconds"]) / elapsed)
        result["supervisor"] = dict(before=supervisor_before, after=supervisor_after,
                                   cpuCorePercent=100 * (supervisor_after["cpuSeconds"] - supervisor_before["cpuSeconds"]) / elapsed)
        if instrumented:
            profile.disable()
            gc.collect()
            final = tracemalloc.take_snapshot()
            result["pythonMemory"] = dict(zip(("current", "peak"), tracemalloc.get_traced_memory()))
            (ctx.directory / "allocation-growth.txt").write_text("\n".join(str(s) for s in final.compare_to(baseline, "lineno")[:40]), encoding="utf-8")
            profile.dump_stats(str(ctx.directory / "worker.prof"))
            stream = io.StringIO()
            pstats.Stats(profile, stream=stream).strip_dirs().sort_stats("tottime").print_stats(50)
            (ctx.directory / "worker-profile.txt").write_text(stream.getvalue(), encoding="utf-8")
        (ctx.directory / "profile.json").write_text(json.dumps(result, indent=2), encoding="utf-8")
        ctx.checkpoint("profiling-samples-collected", samples=len(latencies), elapsed=elapsed)
    finally:
        profile.disable()
        process.close()
        supervisor.close()
        if instrumented:
            tracemalloc.stop()
