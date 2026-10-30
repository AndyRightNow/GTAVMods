"""Bounded normal input for visible in-game cursor interfaces."""
import math
from ._validation import _bounds


def click_at(ctx, x, y, *, button="left", press_ms=70, settle_ms=100, tolerance=0.003):
    """Position the in-game cursor, verify it, and submit one bounded click.

    Coordinates are normalized to the whole game image, from (0, 0) at its top
    left to (1, 1) at its bottom right. The caller must first observe the visible
    target and resume the game. This does not focus an interface, dismiss an
    idle camera, interpret a screenshot, or operate Windows applications.

    Uses GTA's cursor controls (group 2). A rejected position request, mismatched
    cursor, or pause-menu transition withholds the click. An uncertain receipt
    propagates without replay. The returned input-submitted result is not proof
    that a button was activated; inspect the resulting UI/objective separately.
    Simulation remains running on return. Requires the supervisor's current lease.
    """
    _bounds(x=(x, 0, 1), y=(y, 0, 1), press_ms=(press_ms, 1, 500),
            settle_ms=(settle_ms, 1, 1000), tolerance=(tolerance, 0.0001, 0.02))
    if button not in ("left", "right"):
        raise ValueError("button must be left or right.")
    if not isinstance(press_ms, int) or not isinstance(settle_ms, int):
        raise ValueError("press_ms and settle_ms must be integer milliseconds.")
    game = ctx.game
    state = ctx.check(sections=["player"])
    if state["session"]["pauseMenu"]:
        ctx.uncertain("Close the pause menu and observe the intended cursor interface first.")
    accepted = game.native("SET_CURSOR_POSITION", dict(type="float", value=x),
                           dict(type="float", value=y), returns="bool")
    if accepted is not True:
        ctx.uncertain("Game rejected cursor positioning; click withheld.")
    game.action("wait", ms=settle_ms)
    observed = dict(x=game.native("GET_CONTROL_NORMAL", 2, 239, returns="float"),
                    y=game.native("GET_CONTROL_NORMAL", 2, 240, returns="float"))
    ctx.log("ui-cursor", requested=dict(x=x, y=y), observed=observed)
    if any(not isinstance(value, (int, float)) or isinstance(value, bool)
           or not math.isfinite(value) or abs(value - expected) > tolerance
           for value, expected in ((observed["x"], x), (observed["y"], y))):
        ctx.uncertain("Observed cursor differs from requested position; click withheld.")
    state = ctx.check(sections=["player"])
    if state["session"]["pauseMenu"]:
        ctx.uncertain("Pause menu opened before the click; click withheld.")
    game.action("controls", ms=press_ms, clock="wall",
                inputs=[dict(control="CursorAccept" if button == "left" else "CursorCancel",
                             group=2, value=1)])
    game.action("wait", ms=settle_ms)
    ctx.check(sections=["player"])
    result = dict(outcome="input-submitted", button=button, position=observed)
    ctx.log("ui-click", **result)
    return result
