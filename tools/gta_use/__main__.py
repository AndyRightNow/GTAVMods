"""python -m gta_use run segment.py --timeout 180 --stuck-timeout 30"""
import argparse
import asyncio
import hashlib
import inspect
import json
import os
from pathlib import Path
import runpy
import subprocess
import sys
import time
import traceback
import uuid
from .client import Client
from .segment import Segment, StopSegment, save_json
from .windows import read_json
from .inspection import inspect_game, select_capabilities


def child(script):
    context = Segment()
    try:
        sys.path.insert(0, os.environ["GTA_USE_SCRIPT_DIRECTORY"])
        module = runpy.run_path(script, run_name="gta_segment")
        result = module["run"](context)
        if inspect.isawaitable(result):
            asyncio.run(result)
        if context.status["state"] != "checkpoint":
            raise StopSegment("Script returned without an observed checkpoint.")
        return 0
    except BaseException as error:
        context.log("failed", error=str(error), traceback=traceback.format_exc())
        traceback.print_exc()
        return 1


def supervise(args):
    game = Client(args.discovery, args.process, token=uuid.uuid4().hex)
    directory = Path(args.output or game.directory / ("run-" + uuid.uuid4().hex)).resolve()
    directory.mkdir(parents=True, exist_ok=True)
    if any(p.name != "cancel" for p in directory.iterdir()):
        raise ValueError("Run directory already contains evidence; choose a fresh directory.")
    source = Path(args.script).resolve()
    content = source.read_bytes()
    snapshot = directory / "script.py"
    snapshot.write_bytes(content)
    save_json(directory / "run.json", dict(script=str(source), sha256=hashlib.sha256(content).hexdigest(),
                                         session=game.session, timeout=args.timeout, stuckTimeout=args.stuck_timeout,
                                         checkTimeout=args.check_timeout, started=time.time()))
    print(json.dumps({"directory": str(directory), "session": game.session}), flush=True)
    environment = dict(os.environ, GTA_USE_RUN_DIRECTORY=str(directory), GTA_USE_TOKEN=game.token,
                       GTA_USE_DISCOVERY=str(game.discovery_root),
                       GTA_USE_PROCESS=str(game.discovery["process"]), GTA_USE_SCRIPT_DIRECTORY=str(source.parent))
    environment["PYTHONPATH"] = str(Path(__file__).resolve().parent.parent) + os.pathsep + environment.get("PYTHONPATH", "")
    process = None
    acquired = False
    outcome = {"state": "failed", "error": "Runner did not start."}
    started = time.monotonic()
    wall_start = time.time()
    try:
        game.lease("acquire")
        acquired = True
        with (directory / "stdout.log").open("wb") as stdout, (directory / "stderr.log").open("wb") as stderr:
            process = subprocess.Popen([sys.executable, "-u", "-m", "gta_use", "_child", str(snapshot)],
                                       env=environment, stdout=stdout, stderr=stderr,
                                       creationflags=getattr(subprocess, "CREATE_NO_WINDOW", 0))
            heartbeat = time.monotonic() + 1
            while True:
                elapsed = time.monotonic() - started
                try:
                    progress = read_json(directory / "progress.json")
                except FileNotFoundError:
                    progress = {"checked": wall_start, "progress": wall_start}
                if (directory / "cancel").exists():
                    raise StopSegment("External cancellation requested.")
                if elapsed >= args.timeout:
                    raise StopSegment("Segment deadline exceeded.")
                if time.time() - progress["checked"] >= args.check_timeout:
                    raise StopSegment("Script stopped checking game state.")
                if time.time() - progress["progress"] >= args.stuck_timeout:
                    raise StopSegment("No observed progress within the stall limit.")
                code = process.poll()
                if code is not None:
                    if code != 0 or progress.get("state") != "checkpoint":
                        raise StopSegment(f"Script exited {code} without a verified checkpoint; inspect stderr.log/events.jsonl.")
                    outcome = dict(progress, state="checkpoint", exitCode=code)
                    break
                if time.monotonic() >= heartbeat:
                    game.lease("heartbeat")
                    heartbeat = time.monotonic() + 1
                time.sleep(.1)
    except BaseException as error:
        outcome = {"state": "failed", "error": str(error)}
    finally:
        if process is not None and process.poll() is None:
            process.terminate()
            process.wait(timeout=5)
        if acquired:
            try:
                game.lease("release")
                outcome["paused"] = True
            except Exception as error:
                outcome.update(paused=None, stopError=str(error))
        outcome["elapsedSeconds"] = time.monotonic() - started
        save_json(directory / "result.json", outcome)
        game.close()
    print(json.dumps(outcome, ensure_ascii=False), flush=True)
    return 0 if outcome["state"] == "checkpoint" and outcome.get("paused") is True else 1


def positive(value):
    number = float(value)
    if not 0 < number <= 86400:
        raise argparse.ArgumentTypeError("Expected seconds greater than zero and at most 86400.")
    return number


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    commands = parser.add_subparsers(dest="command", required=True)
    run = commands.add_parser("run", help="Run a Python segment with independent supervision and a game watchdog.")
    run.add_argument("script")
    run.add_argument("--timeout", type=positive, default=180)
    run.add_argument("--stuck-timeout", type=positive, default=30)
    run.add_argument("--check-timeout", type=positive, default=10)
    run.add_argument("--output")
    for name in ("status", "cancel"):
        commands.add_parser(name).add_argument("directory")
    request = commands.add_parser("request", help="Send a JSON operation, e.g. state or capture.")
    request.add_argument("op")
    request.add_argument("--args", default="{}")
    describe = commands.add_parser("describe", help="List installed capabilities compactly, or select a command/control.")
    selection = describe.add_mutually_exclusive_group()
    selection.add_argument("--command", dest="command_name")
    selection.add_argument("--control", help="Case-insensitive substring of GTA.Control names.")
    helpers = commands.add_parser("helpers", help="List Python SDK helpers offline, or show matching signatures and contracts.")
    helpers.add_argument("query", nargs="?", help="Case-insensitive helper/module name substring.")
    inspection = commands.add_parser("inspect", help="Read bridge status and a filtered world pack without taking control.")
    inspection.add_argument("--profile", choices=("minimal", "walking", "driving", "combat", "full"), default="minimal")
    inspection.add_argument("--sections", nargs="+", help="Explicit section names replace profile defaults.")
    inspection.add_argument("--radius", type=float, help="Nearby radius in metres (1..1000).")
    inspection.add_argument("--limit", type=int, help="Maximum nearby entities (1..128).")
    inspection.add_argument("--blip-limit", type=int, help="Maximum navigation markers (1..256).")
    inspection.add_argument("--types", nargs="+", choices=("ped", "vehicle", "object"))
    inspection.add_argument("--line-of-sight", choices=("on", "off"))
    inspection.add_argument("--capture", action="store_true", help="Also capture the visible foreground GTA client.")
    for command in (describe, inspection):
        command.add_argument("--output", help="Save full JSON here and print only its path.")
    for command in (run, request, describe, inspection):
        command.add_argument("--discovery")
        command.add_argument("--process", type=int)
    commands.add_parser("_child", help=argparse.SUPPRESS).add_argument("script")
    args = parser.parse_args()
    if args.command == "helpers":
        from .catalogue import describe_helpers
        try:
            result = describe_helpers(args.query)
        except ValueError as error:
            parser.error(str(error))
        print(json.dumps(result, ensure_ascii=False, allow_nan=False))
        return 0
    if args.command == "run":
        return supervise(args)
    if args.command == "_child":
        return child(args.script)
    if args.command == "cancel":
        Path(args.directory, "cancel").touch()
    elif args.command == "status":
        directory = Path(args.directory)
        file = directory / "result.json"
        print(json.dumps(read_json(file if file.exists() else directory / "progress.json")))
    else:
        game = Client(args.discovery, args.process)
        try:
            if args.command == "describe":
                result = select_capabilities(game.request("describe"), args.command_name, args.control)
            elif args.command == "inspect":
                filters = {"profile": args.profile}
                for argument, field in (("sections", "sections"), ("radius", "radius"), ("limit", "limit"),
                                        ("blip_limit", "blipLimit"), ("types", "types"), ("line_of_sight", "lineOfSight")):
                    value = getattr(args, argument)
                    if value is not None:
                        filters[field] = value
                result = inspect_game(game, filters, args.capture)
            else:
                result = game.request(args.op, **json.loads(args.args))
            output = getattr(args, "output", None)
            if output:
                path = Path(output).resolve()
                # Evaluation evidence must not silently replace a previous capture.
                with path.open("x", encoding="utf-8") as stream:
                    stream.write(json.dumps(result, ensure_ascii=False, allow_nan=False))
                result = {"file": str(path)}
            print(json.dumps(result, ensure_ascii=False, allow_nan=False))
        finally:
            game.close()
    return 0


if __name__ == "__main__":
    sys.exit(main())
