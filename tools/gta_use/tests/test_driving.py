"""Driving and pursuit scenarios through the shipped Rust service and runner."""
from collections import deque
import json
from pathlib import Path
import tempfile
import unittest

from observed_game import ObservedGame
import test_runner as runner


def scene(y=0, speed=0, **session):
    return dict(gameTime=100,
        session=dict(playerControl=True, cutscenePlaying=False, pauseMenu=False, missionActive=True, **session),
        sections=dict(player=dict(entity=dict(handle=10, model=42, health=175, dead=False,
                          position=dict(x=0, y=y, z=0)), inVehicle=True, vehicle=20),
                      vehicle=dict(entity=dict(handle=20, model=45), driver=10, speed=speed,
                                   driveable=True, upsideDown=False)))


class DrivingWorld(ObservedGame):
    def __init__(self, root, observations, targets=(), lost=False):
        super().__init__(root)
        self.observations = deque(observations)
        self.targets = deque(targets or [dict(y=50, health=150, vehicle=40)])
        self.lost = lost

    def request(self, envelope):
        self.requests.append(envelope)
        result = {}
        if envelope["op"] == "state":
            result = self.observations[0]
            if len(self.observations) > 1:
                self.observations.popleft()
        elif envelope["op"] == "action":
            step, value = envelope["steps"][0], None
            if step["op"] == "command" and "gta-use:pursuit-target" in step["code"]:
                t = self.targets[0]
                value = None if t.get("missing") else dict(position=dict(x=0, y=t["y"], z=0),
                                                            health=t["health"], vehicle=t["vehicle"])
                if len(self.targets) > 1:
                    self.targets.popleft()
            if step["op"] == "command" and "gta-use:follow-target" in step["code"]:
                t = self.targets[0]
                value = None if t.get("missing") else dict(position=dict(x=0, y=t["y"], z=t.get("z", 0)),
                    velocity=dict(x=0,y=10,z=0), relative=t.get("relative", dict(x=0,y=20,z=0)),
                    health=t["health"], dead=t["health"] <= 0, vehicle=t["vehicle"])
                if len(self.targets) > 1:
                    self.targets.popleft()
            if step["op"] == "native":
                name, args = step["name"], step["args"]
                if self.lost and name in ("TASK_VEHICLE_DRIVE_TO_COORD_LONGRANGE", "TASK_VEHICLE_MISSION", "TASK_VEHICLE_FOLLOW"):
                    return dict(id=envelope["id"], session=self.session, ok=False, uncertain=True, error="Lost drive receipt")
                if name == "DOES_ENTITY_EXIST":
                    value = True
                elif name == "GET_ENTITY_MODEL":
                    value = {30: 46, 40: 47}[args[0]]
                elif name == "GET_ENTITY_COORDS":
                    value = dict(x=0, y=self.targets[0]["y"], z=0)
                elif name == "GET_ENTITY_HEALTH":
                    value = self.targets[0]["health"]
                elif name == "GET_VEHICLE_PED_IS_IN":
                    value = self.targets[0]["vehicle"]
                    if len(self.targets) > 1:
                        self.targets.popleft()
            result = dict(completed=True, value=value)
        return dict(id=envelope["id"], session=self.session, ok=True, result=result)


class DrivingScenarios(unittest.TestCase):
    start_server = runner.RunnerScenarios.start_server
    tearDown = runner.RunnerScenarios.tearDown
    run_script = runner.RunnerScenarios.run_script

    def setUp(self):
        self.storage = tempfile.TemporaryDirectory(prefix="GTAVMods-driving-test-")
        self.root = Path(self.storage.name)

    def exercise(self, observations, code=None, targets=(), lost=False):
        self.game = DrivingWorld(self.root, observations, targets, lost)
        self.start_server()
        return self.run_script(code or '''from gta_use.driving import drive_to
def run(ctx):
    result = drive_to(ctx, dict(x=0,y=100,z=0), radius=4, timeout=3,
                      stall_timeout=.25, check_interval=.05)
    ctx.checkpoint("drive-result", **result)
''', "--timeout", "8", "--stuck-timeout", "5", "--check-timeout", "4")

    def natives(self):
        return [s for r in self.game.requests for s in r.get("steps", []) if s["op"] == "native"]

    def events(self):
        return [json.loads(line) for line in (self.root / "run/events.jsonl").read_text().splitlines()]

    def test_delivery_avoids_light_waits_slows_down_and_requires_stopped_arrival(self):
        result, outcome = self.exercise([scene(), scene(30, 25), scene(98, 10), scene(98, 0)])
        self.assertEqual(0, result.returncode, result.stderr)
        self.assertEqual("arrived", outcome["evidence"]["outcome"])
        self.assertEqual(0, outcome["evidence"]["state"]["sections"]["vehicle"]["speed"])
        tasks = self.natives()
        drive = next(n for n in tasks if n["name"] == "TASK_VEHICLE_DRIVE_TO_COORD_LONGRANGE")
        self.assertEqual(1, sum(n["name"] == drive["name"] for n in tasks))
        style = drive["args"][6]
        self.assertEqual(0, style & 128, "Delivery should not wait for traffic lights.")
        self.assertTrue(style & 1 and style & 4 and style & 32, "Retain braking and obstacle avoidance.")
        speeds = [n["args"][1]["value"] for n in tasks if n["name"] == "SET_DRIVE_TASK_CRUISE_SPEED"]
        self.assertGreater(speeds[0], 6, "Do not crawl the entire approach zone at arrival speed.")
        self.assertLess(speeds[0], 28)
        self.assertEqual(6, speeds[-1])
        self.assertEqual("CLEAR_PED_TASKS", tasks[-1]["name"])
        self.assertTrue(outcome["paused"])
        self.assertTrue(all(r.get("sections") == ["player", "vehicle"] for r in self.game.requests if r["op"] == "state"))

    def test_red_light_or_obstacle_stall_stops_with_evidence_instead_of_reissuing_task(self):
        result, outcome = self.exercise([scene()])
        self.assertEqual(1, result.returncode)
        self.assertTrue(outcome["paused"])
        self.assertEqual(["TASK_VEHICLE_DRIVE_TO_COORD_LONGRANGE", "CLEAR_PED_TASKS"], [n["name"] for n in self.natives()])
        self.assertTrue(any(e["event"] == "drive-stalled" for e in self.events()))
        self.assertFalse(any(e["event"] == "progress" for e in self.events()))

    def test_cutscene_then_cleared_mission_flag_continues_without_cancelling_mission_tasks(self):
        entering, playing, end = scene(98), scene(98), scene(98)
        entering["session"].update(playerControl=False, cutscenePlaying=True, cutsceneTime=1)
        playing["session"].update(playerControl=False, cutscenePlaying=True, cutsceneTime=28000)
        end["session"]["missionActive"] = False
        result, outcome = self.exercise([scene(), entering, entering, playing, end], '''from gta_use.driving import drive_to, wait_for_control
def run(ctx):
    result = drive_to(ctx, dict(x=0,y=100,z=0), timeout=2, check_interval=.05)
    assert result["outcome"] == "transition"
    state = wait_for_control(ctx, timeout=2, check_interval=.05)
    ctx.checkpoint("control-returned", session=state["session"])
''')
        self.assertEqual(0, result.returncode, result.stderr)
        self.assertFalse(outcome["evidence"]["session"]["missionActive"])
        self.assertFalse(any(n["name"] == "CLEAR_PED_TASKS" for n in self.natives()))
        self.assertTrue(any(e["event"] == "progress" and e["evidence"]["session"]["cutsceneTime"] == 28000 for e in self.events()))

    def test_changed_vehicle_identity_does_not_cancel_new_occupants_tasks(self):
        changed = scene(50)
        changed["sections"]["vehicle"]["entity"]["model"] = 99
        result, outcome = self.exercise([scene(), changed])
        self.assertEqual(1, result.returncode)
        self.assertFalse(any(n["name"] == "CLEAR_PED_TASKS" for n in self.natives()))
        self.assertTrue(outcome["paused"])

    def test_health_loss_stops_the_owned_drive(self):
        hurt = scene(50)
        hurt["sections"]["player"]["entity"]["health"] = 80
        result, _ = self.exercise([scene(), hurt])
        self.assertEqual(1, result.returncode)
        self.assertEqual("CLEAR_PED_TASKS", self.natives()[-1]["name"])

    def test_lost_drive_receipt_never_replays_or_clears_an_uncertain_task(self):
        result, outcome = self.exercise([scene()], lost=True)
        self.assertEqual(1, result.returncode)
        self.assertEqual(["TASK_VEHICLE_DRIVE_TO_COORD_LONGRANGE"], [n["name"] for n in self.natives()])
        self.assertTrue(outcome["paused"])

    def test_pursuit_keeps_chasing_through_engagement_range_until_rider_dismounts(self):
        result, outcome = self.exercise([scene(), scene(15, 20), scene(35, 20), scene(48, 2)], targets=[
            dict(y=50, health=150, vehicle=40), dict(y=50, health=150, vehicle=40),
            dict(y=50, health=120, vehicle=40), dict(y=50, health=120, vehicle=0)], code='''from gta_use.driving import pursue_vehicle
def run(ctx):
    def engage(ctx, state, target):
        ctx.log("engagement-observation", distance=target["distance"])
    result = pursue_vehicle(ctx, 30, 40, rider_model=46, vehicle_model=47,
                             on_tick=engage, check_interval=.05, timeout=3)
    ctx.checkpoint("pursuit-transition", **result)
''')
        self.assertEqual(0, result.returncode, result.stderr)
        self.assertEqual("target-left-vehicle", outcome["evidence"]["outcome"])
        self.assertTrue(any(e["event"] == "engagement-observation" and e["distance"] < 25 for e in self.events()))
        self.assertEqual(1, sum(n["name"] == "TASK_VEHICLE_CHASE" for n in self.natives()))
        self.assertEqual("CLEAR_PED_TASKS", self.natives()[-1]["name"])

    def test_constant_gap_travel_does_not_mask_ineffective_pursuit(self):
        # Both vehicles move along the road, but the gap and target health stay
        # unchanged. Travelling farther must not conceal an ineffective chase.
        observations = [scene()] + [scene(y, 20) for y in range(10, 210, 10)]
        targets = [dict(y=50, health=150, vehicle=40)] + [
            dict(y=y+50, health=150, vehicle=40) for y in range(10, 210, 10)]
        result, outcome = self.exercise(observations, targets=targets, code='''from gta_use.driving import pursue_vehicle
def run(ctx):
    pursue_vehicle(ctx, 30, 40, rider_model=46, vehicle_model=47,
                   no_progress_timeout=.2, timeout=3, check_interval=.05)
''')
        self.assertEqual(1, result.returncode)
        self.assertTrue(outcome["paused"])
        self.assertTrue(any(e["event"] == "pursuit-no-effect" for e in self.events()))
        self.assertFalse(any(e["event"] == "progress" for e in self.events()))

    def test_changed_rider_model_rejects_pursuit_before_any_driving_task(self):
        result, outcome = self.exercise([scene()], code='''from gta_use.driving import pursue_vehicle
def run(ctx):
    pursue_vehicle(ctx, 30, 40, rider_model=999, vehicle_model=47)
''')
        self.assertEqual(1, result.returncode)
        self.assertTrue(outcome["paused"])
        self.assertFalse(any(n["name"] == "TASK_VEHICLE_CHASE" for n in self.natives()))

    def test_invalid_speed_is_rejected_before_any_game_action(self):
        result, _ = self.exercise([scene()], code='''from gta_use.driving import drive_to
def run(ctx):
    drive_to(ctx, dict(x=0,y=100,z=0), speed=float("nan"))
''')
        self.assertEqual(1, result.returncode)
        self.assertFalse(self.natives())

    def test_interception_reaches_road_anchor_then_keeps_ram_task_until_rider_separates(self):
        result, outcome = self.exercise([scene(), scene(95, 25), scene(110, 25), scene(118, 5)], targets=[
            dict(y=1000, health=150, vehicle=40), dict(y=300, health=150, vehicle=40),
            dict(y=120, health=150, vehicle=40), dict(y=120, health=0, vehicle=0)], code='''from gta_use.driving import intercept_vehicle
def run(ctx):
    result=intercept_vehicle(ctx,30,40,rider_model=46,vehicle_model=47,
        waypoints=[dict(x=0,y=100,z=0)],waypoint_radius=10,engagement="ram",timeout=3,check_interval=.05)
    ctx.checkpoint("rider-separated",**result)
''')
        self.assertEqual(0, result.returncode, result.stderr)
        self.assertEqual("rider-separated", outcome["evidence"]["outcome"])
        self.assertEqual(0, outcome["evidence"]["target"]["health"])
        tasks = self.natives()
        self.assertEqual(["TASK_VEHICLE_DRIVE_TO_COORD_LONGRANGE", "TASK_VEHICLE_MISSION", "CLEAR_PED_TASKS"],
                         [t["name"] for t in tasks])
        self.assertEqual((40, 2), tuple(tasks[1]["args"][2:4]), "Physical interception must target the observed vehicle, using Ram.")
        queries = [s for r in self.game.requests for s in r.get("steps", []) if s["op"] == "command"]
        self.assertEqual(4, len(queries), "One target query per observation, no per-field RPC loop.")
        self.assertEqual(1, sum(s["name"] == "CLEAR_PED_TASKS" for s in tasks), "No braking/clearing between route and pursuit.")

    def test_interception_defaults_to_chase_and_never_silently_selects_ram(self):
        result, _ = self.exercise([scene(), scene(30)], targets=[dict(y=40,health=150,vehicle=40),
             dict(y=40,health=150,vehicle=0)], code='''from gta_use.driving import intercept_vehicle
def run(ctx):
    result=intercept_vehicle(ctx,30,40,rider_model=46,vehicle_model=47,timeout=2,check_interval=.05)
    ctx.checkpoint("target-dismounted",**result)
''')
        self.assertEqual(0, result.returncode, result.stderr)
        self.assertEqual(["TASK_VEHICLE_CHASE", "CLEAR_PED_TASKS"], [n["name"] for n in self.natives()])

    def test_interception_constant_gap_times_out_without_resetting_progress_for_travel(self):
        result, outcome = self.exercise([scene()], targets=[dict(y=40, health=150, vehicle=40)], code='''from gta_use.driving import intercept_vehicle
def run(ctx):
    intercept_vehicle(ctx,30,40,rider_model=46,vehicle_model=47,timeout=2,
                      no_progress_timeout=.2,check_interval=.05)
''')
        self.assertEqual(1, result.returncode)
        self.assertTrue(outcome["paused"])
        self.assertTrue(any(e["event"] == "pursuit-no-effect" for e in self.events()))
        self.assertFalse(any(e["event"] == "progress" for e in self.events()))
        self.assertEqual(["TASK_VEHICLE_CHASE", "CLEAR_PED_TASKS"], [n["name"] for n in self.natives()])

    def test_lost_ram_receipt_never_replays_or_clears_uncertain_pursuit(self):
        result, outcome = self.exercise([scene()], lost=True, code='''from gta_use.driving import intercept_vehicle
def run(ctx):
    intercept_vehicle(ctx,30,40,rider_model=46,vehicle_model=47,engagement="ram",timeout=2,check_interval=.05)
''')
        self.assertEqual(1, result.returncode)
        self.assertTrue(outcome["paused"])
        self.assertEqual(["TASK_VEHICLE_MISSION"], [n["name"] for n in self.natives()])

    def test_control_must_stay_available_for_requested_settle_interval(self):
        playing = scene()
        playing["session"].update(playerControl=False, cutscenePlaying=True, cutsceneTime=2000)
        result, outcome = self.exercise([scene(), playing, scene(), scene()], code='''from gta_use.driving import wait_for_control
def run(ctx):
    state=wait_for_control(ctx,timeout=2,check_interval=.05,stable_for=.1)
    ctx.checkpoint("stable-control",session=state["session"])
''')
        self.assertEqual(0, result.returncode, result.stderr)
        self.assertGreaterEqual(sum(r["op"]=="state" for r in self.game.requests), 5)
        self.assertTrue(any(e["event"] == "progress" and e["evidence"]["session"]["cutscenePlaying"] for e in self.events()))


    def test_follow_approaches_scripted_actor_without_requiring_a_vehicle_seat(self):
        result, outcome = self.exercise([scene(), scene(20,20), scene(47,18)], targets=[
            dict(y=50,health=150,vehicle=0),dict(y=55,health=150,vehicle=0),dict(y=50,z=2,health=150,vehicle=0)], code="""from gta_use.driving import follow_entity
def run(ctx):
    r=follow_entity(ctx,dict(handle=30,model=46),speed=35,min_distance=0,
                    arrival_radius=4,timeout=3,check_interval=.05)
    ctx.checkpoint("approach",**r)
""")
        self.assertEqual(0,result.returncode,result.stderr)
        evidence=outcome["evidence"]
        self.assertEqual("near-target",evidence["outcome"])
        self.assertEqual(0,evidence["target"]["vehicle"],"Scripted boarding is not normal seated occupancy.")
        self.assertAlmostEqual(13**.5,evidence["target"]["distance"])
        tasks=self.natives()
        self.assertEqual(["TASK_VEHICLE_FOLLOW","CLEAR_PED_TASKS"],[n["name"] for n in tasks])
        self.assertEqual([10,20,30,{"type":"float","value":35},786469,0],tasks[0]["args"])

    def test_follow_constant_gap_stops_instead_of_treating_road_travel_as_progress(self):
        result,outcome=self.exercise([scene()],targets=[dict(y=50,health=150,vehicle=0)],code="""from gta_use.driving import follow_entity
def run(ctx):
    follow_entity(ctx,dict(handle=30,model=46),timeout=2,no_progress_timeout=.2,check_interval=.05)
""")
        self.assertEqual(1,result.returncode)
        self.assertTrue(outcome["paused"])
        self.assertTrue(any(e["event"]=="follow-no-effect" for e in self.events()))
        self.assertEqual(["TASK_VEHICLE_FOLLOW","CLEAR_PED_TASKS"],[n["name"] for n in self.natives()])

    def test_follow_policy_can_end_on_observed_scripted_boarding_without_claiming_mission_success(self):
        result,outcome=self.exercise([scene(),scene(20)],targets=[dict(y=50,health=150,vehicle=0),
            dict(y=21,health=150,vehicle=0,relative=dict(x=0,y=1,z=2))],code="""from gta_use.driving import follow_entity
def run(ctx):
    def observe(ctx,state,target):
        return abs(target['relative']['y'])<2
    r=follow_entity(ctx,dict(handle=30,model=46),on_tick=observe,timeout=2,check_interval=.05)
    ctx.checkpoint("boarding-needs-inspection",**r)
""")
        self.assertEqual(0,result.returncode,result.stderr)
        self.assertEqual("policy",outcome["evidence"]["outcome"])
        self.assertTrue(outcome["evidence"]["state"]["session"]["missionActive"])
        self.assertEqual("CLEAR_PED_TASKS",self.natives()[-1]["name"])

    def test_follow_mission_takeover_preserves_mission_tasks(self):
        transition=scene(20);transition['session'].update(playerControl=False,cutscenePlaying=True)
        result,outcome=self.exercise([scene(),transition],code="""from gta_use.driving import follow_entity
def run(ctx):
    ctx.checkpoint("takeover",**follow_entity(ctx,dict(handle=30,model=46),timeout=2,check_interval=.05))
""")
        self.assertEqual(0,result.returncode,result.stderr)
        self.assertEqual("transition",outcome['evidence']['outcome'])
        self.assertEqual(["TASK_VEHICLE_FOLLOW"],[n['name'] for n in self.natives()])

    def test_follow_changed_target_stops_owned_drive_without_reusing_handle(self):
        result,outcome=self.exercise([scene()],targets=[dict(y=50,health=150,vehicle=0),dict(missing=True)],code="""from gta_use.driving import follow_entity
def run(ctx):
    follow_entity(ctx,dict(handle=30,model=46),timeout=2,check_interval=.05)
""")
        self.assertEqual(1,result.returncode)
        self.assertTrue(outcome['paused'])
        self.assertEqual(["TASK_VEHICLE_FOLLOW","CLEAR_PED_TASKS"],[n['name'] for n in self.natives()])
        self.assertTrue(any('identity changed' in e.get('error','') for e in self.events()))

    def test_follow_lost_task_receipt_stops_without_replay_or_task_cancellation(self):
        result,outcome=self.exercise([scene()],lost=True,code="""from gta_use.driving import follow_entity
def run(ctx):
    follow_entity(ctx,dict(handle=30,model=46),timeout=2,check_interval=.05)
""")
        self.assertEqual(1,result.returncode)
        self.assertTrue(outcome['paused'])
        self.assertEqual(["TASK_VEHICLE_FOLLOW"],[n['name'] for n in self.natives()])

    def test_follow_rejects_invalid_distance_before_submitting_game_input(self):
        result,_=self.exercise([scene()],code="""from gta_use.driving import follow_entity
def run(ctx):
    follow_entity(ctx,dict(handle=30,model=46),min_distance=1.5)
""")
        self.assertEqual(1,result.returncode)
        self.assertFalse(self.natives())
