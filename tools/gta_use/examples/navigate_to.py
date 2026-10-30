"""Run/climb a short observed route. GTA_USE_DESTINATION is a JSON x/y/z object.

GTA_USE_WAYPOINTS optionally supplies a JSON array of approach/far-side points.
Use vehicle travel for longer distances; this is access traversal, not a pursuit.
"""
import json
import os
from gta_use.navigation import navigate_to


def run(ctx):
    result = navigate_to(ctx, json.loads(os.environ["GTA_USE_DESTINATION"]),
                         waypoints=json.loads(os.environ.get("GTA_USE_WAYPOINTS", "[]")))
    ctx.checkpoint("on-foot-destination-reached", **result)
