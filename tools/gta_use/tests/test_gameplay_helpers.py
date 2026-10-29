"""Vehicle access and cover firing via real SDK, supervisor, Rust and game pipe."""
from collections import deque
import json
from pathlib import Path
import re
import tempfile
import unittest

from observed_game import ObservedGame
import test_runner as runner


def entity(handle, model, x=0, y=0, z=0):
    return dict(handle=handle, model=model, health=175, dead=False,
                position=dict(x=x, y=y, z=z), velocity=dict(x=0, y=0, z=0),
                rotation=dict(x=0, y=0, z=0), relative=dict(distance=abs(y)))


def scene(*, car=None, cover=False, entering=False, ammo=15):
    return dict(gameTime=100,
        session=dict(playerControl=True, cutscenePlaying=False, pauseMenu=False, missionActive=True),
        sections=dict(player=dict(entity=entity(10, 42), inVehicle=car is not None,
                                 vehicle=car or 0, inCover=cover, goingIntoCover=entering, reloading=False),
                      vehicle=dict(entity=entity(car, 45), driver=10, driveable=True,
                                   onAllWheels=True, upsideDown=False, speed=0) if car else None,
                      weapons=dict(hash=453432689, totalAmmo=ammo, ammoInClip=ammo),
                      nearby=dict(nodes=[dict(kind="vehicle", entity=entity(20, 45, y=8)),
                                         dict(kind="vehicle", entity=entity(21, 45, y=12))])))


def access(handle, **changes):
    return dict(handle=handle, model=45, driveable=True, locked="CanBeBrokenInto",
                passenger=True, occupant=0, occupantModel=0, occupantAlive=False) | changes


def muzzle(**changes):
    return dict(source=dict(x=0, y=0, z=1), target=dict(x=0, y=10, z=1),
                hit=True, hitEntity=30, allies=[dict(handle=31, model=47, dead=False,
                                                   position=dict(x=2, y=5, z=1))]) | changes


class HelperWorld(ObservedGame):
    """Explicit external observations, not a simulated entry/combat decision engine."""
    def __init__(self, root, observations, queries=None, lost=None):
        super().__init__(root)
        self.observations = deque(observations)
        self.queries = {k: deque(v) for k, v in (queries or {}).items()}
        self.lost = lost
        self.screenshot = root / "frame.png"
        self.screenshot.write_bytes(b"retained external frame")

    def request(self, envelope):
        self.requests.append(envelope)
        result = {}
        if envelope["op"] == "state":
            result = self.observations[0]
            if len(self.observations) > 1:
                self.observations.popleft()
        elif envelope["op"] == "capture":
            result = dict(screenshot=str(self.screenshot))
        elif envelope["op"] == "action":
            step, value = envelope["steps"][0], None
            if step["op"] == "command":
                match = re.search(r"gta-use:([a-z-]+)", step["code"])
                name = match[1] if match else "other"
                if name == self.lost:
                    return dict(id=envelope["id"], session=self.session, ok=False, uncertain=True, error="Lost action receipt")
                observations = self.queries.get(name)
                if observations:
                    value = observations[0]
                    if len(observations) > 1:
                        observations.popleft()
            elif step["op"] == "aim":
                if self.lost == "aim":
                    return dict(id=envelope["id"], session=self.session, ok=False, uncertain=True, error="Lost aim receipt")
                value = dict(targetAvailable=True, attackRequestedFrames=3 if step.get("shoot") else 0)
            result = dict(completed=True, value=value)
        return dict(id=envelope["id"], session=self.session, ok=True, result=result)


class GameplayHelperScenarios(unittest.TestCase):
    start_server = runner.RunnerScenarios.start_server
    tearDown = runner.RunnerScenarios.tearDown
    run_script = runner.RunnerScenarios.run_script

    def setUp(self):
        self.storage = tempfile.TemporaryDirectory(prefix="GTAVMods-helper-test-")
        self.root = Path(self.storage.name)

    def exercise(self, script, observations, queries=None, lost=None):
        self.game = HelperWorld(self.root, observations, queries, lost)
        self.start_server()
        return self.run_script(script, "--timeout", "8", "--stuck-timeout", "5", "--check-timeout", "4")

    def commands(self, name):
        return [s["code"] for r in self.game.requests for s in r.get("steps", [])
                if s["op"] == "command" and "gta-use:" + name in s["code"]]

    def aims(self):
        return [s for r in self.game.requests for s in r.get("steps", []) if s["op"] == "aim"]

    def test_carjack_selection_skips_inaccessible_nearest_car_and_verifies_driver_entry(self):
        result, outcome = self.exercise('''from gta_use.vehicles import acquire_vehicle
def run(ctx):
    car=acquire_vehicle(ctx, passenger=True, companion=dict(handle=31,model=47), check_interval=.05)
    ctx.checkpoint("driver-entered", vehicle=car)
''', [scene(), scene(), scene(car=21)], {"vehicle-candidates": [[access(20, locked="CannotEnter"),
             access(21, passenger=False, occupant=31, occupantModel=47, occupantAlive=True)]]})
        self.assertEqual(0, result.returncode, result.stderr)
        self.assertEqual(21, outcome["evidence"]["vehicle"]["handle"])
        self.assertEqual(1, len(self.commands("vehicle-candidates")), "Inspect all candidate access in one RPC.")
        self.assertIn("FromHandle(21)", self.commands("enter-vehicle")[0])
        self.assertIn("VehicleSeat.Driver,-1,2f,EnterVehicleFlags.ResumeIfInterupted", self.commands("enter-vehicle")[0],
                      "Python owns the deadline; finite engine timeouts can warp the ped.")
        self.assertFalse(self.commands("clear-entry"), "Successful driving must not be cancelled.")

    def test_required_car_does_not_succeed_just_because_player_drives_another_car(self):
        result, outcome = self.exercise('''from gta_use.vehicles import acquire_vehicle
def run(ctx):
    acquire_vehicle(ctx, dict(handle=21,model=45))
''', [scene(car=20)])
        self.assertEqual(1, result.returncode)
        self.assertTrue(outcome["paused"])
        self.assertFalse(self.commands("enter-vehicle"))

    def test_recycled_companion_cannot_make_an_occupied_passenger_seat_eligible(self):
        result, _ = self.exercise('''from gta_use.vehicles import acquire_vehicle
def run(ctx):
    acquire_vehicle(ctx, dict(handle=20,model=45), passenger=True, companion=dict(handle=31,model=47))
''', [scene()], {"vehicle-candidates": [[access(20, passenger=False, occupant=31, occupantModel=99, occupantAlive=True)]]})
        self.assertEqual(1, result.returncode)
        self.assertFalse(self.commands("enter-vehicle"))

    def test_accepted_entry_without_observed_occupancy_times_out_and_clears_owned_entry(self):
        result, outcome = self.exercise('''from gta_use.vehicles import acquire_vehicle
def run(ctx):
    acquire_vehicle(ctx, dict(handle=20,model=45), entry_timeout=.2, check_interval=.05)
''', [scene()], {"vehicle-candidates": [[access(20)]]})
        self.assertEqual(1, result.returncode)
        self.assertTrue(outcome["paused"])
        self.assertEqual(1, len(self.commands("enter-vehicle")))
        self.assertEqual(1, len(self.commands("clear-entry")))

    def test_lost_entry_receipt_is_neither_retried_nor_followed_by_clear(self):
        result, outcome = self.exercise('''from gta_use.vehicles import acquire_vehicle
def run(ctx):
    acquire_vehicle(ctx, entry_timeout=.2)
''', [scene()], {"vehicle-candidates": [[access(20)]]}, lost="enter-vehicle")
        self.assertEqual(1, result.returncode)
        self.assertTrue(outcome["paused"])
        self.assertEqual(1, len(self.commands("enter-vehicle")))
        self.assertFalse(self.commands("clear-entry"))

    def test_exit_waits_for_on_foot_observation_and_forbids_automatic_door_warp(self):
        result, outcome = self.exercise('''from gta_use.vehicles import leave_vehicle
def run(ctx):
    state=leave_vehicle(ctx,check_interval=.05)
    ctx.checkpoint("on-foot",player=state["sections"]["player"])
''', [scene(car=20), scene(car=20), scene()])
        self.assertEqual(0, result.returncode, result.stderr)
        self.assertFalse(outcome["evidence"]["player"]["inVehicle"])
        self.assertIn("DontDefaultWarpIfDoorBlocked", self.commands("leave-vehicle")[0])

    def test_cover_entry_in_progress_waits_without_toggling_out_of_cover(self):
        result, outcome = self.exercise('''from gta_use.combat import take_cover
def run(ctx):
    state=take_cover(ctx,check_interval=.05)
    ctx.checkpoint("settled-cover",player=state["sections"]["player"])
''', [scene(entering=True), scene(cover=True, entering=True), scene(cover=True)])
        self.assertEqual(0, result.returncode, result.stderr)
        self.assertFalse(outcome["evidence"]["player"]["goingIntoCover"])
        self.assertFalse([s for r in self.game.requests for s in r.get("steps", []) if s["op"] == "controls"])

    def test_missing_cover_stops_after_one_tap_without_repeated_toggling(self):
        result, outcome = self.exercise('''from gta_use.combat import take_cover
def run(ctx):
    take_cover(ctx,timeout=.2,check_interval=.05)
''', [scene()])
        self.assertEqual(1, result.returncode)
        taps = [s for r in self.game.requests for s in r.get("steps", []) if s["op"] == "controls"]
        self.assertEqual(1, len(taps))
        self.assertEqual("Cover", taps[0]["inputs"][0]["control"])
        self.assertTrue(outcome["paused"])

    def fire(self, query=None, hp=None, observations=None):
        return self.exercise('''from gta_use.combat import fire_from_cover
def run(ctx):
    result=fire_from_cover(ctx,dict(handle=30,model=46),allies=[dict(handle=31,model=47)])
    ctx.checkpoint("burst-observed",**result)
''', observations or [scene(cover=True), scene(cover=True, ammo=12)],
            {"target-health": hp if hp is not None else [150, 90], "muzzle-clear": [muzzle() if query is None else query]})

    def test_settled_cover_peeks_before_firing_and_reports_observed_damage_and_ammo(self):
        result, outcome = self.fire()
        self.assertEqual(0, result.returncode, result.stderr)
        evidence = outcome["evidence"]
        self.assertEqual([False, True], [s["shoot"] for s in self.aims()])
        self.assertTrue(self.aims()[-1]["requireAimTarget"])
        geometry = self.commands("muzzle-clear")[0]
        self.assertIn("Hash.GET_PED_BONE_COORDS,t.Handle,31086,0f,0f,0f", geometry)
        self.assertIn('Hash.GET_ENTITY_BONE_INDEX_BY_NAME,w,"gun_muzzle"', geometry)
        self.assertEqual((150, 90, 15, 12), (evidence["before"], evidence["after"], evidence["ammoBefore"], evidence["ammoAfter"]))
        self.assertTrue(evidence["damaged"])

    def test_companion_crossing_muzzle_line_withholds_fire_after_peek(self):
        blocked = muzzle()
        blocked["allies"][0]["position"]["x"] = .4
        result, outcome = self.fire(query=blocked)
        self.assertEqual(0, result.returncode, result.stderr)
        self.assertEqual("withheld", outcome["evidence"]["outcome"])
        self.assertEqual([False], [s["shoot"] for s in self.aims()])

    def test_cover_or_other_object_blocking_muzzle_withholds_fire(self):
        result, outcome = self.fire(query=muzzle(hitEntity=99))
        self.assertEqual(0, result.returncode, result.stderr)
        self.assertEqual("withheld", outcome["evidence"]["outcome"])
        self.assertEqual([False], [s["shoot"] for s in self.aims()])

    def test_recycled_target_after_firing_is_unknown_not_a_confirmed_kill(self):
        result, outcome = self.fire(hp=[150, None])
        self.assertEqual(0, result.returncode, result.stderr)
        self.assertIsNone(outcome["evidence"]["after"])
        self.assertFalse(outcome["evidence"]["damaged"])

    def test_empty_clip_returns_not_ready_without_aiming_or_spawning_ammo(self):
        result, outcome = self.fire(observations=[scene(cover=True, ammo=0)])
        self.assertEqual(0, result.returncode, result.stderr)
        self.assertEqual("not-ready", outcome["evidence"]["outcome"])
        self.assertFalse(self.aims())

    def vehicle_fire(self, observations=None, target=None, hp=60, lost=None):
        return self.exercise('''from gta_use.combat import fire_from_vehicle
def run(ctx):
    result=fire_from_vehicle(ctx,dict(handle=30,model=46))
    ctx.checkpoint("vehicle-burst-observed",**result)
''', observations or [scene(car=20), scene(car=20, ammo=12)],
            {"vehicle-fire-target": [target or dict(health=150,distance=20)], "target-health": [hp]}, lost=lost)

    def test_vehicle_burst_measures_ammo_and_damage_without_replacing_driving_task(self):
        result, outcome = self.vehicle_fire()
        self.assertEqual(0, result.returncode, result.stderr)
        evidence = outcome["evidence"]
        self.assertEqual((150, 60, 3), (evidence["before"], evidence["after"], evidence["ammoSpent"]))
        self.assertTrue(evidence["damaged"])
        self.assertEqual([dict(op="aim",entity=30,bone=31086,ms=1100,warmupMs=200,shoot=True,requireAimTarget=True)], self.aims())
        steps = [s for r in self.game.requests for s in r.get("steps", [])]
        self.assertFalse([s for s in steps if s["op"] == "native"], "Firing must not clear or replace the driving task.")

    def test_vehicle_aim_acceptance_without_ammo_or_damage_is_not_reported_as_effect(self):
        result, outcome = self.vehicle_fire(observations=[scene(car=20)], hp=150)
        self.assertEqual(0, result.returncode, result.stderr)
        self.assertEqual(0, outcome["evidence"]["ammoSpent"])
        self.assertFalse(outcome["evidence"]["damaged"])

    def test_distant_target_is_withheld_even_if_previous_pursuit_observed_it_nearby(self):
        result, outcome = self.vehicle_fire(target=dict(health=150,distance=70))
        self.assertEqual(0, result.returncode, result.stderr)
        self.assertEqual("withheld", outcome["evidence"]["outcome"])
        self.assertFalse(self.aims())

    def test_active_phone_call_withholds_vehicle_fire_without_cancelling_mission_call(self):
        calling = scene(car=20)
        calling["session"]["phoneCall"] = True
        result, outcome = self.vehicle_fire(observations=[calling])
        self.assertEqual(0, result.returncode, result.stderr)
        self.assertEqual("not-ready", outcome["evidence"]["outcome"])
        self.assertFalse(self.aims())
        self.assertFalse(self.commands("vehicle-fire-target"))

    def test_weapon_switch_during_vehicle_burst_does_not_count_different_ammo_as_shots(self):
        changed = scene(car=20,ammo=0)
        changed["sections"]["weapons"]["hash"] = 2725352035
        result, outcome = self.vehicle_fire(observations=[scene(car=20), changed], hp=None)
        self.assertEqual(0, result.returncode, result.stderr)
        self.assertIsNone(outcome["evidence"]["ammoSpent"])
        self.assertIsNone(outcome["evidence"]["after"])
        self.assertFalse(outcome["evidence"]["damaged"])

    def test_lost_vehicle_fire_receipt_stops_without_replaying_the_burst(self):
        result, outcome = self.vehicle_fire(lost="aim")
        self.assertEqual(1, result.returncode)
        self.assertTrue(outcome["paused"])
        self.assertEqual(1, len(self.aims()))
        self.assertFalse(self.commands("target-health"))

    def test_numbered_screenshots_are_retained_with_state_inside_run_directory(self):
        result, outcome = self.exercise('''def run(ctx):
    state=ctx.check(sections=["player"])
    first=ctx.capture("before",state=state)
    second=ctx.capture("after",state=state)
    ctx.checkpoint("evidence-retained",first=first,second=second)
''', [scene()])
        self.assertEqual(0, result.returncode, result.stderr)
        self.assertEqual(b"retained external frame", (self.root / "run/001-before.png").read_bytes())
        self.assertTrue((self.root / "run/002-after.png").samefile(outcome["evidence"]["second"]))
        events = [json.loads(s) for s in (self.root / "run/events.jsonl").read_text().splitlines()]
        self.assertEqual(2, len([e for e in events if e["event"] == "screenshot" and e["state"]["gameTime"] == 100]))

    def test_roll_recovery_uses_steering_until_observed_back_on_wheels(self):
        rolled = scene(car=20)
        rolled["sections"]["vehicle"].update(onAllWheels=False, upsideDown=True)
        rolled["sections"]["vehicle"]["entity"]["rotation"]["y"] = 110
        result, outcome = self.exercise('''from gta_use.vehicles import recover_upright
def run(ctx):
    state=recover_upright(ctx,attempts=2)
    ctx.checkpoint("on-wheels",vehicle=state["sections"]["vehicle"])
''', [rolled, scene(car=20)])
        self.assertEqual(0, result.returncode, result.stderr)
        self.assertTrue(outcome["evidence"]["vehicle"]["onAllWheels"])
        steps = [s for r in self.game.requests for s in r.get("steps", []) if s["op"] != "pause"]
        self.assertEqual([dict(op="resume"), dict(op="controls", ms=500, inputs=[dict(control="VehicleMoveLeftRight",value=1)])], steps)
