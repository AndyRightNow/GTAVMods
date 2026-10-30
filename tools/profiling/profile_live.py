"""Repeatable live GTA-use benchmark. Run from uv: python tools/profiling/profile_live.py --help.

Uses the installed bridge, real HTTP/WS, file diagnostics and Windows process counters.
Pauses the backed-up Story session and disables Thor for a stable observation load.
Raw observations are ephemeral; this report stores timings/costs only.
"""
import argparse
import asyncio
import ctypes as c
from ctypes import wintypes as w
import json
import math
import os
from pathlib import Path
import statistics
import threading
import time
from gta_use import Client


class Memory(c.Structure):
    _fields_ = [("cb", w.DWORD), ("faults", w.DWORD)] + [
        (name, c.c_size_t) for name in ("peakWorkingSet", "workingSet", "peakPaged", "paged",
                                      "peakNonpaged", "nonpaged", "pagefile", "peakPagefile", "private")]


class Process:
    def __init__(self, pid):
        self.pid = pid
        self.kernel = c.WinDLL("kernel32", use_last_error=True)
        self.psapi = c.WinDLL("psapi", use_last_error=True)
        self.kernel.OpenProcess.argtypes = [w.DWORD, w.BOOL, w.DWORD]
        self.kernel.OpenProcess.restype = w.HANDLE
        self.kernel.GetProcessTimes.argtypes = [w.HANDLE] + [c.POINTER(w.FILETIME)] * 4
        self.kernel.GetProcessHandleCount.argtypes = [w.HANDLE, c.POINTER(w.DWORD)]
        self.kernel.CloseHandle.argtypes = [w.HANDLE]
        self.psapi.GetProcessMemoryInfo.argtypes = [w.HANDLE, c.POINTER(Memory), w.DWORD]
        self.handle = self.kernel.OpenProcess(0x0400 | 0x0010, False, pid)
        if not self.handle:
            raise c.WinError(c.get_last_error())

    def read(self):
        times = [w.FILETIME() for _ in range(4)]
        memory = Memory(cb=c.sizeof(Memory))
        handles = w.DWORD()
        for ok in (self.kernel.GetProcessTimes(self.handle, *map(c.byref, times)),
                   self.psapi.GetProcessMemoryInfo(self.handle, c.byref(memory), c.sizeof(memory)),
                   self.kernel.GetProcessHandleCount(self.handle, c.byref(handles))):
            if not ok:
                raise c.WinError(c.get_last_error())
        cpu = sum((t.dwHighDateTime << 32) + t.dwLowDateTime for t in times[2:]) / 10_000_000
        return dict(pid=self.pid, cpuSeconds=cpu, privateBytes=memory.private,
                    workingSetBytes=memory.workingSet, handles=handles.value)

    def close(self):
        self.kernel.CloseHandle(self.handle)


def distribution(values):
    ordered = sorted(values)
    return {"count": len(values), "mean": statistics.mean(values), "p50": statistics.median(values),
            "p95": ordered[math.ceil(len(values) * .95) - 1], "max": ordered[-1]} if values else None


MONITOR = '''
System.AppDomain.MonitoringIsEnabled = true;
((GTAVMods.Game.AgentWorld)CONTEXT.World).StatePack.Register("profile.runtime", c => new {
    allocated = System.AppDomain.CurrentDomain.MonitoringTotalAllocatedMemorySize,
    survived = System.AppDomain.CurrentDomain.MonitoringSurvivedMemorySize,
    cpuMs = System.AppDomain.CurrentDomain.MonitoringTotalProcessorTime.TotalMilliseconds,
    heap = System.GC.GetTotalMemory(false), gen0 = System.GC.CollectionCount(0),
    gen1 = System.GC.CollectionCount(1), gen2 = System.GC.CollectionCount(2),
    frameSeconds = GTA.Game.LastFrameTime
});
'''


class Benchmark:
    def __init__(self, args):
        self.args = args
        self.game = Client(process=args.process)
        self.output = Path(args.output).resolve()
        self.output.mkdir(parents=True, exist_ok=False)
        self.processes = {"game": Process(args.process), "service": Process(self.game.service["serviceProcess"]),
                          "client": Process(os.getpid())}
        self.phase = "prepare"
        self.stop = threading.Event()
        self.report = {"session": self.game.session, "process": args.process,
                       "serviceProcess": self.game.service["serviceProcess"], "phases": [],
                       "note": "CPU percent is percent of one logical core. Game process includes the entire engine; AppDomain counters include all managed scripts in the domain. Timings use uninstrumented service unless the operator explicitly labels an instrumented run."}

    def sample(self):
        with (self.output / "processes.jsonl").open("w", encoding="utf-8") as stream:
            while not self.stop.is_set():
                row = {"time": time.monotonic(), "phase": self.phase,
                       "processes": {k: v.read() for k, v in self.processes.items()}}
                stream.write(json.dumps(row) + "\n")
                stream.flush()
                self.stop.wait(1)

    def runtime(self):
        return self.game.state(sections=["profile.runtime"])["sections"]["profile.runtime"]

    def begin(self, name):
        self.phase = name
        return {"name": name, "runtimeBefore": self.runtime(),
                "processBefore": {k: v.read() for k, v in self.processes.items()}, "started": time.monotonic()}

    def finish(self, row, latencies=(), captures=(), sizes=(), cost=None):
        row["elapsedSeconds"] = time.monotonic() - row.pop("started")
        row["processAfter"] = {k: v.read() for k, v in self.processes.items()}
        row["runtimeAfter"] = self.runtime()
        row["latencyMs"] = distribution(latencies)
        row["captureMs"] = distribution(captures)
        row["responseBytes"] = distribution(sizes)
        row["lastCost"] = cost
        row["cpuCorePercent"] = {k: 100 * (after["cpuSeconds"] - row["processBefore"][k]["cpuSeconds"]) / row["elapsedSeconds"]
                                 for k, after in row["processAfter"].items()}
        row["managedAllocationBytes"] = row["runtimeAfter"]["allocated"] - row["runtimeBefore"]["allocated"]
        self.report["phases"].append(row)
        (self.output / "report.json").write_text(json.dumps(self.report, indent=2), encoding="utf-8")
        print(json.dumps({k: row[k] for k in ("name", "elapsedSeconds", "latencyMs", "captureMs", "cpuCorePercent", "managedAllocationBytes", "lastCost")}), flush=True)

    def http(self, name, query, seconds):
        for _ in range(5):
            self.game.state(**query)
        row = self.begin(name)
        latencies, captures, sizes = [], [], []
        cost = None
        deadline = time.monotonic() + seconds
        while time.monotonic() < deadline:
            started = time.perf_counter()
            state = self.game.state(**query)
            latencies.append((time.perf_counter() - started) * 1000)
            captures.append(state["captureMilliseconds"])
            sizes.append(len(json.dumps(state).encode()))
            cost = state["cost"]
        self.finish(row, latencies, captures, sizes, cost)

    async def websocket(self, name, query, seconds, hz):
        row = self.begin(name)
        intervals, captures, sizes = [], [], []
        cost, previous = None, None
        deadline = time.monotonic() + seconds
        async with self.game.connect() as live:
            async for state in live.states(hz=hz, **query):
                now = time.monotonic()
                if previous is not None:
                    intervals.append((now - previous) * 1000)
                previous = now
                captures.append(state["captureMilliseconds"])
                sizes.append(len(json.dumps(state).encode()))
                cost = state["cost"]
                if now >= deadline:
                    break
        row["metric"] = "WebSocket inter-arrival time, not request latency"
        self.finish(row, intervals, captures, sizes, cost)

    def run(self):
        monitor = threading.Thread(target=self.sample, daemon=True)
        monitor.start()
        try:
            self.game.command("Thor.Thor.Current.SetEnabled(false);")
            self.game.action("pause")
            if "profile.runtime" not in self.game.request("describe")["stateSections"]:
                self.game.command(MONITOR)
            self.report["describe"] = self.game.request("describe")["stateSections"]
            row = self.begin("idle-paused")
            time.sleep(self.args.seconds)
            self.finish(row)
            cases = [("minimal", {"sections": ["player"]}),
                     ("combat", {"profile": "combat", "types": ["ped"], "radius": 80, "limit": 24, "lineOfSight": "on"}),
                     ("full", {"profile": "full", "radius": 100, "limit": 48, "blipLimit": 32, "lineOfSight": "on"})]
            for name, query in cases:
                # Dynamic profile metrics are sampled separately, not in each payload.
                if name == "full":
                    query = dict(query, sections=[s for s in self.report["describe"] if s != "profile.runtime"])
                self.http("http-" + name, query, self.args.seconds)
            asyncio.run(self.websocket("ws-minimal-10hz", cases[0][1], self.args.seconds, 10))
            asyncio.run(self.websocket("ws-combat-5hz", cases[1][1], self.args.seconds, 5))
            for i in range(self.args.soak_blocks):
                self.http(f"soak-minimal-{i+1}", cases[0][1], self.args.seconds)
            self.game.command("System.GC.Collect(); System.GC.WaitForPendingFinalizers(); System.GC.Collect();")
            self.report["afterExplicitGC"] = self.runtime()
            row = self.begin("cooldown-paused")
            time.sleep(self.args.seconds)
            self.finish(row)
        finally:
            self.stop.set()
            monitor.join(timeout=3)
            self.game.close()
            for process in self.processes.values():
                process.close()


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--process", type=int, required=True)
    parser.add_argument("--output", required=True, help="Fresh local evidence directory, outside Git.")
    parser.add_argument("--seconds", type=float, default=15)
    parser.add_argument("--soak-blocks", type=int, default=8)
    args = parser.parse_args()
    if not 1 <= args.seconds <= 60 or not 0 <= args.soak_blocks <= 60:
        parser.error("Use 1..60 seconds and 0..60 soak blocks.")
    Benchmark(args).run()
