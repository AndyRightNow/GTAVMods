"""Run real Python policies through the shipped Rust service and observed game pipe."""
import json
import os
from collections import deque
from pathlib import Path
import subprocess
import sys
import tempfile
import time
import unittest
from gta_use.segment import save_json
from observed_game import ObservedGame, RustScene


class ObservedCombat(ObservedGame):
    """External engine observations and receipts; no physics or combat decisions."""
    def __init__(self, root, observations, aim_result=None):
        super().__init__(root)
        self.observations = deque(observations)
        self.aim_result = aim_result or dict(targetAvailable=True, attackRequestedFrames=2)

    def request(self, envelope):
        self.requests.append(envelope)
        if envelope["op"] == "batch":
            identifier = envelope["id"]
            for index, step in enumerate(envelope["steps"]):
                save_json(self.directory / f"result-{identifier}-{index}.json", {"result": None})
            save_json(self.directory / f"batch-{identifier}-status.json", dict(batch=identifier, state="completed", total=len(envelope["steps"])))
            result = {"batch": identifier}
        elif envelope["op"] == "action":
            result = {"completed": True, "value": self.aim_result if envelope["steps"][0]["op"] == "aim" else None}
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
        self.server = RustScene(self.game)

    def tearDown(self):
        self.server.close()
        self.storage.cleanup()

    def run_script(self, script, *arguments, env=None):
        source = self.root / "segment.py"
        source.write_text(script)
        result = subprocess.run([sys.executable, "-m", "gta_use", "run", str(source), "--discovery", str(self.root),
                                 "--process", str(self.game.process), "--output", str(self.root / "run"), *arguments],
                                capture_output=True, text=True, timeout=15,
                                env=None if env is None else dict(os.environ, **env))
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

    def exercise(self, observations, allies="[]", aim_result=None):
        self.game = ObservedCombat(self.root, observations, aim_result)
        self.start_server()
        source = Path(__file__).parents[1] / "examples" / "cover_segment.py"
        return self.run_script(source.read_text(), "--timeout", "10", "--stuck-timeout", "8",
                               env={"GTA_USE_ALLIES": allies, "GTA_USE_TARGETS": "[]"})

    @staticmethod
    def observation(human=True, player_health=175, target_health=100):
        return {"gameTime": 100, "sections": {
            "player": {"entity": {"handle": 10, "health": player_health, "dead": False,
                                    "position": {"x": 0, "y": 0, "z": 0}}, "inCover": True, "reloading": False},
            "weapons": {"totalAmmo": 20, "ammoInClip": 8},
            "navigation": {"blips": []},
            "nearby": {"nodes": [{"entity": {"handle": 20, "health": target_health, "dead": target_health == 0,
                                              "position": {"x": 0, "y": 15, "z": 0},
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
        result, outcome = self.exercise([alive, alive, dead])
        self.assertEqual(0, result.returncode, result.stderr)
        self.assertEqual("no-visible-combat-targets", outcome["checkpoint"])
        self.assertTrue(outcome["paused"])
        shots = [step for step in self.actions() if step["op"] == "aim"]
        self.assertEqual([False, True], [step["shoot"] for step in shots])
        self.assertTrue(shots[1]["requireAimTarget"])
        self.assertEqual({"20": 0}, outcome["evidence"]["previousTargets"])

    def test_companion_in_shot_corridor_prevents_fire_despite_clear_entity_los(self):
        observed = self.observation()
        observed["sections"]["navigation"]["blips"] = [{"entity": 30, "position": {"x": 1, "y": 7, "z": 0}}]
        result, outcome = self.exercise([observed], allies="[30]")
        self.assertEqual(1, result.returncode)
        self.assertTrue(outcome["paused"])
        self.assertNotIn("aim", [step["op"] for step in self.actions()])
        self.assertIn("line of fire", (self.root / "run/stderr.log").read_text())

    def test_missing_companion_stops_instead_of_treating_an_incomplete_scan_as_safe(self):
        result, outcome = self.exercise([self.observation()], allies="[30]")
        self.assertEqual(1, result.returncode)
        self.assertTrue(outcome["paused"])
        self.assertNotIn("aim", [step["op"] for step in self.actions()])
        self.assertIn("cannot be located", (self.root / "run/stderr.log").read_text())

    def test_unknown_human_in_the_firing_corridor_is_not_treated_as_a_target(self):
        observed = self.observation()
        observed["sections"]["nearby"]["nodes"].append({"entity": {"handle": 30, "health": 175, "dead": False,
            "position": {"x": 0, "y": 5, "z": 0}}, "ped": {"human": True, "inCombatWithPlayer": False}})
        result, outcome = self.exercise([observed])
        self.assertEqual(1, result.returncode)
        self.assertTrue(outcome["paused"])
        self.assertNotIn("aim", [step["op"] for step in self.actions()])

    def test_off_axis_companion_permits_gated_fire_and_records_whole_interval_ammo(self):
        alive, dead = self.observation(), self.observation(target_health=0)
        alive["sections"]["navigation"]["blips"] = [{"entity": 30, "position": {"x": 6, "y": 7, "z": 0}}]
        dead["sections"]["weapons"] = {"totalAmmo": 19, "ammoInClip": 7}
        result, outcome = self.exercise([alive, alive, dead], allies="[30]")
        self.assertEqual(0, result.returncode, result.stderr)
        self.assertTrue(outcome["paused"])
        events = [json.loads(line) for line in (self.root / "run/events.jsonl").read_text().splitlines()]
        before = next(e for e in events if e["event"] == "before-aim")
        after = next(e for e in events if e["event"] == "after-aim")
        self.assertEqual((20, 19, 0), (before["ammo"], after["ammo"], after["target"]["health"]))

    def test_withheld_fire_stops_for_geometry_inspection_without_repeating_bursts(self):
        result, outcome = self.exercise([self.observation()], aim_result=dict(targetAvailable=True, attackRequestedFrames=0,
            last=dict(aimTarget=30, lineOfSight=True)))
        self.assertEqual(1, result.returncode)
        self.assertTrue(outcome["paused"])
        self.assertEqual([False, True], [step["shoot"] for step in self.actions() if step["op"] == "aim"])
        self.assertIn("Fire withheld", (self.root / "run/stderr.log").read_text())

    def test_tucked_cover_los_does_not_prevent_an_aim_only_peek_before_gated_fire(self):
        tucked, peeking, dead = self.observation(), self.observation(), self.observation(target_health=0)
        tucked["sections"]["nearby"]["nodes"][0]["lineOfSight"] = False
        result, outcome = self.exercise([tucked, peeking, dead])
        self.assertEqual(0, result.returncode, result.stderr)
        self.assertTrue(outcome["paused"])
        aims = [step for step in self.actions() if step["op"] == "aim"]
        self.assertEqual([False, True], [step["shoot"] for step in aims])
        self.assertTrue(aims[1]["requireAimTarget"])

    def test_companion_entering_corridor_during_peek_prevents_the_following_shot(self):
        tucked, crossed = self.observation(), self.observation()
        tucked["sections"]["navigation"]["blips"] = [{"entity": 30, "position": {"x": 6, "y": 7, "z": 0}}]
        crossed["sections"]["navigation"]["blips"] = [{"entity": 30, "position": {"x": 0, "y": 7, "z": 0}}]
        result, outcome = self.exercise([tucked, crossed], allies="[30]")
        self.assertEqual(1, result.returncode)
        self.assertTrue(outcome["paused"])
        self.assertEqual([False], [step["shoot"] for step in self.actions() if step["op"] == "aim"])
        self.assertIn("line of fire", (self.root / "run/stderr.log").read_text())


if __name__ == "__main__":
    unittest.main()
