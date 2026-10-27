"""External client used by player integration tests against the shipped game bridge."""
import asyncio
import json
import sys
import time
from gta_use import Client


async def main():
    game = Client(discovery=sys.argv[1], process=int(sys.argv[2]))
    assert "lease" in game.request("describe")["operations"]
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
