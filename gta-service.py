"""Game-launched bootstrap: capture startup/import errors before loading FastAPI."""
import datetime
import os
from pathlib import Path
import sys
import traceback

root = Path(sys.argv[1])
process = int(sys.argv[2])
root.mkdir(parents=True, exist_ok=True)
with (root / f"service-{process}-stdio.log").open("a", encoding="utf-8", buffering=1) as output:
    sys.stdout = sys.stderr = output
    try:
        print(datetime.datetime.now(datetime.timezone.utc).isoformat(), "Starting GTA use", sys.version)
        from gta_use.service import serve
        serve(root, process, 0)
    except BaseException:
        traceback.print_exc()
        raise SystemExit(1)
