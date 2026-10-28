"""Bounded cover/fire policy. Run only after identifying the current combat objective.

Use the prepared Python interpreter: python -m gta_use run this-file.py --process PID
Optional GTA_USE_TARGETS is a JSON list of mission-confirmed ped handles.
GTA_USE_ALLIES is a JSON list of currently identified companion ped handles;
use [] only after checking the scene has no identified companions.
An empty nearby scan is not proof of mission completion.
"""
import json
import math
import os
import time


def near_segment(start, end, point, margin=3.0):
    dx, dy = end["x"] - start["x"], end["y"] - start["y"]
    length2 = dx * dx + dy * dy
    if length2 < .01:
        return True
    t = ((point["x"] - start["x"]) * dx + (point["y"] - start["y"]) * dy) / length2
    if not 0 <= t <= 1.05:
        return False
    return math.hypot(point["x"] - start["x"] - t * dx,
                      point["y"] - start["y"] - t * dy) < margin


def run(ctx):
    game = ctx.game
    confirmed = set(json.loads(os.environ.get("GTA_USE_TARGETS", "[]")))
    if "GTA_USE_ALLIES" not in os.environ:
        ctx.uncertain("Supply GTA_USE_ALLIES after identifying companions, even if it is [].")
    ally_values = json.loads(os.environ["GTA_USE_ALLIES"])
    if not isinstance(ally_values, list) or any(type(h) is not int or h <= 0 for h in ally_values):
        ctx.uncertain("GTA_USE_ALLIES must be a JSON list of positive ped handles.")
    allies = set(ally_values)
    observed_health = {}
    peeked = set()
    cover_started = None
    clear_started = None
    low_started = None
    game.action("resume")
    while True:
        state = ctx.check(sections=["player", "weapons", "navigation", "nearby"], types=["ped"],
                          radius=80, limit=48, blipLimit=128, lineOfSight="on")
        player = state["sections"]["player"]
        health = player["entity"]["health"]
        nodes = state["sections"]["nearby"]["nodes"]
        targets = [n for n in nodes if n["ped"]["human"] and not n["entity"]["dead"]
                   and n["entity"]["handle"] not in allies
                   and (n["ped"]["inCombatWithPlayer"] or n["entity"]["handle"] in confirmed)]
        for node in nodes:
            handle, hp = node["entity"]["handle"], node["entity"]["health"]
            if handle in observed_health and hp < observed_health[handle]:
                ctx.progress("Observed target damage", target=handle, health=hp, playerHealth=health)
            if node in targets or handle in observed_health:
                observed_health[handle] = hp
        if health <= 115:
            ctx.uncertain("Critical health: pause and inspect recovery options.")
        if not targets:
            clear_started = clear_started or time.monotonic()
            if time.monotonic() - clear_started >= 3:
                if not observed_health:
                    ctx.uncertain("No confirmed combat targets observed.")
                ctx.checkpoint("no-visible-combat-targets", previousTargets=observed_health,
                               playerHealth=health, screenshot=game.request("capture"))
                return
            time.sleep(.25)
            continue
        clear_started = None
        if not player["inCover"]:
            peeked.clear()
            if cover_started is None:
                game.action("controls", ms=200, inputs=[{"control": "Cover"}])
                cover_started = time.monotonic()
            elif time.monotonic() - cover_started > 5:
                ctx.uncertain("Cover was not acquired. Inspect geometry and move to a verified cover position.")
            time.sleep(.2)
            continue
        if cover_started is not None:
            ctx.progress("Observed cover acquired", position=player["entity"]["position"])
            cover_started = None
        if health < 145:
            low_started = low_started or time.monotonic()
            if time.monotonic() - low_started > 10:
                ctx.uncertain("Health did not recover in cover.")
            time.sleep(.25)
            continue
        low_started = None
        weapon = state["sections"]["weapons"]
        if weapon["totalAmmo"] == 0:
            ctx.uncertain("No ammo in selected weapon. Inspect inventory and pickups.")
        if player["reloading"]:
            time.sleep(.2)
            continue
        if weapon["ammoInClip"] == 0:
            game.action("controls", ms=150, inputs=[{"control": "Reload"}])
            continue
        # Unknown live humans are treated conservatively as possible friendlies.
        target_handles = {n["entity"]["handle"] for n in targets}
        obstacles = [n["entity"]["position"] for n in nodes if n["ped"]["human"]
                     and not n["entity"]["dead"] and n["entity"]["handle"] not in target_handles]
        for ally in allies:
            peds = [n for n in nodes if n["entity"]["handle"] == ally and n["ped"]["human"]
                    and not n["entity"]["dead"]]
            blips = [b for b in state["sections"]["navigation"]["blips"] if b["entity"] == ally]
            if len(peds) == 1:
                obstacles.append(peds[0]["entity"]["position"])
            elif len(blips) == 1:
                obstacles.append(blips[0]["position"])
            else:
                ctx.uncertain("An identified companion cannot be located reliably.")
        safe = [n for n in targets if not any(near_segment(player["entity"]["position"],
                n["entity"]["position"], point) for point in obstacles)]
        if not safe:
            ctx.uncertain("A companion or unknown human may be in the line of fire.")
        target = min(safe, key=lambda n: n["entity"]["relative"]["distance"])
        handle = target["entity"]["handle"]
        if handle not in peeked:
            # Tucked-position LOS can be blocked by the player's own cover.
            # Aim without firing to peek, then refresh identity and companion positions.
            result = game.action("aim", entity=handle, bone=31086, ms=800, shoot=False)
            ctx.log("cover-peek", target=handle, result=result)
            if result["targetAvailable"]:
                peeked.add(handle)
            continue
        ctx.log("before-aim", target=target["entity"]["handle"],
                targetHealth=target["entity"]["health"], playerHealth=health,
                ammo=weapon["totalAmmo"], ammoInClip=weapon["ammoInClip"])
        result = game.action("aim", entity=target["entity"]["handle"], bone=31086,
                             ms=350, shoot=True, requireAimTarget=True)
        after = ctx.check(sections=["player", "weapons", "nearby"], types=["ped"], radius=80, limit=48)
        observed_target = next((n["entity"] for n in after["sections"]["nearby"]["nodes"]
                                if n["entity"]["handle"] == target["entity"]["handle"]), None)
        ctx.log("after-aim", result=result, target=observed_target,
                playerHealth=after["sections"]["player"]["entity"]["health"],
                ammo=after["sections"]["weapons"]["totalAmmo"],
                ammoInClip=after["sections"]["weapons"]["ammoInClip"])
        if result["targetAvailable"] and result["attackRequestedFrames"] == 0:
            ctx.uncertain("Fire withheld: inspect aim-target feedback and cover geometry before retrying.")
