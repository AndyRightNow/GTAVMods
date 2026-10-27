"""Run real Python segment processes against a local FastAPI service and observed game responses."""
import json
from collections import deque
import logging
from pathlib import Path
import socket
import subprocess
import sys
import tempfile
import threading
import time
import unittest
import uvicorn
from gta_use.service import make_app
from gta_use.segment import save_json
from test_service import ObservedGame


class ObservedCombat(ObservedGame):
    """External engine observations and receipts; no physics or combat decisions."""
    def __init__(self, root, observations):
        super().__init__(root)
        self.observations = deque(observations)

    async def request(self, envelope):
        self.requests.append(envelope)
        if envelope["op"] == "batch":
            identifier = envelope["id"]
            for index, step in enumerate(envelope["steps"]):
                save_json(self.directory / f"result-{identifier}-{index}.json", {"result": None})
            save_json(self.directory / f"batch-{identifier}-status.json", dict(batch=identifier, state="completed", total=len(envelope["steps"])))
            result = {"batch": identifier}
        elif envelope["op"] == "action":
            result = {"completed": True, "value": None}
        elif envelope["op"] == "state":
            result = self.observations[0]
            if len(self.observations) > 1:
                self.observations.popleft()
        else:
            result = {"screenshot": "observed-frame.png"} if envelope["op"] == "capture" else {}
        return dict(id=envelope["id"], session=self.session, ok=True, result=result)


class RunnerScenarios(unittest.TestCase):
    def setUp(self):
        self.storage = tempfile.TemporaryDirectory(prefix="GTAVMods-runner-test-")
        self.root = Path(self.storage.name)
        self.game = ObservedGame(self.root)
        self.start_server()

    def start_server(self):
        self.socket = socket.socket()
        self.socket.bind(("127.0.0.1", 0))
        self.server = uvicorn.Server(uvicorn.Config(make_app(self.game, "runner-secret", logging.getLogger("runner-test")),
                                                    log_config=None, access_log=False))
        self.thread = threading.Thread(target=lambda: self.server.run(sockets=[self.socket]), daemon=True)
        self.thread.start()
        deadline = time.monotonic() + 5
        while not self.server.started and time.monotonic() < deadline:
            time.sleep(.01)
        self.assertTrue(self.server.started)
        (self.root / "service-123.json").write_text(json.dumps({"url": f"http://127.0.0.1:{self.socket.getsockname()[1]}", "token": "runner-secret"}))

    def tearDown(self):
        self.server.should_exit = True
        self.thread.join(timeout=10)
        self.socket.close()
        self.storage.cleanup()

    def run_script(self, script, *arguments):
        source = self.root / "segment.py"
        source.write_text(script)
        result = subprocess.run([sys.executable, "-m", "gta_use", "run", str(source), "--discovery", str(self.root),
                                 "--process", "123", "--output", str(self.root / "run"), *arguments],
                                capture_output=True, text=True, timeout=15)
        self.assertTrue((self.root / "run/result.json").exists(), result.stdout + result.stderr)
        return result, json.loads((self.root / "run/result.json").read_text())

    def test_checkpoint_requires_observation_and_writes_source_logs_state_and_final_result(self):
        result, outcome = self.run_script('''def run(ctx):
    state = ctx.check(sections=["player"])
    health = state["sections"]["player"]["entity"]["health"]
    print("observed", health)
    ctx.progress("Player observed alive", health=health)
    ctx.checkpoint("player-inspected", health=health)
''')
        self.assertEqual(0, result.returncode, result.stderr)
        self.assertEqual("checkpoint", outcome["state"])
        self.assertEqual(175, outcome["evidence"]["health"])
        self.assertTrue(outcome["paused"])
        self.assertIn("observed 175", (self.root / "run/stdout.log").read_text())
        self.assertIn('"event": "checkpoint"', (self.root / "run/events.jsonl").read_text())
        leases = [r["action"] for r in self.game.requests if r["op"] == "lease"]
        self.assertEqual("acquire", leases[0])
        self.assertEqual("release", leases[-1])
        self.assertTrue(all(action == "heartbeat" for action in leases[1:-1]))

    def test_async_python_segment_uses_streamed_state_to_confirm_checkpoint(self):
        result, outcome = self.run_script('''async def run(ctx):
    async with ctx.game.connect() as game:
        async for state in game.states(sections=["player"], hz=5):
            ctx.observed(state)
            ctx.checkpoint("stream-confirmed", gameTime=state["gameTime"])
            return
''')
        self.assertEqual(0, result.returncode, result.stderr)
        self.assertEqual("stream-confirmed", outcome["checkpoint"])

    def test_busy_python_loop_is_terminated_by_independent_deadline_and_releases_lease(self):
        result, outcome = self.run_script("def run(ctx):\n    while True: pass\n", "--timeout", "1.5", "--check-timeout", "5")
        self.assertEqual(1, result.returncode)
        self.assertIn("deadline", outcome["error"])
        self.assertTrue(outcome["paused"])
        self.assertEqual("release", self.game.requests[-1]["action"])

    def test_repeated_identical_observations_do_not_reset_the_progress_deadline(self):
        result, outcome = self.run_script('''import time
def run(ctx):
    while True:
        ctx.check(sections=["player"])
        time.sleep(.1)
''', "--stuck-timeout", "2", "--timeout", "8")
        self.assertEqual(1, result.returncode)
        self.assertIn("No observed progress", outcome["error"], (self.root / "run/stderr.log").read_text())
        self.assertTrue(outcome["paused"])

    def test_script_exception_or_return_without_checkpoint_cannot_be_reported_as_success(self):
        result, outcome = self.run_script("def run(ctx):\n    raise RuntimeError('uncertain mission objective')\n")
        self.assertEqual(1, result.returncode)
        self.assertEqual("failed", outcome["state"])
        self.assertIn("uncertain mission objective", (self.root / "run/stderr.log").read_text())
        self.assertTrue(outcome["paused"])


class CoverPolicyScenarios(unittest.TestCase):
    # Use the same real service/runner harness without inheriting unrelated cases.
    start_server = RunnerScenarios.start_server
    tearDown = RunnerScenarios.tearDown
    run_script = RunnerScenarios.run_script

    def setUp(self):
        self.storage = tempfile.TemporaryDirectory(prefix="GTAVMods-cover-test-")
        self.root = Path(self.storage.name)

    def exercise(self, observations):
        self.game = ObservedCombat(self.root, observations)
        self.start_server()
        source = Path(__file__).parents[1] / "examples" / "cover_segment.py"
        return self.run_script(source.read_text(), "--timeout", "10", "--stuck-timeout", "8")

    @staticmethod
    def observation(human=True, player_health=175, target_health=100):
        return {"gameTime": 100, "sections": {
            "player": {"entity": {"handle": 10, "health": player_health, "dead": False}, "inCover": True, "reloading": False},
            "weapons": {"totalAmmo": 20, "ammoInClip": 8},
            "nearby": {"nodes": [{"entity": {"handle": 20, "health": target_health, "dead": target_health == 0,
                                              "relative": {"distance": 15}},
                                   "ped": {"human": human, "inCombatWithPlayer": True}, "lineOfSight": True}]}}}

    def actions(self):
        return [step for request in self.game.requests if request["op"] in ("batch", "action") for step in request["steps"]]

    def test_observed_animal_is_not_shot_even_when_engine_reports_combat(self):
        result, outcome = self.exercise([self.observation(human=False)])
        self.assertEqual(1, result.returncode)
        self.assertTrue(outcome["paused"])
        self.assertEqual(["resume"], [step["op"] for step in self.actions()])
        self.assertIn("No confirmed combat targets", (self.root / "run/stderr.log").read_text())

    def test_critical_health_stops_before_firing_despite_a_visible_enemy(self):
        result, outcome = self.exercise([self.observation(player_health=110)])
        self.assertEqual(1, result.returncode)
        self.assertTrue(outcome["paused"])
        self.assertNotIn("aim", [step["op"] for step in self.actions()])
        self.assertIn("Critical health", (self.root / "run/stderr.log").read_text())

    def test_confirmed_cover_fire_stops_after_observed_target_death_without_claiming_mission_pass(self):
        alive = self.observation()
        dead = self.observation(target_health=0)
        result, outcome = self.exercise([alive, dead])
        self.assertEqual(0, result.returncode, result.stderr)
        self.assertEqual("no-visible-combat-targets", outcome["checkpoint"])
        self.assertTrue(outcome["paused"])
        shots = [step for step in self.actions() if step["op"] == "aim"]
        self.assertEqual([dict(op="aim", entity=20, bone=31086, ms=350, shoot=True)], shots)
        self.assertEqual({"20": 0}, outcome["evidence"]["previousTargets"])


if __name__ == "__main__":
    unittest.main()
