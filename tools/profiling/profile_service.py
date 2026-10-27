"""Instrumented companion host for diagnosis, separate from latency benchmarks.

Run with the prepared Python interpreter, discovery root, game PID and a fresh
output directory. With --wait-for-start, wait for READY, stop the discovered
companion, then create START immediately. This avoids racing automatic startup
while importing instrumented dependencies. The normal game watchdog recognizes
this host's discovery and keeps automatic supervision. Create the
output directory's STOP file to flush profiles and exit, or close GTA normally.
Never use profiled timings as production latency measurements.
"""
import argparse
import cProfile
import gc
import io
import json
from pathlib import Path
import pstats
import threading
import time
import tracemalloc


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("discovery")
    parser.add_argument("process", type=int)
    parser.add_argument("output")
    parser.add_argument("--wait-for-start", action="store_true")
    args = parser.parse_args()
    output = Path(args.output).resolve()
    output.mkdir(parents=True, exist_ok=False)
    tracemalloc.start(10)
    from gta_use import service

    server = None
    original_server = service.uvicorn.Server

    class ProfiledServer(original_server):
        def __init__(self, *args, **kwargs):
            nonlocal server
            super().__init__(*args, **kwargs)
            server = self

    service.uvicorn.Server = ProfiledServer
    # Python 3.14 cProfile owns one interpreter-wide monitoring tool ID.
    # A second profiler around the pipe worker conflicts with the active host.
    if args.wait_for_start:
        import os
        (output / "READY").write_text(str(os.getpid()), encoding="utf-8")
        deadline = time.monotonic() + 120
        while not (output / "START").exists():
            if time.monotonic() >= deadline:
                raise TimeoutError("Profiler was ready but START was not supplied within 120 seconds.")
            time.sleep(.1)
    stopped = threading.Event()
    baseline = None

    def snapshots():
        nonlocal baseline
        started = time.monotonic()
        with (output / "memory.jsonl").open("w", encoding="utf-8") as log:
            while not stopped.wait(10):
                current, peak = tracemalloc.get_traced_memory()
                log.write(json.dumps(dict(elapsed=time.monotonic() - started, current=current,
                                          peak=peak, tracerBytes=tracemalloc.get_tracemalloc_memory())) + "\n")
                log.flush()
                if baseline is None and time.monotonic() - started >= 30:
                    baseline = tracemalloc.take_snapshot()
                    baseline.dump(str(output / "warm.snapshot"))
                if (output / "STOP").exists() and server is not None:
                    server.should_exit = True
                    return

    monitor = threading.Thread(target=snapshots, daemon=True)
    monitor.start()
    main_profile = cProfile.Profile()
    try:
        main_profile.runcall(service.serve, args.discovery, args.process, 0)
    finally:
        stopped.set()
        monitor.join(timeout=15)
        gc.collect()
        final = tracemalloc.take_snapshot()
        final.dump(str(output / "final.snapshot"))
        allocations = final.compare_to(baseline, "lineno") if baseline else final.statistics("lineno")
        (output / "allocation-growth.txt").write_text("\n".join(str(row) for row in allocations[:60]), encoding="utf-8")
        for name, profile in (("service", main_profile),):
            profile.dump_stats(str(output / f"{name}.prof"))
            stream = io.StringIO()
            if profile.getstats():
                pstats.Stats(profile, stream=stream).strip_dirs().sort_stats("tottime").print_stats(60)
            else:
                stream.write("No calls recorded; verify this host owns the service before claiming a profile.\n")
            (output / f"{name}.txt").write_text(stream.getvalue(), encoding="utf-8")


if __name__ == "__main__":
    main()
