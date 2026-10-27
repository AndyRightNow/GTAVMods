"""External client used by player integration tests against the shipped game bridge."""
import asyncio
import json
import sys
import time
import subprocess
from pathlib import Path
from gta_use import Client


async def main():
    game = Client(discovery=sys.argv[1], process=int(sys.argv[2]))
    assert "lease" in game.request("describe")["operations"]
    # An agent can discover one real command and inspect the world without a
    # lease, mutation, control-enum dump or PowerShell inline-JSON quoting.
    def cli(*args):
        return subprocess.run([sys.executable, "-m", "gta_use", *args, "--discovery", sys.argv[1],
                               "--process", sys.argv[2]], capture_output=True, text=True, timeout=10)
    overview = cli("describe")
    assert overview.returncode == 0, overview.stderr
    capabilities = json.loads(overview.stdout)
    assert "aim" in capabilities["commands"] and "controls" not in capabilities
    assert capabilities["controlCount"] > 100
    command = cli("describe", "--command", "controls")
    assert json.loads(command.stdout)["arguments"]["inputs"].startswith("1..32")
    control = cli("describe", "--control", "moveupdown")
    assert "MoveUpDown" in json.loads(control.stdout)["controls"]
    invalid = cli("describe", "--command", "imagined-cover-finder")
    assert invalid.returncode != 0 and "Unknown command" in invalid.stderr
    observation = Path(sys.argv[1]) / ("agent-inspection-" + game.session + ".json")
    inspected = cli("inspect", "--sections", "player", "nearby", "navigation", "--radius", "7", "--limit", "1",
                    "--types", "ped", "--line-of-sight", "off", "--blip-limit", "1", "--capture", "--output", str(observation))
    assert inspected.returncode == 0, inspected.stderr
    assert json.loads(inspected.stdout) == {"file": str(observation.resolve())}
    pack = json.loads(observation.read_text())
    assert pack["state"]["sections"]["player"]["entity"]["position"] == {"x": 1, "y": 2, "z": 3}
    assert not pack["status"]["lease"]["active"]
    assert pack["state"]["sections"]["nearby"]["radius"] == 7
    nearby = pack["state"]["sections"]["nearby"]
    assert nearby["total"] == 2 and nearby["returned"] == 1 and nearby["truncated"]
    assert nearby["nodes"][0]["entity"]["handle"] == 30
    navigation = pack["state"]["sections"]["navigation"]
    assert navigation["totalBlips"] == 2 and len(navigation["blips"]) == 1 and navigation["truncated"]
    assert Path(pack["capture"]["screenshot"]).is_file()
    assert pack["state"]["cost"]["lineOfSightTests"] == 0
    original = observation.read_bytes()
    duplicate = cli("inspect", "--output", str(observation))
    assert duplicate.returncode != 0 and observation.read_bytes() == original
    # Immediate calls return their result in one RPC; no batch files or polling.
    assert game.action("pause") == {"paused": True}
    assert not list(game.directory.glob("batch-*.json"))
    assert game.command("return new Vector3(4, 5, 6);") == {"x": 4, "y": 5, "z": 6}
    async with game.connect() as live:
        assert await live.action("resume") == {"paused": False}
        timed = await live.action("wait", ms=32)
        assert timed["elapsedGameMs"] >= 32

        stream = live.states(hz=5, sections=["player"])
        initial = await anext(stream)
        assert initial["sections"]["player"]["entity"]["position"] == {"x": 1, "y": 2, "z": 3}
        result = await live.batch([
            {"op": "resume"},
            {"op": "controls", "ms": 64, "inputs": [{"control": "MoveUpDown", "value": -0.5}]},
            {"op": "pause"}, {"op": "state", "sections": ["player"]}, {"op": "capture"}])
        assert result[1]["elapsedGameMs"] >= 64
        assert result[3]["gameTime"] > initial["gameTime"]
        assert result[3]["sections"]["player"]["entity"]["position"] == initial["sections"]["player"]["entity"]["position"]
        assert (await live.request("status"))["paused"] is True
        print(json.dumps({"screenshot": result[4]["screenshot"], "elapsed": result[1]["elapsedGameMs"]}))
    # Rust launches the Python policy. A checkpoint must
    # contain observed world evidence; cancellation must pause and release it.
    source = game.directory / "external-policy.py"
    source.write_text('def run(ctx):\n    state = ctx.check(sections=["player"])\n    ctx.checkpoint("position observed", position=state["sections"]["player"]["entity"]["position"])\n')
    started = game.http("POST", "/v1/jobs", {"script": str(source), "timeout": 10})
    deadline = time.monotonic() + 12
    while time.monotonic() < deadline:
        response = game.connection.get("/v1/jobs/" + started["id"])
        if response.is_success and "paused" in response.json():
            break
        time.sleep(.05)
    outcome = game.http("GET", "/v1/files/run-" + started["id"] + "/result.json")
    assert outcome["state"] == "checkpoint" and outcome["paused"] is True, outcome
    assert outcome["evidence"]["position"] == {"x": 1, "y": 2, "z": 3}
    assert (game.directory / ("run-" + started["id"]) / "events.jsonl").is_file()
    assert game.connection.get("/v1/session", headers={"Authorization": "invalid"}).status_code == 401
    assert game.connection.get("/v1/session", headers={"Origin": "https://example.com"}).status_code == 401
    assert game.connection.post("/v1/request", json=[]).status_code == 400
    assert game.connection.get("/v1/files/missing.png").status_code == 404
    game.close()


asyncio.run(main())
