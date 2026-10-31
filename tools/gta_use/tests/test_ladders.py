"""Observed ladder traversal through the shipped SDK, supervisor and Rust pipe."""
from collections import deque
from pathlib import Path
import tempfile
import unittest

from observed_game import ObservedGame
from test_navigation import observation
import test_runner as runner


def sample(z=0, attached=False, control=True, handle=10, health=175, packed=False, **flags):
    state = observation(z=z, **flags)
    state['sections']['player']['entity'].update(handle=handle, health=health)
    state['session']['playerControl'] = control
    if packed:
        state['sections']['player']['climbingLadder'] = attached
    return state, attached


class LadderWorld(ObservedGame):
    def __init__(self, root, samples, initial=None, lost_mount=False):
        super().__init__(root)
        self.current = initial or sample()
        self.samples = samples
        self.pending = deque()
        self.lost_mount = lost_mount

    def request(self, envelope):
        self.requests.append(envelope)
        result = {}
        if envelope['op'] == 'state':
            if self.pending:
                self.current = self.pending.popleft()
            result = self.current[0]
        elif envelope['op'] == 'action':
            step = envelope['steps'][0]
            value = None
            if step['op'] == 'native':
                if step['name'] == 'TASK_CLIMB_LADDER':
                    if self.lost_mount:
                        return dict(id=envelope['id'], session=self.session, ok=False, uncertain=True, error='Mount receipt lost')
                    self.pending = deque(self.samples)
                    value = True
                elif step['name'] == 'GET_IS_TASK_ACTIVE':
                    value = self.current[1]
            result = dict(completed=True, value=value)
        return dict(id=envelope['id'], session=self.session, ok=True, result=result)


class LadderScenarios(unittest.TestCase):
    start_server = runner.RunnerScenarios.start_server
    tearDown = runner.RunnerScenarios.tearDown
    run_script = runner.RunnerScenarios.run_script

    def setUp(self):
        self.storage = tempfile.TemporaryDirectory(prefix='GTAVMods-ladder-test-')
        self.root = Path(self.storage.name)

    def exercise(self, samples, *, initial=None, lost_mount=False, direction='up', timeout=4, stall=.7):
        self.game = LadderWorld(self.root, samples, initial, lost_mount)
        self.start_server()
        source = f'''from gta_use.navigation import climb_ladder
def run(ctx):
    result = climb_ladder(ctx, {{'x':0,'y':2,'z':0}}, direction={direction!r},
        timeout={timeout}, stall_timeout={stall}, settle_for=.2, check_interval=.05, input_ms=50)
    ctx.checkpoint('ladder-outcome', **result)
'''
        return self.run_script(source, '--timeout', '8', '--stuck-timeout', '6', '--check-timeout', '5')

    def steps(self):
        return [s for r in self.game.requests if r['op'] == 'action' for s in r['steps']]

    def assert_stopped(self, result, outcome):
        self.assertEqual(1, result.returncode)
        self.assertTrue(outcome['paused'])
        self.assertNotIn('checkpoint', outcome)
        self.assertEqual(1, sum(s['op']=='native' and s['name']=='TASK_CLIMB_LADDER' for s in self.steps()))
        self.assertFalse(any(s['op']=='native' and s['name']=='CLEAR_PED_TASKS' for s in self.steps()))

    def test_ascent_holds_up_and_waits_past_false_exit_height_for_stable_landing(self):
        result, outcome = self.exercise([sample(.4, True), sample(1.5, True), sample(3), sample(2.5), sample(2)])
        self.assertEqual(0, result.returncode, result.stderr)
        self.assertEqual('arrived', outcome['evidence']['outcome'])
        self.assertEqual(2, outcome['evidence']['position']['z'])
        controls = [s for s in self.steps() if s['op']=='controls']
        self.assertGreaterEqual(len(controls), 2)
        self.assertTrue(all(s['ms']==50 and s['inputs']==[dict(control='MoveUpDown',value=-1.0)] for s in controls))
        self.assertTrue(outcome['paused'])

    def test_state_pack_attachment_avoids_extra_native_queries_or_csharp_compilation(self):
        result, outcome = self.exercise([sample(.4, True, packed=True), sample(2, packed=True)], initial=sample(packed=True))
        self.assertEqual(0, result.returncode, result.stderr)
        self.assertEqual('arrived', outcome['evidence']['outcome'])
        self.assertFalse(any(s['op']=='command' or (s['op']=='native' and s['name']=='GET_IS_TASK_ACTIVE') for s in self.steps()))

    def test_descent_uses_down_input_and_signed_height_progress(self):
        result, outcome = self.exercise([sample(4.5, True), sample(3, True), sample(1.8)], initial=sample(5), direction='down')
        self.assertEqual(0, result.returncode, result.stderr)
        self.assertAlmostEqual(-3.2, outcome['evidence']['heightChange'])
        self.assertTrue(all(s['inputs'][0]['value']==1.0 for s in self.steps() if s['op']=='controls'))

    def test_non_ladder_wall_never_fabricates_mount_or_sends_walking_input(self):
        result, outcome = self.exercise([sample()], stall=.3)
        self.assert_stopped(result, outcome)
        self.assertFalse(any(s['op']=='controls' for s in self.steps()))

    def test_attached_but_stalled_player_stops_bounded_inputs_without_replaying_mount(self):
        result, outcome = self.exercise([sample(0, True)], stall=.3)
        self.assert_stopped(result, outcome)
        self.assertTrue(any(s['op']=='controls' for s in self.steps()))

    def test_lost_mount_receipt_is_not_replayed_or_interpreted_as_attachment(self):
        result, outcome = self.exercise([], lost_mount=True)
        self.assert_stopped(result, outcome)
        self.assertFalse(any(s['op']=='controls' for s in self.steps()))

    def test_character_change_during_ladder_stops_input_to_replacement(self):
        result, outcome = self.exercise([sample(.4, True), sample(2, True, handle=11)])
        self.assert_stopped(result, outcome)
        self.assertEqual(1, sum(s['op']=='controls' for s in self.steps()))

    def test_low_health_interrupts_ladder_before_another_input(self):
        result, outcome = self.exercise([sample(.4, True), sample(2, True, health=80)])
        self.assert_stopped(result, outcome)
        self.assertEqual(1, sum(s['op']=='controls' for s in self.steps()))

    def test_control_takeover_returns_transition_and_preserves_mission_task(self):
        result, outcome = self.exercise([sample(.4, True), sample(2, True, control=False)])
        self.assertEqual(0, result.returncode, result.stderr)
        self.assertEqual('transition', outcome['evidence']['outcome'])
        self.assertEqual(1, sum(s['op']=='controls' for s in self.steps()))
        self.assertFalse(any(s['op']=='native' and s['name']=='CLEAR_PED_TASKS' for s in self.steps()))

    def test_dismount_without_required_height_change_does_not_claim_arrival(self):
        result, outcome = self.exercise([sample(.1, True), sample(.2)], stall=.3)
        self.assert_stopped(result, outcome)

    def test_overall_deadline_bounds_a_ladder_that_has_not_exited(self):
        result, outcome = self.exercise([sample(.2, True), sample(.5, True), sample(.8, True)], timeout=1.2, stall=5)
        self.assert_stopped(result, outcome)
        self.assertIn('overall deadline', (self.root/'run/stderr.log').read_text())
