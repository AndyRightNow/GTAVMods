"""One controller, one uninterrupted Repossession attempt; normal Story gameplay.

Route anchors come from the previous recorded live run and are checked against
current mission/companion/vehicle observations. No checkpoints until the ending.
No teleport, health, ammunition, damage, locks, mission globals or time changes.
"""
import hashlib
import math
from pathlib import Path
import time

from gta_use.driving import drive_to, intercept_vehicle, wait_for_control
from gta_use.vehicles import acquire_vehicle, leave_vehicle, recover_upright
from gta_use.combat import take_cover, fire_from_cover, fire_from_vehicle
from gta_use.navigation import navigate_to
from gta_use.segment import StopSegment

AXES = ('x', 'y', 'z')
LAMAR = 1706635382
GANG = {653210662, -1773333796}
PISTOL = 453432689
FENCE_IN = dict(x=-1128.35, y=-1594.16, z=4.39)
FENCE_IN_APPROACH = dict(x=-1131.6, y=-1589.3, z=4.39)
FENCE_OUT_APPROACH = dict(x=-1130.5, y=-1592.0, z=4.39)
FENCE_OUT = dict(x=-1134.0, y=-1587.5, z=4.39)
TRUCK_COVER = dict(x=-1078.25, y=-1654.4, z=4.35)


def dist(a, b):
    return math.dist([a[k] for k in AXES], [b[k] for k in AXES])


def f(v):
    return dict(type='float', value=float(v))


class Mission:
    def __init__(self, ctx):
        self.ctx, self.g = ctx, ctx.game
        self.started = time.monotonic()
        self.stage = 'preflight'
        self.friend = None
        self.car = None
        self.bike = None
        self.phase_times = {}
        self.last_anchor = None
        self.last_capture = 0
        self.action_count = 0
        self.mission_started = False
        self.ending = False
        self.last_cut = -1
        original = self.g.request
        def audited(op, **args):
            steps = args.get('steps', [])
            if op in ('pause', 'step', 'reload') or any(s.get('op') in ('pause', 'step', 'reload') for s in steps):
                raise RuntimeError('Continuous mission forbids pause/step/reload')
            if op in ('action', 'batch'):
                self.action_count += 1
                self.ctx.log('action', stage=self.stage, op=op, steps=steps)
            return original(op, **args)
        self.g.request = audited
        self.bike_model = self.g.native('GET_HASH_KEY','bagger',returns='int')

    def read(self, full=True):
        opts = dict(sections=['player', 'vehicle', 'weapons', 'navigation'], blipLimit=256)
        if full:
            opts.update(sections=opts['sections']+['nearby'], types=['ped', 'vehicle'],
                        radius=130, limit=128, lineOfSight='on')
        s = self.ctx.check(**opts)
        p = s['sections'].get('player')
        if s['session']['pauseMenu']:
            self.ctx.uncertain('Unexpected menu interrupted continuous play')
        if p:
            pos = p['entity']['position']
            if self.last_anchor is None or dist(pos, self.last_anchor) > 4:
                self.last_anchor = pos
                self.ctx.progress('Observed movement', stage=self.stage, position=pos,
                                  health=p['entity']['health'], gameTime=s['gameTime'])
            if p['entity']['health'] < 103:
                self.ctx.uncertain('Player health is critical')
        if s['session']['cutscenePlaying']:
            cut = s['session']['cutsceneTime']
            if cut > self.last_cut + 1500 or cut < self.last_cut:
                self.ctx.progress('Cutscene advancing', stage=self.stage, time=cut)
                self.last_cut = cut
        if full:
            for n in s['sections']['nearby']['nodes']:
                if n['entity']['model'] == LAMAR and n['ped'] and not n['entity']['dead']:
                    self.friend = n['entity']['handle']
        return s

    def capture(self, label, state=None):
        return self.ctx.capture(label, state=state, allow_unavailable=True,
                                stage=self.stage, elapsed=time.monotonic()-self.started)

    def phase(self, name):
        self.stage = name
        self.phase_times[name] = time.monotonic()-self.started
        s = self.read()
        self.ctx.progress('Stage transition', stage=name, session=s['session'])
        self.capture(name, s)
        return s

    def controls(self, ms, **inputs):
        return self.g.action('controls', ms=ms,
                             inputs=[dict(control=k, value=v) for k,v in inputs.items()])

    def take_cover(self):
        return take_cover(self.ctx, minimum_health=103)

    def clear(self):
        self.g.native('CLEAR_PED_TASKS', self.read(False)['sections']['player']['entity']['handle'])

    def wait(self, seconds):
        end = time.monotonic()+seconds
        while time.monotonic()<end:
            self.read(False)
            time.sleep(.25)

    def blips(self, s):
        return s['sections']['navigation']['blips']

    def nodes(self, s):
        return s['sections'].get('nearby', {}).get('nodes', [])

    def walk(self, goal, radius=1.3, timeout=40, until=None):
        s = self.read()
        p = s['sections']['player']
        if p['inCover']:
            self.controls(180, Cover=1)
        h = p['entity']['handle']
        self.g.native('TASK_FOLLOW_NAV_MESH_TO_COORD', h, *[f(goal[k]) for k in AXES],
                      f(2.5), -1, f(.25), 32, f(40000))
        deadline, anchor, moved = time.monotonic()+timeout, p['entity']['position'], time.monotonic()
        while time.monotonic()<deadline:
            s = self.read()
            if until and until(s):
                if s['session']['playerControl'] and not s['session']['cutscenePlaying']:
                    self.g.native('CLEAR_PED_TASKS', h)
                return s
            if not s['session']['playerControl'] or s['session']['cutscenePlaying']:
                return s
            pos=s['sections']['player']['entity']['position']
            if dist(pos,goal)<=radius:
                self.g.native('CLEAR_PED_TASKS',h)
                return s
            if dist(pos,anchor)>.6:
                anchor,moved=pos,time.monotonic()
            if time.monotonic()-moved>5:
                self.g.native('CLEAR_PED_TASKS',h)
                # General bounded parkour recovery; only local route already selected.
                navigate_to(self.ctx,goal,radius=radius,timeout=30,max_climbs=2,minimum_health=103)
                return self.read()
            time.sleep(.2)
        self.ctx.uncertain('Local approach timed out: '+self.stage)

    def leave_car(self):
        return leave_vehicle(self.ctx, minimum_health=103)

    def acquire(self, desired=None, passenger=False):
        state = self.read()
        wanted = None
        if desired is not None:
            wanted = next((n['entity'] for n in self.nodes(state)
                           if n['kind']=='vehicle' and n['entity']['handle']==desired), None)
            current = state['sections'].get('vehicle')
            if wanted is None and current and current['entity']['handle']==desired:
                wanted = current['entity']
            if wanted is None:self.ctx.uncertain('Required vehicle is not currently observed')
        companion = dict(handle=self.friend, model=LAMAR) if self.friend else None
        return acquire_vehicle(self.ctx, wanted, passenger=passenger,
                               companion=companion, minimum_health=103)

    def board_friend(self):
        end=time.monotonic()+35
        while time.monotonic()<end:
            s=self.read();p=s['sections']['player']
            ally=next((n for n in self.nodes(s) if n['entity']['handle']==self.friend),None)
            if ally and ally['ped']['inVehicle'] and ally['ped']['vehicle']==p['vehicle']:
                self.ctx.progress('Lamar boarded',vehicle=p['vehicle']);return
            time.sleep(.3)
        self.ctx.uncertain('Lamar failed to board')

    def drive(self, goal, radius=5, speed=28, until=None):
        # Braking + vehicle/ped/object avoidance; permit using an opposing lane
        # rather than becoming trapped behind a stopped bus at a traffic light.
        style=(786469 & ~262144) | 8 | 16 | 512
        def callback(ctx,s):
            if until:return bool(until(self.read()))
            return False
        for recovery in range(3):
            try:
                return drive_to(self.ctx,goal,radius=radius,speed=speed,arrival_speed=5,
                                timeout=600,stall_timeout=30,minimum_health=103,driving_style=style,
                                on_tick=callback if until else None)
            except StopSegment as exc:
                if 'Driving stalled;' not in str(exc) or recovery==2:raise
                # The helper confirmed a motion stall and cleared its known task.
                # This is a new, observed recovery, never replay of an uncertain receipt.
                s=self.read();p=s['sections']['player'];self.capture('traffic-stall',s)
                self.ctx.log('traffic-recovery',attempt=recovery,position=p['entity']['position'])
                self.g.action('resume')
                # Allow a little reverse space before replanning around the blockage.
                probes=self.ctx.check(sections=['player','probes'],probeHeight=.5,
                    rays=[dict(yaw=180,pitch=0,distance=6)])['sections']['probes']
                if probes and not probes[0]['hit']:
                    self.controls(900,VehicleBrake=1)
                    self.controls(250,VehicleHandbrake=1)
                style |= 256

    def cutscene(self, timeout=180):
        wait_for_control(self.ctx, timeout=timeout, check_interval=.3, stable_for=2)
        return self.read()

    def setup_contact(self):
        s=self.phase('reach-simeon')
        if s['session']['missionActive']:self.ctx.uncertain('Full run must begin before the mission')
        self.g.command('Thor.Thor.Current.SetEnabled(false);')
        self.g.action('resume')
        # Imported save starts by the front door. The task walks out normally.
        pos=s['sections']['player']['entity']['position']
        if not s['sections']['player']['inVehicle'] and dist(pos,dict(x=-14.4,y=-1438.5,z=31.1))<30:
            self.walk(dict(x=-14.2,y=-1446.5,z=30.6),radius=2,timeout=35)
        end=time.monotonic()+100;answered=False
        while time.monotonic()<end:
            s=self.read()
            contacts=[b for b in self.blips(s) if b['sprite']==293 and b['name']=='SimeonFamily']
            if contacts and not s['session']['phoneCall']:break
            if s['session']['phoneCall'] and not answered:
                self.controls(180,PhoneUp=1);self.wait(.8)
                self.controls(200,PhoneSelect=1);answered=True
                self.ctx.log('answered-contact-call')
            time.sleep(.4)
        else:self.ctx.uncertain('Contact never became available')
        contact=contacts[0]['position']
        # The recorded garage entry task cannot path from the front doorstep.
        # Approach its open driveway first; never rely on native timeout warping.
        pos=s['sections']['player']['entity']['position']
        if not s['sections']['player']['inVehicle'] and dist(pos,dict(x=-14.2,y=-1446.5,z=30.6))<5:
            garage=next((n['entity'] for n in self.nodes(s) if n['kind']=='vehicle'
                         and dist(n['entity']['position'],dict(x=-25.16,y=-1427.97,z=30.28))<2),None)
            if garage:self.walk(dict(x=-25,y=-1439,z=30.6),radius=1.5,timeout=30)
        self.car=self.acquire(passenger=True)
        current=self.read()['sections']['player']['entity']['position']
        if -29<current['x']<-19 and -1437<current['y']<-1422:
            # Recorded house garage: car faces its closed north wall; open exit south.
            self.clear()
            for _ in range(8):
                self.controls(700,VehicleBrake=1)
                current=self.read()['sections']['player']['entity']['position']
                if current['y']<-1438:break
            else:self.ctx.uncertain('Garage reverse-out did not reach its observed open driveway')
            self.controls(350,VehicleHandbrake=1)
            self.ctx.progress('Reversed out of house garage',position=current)
        self.drive(contact,radius=18,until=lambda x:dist(x['sections']['player']['entity']['position'],contact)<23 or x['session']['missionActive'])
        s=self.read()
        if not s['session']['missionActive']:
            self.leave_car()
            self.walk(contact,radius=1.5,until=lambda x:x['session']['missionActive'] or x['session']['cutscenePlaying'])
        self.mission_started=True
        self.phase('mission-briefing')
        self.cutscene()

    def beach(self):
        s=self.phase('drive-to-vespucci')
        goals=s['sections']['navigation']['gpsTargets']
        if len(goals)!=1:self.ctx.uncertain('Expected one mission route after briefing')
        self.acquire(desired=self.car['handle'],passenger=True)
        self.board_friend()
        marker=goals[0]['handle']
        self.drive(goals[0]['position'],radius=3,until=lambda x:not any(b['handle']==marker for b in self.blips(x)))
        self.cutscene(timeout=60)
        # The pursuit clock starts later. Park facing the observed north-east
        # escape street now, instead of spending its first seconds on a U-turn.
        self.orient_car(-60)
        self.leave_car()

    def orient_car(self, heading):
        self.clear()
        initial=self.read(False)['sections']['vehicle']['entity']['rotation']['z']
        # Observed narrow street: a left forward / right reverse three-point
        # maneuver avoids either building. Only ordinary driving inputs.
        for target,reverse in ((initial+90,False),(heading,True)):
            end=time.monotonic()+14
            previous=None;changed=time.monotonic()
            while time.monotonic()<end:
                s=self.read(False);v=s['sections']['vehicle']
                if not v or v['driver']!=s['sections']['player']['entity']['handle']:
                    self.ctx.uncertain('Lost driver seat during parking turn')
                actual=v['entity']['rotation']['z']
                error=(target-actual+180)%360-180
                self.ctx.log('parking-turn',heading=actual,error=error,reverse=reverse,position=v['entity']['position'])
                if abs(error)<10:break
                if previous is None or abs((actual-previous+180)%360-180)>5:
                    previous,changed=actual,time.monotonic()
                if time.monotonic()-changed>3:self.ctx.uncertain('Parking turn made no heading progress')
                inputs={'VehicleBrake' if reverse else 'VehicleAccelerate':1,
                        'VehicleMoveLeftRight':1 if reverse else -1}
                inputs['VehicleHandbrake']=0
                inputs['VehicleAccelerate' if reverse else 'VehicleBrake']=0
                self.controls(150,**inputs)
            else:self.ctx.uncertain('Parking turn deadline exceeded')
            for _ in range(10):
                self.controls(250,VehicleHandbrake=1)
                v=self.read(False)['sections']['vehicle']
                if v['speed']<.3:break
            else:self.ctx.uncertain('Parking did not stop the car')
        pos=v['entity']['position']
        probes=self.ctx.check(sections=['player','probes'],probeHeight=.5,
            rays=[dict(yaw=y,pitch=0,distance=8) for y in (-8,0,8)])['sections']['probes']
        self.ctx.log('departure-clearance',vehicle=v,probes=probes)
        lateral=pos['y']-.57735*pos['x']
        if not -926<lateral<-917 or any(r['hit'] and r['distance']<5 for r in probes):
            self.ctx.uncertain('Parking lacks street position or departure clearance')
        self.ctx.progress('Car aligned and stopped on departure street',heading=actual,position=pos)
        self.capture('departure-parking',self.read())

    def follow(self, after_combat=False):
        self.phase('follow-after-combat' if after_combat else 'follow-lamar')
        end=time.monotonic()+180;task=False;last=None;moved=time.monotonic();fence_done=after_combat
        while time.monotonic()<end:
            s=self.read();p=s['sections']['player'];pos=p['entity']['position']
            if s['session']['cutscenePlaying'] or not s['session']['playerControl']:
                self.cutscene();task=False;continue
            garages=[b for b in self.blips(s) if b['color']==66 and b['entity']==0 and b['relative']['distance']<35]
            if garages and not after_combat:self.clear();return
            bikes=[b for b in self.blips(s) if b['entity'] and self.g.native('IS_ENTITY_A_VEHICLE',b['entity'],returns='bool')
                   and self.g.native('GET_ENTITY_MODEL',b['entity'],returns='int')==self.bike_model]
            if bikes and after_combat:
                self.bike=bikes[0]['entity'];self.clear();return
            friend=next((n for n in self.nodes(s) if n['entity']['handle']==self.friend),None)
            if not friend:self.ctx.uncertain('Lamar is unavailable while following')
            if not fence_done and dist(pos,FENCE_IN_APPROACH)<9 and dist(friend['entity']['position'],FENCE_IN)<18:
                self.clear()
                self.walk(FENCE_IN_APPROACH,radius=1.4)
                navigate_to(self.ctx,FENCE_IN,route_mode='direct',radius=1,timeout=35,max_climbs=3,minimum_health=103)
                self.capture('entry-fence-crossed',self.read());fence_done=True;task=False;continue
            if not task:
                if p['inCover']:self.controls(150,Cover=1)
                self.g.command(('var a=Entity.FromHandle(%d) as Ped; if(a==null || !a.Exists() || a.Model.Hash!=%d) throw new Exception("companion identity"); '
                    'P.Task.FollowToOffsetFromEntity(a,new Vector3(0f,-2f,0f),2.5f,-1,2f,true);')%(self.friend,LAMAR))
                task=True
            if last is None or dist(pos,last)>.8:last,moved=pos,time.monotonic()
            if time.monotonic()-moved>15:
                self.ctx.uncertain('Companion route made no progress')
            time.sleep(.3)
        self.ctx.uncertain('Companion route timed out')

    def garages(self):
        self.phase('search-garages')
        end=time.monotonic()+150
        while time.monotonic()<end:
            s=self.read()
            if s['session']['cutscenePlaying'] or not s['session']['playerControl']:
                s=self.cutscene()
            pickups=[b for b in self.blips(s) if b['color']==2 and b['entity']==0 and b['relative']['distance']<10]
            if pickups:
                self.walk(pickups[0]['position'],radius=1.1,until=lambda x:not any(b['handle']==pickups[0]['handle'] for b in self.blips(x)))
                self.g.native('SET_CURRENT_PED_WEAPON',s['sections']['player']['entity']['handle'],PISTOL,True)
                self.wait(.5)
                w=self.read()['sections']['weapons']
                if w['selected']!='Pistol' or not w['totalAmmo']:self.ctx.uncertain('Mission pistol was not collected')
                self.take_cover();return
            choices=[b for b in self.blips(s) if b['color']==66 and b['entity']==0 and b['relative']['distance']<35]
            if choices:
                b=min(choices,key=lambda b:b['relative']['distance'])
                self.walk(b['position'],radius=1,until=lambda x:not any(v['handle']==b['handle'] for v in self.blips(x)))
                self.wait(.8)
            else:self.wait(.5)
        self.ctx.uncertain('Garage sequence timed out')

    def enemies(self,s):
        marked={b['entity'] for b in self.blips(s) if b['entity'] and b['relative']['distance']<100}
        return [n for n in self.nodes(s) if n['ped'] and n['ped']['human'] and not n['entity']['dead']
                and n['entity']['model'] in GANG and n['ped']['weapon']!='Unarmed'
                and (n['ped']['inCombatWithPlayer'] or n['entity']['handle'] in marked)]

    def combat(self):
        self.phase('cover-combat')
        deadline=time.monotonic()+300;quiet=None;blocked={};moved_cover=False;last_photo=0
        while time.monotonic()<deadline:
            s=self.read();p=s['sections']['player'];w=s['sections']['weapons']
            if not s['session']['missionActive']:self.ctx.uncertain('Mission ended during combat')
            enemies=self.enemies(s)
            if not enemies:
                quiet=quiet or time.monotonic()
                if time.monotonic()-quiet>5:return
                time.sleep(.25);continue
            quiet=None
            if not p['inCover']:
                self.take_cover()
            if p['entity']['health']<125:
                self.ctx.log('recover-in-cover',health=p['entity']['health']);self.wait(6);continue
            if p['reloading']:self.wait(.4);continue
            if not w['ammoInClip']:self.controls(180,Reload=1);self.wait(1.6);continue
            if w['selected']!='Pistol' or not w['totalAmmo']:self.ctx.uncertain('No usable mission pistol')
            t=min(enemies,key=lambda n:(blocked.get(n['entity']['handle'],0),n['entity']['relative']['distance']))['entity']
            h=t['handle']
            result=fire_from_cover(self.ctx,t,allies=[dict(handle=self.friend,model=LAMAR)],
                                   minimum_health=103)
            if result['outcome']=='fired' and result['damaged']:
                blocked[h]=0
            elif result['outcome']!='not-ready':
                blocked[h]=blocked.get(h,0)+1
            if time.monotonic()-last_photo>8:
                self.capture('combat',self.read());last_photo=time.monotonic()
            if blocked.get(h,0)>=2 and not moved_cover:
                trucks=[n for n in self.nodes(s) if n['kind']=='vehicle' and n['entity']['modelName']=='Bison'
                        and dist(n['entity']['position'],TRUCK_COVER)<5]
                # Move only after nearby ground attackers are gone; roof angle needs truck cover.
                ground=[n for n in enemies if n['entity']['position']['z']<6 and n['entity']['relative']['distance']<24]
                if trucks and not ground:
                    self.controls(180,Cover=1);self.walk(TRUCK_COVER,radius=1.6,timeout=18)
                    self.take_cover();moved_cover=True;blocked={}
                    self.capture('truck-cover',self.read())
                elif blocked[h]>=4:
                    self.controls(350,MoveLeftRight=-1 if blocked[h]%2==0 else 1)
            if blocked.get(h,0)>=8:self.ctx.uncertain('Cover firing angle exhausted bounded recovery')
        self.ctx.uncertain('Combat timed out')

    def chase(self, cross_fence=True):
        self.phase('fence-to-pursuit')
        if cross_fence:
            self.walk(FENCE_OUT_APPROACH,radius=1.2,timeout=45)
            navigate_to(self.ctx,FENCE_OUT,route_mode='direct',radius=2,timeout=40,stall_timeout=.8,max_climbs=3,minimum_health=103)
            self.capture('exit-fence-crossed',self.read())
        self.car=self.acquire(passenger=True)
        recover_upright(self.ctx, minimum_health=103)
        # Lamar may follow into the chase car, but do not idle through a pursuit.
        self.phase('intercept-mission-bike')
        rider=self.g.native('GET_PED_IN_VEHICLE_SEAT',self.bike,-1,False,returns='int')
        if not rider:self.ctx.uncertain('Marked bike has no identified rider')
        rider_model=self.g.native('GET_ENTITY_MODEL',rider,returns='int')
        player=self.read(False)['sections']['player']
        # These road anchors and firing cadence are specific to this mission.
        interception=[dict(x=-1134,y=-850,z=13.5),dict(x=-1343,y=-540,z=30),dict(x=-1540,y=-640,z=28)]
        if player['entity']['position']['y']>=-880:interception=interception[1:]
        last_shot=0;no_shot=0
        def engage(ctx,state,target):
            nonlocal last_shot,no_shot
            if target['distance']<45 and time.monotonic()-last_shot>2:
                burst=fire_from_vehicle(ctx,dict(handle=rider,model=rider_model),minimum_health=103)
                last_shot=time.monotonic()
                if burst['outcome']=='fired':
                    no_shot=0 if burst['ammoSpent'] or burst['damaged'] else no_shot+1
                    if no_shot>=2:ctx.uncertain('Two vehicle firing windows spent no ammunition; inspect weapon/task interaction')
            return False
        result=intercept_vehicle(self.ctx,rider,self.bike,rider_model=rider_model,
                                 vehicle_model=self.bike_model,waypoints=interception,
                                 engagement='chase',recover_rolls=True,minimum_health=103,on_tick=engage)
        if result['outcome']!='rider-separated':self.ctx.uncertain('Pursuit interrupted before rider separation')
        self.ctx.log('pursuit-ended',result=result)
        bike_pos=self.g.native('GET_ENTITY_COORDS',self.bike,False,returns='vector3')
        if dist(self.read(False)['sections']['player']['entity']['position'],bike_pos)>28:
            self.drive(bike_pos,radius=12,speed=22)
        self.leave_car()
        self.acquire(desired=self.bike)
        if self.read()['sections']['player']['vehicle']!=self.bike:self.ctx.uncertain('Recovered a different vehicle')

    def delivery(self):
        s=self.phase('return-mission-bike')
        goals=s['sections']['navigation']['gpsTargets']
        route_deadline=time.monotonic()+15
        while len(goals)!=1 and time.monotonic()<route_deadline:
            time.sleep(.25);s=self.read();goals=s['sections']['navigation']['gpsTargets']
        if len(goals)!=1:self.ctx.uncertain('Missing delivery route')
        goal=goals[0]
        self.drive(goal['position'],radius=2,speed=28,
                   until=lambda x:not x['session']['missionActive'] or not any(b['handle']==goal['handle'] for b in self.blips(x)))
        self.ending=True
        self.phase('ending-and-mission-result')
        end=time.monotonic()+120;returned=None;last_shot=0;captures=[]
        while time.monotonic()<end:
            s=self.read(False);ss=s['session'];now=time.monotonic()
            if now-last_shot>(2 if ss['cutscenePlaying'] else .6):
                captures.append(self.capture('ending',s));last_shot=now
            if not ss['missionActive'] and ss['playerControl'] and not ss['cutscenePlaying']:
                returned=returned or now
                if now-returned>16:
                    self.ctx.checkpoint('entire-mission-ended-evidence',session=ss,captures=captures,
                        phaseTimes=self.phase_times,elapsed=now-self.started,actionCount=self.action_count,
                        policyPauseCommands=0,policyHandoffs=0,gameRetries=0)
                    return
            time.sleep(.2)
        self.ctx.uncertain('Ending did not complete')

    def run(self):
        self.setup_contact()
        self.beach()
        self.follow()
        self.garages()
        self.combat()
        self.follow(after_combat=True)
        self.chase()
        self.delivery()


def run(ctx):
    policy=Mission(ctx)
    import gta_use.driving, gta_use.navigation, gta_use.vehicles, gta_use.combat, gta_use.segment, gta_use._validation
    ctx.log('source-hashes',files={str(Path(p)):hashlib.sha256(Path(p).read_bytes()).hexdigest()
        for p in (__file__,gta_use.driving.__file__,gta_use.navigation.__file__,
                  gta_use.vehicles.__file__,gta_use.combat.__file__,gta_use.segment.__file__,gta_use._validation.__file__)})
    try:
        policy.run()
    except Exception:
        try:policy.capture('failure',policy.read())
        except Exception:pass
        raise
