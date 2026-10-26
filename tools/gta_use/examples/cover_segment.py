"""Bounded cover/fire policy. Run only after identifying the current combat objective.

uv run gta-use run tools/gta_use/examples/cover_segment.py --process PID
Optional GTA_USE_TARGETS is a JSON list of mission-confirmed ped handles.
An empty nearby scan is not proof of mission completion.
"""
import json
import os
import time


def run(ctx):
    game = ctx.game
    confirmed = set(json.loads(os.environ.get("GTA_USE_TARGETS", "[]")))
    observed_health = {}
    cover_started = None
    clear_started = None
    low_started = None
    game.action("resume")
    while True:
        state = ctx.check(sections=["player", "weapons", "nearby"], types=["ped"],
                          radius=80, limit=48, lineOfSight="on")
        player = state["sections"]["player"]
        health = player["entity"]["health"]
        nodes = state["sections"]["nearby"]["nodes"]
        targets = [n for n in nodes if n["ped"]["human"] and not n["entity"]["dead"]
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
        visible = [n for n in targets if n["lineOfSight"]]
        if not visible:
            ctx.uncertain("No clear firing line. Inspect a cover edge or another protected position.")
        weapon = state["sections"]["weapons"]
        if weapon["totalAmmo"] == 0:
            ctx.uncertain("No ammo in selected weapon. Inspect inventory and pickups.")
        if player["reloading"]:
            time.sleep(.2)
            continue
        if weapon["ammoInClip"] == 0:
            game.action("controls", ms=150, inputs=[{"control": "Reload"}])
            continue
        target = min(visible, key=lambda n: n["entity"]["relative"]["distance"])
        game.action("aim", entity=target["entity"]["handle"], bone=31086, ms=350, shoot=True)
