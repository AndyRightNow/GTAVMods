"""Continuous alerted driving. GTA_USE_DESTINATION supplies observed x/y/z."""
import json
import os

from gta_use.driving import drive_to, wait_for_control


def run(ctx):
    result = drive_to(ctx, json.loads(os.environ["GTA_USE_DESTINATION"]))
    if result["outcome"] == "transition":
        state = wait_for_control(ctx)
        ctx.checkpoint("driving-transition-observed", state=state,
                       screenshot=ctx.game.request("capture"))
    else:
        ctx.checkpoint("destination-reached", **result,
                       screenshot=ctx.game.request("capture"))
