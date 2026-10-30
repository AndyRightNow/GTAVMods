"""Supervised companion/pursuit following through real service and explicit world observations."""
from pathlib import Path
import tempfile
import unittest
from test_driving import DrivingWorld
import test_runner as runner


def scene(y=0, health=175, **session):
    return dict(gameTime=100,session=dict(playerControl=True,cutscenePlaying=False,pauseMenu=False,missionActive=True,**session),
                sections=dict(player=dict(entity=dict(handle=10,model=42,health=health,dead=False,position=dict(x=0,y=y,z=0)),
                                          inVehicle=False,swimming=False)))


class FollowingWorld(DrivingWorld):
    def request(self,envelope):
        if self.lost and envelope['op']=='action' and envelope['steps'][0]['op']=='command' and 'gta-use:foot-follow' in envelope['steps'][0]['code']:
            self.requests.append(envelope)
            return dict(id=envelope['id'],session=self.session,ok=False,uncertain=True,error='Lost follow task receipt')
        return super().request(envelope)


class FootFollowingScenarios(unittest.TestCase):
    start_server=runner.RunnerScenarios.start_server
    tearDown=runner.RunnerScenarios.tearDown
    run_script=runner.RunnerScenarios.run_script

    def setUp(self):
        self.storage=tempfile.TemporaryDirectory(prefix='GTAVMods-follow-test-')
        self.root=Path(self.storage.name)

    def exercise(self,observations,targets=None,lost=False,code=None):
        self.game=FollowingWorld(self.root,observations,targets or [dict(y=30,health=150,vehicle=0)],lost)
        self.start_server()
        return self.run_script(code or '''from gta_use.navigation import follow_on_foot
def run(ctx):
    result=follow_on_foot(ctx,dict(handle=30,model=46),timeout=2,stall_timeout=.2,check_interval=.05)
    ctx.checkpoint('following-observed',**result)
''','--timeout','8','--stuck-timeout','5','--check-timeout','4')

    def tasks(self):
        return [s for r in self.game.requests for s in r.get('steps',[]) if s['op']=='command' and 'gta-use:foot-follow' in s['code']]

    def clears(self):
        return [s for r in self.game.requests for s in r.get('steps',[]) if s['op']=='native' and s['name']=='CLEAR_PED_TASKS']

    def test_companion_route_uses_one_persistent_task_until_observed_vehicle_boarding(self):
        result,outcome=self.exercise([scene(),scene(8),scene(16)],targets=[dict(y=10,health=150,vehicle=0),dict(y=20,health=150,vehicle=0),dict(y=22,health=150,vehicle=40)])
        self.assertEqual(0,result.returncode,result.stderr)
        self.assertEqual('target-boarded',outcome['evidence']['outcome'])
        self.assertEqual(40,outcome['evidence']['target']['vehicle'])
        self.assertEqual(1,len(self.tasks()))
        self.assertIn('3.0f,-1,2.0f,true',self.tasks()[0]['code'],'Native timeout must not enable timeout warping')
        self.assertEqual([10],self.clears()[0]['args'])
        self.assertTrue(outcome['paused'])

    def test_blocked_companion_route_stops_without_repeated_tasks_or_false_arrival(self):
        result,outcome=self.exercise([scene()])
        self.assertEqual(1,result.returncode)
        self.assertTrue(outcome['paused'])
        self.assertEqual(1,len(self.tasks()))
        self.assertEqual(1,len(self.clears()))
        self.assertIn('foot-follow-stalled',(self.root/'run/events.jsonl').read_text())

    def test_contact_cutscene_preserves_mission_owned_tasks(self):
        transition=scene(8);transition['session'].update(playerControl=False,cutscenePlaying=True)
        result,outcome=self.exercise([scene(),transition])
        self.assertEqual(0,result.returncode,result.stderr)
        self.assertEqual('transition',outcome['evidence']['outcome'])
        self.assertEqual(1,len(self.tasks()))
        self.assertEqual([],self.clears())

    def test_lost_follow_receipt_is_not_replayed_or_cancelled_as_known_task(self):
        result,outcome=self.exercise([scene()],lost=True)
        self.assertEqual(1,result.returncode)
        self.assertTrue(outcome['paused'])
        self.assertEqual(1,len(self.tasks()))
        self.assertEqual([],self.clears())

    def test_companion_disappearance_stops_route_instead_of_claiming_arrival(self):
        result,outcome=self.exercise([scene(),scene(4)],targets=[dict(y=20,health=150,vehicle=0),dict(missing=True)])
        self.assertEqual(1,result.returncode)
        self.assertTrue(outcome['paused'])
        self.assertEqual(1,len(self.clears()))
        self.assertIn('target changed or disappeared',(self.root/'run/stderr.log').read_text())

    def test_player_entering_mission_vehicle_preserves_new_entry_task(self):
        entering=scene(8);entering['sections']['player']['inVehicle']=True
        result,outcome=self.exercise([scene(),entering])
        self.assertEqual(1,result.returncode)
        self.assertTrue(outcome['paused'])
        self.assertEqual([],self.clears())

    def test_health_loss_stops_following_before_another_movement_request(self):
        result,outcome=self.exercise([scene(),scene(4,health=80)])
        self.assertEqual(1,result.returncode)
        self.assertTrue(outcome['paused'])
        self.assertEqual(1,len(self.tasks()))
        self.assertEqual(1,len(self.clears()))

    def test_policy_can_handoff_at_an_observed_objective_without_waiting_for_stall(self):
        result,outcome=self.exercise([scene(),scene(8)],targets=[dict(y=20,health=150,vehicle=0),dict(y=10,health=150,vehicle=0)],code='''from gta_use.navigation import follow_on_foot
def run(ctx):
    result=follow_on_foot(ctx,dict(handle=30,model=46),timeout=2,check_interval=.05,on_tick=lambda ctx,state,target:target['distance']<3)
    ctx.checkpoint('objective-needs-inspection',**result)
''')
        self.assertEqual(0,result.returncode,result.stderr)
        self.assertEqual('policy',outcome['evidence']['outcome'])
        self.assertEqual(2,outcome['evidence']['target']['distance'])
        self.assertEqual(1,len(self.tasks()))
        self.assertEqual(1,len(self.clears()))
