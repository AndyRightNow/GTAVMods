"""Segment script context: progress is an observation, not a successful task submission."""
import datetime
import json
import os
from pathlib import Path
import re
import shutil
import time
from .client import BridgeError, Client, UncertainOutcome


class StopSegment(RuntimeError):
    pass


def save_json(path, value):
    temporary = path.with_suffix(".tmp")
    with temporary.open("w", encoding="utf-8") as stream:
        # Encode in one pass instead of thousands of Python-level chunk writes.
        # Atomic visibility for external readers; these observations need no durable flush.
        stream.write(json.dumps(value, ensure_ascii=False, allow_nan=False))
    deadline = time.monotonic() + .5
    while True:
        try:
            os.replace(temporary, path)
            return
        except PermissionError:
            # Windows scanners/readers can briefly hold a file without delete sharing.
            # Retry only publishing this artifact, never the corresponding game action.
            if time.monotonic() >= deadline:
                raise
            time.sleep(.01)


class Segment:
    def __init__(self):
        self.directory = Path(os.environ["GTA_USE_RUN_DIRECTORY"])
        self.game = Client(process=int(os.environ["GTA_USE_PROCESS"]))
        self.status = {"state": "running", "checked": time.time(), "progress": time.time(), "checkpoint": None}
        self.capture_count = 0
        self.log("started", session=self.game.session)

    def capture(self, label, *, state=None, allow_unavailable=False, **evidence):
        """Retain a numbered screenshot beside the policy's logs and observations.

        Only a foreground-window rejection may be skipped explicitly. Transport
        uncertainty and other errors propagate; no screenshot action is replayed.
        """
        if not isinstance(label, str) or not re.fullmatch(r"[A-Za-z0-9][A-Za-z0-9_-]{0,79}", label):
            raise ValueError("Screenshot labels must use 1..80 letters, digits, underscores or hyphens.")
        try:
            shot = self.game.request("capture")
        except BridgeError as error:
            if isinstance(error, UncertainOutcome) or not allow_unavailable or "foreground window" not in str(error):
                raise
            self.log("screenshot-unavailable", label=label, reason=str(error))
            return None
        self.capture_count += 1
        target = self.directory / f"{self.capture_count:03}-{label}.png"
        shutil.copyfile(shot["screenshot"], target)
        self.log("screenshot", label=label, path=str(target), state=state, **evidence)
        return str(target)

    def log(self, event, **data):
        record = dict(data, event=event, utc=datetime.datetime.now(datetime.timezone.utc).isoformat())
        with (self.directory / "events.jsonl").open("a", encoding="utf-8") as stream:
            stream.write(json.dumps(record, ensure_ascii=False, allow_nan=False) + "\n")

    def check(self, **filters):
        if (self.directory / "cancel").exists():
            raise StopSegment("External cancellation requested.")
        state = self.game.state(**filters)
        return self.observed(state)

    def observed(self, state):
        """Record a streamed observation and acknowledge that the decision loop is alive."""
        if (self.directory / "cancel").exists():
            raise StopSegment("External cancellation requested.")
        self.status["checked"] = time.time()
        save_json(self.directory / "state.json", state)
        save_json(self.directory / "progress.json", self.status)
        player = state["sections"].get("player")
        if player and player["entity"]["dead"]:
            raise StopSegment("Player died; retry the mission checkpoint before rerunning.")
        return state

    def progress(self, message, **evidence):
        """Call only for meaningful observed changes: distance reduced, target defeated, stage changed."""
        self.status["progress"] = time.time()
        save_json(self.directory / "progress.json", self.status)
        self.log("progress", message=message, evidence=evidence)

    def checkpoint(self, name, **evidence):
        if not name or not evidence:
            raise ValueError("A checkpoint needs a name and observed evidence.")
        self.status.update(state="checkpoint", checkpoint=name, evidence=evidence)
        save_json(self.directory / "progress.json", self.status)
        self.log("checkpoint", name=name, evidence=evidence)

    def uncertain(self, reason):
        raise StopSegment(reason)
