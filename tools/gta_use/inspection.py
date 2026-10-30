"""Small, read-only discovery surfaces for agents; no lease or game actions."""


def select_capabilities(description, command=None, control=None):
    """Select real installed capabilities without dumping the complete control enum."""
    commands = description["commands"]
    if command is not None:
        matches = [item for item in commands if item["op"] == command]
        if not matches:
            raise ValueError(f"Unknown command {command!r}; run describe for installed names.")
        return matches[0]
    if control is not None:
        return {"controls": [name for name in description["controls"] if control.casefold() in name.casefold()]}
    return {"operations": description["operations"], "commands": [item["op"] for item in commands],
            "stateSections": description["stateSections"], "controlCount": len(description["controls"])}


def inspect_game(game, filters, capture=False):
    """Keep the unmodified world pack, bridge readiness and optional image together."""
    status = game.request("status")
    result = {"session": game.session, "status": status, "state": game.state(**filters)}
    if capture:
        result["capture"] = game.request("capture")
    return result
