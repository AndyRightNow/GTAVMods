"""Segment script context: progress is an observation, not a successful task submission."""
import datetime
import json
import os
from pathlib import Path
import time
from .client import Client


class StopSegment(RuntimeError):
    pass


def save_json(path, value):
    temporary = path.with_suffix(".tmp")
    with temporary.open("w", encoding="utf-8") as stream:
        json.dump(value, stream, ensure_ascii=False, allow_nan=False)
        stream.flush()
        os.fsync(stream.fileno())
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
        self.log("started", session=self.game.session)

    def log(self, event, **data):
        record = dict(data, event=event, utc=datetime.datetime.now(datetime.timezone.utc).isoformat())
        with (self.directory / "events.jsonl").open("a", encoding="utf-8") as stream:
            stream.write(json.dumps(record, ensure_ascii=False, allow_nan=False) + "\n")
            stream.flush()
            os.fsync(stream.fileno())

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
