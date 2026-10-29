"""Shared project-local discovery for the client and supervised policy workers."""
import os
from pathlib import Path


def discovery_directory(explicit=None):
    selected = explicit or os.environ.get("GTA_USE_DISCOVERY")
    if selected:
        return Path(selected).resolve()
    project = Path(__file__).resolve().parents[2]
    configuration = project / ".runtime-root"
    root = configuration.read_text(encoding="utf-8-sig").strip() if configuration.exists() else "artifacts/gta-use"
    if not root:
        raise ValueError("GTA use .runtime-root is empty; configure its runtime directory.")
    return (project / root / "Bridge").resolve()
