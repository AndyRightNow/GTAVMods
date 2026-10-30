"""Offline discovery of the shipped Python gameplay helpers."""
import importlib
import inspect

_MODULES = ("combat", "driving", "entities", "navigation", "ui", "vehicles")


def describe_helpers(query=None):
    """List public helper summaries, or matching signatures and full contracts.

    Imports only known SDK modules; never connects to a game or acquires a lease.
    """
    selected = []
    needle = query.casefold() if query is not None else None
    for name in _MODULES:
        module = importlib.import_module("gta_use." + name)
        for symbol, function in inspect.getmembers(module, inspect.isfunction):
            if symbol.startswith("_") or function.__module__ != module.__name__:
                continue
            qualified = name + "." + symbol
            if needle is not None and needle not in qualified.casefold():
                continue
            description = inspect.getdoc(function) or ""
            entry = dict(name=qualified, summary=description.split("\n", 1)[0])
            if query is not None:
                entry.update(signature=qualified + str(inspect.signature(function)),
                             importStatement=f"from gta_use.{name} import {symbol}",
                             description=description)
            selected.append(entry)
    if query is not None and not selected:
        raise ValueError(f"No SDK helper matches {query!r}. Run gta_use helpers to list names.")
    return dict(schema="gta.sdk-helpers/1", helpers=selected)
