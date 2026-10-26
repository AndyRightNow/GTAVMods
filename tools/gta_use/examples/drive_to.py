"""Drive to an operator-selected coordinate and verify arrival.

Set GTA_USE_DESTINATION to {"x":...,"y":...,"z":...} before running.
Uses current vehicle, ordinary driving tasks and explicit observations.
"""
import json
import math
import os
import time


def run(ctx):
    destination = json.loads(os.environ["GTA_USE_DESTINATION"])
    point = [float(destination[axis]) for axis in ("x", "y", "z")]
    if not all(math.isfinite(value) for value in point):
        raise ValueError("Destination must contain finite coordinates.")
    game = ctx.game
    state = ctx.check(sections=["player", "vehicle"])
    player, vehicle = state["sections"]["player"], state["sections"]["vehicle"]
    if not vehicle or vehicle["driver"] != player["entity"]["handle"]:
        ctx.uncertain("Observe the player in the driver seat before starting.")
    game.action("resume")
    f = lambda value: {"type": "float", "value": value}
    game.native("TASK_VEHICLE_DRIVE_TO_COORD_LONGRANGE", player["entity"]["handle"], player["vehicle"],
                *map(f, point), f(18), 786475, f(8))
    best = float("inf")
    visited = set()
    while True:
        state = ctx.check(sections=["player", "vehicle"])
        player, vehicle = state["sections"]["player"], state["sections"]["vehicle"]
        if not vehicle or not vehicle["driveable"] or vehicle["upsideDown"]:
            ctx.uncertain("Vehicle needs recovery.")
        position = player["entity"]["position"]
        remaining = math.dist([position[k] for k in ("x", "y", "z")], point)
        cell = (round(position["x"] / 10), round(position["y"] / 10))
        if remaining < best - 5 or cell not in visited:
            best = min(best, remaining)
            visited.add(cell)
            ctx.progress("Observed route progress", remaining=remaining, speed=vehicle["speed"])
        if remaining <= 10:
            ctx.checkpoint("destination-reached", remaining=remaining, position=position,
                           screenshot=game.request("capture"))
            return
        time.sleep(.4)
