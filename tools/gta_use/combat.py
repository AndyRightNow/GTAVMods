"""Observed cover entry and bounded firing; callers identify targets and tactics."""
import math
import time

from ._validation import _bounds, _identity


def _target_health(game, identity, *, alive=False):
    return game.command(('/* gta-use:target-health */ var t=Entity.FromHandle(%d) as Ped; '
        'if(t==null || !t.Exists() || t.Model.Hash!=%d%s) return null; return t.Health;') %
        (*identity, ' || t.IsDead' if alive else ''))


def melee_burst(ctx, target, *, attack_ms=500, recovery_ms=700, warmup_ms=700,
                maximum_distance=3, minimum_health=140):
    """One normal unarmed Aim/light-punch exchange against a caller-identified ped.

    Does not choose hostility, move/turn the player, switch weapons or retry.
    Check damaged and the returned state; a button press is not a connected punch.
    Stop on transition so mission tasks can finish without another attack.
    """
    identity = _identity(target)
    _bounds(attack_ms=(attack_ms, 1, 1500), recovery_ms=(recovery_ms, 0, 1500),
            warmup_ms=(warmup_ms, 0, 1500), maximum_distance=(maximum_distance, .5, 5),
            minimum_health=(minimum_health, 1, 10000))
    if any(isinstance(v, bool) or not isinstance(v, int) for v in (attack_ms, recovery_ms, warmup_ms)):
        raise ValueError("Melee durations must be integer milliseconds.")
    player_identity = None

    def observe():
        nonlocal player_identity
        state = ctx.check(sections=["player", "weapons", "nearby"], types=["ped"], radius=8, limit=32)
        p, session = state["sections"].get("player"), state["session"]
        if not p or session["pauseMenu"]:
            ctx.uncertain("Melee interrupted by missing player or menu.")
        if not session["playerControl"] or session["cutscenePlaying"]:
            return state, None, "transition"
        current = _identity(p["entity"])
        if player_identity is not None and current != player_identity:
            ctx.uncertain("Player identity changed during melee.")
        player_identity = current
        if p["inVehicle"] or p["inCover"] or p["entity"]["health"] < minimum_health:
            ctx.uncertain("Melee needs an on-foot player with sufficient health and out of cover.")
        if state["sections"]["weapons"]["selected"] != "Unarmed":
            ctx.uncertain("Unarmed melee requires the currently selected Unarmed weapon.")
        t = next((n["entity"] for n in state["sections"]["nearby"]["nodes"]
                  if _identity(n["entity"]) == identity), None)
        if t is None or t["dead"]:
            return state, t, "target-unavailable"
        if t["relative"]["distance"] > maximum_distance or abs(t["relative"]["bearing"]) > 60:
            return state, t, "out-of-reach"
        return state, t, None

    state, before, reason = observe()
    if reason:
        return dict(outcome=reason, state=state)
    ctx.game.action("resume")
    if warmup_ms:
        ctx.game.action("controls", ms=warmup_ms, inputs=[dict(control="Aim", value=1)])
        state, current, reason = observe()
        if reason:
            return dict(outcome=reason, state=state)
        before = current
    ctx.game.action("controls", ms=attack_ms,
                    inputs=[dict(control="Aim", value=1), dict(control="MeleeAttackLight", value=1)])
    state, after, reason = observe()
    if reason == "transition":
        return dict(outcome=reason, state=state)
    if recovery_ms and reason is None:
        ctx.game.action("controls", ms=recovery_ms, inputs=[dict(control="Aim", value=1)])
        state, after, reason = observe()
        if reason == "transition":
            return dict(outcome=reason, state=state)
    hp = after["health"] if after else None
    result = dict(outcome="exchanged", target=identity[0], before=before["health"], after=hp,
                  damaged=hp is not None and hp < before["health"], targetStatus=reason, state=state)
    ctx.log("melee-exchange", **{k: v for k, v in result.items() if k != "state"})
    if result["damaged"]:
        ctx.progress("Observed melee damage", target=identity[0], health=hp)
    return result


def fire_from_vehicle(ctx, target, *, bone=31086, burst_ms=1100, warmup_ms=200,
                      maximum_distance=45, minimum_health=100):
    """One target-gated burst from an observed vehicle seat, without replacing driving.

    Caller identifies the hostile ped and chooses cadence/tactics. Uses the
    currently equipped, earned weapon; never equips/grants ammo, loops or retries.
    A requested burst is not a shot: inspect ammoSpent, damaged and aim feedback.
    Unlike cover fire this relies on the per-frame aim-target gate, not a static
    muzzle/ally ray. Keep intervals short and reassess moving companions.
    """
    identity = _identity(target)
    _bounds(burst_ms=(burst_ms, 1, 3000), warmup_ms=(warmup_ms, 0, 1000),
            maximum_distance=(maximum_distance, 1, 150), minimum_health=(minimum_health, 1, 10000))
    if (any(isinstance(v, bool) or not isinstance(v, int) for v in (bone, burst_ms, warmup_ms))
            or not 0 <= bone <= 65535 or warmup_ms >= burst_ms):
        raise ValueError("Use a valid ped bone and integer durations with warmup shorter than burst.")
    before = ctx.check(sections=["player", "vehicle", "weapons"])
    p, car, weapon = (before["sections"].get(key) for key in ("player", "vehicle", "weapons"))
    session = before["session"]
    if (not p or not car or not p["inVehicle"] or session["pauseMenu"] or session["cutscenePlaying"]
            or not session["playerControl"] or p["entity"]["health"] < minimum_health):
        ctx.uncertain("Vehicle firing interrupted by health, seat, control or menu transition.")
    # A vehicle weapon can report an unavailable clip before ordinary aiming.
    # Unknown is not empty: one gated burst may establish effect when total ammo
    # is present. A measured empty clip still belongs to the reload policy.
    if session.get("phoneCall") or p["reloading"] or weapon["ammoInClip"] == 0 or not weapon["totalAmmo"]:
        return dict(outcome="not-ready", state=before)
    # Obtain fresh target position and identity together; a previous pursuit pack
    # is not sufficient after another action has advanced the simulation.
    current = ctx.game.command(('/* gta-use:vehicle-fire-target */ var t=Entity.FromHandle(%d) as Ped; '
        'if(t==null || !t.Exists() || t.Model.Hash!=%d || t.IsDead) return null; '
        'return new {health=t.Health, distance=t.Position.DistanceTo(P.Position)};') % identity)
    if current is None or current["distance"] > maximum_distance:
        return dict(outcome="withheld", reason="target-unavailable-or-out-of-range", state=before)
    ctx.game.action("resume")
    aim = ctx.game.action("aim", entity=identity[0], bone=bone, ms=burst_ms,
                          warmupMs=warmup_ms, shoot=True, requireAimTarget=True)
    after = ctx.check(sections=["player", "vehicle", "weapons"])
    next_player, next_car, next_weapon = (after["sections"].get(key) for key in ("player", "vehicle", "weapons"))
    hp = _target_health(ctx.game, identity)
    same_loadout = (next_player and next_car and next_player["inVehicle"]
        and _identity(next_player["entity"]) == _identity(p["entity"])
        and _identity(next_car["entity"]) == _identity(car["entity"])
        and next_weapon["hash"] == weapon["hash"])
    ammo_after = next_weapon["totalAmmo"]
    result = dict(outcome="fired", target=identity[0], before=current["health"], after=hp,
        damaged=hp is not None and hp < current["health"],
        ammoBefore=weapon["totalAmmo"], ammoAfter=ammo_after,
        ammoSpent=max(0, weapon["totalAmmo"]-ammo_after) if same_loadout else None,
        aim=aim, state=after)
    ctx.log("vehicle-fire", **{k: v for k, v in result.items() if k != "state"})
    if result["damaged"]:
        ctx.progress("Observed rider damage", target=identity[0], health=hp)
    return result


def take_cover(ctx, *, timeout=5, minimum_health=100, check_interval=.2):
    """Tap Cover once and wait for settled cover; never toggle repeatedly."""
    _bounds(timeout=(timeout, .2, 30), minimum_health=(minimum_health, 1, 10000),
            check_interval=(check_interval, .05, 2))
    deadline, requested, identity = time.monotonic() + timeout, False, None
    while time.monotonic() < deadline:
        state = ctx.check(sections=["player"])
        p, session = state["sections"].get("player"), state["session"]
        if (not p or session["pauseMenu"] or session["cutscenePlaying"] or not session["playerControl"]
                or p["inVehicle"] or p["entity"]["health"] < minimum_health):
            ctx.uncertain("Cover entry interrupted by health, control or vehicle transition.")
        current = _identity(p["entity"])
        if identity is not None and current != identity:
            ctx.uncertain("Player identity changed during cover entry.")
        identity = current
        if p["inCover"] and not p["goingIntoCover"]:
            if requested:
                ctx.progress("Observed settled cover", position=p["entity"]["position"])
            return state
        if not requested:
            ctx.game.action("resume")
            if not p["goingIntoCover"]:
                ctx.game.action("controls", ms=200, inputs=[dict(control="Cover", value=1)])
            requested = True
        time.sleep(check_interval)
    ctx.uncertain("Cover entry did not settle within its deadline.")


def muzzle_clear(game, target, *, allies=(), bone=31086, clearance=.7):
    """One game-thread query of current muzzle ray and identified ally clearance.

    Caller must first peek/aim and identify all companions. Missing/recycled/dead
    identities withhold fire. A ray hit does not establish target hostility or
    guarantee that a moving ally stays clear during a later burst.
    """
    identity, friends = _identity(target), [_identity(a) for a in allies]
    if len(friends) > 32 or isinstance(bone, bool) or not isinstance(bone, int) or not 0 <= bone <= 65535:
        raise ValueError("Use at most 32 allies and a valid ped bone identifier.")
    _bounds(clearance=(clearance, .1, 10))
    pairs = ",".join("new int[]{%d,%d}" % a for a in friends)
    observation = game.command(("/* gta-use:muzzle-clear */ var t=Entity.FromHandle(%d) as Ped; "
        "if(t==null || !t.Exists() || t.Model.Hash!=%d || t.IsDead) return false; ") % identity + """
        var head=Function.Call<Vector3>(Hash.GET_PED_BONE_COORDS,t.Handle,TARGET_BONE,0f,0f,0f);
        int w=Function.Call<int>(Hash.GET_CURRENT_PED_WEAPON_ENTITY_INDEX,P.Handle,false);
        int bone=Function.Call<int>(Hash.GET_ENTITY_BONE_INDEX_BY_NAME,w,"gun_muzzle");
        if(w==0 || bone<0) return false;
        var src=Function.Call<Vector3>(Hash.GET_WORLD_POSITION_OF_ENTITY_BONE,w,bone);
        var ray=World.Raycast(src,head,IntersectFlags.Everything,P);
        var allies=new System.Collections.Generic.List<object>();
        foreach(var id in new int[][]{ALLIES}) {
            var a=Entity.FromHandle(id[0]) as Ped;
            if(a==null || !a.Exists()) return null;
            allies.Add(new {handle=a.Handle, model=a.Model.Hash, dead=a.IsDead,
                position=Function.Call<Vector3>(Hash.GET_PED_BONE_COORDS,a.Handle,TARGET_BONE,0f,0f,0f)});
        }
        return new {source=src, target=head, hit=ray.DidHit,
                    hitEntity=ray.HitEntity==null ? 0 : ray.HitEntity.Handle, allies=allies};
    """.replace("TARGET_BONE", str(bone)).replace("ALLIES", pairs))
    if not observation or not observation["hit"] or observation["hitEntity"] != identity[0]:
        return False
    start = tuple(observation["source"][a] for a in ("x", "y", "z"))
    end = tuple(observation["target"][a] for a in ("x", "y", "z"))
    delta = tuple(b-a for a,b in zip(start,end))
    length2 = sum(d*d for d in delta)
    if length2 < .01 or not all(math.isfinite(v) for v in (*start, *end)):
        return False
    if len(observation["allies"]) != len(friends):
        return False
    for expected, ally in zip(friends, observation["allies"]):
        if _identity(ally) != expected or ally["dead"]:
            return False
        point = tuple(ally["position"][a] for a in ("x", "y", "z"))
        if not all(math.isfinite(v) for v in point):
            return False
        u = max(0., min(1., sum((a-b)*d for a,b,d in zip(point,start,delta)) / length2))
        if math.dist(point, tuple(a+d*u for a,d in zip(start,delta))) <= clearance:
            return False
    return True
def fire_from_cover(ctx, target, *, allies=(), bone=31086, clearance=.7,
                    peek_ms=650, burst_ms=1100, warmup_ms=150, minimum_health=100):
    """Peek, verify muzzle/companions, fire one bounded burst and measure effect.

    Requires settled cover and usable ammunition. Returns withheld/not-ready/
    fired with observed ammo/health; no automatic reload, target choice, recovery,
    retry or claim of mission success. The policy owns conservative tactics.
    """
    identity = _identity(target)
    friends = tuple(allies)
    for friend in friends:
        _identity(friend)
    if len(friends) > 32 or isinstance(bone, bool) or not isinstance(bone, int) or not 0 <= bone <= 65535:
        raise ValueError("Use at most 32 allies and a valid ped bone identifier.")
    _bounds(peek_ms=(peek_ms, 1, 3000), burst_ms=(burst_ms, 1, 3000),
            warmup_ms=(warmup_ms, 0, 1000), minimum_health=(minimum_health, 1, 10000),
            clearance=(clearance, .1, 10))
    if any(isinstance(v, bool) or not isinstance(v, int) for v in (peek_ms, burst_ms, warmup_ms)):
        raise ValueError("Aim durations must be integer milliseconds.")
    before = ctx.check(sections=["player", "weapons"])
    p, weapon, session = before["sections"].get("player"), before["sections"]["weapons"], before["session"]
    if (not p or session["pauseMenu"] or session["cutscenePlaying"] or not session["playerControl"]
            or p["entity"]["health"] < minimum_health):
        ctx.uncertain("Firing interrupted by health, player control or menu transition.")
    if not p["inCover"] or p["goingIntoCover"] or p["reloading"] or not weapon["ammoInClip"] or not weapon["totalAmmo"]:
        return dict(outcome="not-ready", state=before)
    # Preflight identity before aiming; the muzzle query rechecks it after peek.
    current = _target_health(ctx.game, identity, alive=True)
    if current is None:
        return dict(outcome="withheld", reason="target-unavailable", state=before)
    ctx.game.action("resume")
    ctx.game.action("aim", entity=identity[0], bone=bone, ms=peek_ms, shoot=False)
    if not muzzle_clear(ctx.game, target, allies=friends, bone=bone, clearance=clearance):
        ctx.log("muzzle-withheld", target=identity[0])
        return dict(outcome="withheld", reason="muzzle-or-companion", state=before)
    aim = ctx.game.action("aim", entity=identity[0], bone=bone, ms=burst_ms,
                          warmupMs=warmup_ms, shoot=True, requireAimTarget=True)
    after = ctx.check(sections=["player", "weapons"])
    hp = _target_health(ctx.game, identity)
    damaged = hp is not None and hp < current
    result = dict(outcome="fired", target=identity[0], before=current, after=hp, damaged=damaged,
                  ammoBefore=weapon["totalAmmo"], ammoAfter=after["sections"]["weapons"]["totalAmmo"],
                  aim=aim, state=after)
    ctx.log("cover-fire", **{k: v for k, v in result.items() if k != "state"})
    if damaged:
        ctx.progress("Observed attacker damage", target=identity[0], health=hp)
    return result
