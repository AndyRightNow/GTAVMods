# Selecting useful world observations

The [state contract](../../../../docs/world-state.md) is the field/filter source
of truth. Use `describe.stateSections` to discover installed extensions. Returned
`gta.world-state/1` packs contain session/UI flags, `sections`, conventions and
measured query costs. Treat them as time-bound observations, not persistent DOM
nodes. Handles can be reused; revalidate identity, model, location and current role.

## Choose only what the policy consumes

| Scenario | Starting selection | Add when needed |
| --- | --- | --- |
| Readiness/health | `sections=["player"]` | Weapons after a pickup |
| Walking/navigation | `player,navigation` | Nearby peds/objects and a few forward probes around obstacles |
| Driving | `player,vehicle,navigation` | Nearby vehicles, speed-appropriate probes before a maneuver |
| Cover/fire | `player,weapons,nearby`, `types=["ped"]` | LOS on, bounded radius/limit; camera for aim diagnosis |
| Loadout inspection | `weapons,inventory` | Once after weapon/ammo changes, not every combat tick |
| Objective/menu uncertainty | `player,navigation` plus session flags | Fresh screenshot for HUD text or modal UI |

Explicit `sections` replaces profile defaults, it does not extend them. Omitted
sections do no work. Use `minimal`, `walking`, `driving`, `combat` or `full` when
their defaults fit; avoid `full` as a permanent stream. Start with 2–5 Hz for a
reactive policy, then adjust from observed latency/cost (10 Hz maximum request).
The bridge already injects controls and aims each frame; Python need not run at FPS.

For example, inspect a small combat area:

```python
state = ctx.check(sections=["player", "weapons", "nearby"], types=["ped"],
                  radius=60, limit=16, lineOfSight="on")
```

`nearbyCandidates` and `returnedEntities` differ when a query truncates. A small
limit may omit the mission target. Follow its blip or increase the relevant bound
once before declaring it absent. LOS is off by default; turning it on tests each
returned node. Inventory enumeration and ray tests are separate optional costs.
Use `cost` counters and `captureMilliseconds` to justify changing query frequency.

## Interpret before acting

- Position uses metres, velocity metres/second and angles degrees. Relative
  coordinates are right/forward/up. Probe yaw is relative to player heading;
  positive yaw is right, not necessarily camera right or vehicle travel direction.
- A hit with entity handle zero can be terrain. A missed ray is not proof a whole
  vehicle fits; rays are samples, not a navmesh, swept body or route planner.
- A ped's hostile relationship does not prove it is a mission enemy. Require
  `ped.human` plus observed combat or objective-confirmed identity. Animals can
  belong to hostile groups. Target absence is not proof of death.
- `ped.vehicle` identifies a rider's current vehicle; inspect it again during
  pursuit. Actual player occupancy/driver seat must be observed before driving.
- Check selected weapon, clip/total ammo and reload state separately. Inventory
  ownership does not imply the pickup equipped that weapon. Null results mean
  unknown, not zero ammo or a negative observation.
- Cover input/task acceptance does not establish `player.inCover`. LOS alone
  does not establish a useful muzzle angle from cover; verify target damage.
- Native GPS route queries can be unavailable even when the HUD draws a route.
  Target positions and nearby road context remain useful. Blip sprites are icon
  types, not mission titles. Requery when the objective changes.
- `playerAvailable:false`, null player sections or cutscene/control flags can
  indicate loading or transitions. Wait with bounded checks or stop for inspection;
  do not dereference absent player data or keep executing an old combat policy.

## Screenshots and extensions

Use screenshots when text/visual context changes: new objective, failure menu,
cutscene choice, ambiguous cover, unexpected obstacle, final mission result.
The generic pack cannot expose every mission's private variables or arbitrary
HUD subtitle text. Keep an uncertainty path in every policy.

If a missing observation recurs, add a namespaced state provider through
`AgentWorld.StatePack.Register(name, provider)` and select it explicitly. Providers
run on the game thread: bound enumeration/rays, preserve null/unknown semantics,
and avoid mutations. Add scenario tests for a shipped provider, and verify native
behavior live. Re-register dynamic providers after reload. Put expensive state
behind an explicit section/parameter rather than inflating all default snapshots.
