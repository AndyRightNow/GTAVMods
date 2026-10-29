"""Player traversal scenarios through the real Rust transport and Python runner."""
from collections import deque
import json
from pathlib import Path
import tempfile
import unittest

from observed_game import ObservedGame
import test_runner as runner


def observation(y=0, z=0, **flags):
    player = dict(entity=dict(handle=10, model=42, health=175, dead=False,
                             position=dict(x=0, y=y, z=z), rotation=dict(x=0, y=0, z=0)),
                  inVehicle=False, inCover=False, swimming=False, climbing=False,
                  vaulting=False, jumping=False, falling=False, inAir=False, ragdoll=False)
    player.update(flags)
    return dict(gameTime=100, session=dict(playerControl=True, cutscenePlaying=False, pauseMenu=False),
                sections=dict(player=player))


class ObservedTraversal(ObservedGame):
    """Explicit engine observations after submitted tasks, with no simulated motion."""
    def __init__(self, root, routes, climbs=(), probe=None, uncertain_climb=False, approach=(),
                 route_name="TASK_FOLLOW_NAV_MESH_TO_COORD", low_probe=None):
        super().__init__(root)
        self.observations = deque([observation()])
        self.routes = deque(routes)
        self.climbs = deque(climbs)
        self.probe = probe or dict(hit=True, entity=0, position=dict(x=0, y=1, z=.25), distance=1)
        self.uncertain_climb = uncertain_climb
        self.approach = approach
        self.route_name = route_name
        self.low_probe = low_probe

    def request(self, envelope):
        self.requests.append(envelope)
        result = {}
        if envelope["op"] == "state":
            result = self.observations[0]
            if len(self.observations) > 1:
                self.observations.popleft()
            if "probes" in envelope.get("sections", []):
                probes = self.probe if isinstance(self.probe, list) else [self.probe]
                if envelope.get("probeHeight", 0) < 0 and self.low_probe is not None:
                    probes = [self.low_probe]
                result = dict(result, sections=dict(result["sections"], probes=probes))
        elif envelope["op"] == "action":
            step = envelope["steps"][0]
            value = None
            if step["op"] == "native":
                name = step["name"]
                if name == self.route_name and self.routes:
                    self.observations = deque(self.routes.popleft())
                elif name == "TASK_GO_STRAIGHT_TO_COORD" and self.approach:
                    self.observations = deque(self.approach)
                elif name == "TASK_CLIMB":
                    if self.uncertain_climb:
                        return dict(id=envelope["id"], session=self.session, ok=False, uncertain=True,
                                    error="Observed lost climb receipt; completion unknown.")
                    if self.climbs:
                        self.observations = deque(self.climbs.popleft())
                elif name == "GET_NAVMESH_ROUTE_RESULT":
                    value = 2  # Explicit route-not-found observation in blocked scenes.
                elif name == "GET_ENTITY_TYPE":
                    value = 1  # Explicit pedestrian obstruction, not a climbable prop.
            result = dict(completed=True, value=value)
        return dict(id=envelope["id"], session=self.session, ok=True, result=result)


class NavigationScenarios(unittest.TestCase):
    start_server = runner.RunnerScenarios.start_server
    tearDown = runner.RunnerScenarios.tearDown
    run_script = runner.RunnerScenarios.run_script

    def setUp(self):
        self.storage = tempfile.TemporaryDirectory(prefix="GTAVMods-navigation-test-")
        self.root = Path(self.storage.name)

    def exercise(self, routes, climbs=(), probe=None, uncertain_climb=False, destination=None, options="", approach=(),
                 route_name="TASK_FOLLOW_NAV_MESH_TO_COORD", low_probe=None):
        self.game = ObservedTraversal(self.root, routes, climbs, probe, uncertain_climb, approach, route_name, low_probe)
        self.start_server()
        point = destination or dict(x=0, y=4, z=0)
        source = f'''from gta_use.navigation import navigate_to
def run(ctx):
    result = navigate_to(ctx, {point!r}, radius=.5, timeout=8, stall_timeout=.2,
                         climb_timeout=.5, check_interval=.05{options})
    ctx.checkpoint("route-arrival-observed", **result)
'''
        return self.run_script(source, "--timeout", "10", "--stuck-timeout", "8", "--check-timeout", "5")

    def natives(self):
        return [step for request in self.game.requests if request["op"] == "action"
                for step in request["steps"] if step["op"] == "native"]

    def events(self):
        return [json.loads(line) for line in (self.root / "run/events.jsonl").read_text().splitlines()]

    def test_open_route_reaches_observed_waypoints_without_jumping_or_recompiling(self):
        result, outcome = self.exercise([[observation(), observation(2)], [observation(3), observation(4)]],
                                       options=", waypoints=[{'x':0,'y':2,'z':0}]")
        self.assertEqual(0, result.returncode, result.stderr)
        self.assertEqual(2, outcome["evidence"]["waypoints"])
        self.assertEqual(0, outcome["evidence"]["climbAttempts"])
        self.assertTrue(outcome["paused"])
        paths = [n for n in self.natives() if n["name"] == "TASK_FOLLOW_NAV_MESH_TO_COORD"]
        self.assertEqual(2, len(paths))
        self.assertTrue(all(n["args"][5] == -1 for n in paths), "Navigation may not warp after a timeout.")
        self.assertFalse(any(n["name"] == "TASK_CLIMB" for n in self.natives()))
        self.assertFalse(any(step["op"] == "command" for r in self.game.requests
                             for step in r.get("steps", [])), "Repeated navigation must not consume C# compilations.")

    def test_blocked_fence_climbs_then_requires_a_settled_far_side_observation(self):
        result, outcome = self.exercise([[observation()]],
            climbs=[[observation(1, 1, climbing=True, inAir=True), observation(4, 1, falling=True, inAir=True), observation(4)]])
        self.assertEqual(0, result.returncode, result.stderr)
        self.assertEqual(1, outcome["evidence"]["climbAttempts"])
        self.assertEqual(dict(x=0, y=4, z=0), outcome["evidence"]["position"])
        self.assertEqual(1, sum(n["name"] == "TASK_CLIMB" for n in self.natives()))
        self.assertTrue(any(e.get("message") == "Landed with navigation progress" for e in self.events()))

    def test_unclimbable_wall_stops_after_two_attempts_without_false_progress(self):
        result, outcome = self.exercise([[observation()]], climbs=[[observation()], [observation()]],
                                       options=", max_climbs=2")
        self.assertEqual(1, result.returncode)
        self.assertTrue(outcome["paused"])
        self.assertEqual(2, sum(n["name"] == "TASK_CLIMB" for n in self.natives()))
        self.assertFalse(any(e["event"] in ("checkpoint", "progress") for e in self.events()))
        self.assertEqual("CLEAR_PED_TASKS", self.natives()[-1]["name"])

    def test_observed_direct_crossing_climbs_instead_of_requesting_a_navmesh_detour(self):
        # Explicit engine observations: the ped shuffles against an edge while
        # the centre ray misses it. Motion alone must not postpone recovery.
        result, outcome = self.exercise([[observation(y) for y in (0, -1) * 20]],
            probe=[dict(hit=False, entity=None, position=None, distance=None),
                   dict(hit=True, entity=0, position=dict(x=0, y=1, z=.25), distance=1)],
            climbs=[[observation(1, 1, climbing=True, inAir=True), observation(4)]],
            options=", route_mode='direct'", route_name="TASK_GO_STRAIGHT_TO_COORD")
        self.assertEqual(0, result.returncode, result.stderr)
        self.assertEqual(1, outcome["evidence"]["climbAttempts"])
        self.assertEqual(dict(x=0, y=4, z=0), outcome["evidence"]["position"])
        self.assertFalse(any(n["name"] == "TASK_FOLLOW_NAV_MESH_TO_COORD" for n in self.natives()))
        path = self.natives()[0]
        self.assertEqual("TASK_GO_STRAIGHT_TO_COORD", path["name"])
        self.assertEqual(-1, path["args"][5], "Direct crossing may not warp after a timeout.")
        scan = next(r for r in self.game.requests if "probes" in r.get("sections", []))
        self.assertEqual(3, len(scan["rays"]), "A bounded fan must cover the body's edge collision.")

    def test_direct_mode_rejects_cross_town_travel_before_moving(self):
        result, outcome = self.exercise([], destination=dict(x=0, y=100, z=0), options=", route_mode='direct'")
        self.assertEqual(1, result.returncode)
        self.assertTrue(outcome["paused"])
        self.assertFalse(self.natives())
        self.assertIn("no longer than 30 m", (self.root / "run/stderr.log").read_text())

    def test_low_fence_gets_a_lower_probe_before_normal_vault_and_observed_landing(self):
        result, outcome = self.exercise([[observation()]],
            probe=dict(hit=False, entity=None, position=None, distance=None),
            low_probe=dict(hit=True, entity=0, position=dict(x=0, y=1, z=-.4), distance=1),
            climbs=[[observation(1, 1, vaulting=True, inAir=True), observation(4)]],
            options=", route_mode='direct'", route_name="TASK_GO_STRAIGHT_TO_COORD")
        self.assertEqual(0, result.returncode, result.stderr)
        self.assertEqual(1, outcome["evidence"]["climbAttempts"])
        scans = [r for r in self.game.requests if "probes" in r.get("sections", [])]
        self.assertEqual(2, len(scans))
        self.assertGreater(scans[0]["probeHeight"], 0)
        self.assertLess(scans[1]["probeHeight"], 0)

    def test_observed_obstacle_beyond_reach_gets_a_bounded_approach_before_climbing(self):
        result, outcome = self.exercise([[observation()]],
            probe=dict(hit=True, entity=0, position=dict(x=0, y=3, z=.25), distance=3),
            approach=[observation(1), observation(2.2)],
            climbs=[[observation(2.8, 1, climbing=True, inAir=True), observation(4)]])
        self.assertEqual(0, result.returncode, result.stderr)
        names = [n["name"] for n in self.natives()]
        self.assertLess(names.index("TASK_GO_STRAIGHT_TO_COORD"), names.index("TASK_CLIMB"))
        self.assertEqual(1, outcome["evidence"]["climbAttempts"])

    def test_missing_obstacle_evidence_stops_instead_of_jumping_blindly(self):
        result, outcome = self.exercise([[observation()]], probe=dict(hit=False, entity=None, position=None, distance=None))
        self.assertEqual(1, result.returncode)
        self.assertTrue(outcome["paused"])
        self.assertIn("No nearby obstacle", (self.root / "run/stderr.log").read_text())
        self.assertFalse(any(n["name"] == "TASK_CLIMB" for n in self.natives()))

    def test_health_loss_during_travel_cancels_the_owned_route_before_parkour(self):
        wounded = observation(1)
        wounded["sections"]["player"]["entity"]["health"] = 90
        result, outcome = self.exercise([[wounded]])
        self.assertEqual(1, result.returncode)
        self.assertTrue(outcome["paused"])
        self.assertIn("health or water", (self.root / "run/stderr.log").read_text())
        self.assertEqual(["TASK_FOLLOW_NAV_MESH_TO_COORD", "CLEAR_PED_TASKS"], [n["name"] for n in self.natives()])

    def test_disabled_parkour_leaves_blocked_route_for_inspection_without_a_jump(self):
        result, outcome = self.exercise([[observation()]], options=", parkour=False")
        self.assertEqual(1, result.returncode)
        self.assertTrue(outcome["paused"])
        self.assertFalse(any(n["name"] == "TASK_CLIMB" for n in self.natives()))
        self.assertFalse(any("probes" in r.get("sections", []) for r in self.game.requests))

    def test_pedestrian_obstruction_is_not_treated_as_a_fence(self):
        result, outcome = self.exercise([[observation()]],
            probe=dict(hit=True, entity=30, position=dict(x=0, y=1, z=.25), distance=1))
        self.assertEqual(1, result.returncode)
        self.assertTrue(outcome["paused"])
        self.assertIn("ped or vehicle", (self.root / "run/stderr.log").read_text())
        self.assertFalse(any(n["name"] == "TASK_CLIMB" for n in self.natives()))

    def test_same_horizontal_coordinate_on_another_level_is_not_arrival(self):
        result, outcome = self.exercise([[observation()]], destination=dict(x=0, y=0, z=5))
        self.assertEqual(1, result.returncode)
        self.assertTrue(outcome["paused"])
        self.assertIn("another level", (self.root / "run/stderr.log").read_text())
        self.assertFalse(any(e["event"] == "checkpoint" for e in self.events()))

    def test_mission_vehicle_transition_stops_without_clearing_the_new_task(self):
        result, outcome = self.exercise([[observation(inVehicle=True)]])
        self.assertEqual(1, result.returncode)
        self.assertTrue(outcome["paused"])
        self.assertEqual(["TASK_FOLLOW_NAV_MESH_TO_COORD"], [n["name"] for n in self.natives()])

    def test_lost_climb_receipt_is_not_replayed_or_followed_by_more_movement(self):
        result, outcome = self.exercise([[observation()]], uncertain_climb=True)
        self.assertEqual(1, result.returncode)
        self.assertEqual("TASK_CLIMB", self.natives()[-1]["name"])
        self.assertEqual(1, sum(n["name"] == "TASK_CLIMB" for n in self.natives()))
        self.assertIn("completion unknown", (self.root / "run/stderr.log").read_text())

    def test_unfinished_climb_is_not_cancelled_to_fabricate_a_landing(self):
        result, outcome = self.exercise([[observation()]], climbs=[[observation(1, 1, climbing=True, inAir=True)]])
        self.assertEqual(1, result.returncode)
        self.assertTrue(outcome["paused"])
        self.assertEqual("TASK_CLIMB", self.natives()[-1]["name"])
        self.assertIn("did not settle", (self.root / "run/stderr.log").read_text())

    def test_nonfinite_destination_rejects_before_any_game_mutation(self):
        result, outcome = self.exercise([], destination=dict(x="nan", y=4, z=0))
        self.assertEqual(1, result.returncode)
        self.assertTrue(outcome["paused"])
        self.assertFalse(self.natives())
        self.assertIn("finite", (self.root / "run/stderr.log").read_text())

    def climb(self, observations, *, lost=False):
        self.game = ObservedTraversal(self.root, [], climbs=[observations], uncertain_climb=lost)
        self.start_server()
        return self.run_script('''from gta_use.navigation import climb_toward
def run(ctx):
    result=climb_toward(ctx, dict(x=0,y=2,z=2), timeout=1, check_interval=.05)
    ctx.checkpoint("edge-result", **result)
''', "--timeout", "8", "--stuck-timeout", "5", "--check-timeout", "4")

    def test_observed_vehicle_roof_climb_returns_height_only_after_stable_landing(self):
        result, outcome = self.climb([observation(.5, 1, climbing=True),
                                     observation(1, 2, falling=True), observation(1, 2)])
        self.assertEqual(0, result.returncode, result.stderr)
        self.assertEqual("climbed", outcome["evidence"]["outcome"])
        self.assertEqual(2, outcome["evidence"]["heightChange"])
        self.assertEqual(dict(x=0,y=1,z=2), outcome["evidence"]["position"])
        self.assertEqual(["TASK_TURN_PED_TO_FACE_COORD", "TASK_CLIMB"], [n["name"] for n in self.natives()])
        self.assertNotIn("arrived", outcome["evidence"], "A landing does not establish the intended route arrival.")

    def test_normal_jump_at_missing_edge_is_not_reported_as_a_climb(self):
        result, outcome = self.climb([observation(0, .4, jumping=True), observation()])
        self.assertEqual(0, result.returncode, result.stderr)
        self.assertEqual("landed", outcome["evidence"]["outcome"])
        self.assertEqual(0, outcome["evidence"]["displacement"])
        self.assertFalse(any(e["event"] == "progress" for e in self.events()))

    def test_accepted_climb_without_animation_returns_no_effect_without_retry(self):
        result, outcome = self.climb([observation()])
        self.assertEqual(0, result.returncode, result.stderr)
        self.assertEqual("no-effect", outcome["evidence"]["outcome"])
        self.assertEqual(1, sum(n["name"] == "TASK_CLIMB" for n in self.natives()))

    def test_climb_triggered_mission_transition_is_returned_without_task_cleanup(self):
        transition = observation(1, 2)
        transition["session"]["playerControl"] = False
        result, outcome = self.climb([transition])
        self.assertEqual(0, result.returncode, result.stderr)
        self.assertEqual("transition", outcome["evidence"]["outcome"])
        self.assertEqual("TASK_CLIMB", self.natives()[-1]["name"])

    def test_climb_timeout_does_not_cancel_an_airborne_player(self):
        result, outcome = self.climb([observation(1, 2, falling=True)])
        self.assertEqual(1, result.returncode)
        self.assertTrue(outcome["paused"])
        self.assertEqual("TASK_CLIMB", self.natives()[-1]["name"])
        self.assertIn("did not settle", (self.root / "run/stderr.log").read_text())

    def test_uncertain_standalone_climb_stops_without_replay(self):
        result, outcome = self.climb([observation()], lost=True)
        self.assertEqual(1, result.returncode)
        self.assertTrue(outcome["paused"])
        self.assertEqual(1, sum(n["name"] == "TASK_CLIMB" for n in self.natives()))


if __name__ == "__main__":
    unittest.main()
