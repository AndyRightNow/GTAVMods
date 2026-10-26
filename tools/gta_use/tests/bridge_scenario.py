"""External client used by player integration tests against the shipped game bridge."""
import asyncio
import json
import sys
from gta_use import Client


async def main():
    game = Client(discovery=sys.argv[1], process=int(sys.argv[2]))
    assert "lease" in game.request("describe")["operations"]
    async with game.connect() as live:
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
    game.close()


asyncio.run(main())
