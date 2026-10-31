"""Observed boat trips through the shipped Rust transport and supervised runner."""
import json
from pathlib import Path
import tempfile
import unittest

from test_driving import DrivingWorld, scene
import test_runner as runner


class BoatWorld(DrivingWorld):
    def __init__(self, root, observations, *, boat=True, lost=False, lost_controls=False):
        super().__init__(root, observations)
        self.boat, self.lost_boat_receipt = boat, lost
        self.lost_controls = lost_controls

    def request(self, envelope):
        if envelope['op'] == 'action':
            step = envelope['steps'][0]
            if step['op'] == 'controls.update' and self.lost_controls:
                self.requests.append(envelope)
                return dict(id=envelope['id'], session=self.session, ok=False,
                            uncertain=True, error='Lost steering receipt')
            if step['op'] == 'native' and step['name'] == 'IS_THIS_MODEL_A_BOAT':
                self.requests.append(envelope)
                return dict(id=envelope['id'], session=self.session, ok=True,
                            result=dict(completed=True, value=self.boat))
            if step['op'] == 'native' and step['name'] == 'TASK_BOAT_MISSION' and self.lost_boat_receipt:
                self.requests.append(envelope)
                return dict(id=envelope['id'], session=self.session, ok=False,
                            uncertain=True, error='Lost boat navigation receipt')
        return super().request(envelope)


class BoatScenarios(unittest.TestCase):
    start_server = runner.RunnerScenarios.start_server
    tearDown = runner.RunnerScenarios.tearDown
    run_script = runner.RunnerScenarios.run_script

    def setUp(self):
        self.storage = tempfile.TemporaryDirectory(prefix='GTAVMods-boats-test-')
        self.root = Path(self.storage.name)

    def exercise(self, observations, *, code=None, boat=True, lost=False, lost_controls=False):
        self.game = BoatWorld(self.root, observations, boat=boat, lost=lost, lost_controls=lost_controls)
        self.start_server()
        return self.run_script(code or '''from gta_use.driving import boat_to
def run(ctx):
    result = boat_to(ctx, dict(x=0,y=100,z=0), radius=8,
                     timeout=2, stall_timeout=.25, check_interval=.05)
    ctx.checkpoint('boat-result', **result)
''', '--timeout', '8', '--stuck-timeout', '5', '--check-timeout', '4')

    def natives(self):
        return [s for r in self.game.requests for s in r.get('steps', []) if s['op'] == 'native']

    def events(self):
        return [json.loads(s) for s in (self.root/'run/events.jsonl').read_text().splitlines()]

    def test_water_delivery_passes_channel_gate_without_waiting_and_requires_stopped_final_arrival(self):
        result, outcome = self.exercise([scene(), scene(), scene(42,20), scene(94,12), scene(96,0)], code='''from gta_use.driving import boat_to
def run(ctx):
    result = boat_to(ctx, dict(x=0,y=100,z=0), waypoints=[dict(x=0,y=50,z=0)],
                     waypoint_radius=10, radius=8, speed=24, timeout=2, check_interval=.05)
    ctx.checkpoint('water-delivery', **result)
''')
        self.assertEqual(0, result.returncode, result.stderr)
        self.assertEqual('arrived', outcome['evidence']['outcome'])
        self.assertEqual(0, outcome['evidence']['state']['sections']['vehicle']['speed'])
        tasks=[n for n in self.natives() if n['name']=='TASK_BOAT_MISSION']
        self.assertEqual(2,len(tasks), 'Submit each water leg once; do not restart every observation.')
        self.assertEqual([50,100],[t['args'][5]['value'] for t in tasks])
        for task in tasks:
            self.assertEqual([10,20,0,0], task['args'][:4])
            self.assertEqual(4, task['args'][7], 'Normal GoTo mission, not fleeing or forced beaching.')
            self.assertEqual(24, task['args'][8]['value'])
            self.assertEqual(7, task['args'][11], 'Preserve normal shore avoidance flags.')
        self.assertEqual('CLEAR_PED_TASKS',self.natives()[-1]['name'])
        self.assertTrue(outcome['paused'])

    def test_a_land_vehicle_is_rejected_before_boat_task_submission(self):
        result, outcome=self.exercise([scene()],boat=False)
        self.assertEqual(1,result.returncode)
        self.assertEqual(['IS_THIS_MODEL_A_BOAT'],[n['name'] for n in self.natives()])
        self.assertTrue(outcome['paused'])

    def test_a_wall_stall_preserves_evidence_and_cancels_only_the_owned_route(self):
        result,outcome=self.exercise([scene()])
        self.assertEqual(1,result.returncode)
        self.assertTrue(outcome['paused'])
        self.assertTrue(any(e['event']=='boat-stalled' for e in self.events()))
        self.assertFalse(any(e['event']=='progress' for e in self.events()))
        self.assertEqual(1,sum(n['name']=='TASK_BOAT_MISSION' for n in self.natives()))
        self.assertEqual('CLEAR_PED_TASKS',self.natives()[-1]['name'])

    def test_loss_of_route_receipt_never_replays_or_clears_unknown_movement(self):
        result,outcome=self.exercise([scene()],lost=True)
        self.assertEqual(1,result.returncode)
        self.assertTrue(outcome['paused'])
        self.assertEqual(['IS_THIS_MODEL_A_BOAT','TASK_BOAT_MISSION'],[n['name'] for n in self.natives()])

    def test_mission_takeover_preserves_the_new_scene_tasks(self):
        takeover=scene(70);takeover['session'].update(playerControl=False,cutscenePlaying=True)
        result,outcome=self.exercise([scene(),scene(),takeover])
        self.assertEqual(0,result.returncode,result.stderr)
        self.assertEqual('transition',outcome['evidence']['outcome'])
        self.assertFalse(any(n['name']=='CLEAR_PED_TASKS' for n in self.natives()))

    def test_changed_boat_identity_does_not_clear_the_new_vehicle_task(self):
        changed=scene(40);changed['sections']['vehicle']['entity']['model']=99
        result,outcome=self.exercise([scene(),scene(),changed])
        self.assertEqual(1,result.returncode)
        self.assertTrue(outcome['paused'])
        self.assertFalse(any(n['name']=='CLEAR_PED_TASKS' for n in self.natives()))

    def test_low_health_ends_route_before_more_exposed_travel(self):
        hurt=scene(30);hurt['sections']['player']['entity']['health']=80
        result,outcome=self.exercise([scene(),scene(),hurt])
        self.assertEqual(1,result.returncode)
        self.assertTrue(outcome['paused'])
        self.assertEqual('CLEAR_PED_TASKS',self.natives()[-1]['name'])

    def test_objective_callback_can_end_a_passage_without_waiting_for_final_stop(self):
        result,outcome=self.exercise([scene(),scene(),scene(40,24)],code='''from gta_use.driving import boat_to
def run(ctx):
    result=boat_to(ctx,dict(x=0,y=100,z=0),timeout=2,check_interval=.05,
        on_tick=lambda ctx,s:s['sections']['player']['entity']['position']['y']>=40)
    ctx.checkpoint('entered-next-segment',**result)
''')
        self.assertEqual(0,result.returncode,result.stderr)
        self.assertEqual('policy',outcome['evidence']['outcome'])
        self.assertEqual(24,outcome['evidence']['state']['sections']['vehicle']['speed'])
        self.assertEqual('CLEAR_PED_TASKS',self.natives()[-1]['name'])

    def test_repeated_circle_motion_does_not_reset_route_progress_deadline(self):
        observations=[scene(),scene()]+[scene(y,10) for y in [10,0]*15]
        result,outcome=self.exercise(observations,code='''from gta_use.driving import boat_to
def run(ctx):
    boat_to(ctx,dict(x=0,y=100,z=0),timeout=2,stall_timeout=1,
            route_progress_timeout=.2,check_interval=.05)
''')
        self.assertEqual(1,result.returncode)
        self.assertTrue(outcome['paused'])
        self.assertIn('repeated its route',(self.root/'run/stderr.log').read_text())

    def test_invalid_speed_is_rejected_before_any_game_mutation(self):
        result,_=self.exercise([scene()],code='''from gta_use.driving import boat_to
def run(ctx):
    boat_to(ctx,dict(x=0,y=100,z=0),speed=float('nan'))
''')
        self.assertEqual(1,result.returncode)
        self.assertFalse(self.natives())



class WaterSteeringScenarios(unittest.TestCase):
    setUp = BoatScenarios.setUp
    start_server = BoatScenarios.start_server
    tearDown = BoatScenarios.tearDown
    run_script = BoatScenarios.run_script
    exercise = BoatScenarios.exercise
    natives = BoatScenarios.natives
    events = BoatScenarios.events
    def steering(self, observations, *, parameters="", boat=True, lost_controls=False):
        return self.exercise(observations, boat=boat, lost_controls=lost_controls, code="""from gta_use.driving import steer_water_route
def run(ctx):
    result=steer_water_route(ctx,[dict(x=100,y=0,z=0),dict(x=100,y=100,z=0)],
        timeout=2,stall_timeout=.25,check_interval=.05%s)
    ctx.checkpoint('route',**result)
""" % parameters)

    def controls(self):
        return [s for r in self.game.requests for s in r.get('steps',[]) if s['op']=='controls.update']

    def test_observed_water_gates_require_turning_and_release_inputs_while_still_moving(self):
        result,outcome=self.steering([steering_scene(),steering_scene(),steering_scene(100,0,-90,15),steering_scene(100,98,0,18)])
        self.assertEqual(0,result.returncode,result.stderr)
        self.assertEqual('passed',outcome['evidence']['outcome'])
        self.assertEqual(18,outcome['evidence']['state']['sections']['vehicle']['speed'])
        controls=self.controls()
        self.assertEqual(2,len(controls))
        self.assertEqual(1,controls[0]['inputs'][1]['value'], 'East from north requires right steering.')
        self.assertEqual(-1,controls[1]['inputs'][1]['value'], 'North from east requires left steering.')
        self.assertEqual(0,controls[1]['inputs'][0]['value'], 'Coast above the turn speed.')
        self.assertEqual(1,sum(n['name']=='CLEAR_PED_TASKS' for n in self.natives()))
        self.assertFalse(any(n['name']=='TASK_BOAT_MISSION' for n in self.natives()))
        self.assertEqual(1,sum(s['op']=='controls.release' for r in self.game.requests for s in r.get('steps',[])))

    def test_pinned_hull_stops_without_resetting_the_stall_deadline(self):
        result,outcome=self.steering([steering_scene()])
        self.assertEqual(1,result.returncode)
        self.assertTrue(outcome['paused'])
        self.assertTrue(any('stalled' in e.get('error','') for e in self.events()))
        self.assertEqual(1,sum(n['name']=='CLEAR_PED_TASKS' for n in self.natives()))

    def test_mission_takeover_releases_held_controls_without_clearing_mission_tasks(self):
        transition=steering_scene(20,0,-90,12);transition['session']['playerControl']=False
        result,outcome=self.steering([steering_scene(),steering_scene(),transition])
        self.assertEqual(0,result.returncode,result.stderr)
        self.assertEqual('transition',outcome['evidence']['outcome'])
        self.assertEqual(1,sum(n['name']=='CLEAR_PED_TASKS' for n in self.natives()))
        self.assertEqual(1,sum(s['op']=='controls.release' for r in self.game.requests for s in r.get('steps',[])))

    def test_unknown_steering_receipt_never_replays_or_sends_cleanup(self):
        result,outcome=self.steering([steering_scene()],lost_controls=True)
        self.assertEqual(1,result.returncode)
        self.assertEqual(1,len(self.controls()))
        self.assertFalse(any(s['op']=='controls.release' for r in self.game.requests for s in r.get('steps',[])))
        self.assertTrue(outcome['paused'])

    def test_driver_health_drop_releases_controls_and_preserves_failure(self):
        hurt=steering_scene(20,0,-90,10);hurt['sections']['player']['entity']['health']=80
        result,outcome=self.steering([steering_scene(),steering_scene(),hurt])
        self.assertEqual(1,result.returncode)
        self.assertEqual(1,len(self.controls()))
        self.assertEqual(1,sum(s['op']=='controls.release' for r in self.game.requests for s in r.get('steps',[])))

    def test_land_vehicle_is_not_given_water_controls(self):
        result,outcome=self.steering([steering_scene()],boat=False)
        self.assertEqual(1,result.returncode)
        self.assertFalse(self.controls())
        self.assertFalse(any(n['name']=='CLEAR_PED_TASKS' for n in self.natives()))


def steering_scene(x=0,y=0,yaw=0,speed=0):
    result=scene(y,speed)
    result['sections']['player']['entity']['position']['x']=x
    result['sections']['vehicle']['entity']['rotation']=dict(x=0,y=0,z=yaw)
    return result


if __name__ == '__main__':
    unittest.main()
