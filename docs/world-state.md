# Structured world-state packs

`Client.state(...)`, WebSocket subscriptions and `Invoke-Gta.ps1 state` return `gta.world-state/1` JSON: game time, coordinate
conventions, session/UI flags, named `sections`, query `cost` counters and
measured capture time. The default `minimal` profile reads only the player.
`walking`, `driving`, `combat` and `full` select useful section sets. Explicit
`sections` replaces the profile selection. Omitted sections do **no work**.

| Section | Decision data |
| --- | --- |
| `player` | Handle/model, coordinates, velocity, heading, health/max health, armor, actual vehicle occupancy, movement, climbing/vaulting/jumping/falling, cover, reload, swimming and height above ground |
| `vehicle` | Current vehicle, speed, engine/body health, driveability, wheels/rollover, engine, driver and passenger counts; null on foot |
| `weapons` | Selected weapon/hash, total ammo, clip ammo/capacity, shooting/reloading, aiming and observed aim target |
| `inventory` | Owned known SHVDNE weapons and ammo; opt-in enumeration, excluded from combat defaults |
| `nearby` | Distance-sorted entity nodes, relative right/forward/up and bearing, type counts, truncation, ped human/animal classification, weapons, relationships, combat and current vehicle handle; optional visibility |
| `navigation` | Zone/street/crossing, GPS route availability/length, routed targets, waypoint and map markers with world/relative positions |
| `probes` | Bounded collision ray fan with hit/miss, range, direction, position, hit distance and entity; entity zero can mean terrain |
| `camera` | Position, direction and rotation |

Entity records preserve the numeric `model` hash and include `modelName` when
the selected section knows the entity is a ped or vehicle and SHVDNE recognizes
its model (for example, `Franklin`, `Bison` or `Bati`). Names are SDK symbols,
not localized display text or mission roles. Custom/unknown models, objects and
untyped aim targets have `modelName:null`; do not infer a mission bike or enemy
from a model name alone. Name lookup uses the already-read hash without adding
native queries.

```powershell
$gta = '.\scripts\Invoke-Gta.ps1'
& $gta state -StateJson '{"sections":["player","navigation"],"blipLimit":8}'
& $gta state -StateJson '{"profile":"combat","types":["ped"],"limit":8,"radius":50,"lineOfSight":"on"}'
& $gta state -StateJson '{"sections":["probes"],"probeHeight":1,"rays":[{"yaw":0,"pitch":0,"distance":25},{"yaw":45,"distance":8}]}'
& $gta step -Milliseconds 500 -StateJson '{"profile":"driving","types":["vehicle"],"limit":12}'
```

The same options work on a `{"op":"state",...}` step in a sequential batch.
`describe` lists available sections, controls and command argument schemas.

Nearby radius is 1–1000 m and returned-node limit 1–128 (defaults 80 m/24).
`types` filters ped/vehicle/object nodes before detailed queries. Counts describe
the matching observed pool before truncation. `lineOfSight` defaults to `off`;
`on` performs at most one native visibility test per returned node. Navigation
prioritizes routed targets, waypoints and colored contact/objective markers
before applying `blipLimit` (1–256). Sprite names describe icon types, not mission
titles. Native route availability/length can be unavailable even when the HUD
draws a waypoint route; target coordinates and relative distance remain useful.

Ray probes run only when requested: default five horizontal rays at -90, -45,
0, 45 and 90 degrees, each 12 m; custom arrays allow 1–16 rays, yaw -180..180,
pitch -90..90 and distance .1..200 m. Positive yaw is right of player heading.
Rays ignore the current vehicle or player and distinguish a miss from a world
hit with handle zero. They sample collision geometry, not a complete walkable
navmesh or guaranteed clearance for a vehicle's width.

The tree is an observation, not a persistent DOM: handles can disappear or be
reused. Reinspect after advancing time. Loading/character replacement reports
`playerAvailable:false` and null sections. Unknown clip/aim results remain null.
Session flags expose mission activity, player control, cutscene playback/time/duration, and phone/help/message activity, but arbitrary mission subtitle
and help text are not exposed by these natives; use a screenshot when needed.
`cost` reports actual scan/probe/visibility/inventory counts so agents can narrow
the next query. Full inventory and ray fans are intentionally opt-in.

Animals can report a hostile relationship, so a relationship number alone does
not identify a mission enemy. Combine `ped.human`, actual combat, and the current
objective. A nearby-pool omission does not prove death or despawn: reacquire an
objective through its blip and inspect it, or take another observation. Selecting
a weapon and owning its ammo are separate facts; check both weapon and inventory
sections when a pickup does not equip automatically.

Providers can be added through `AgentWorld.StatePack.Register(name,
Func<StateContext,object>)`. They appear in `describe.stateSections` and can be
selected without changing the protocol. Keep providers bounded and free of
gameplay side effects. Register again after script reload.

For example, this live-verified extension supplies cutscene progress on demand:

```powershell
& $gta command -Code @'
((GTAVMods.Game.AgentWorld)CONTEXT.World).StatePack.Register("cutscene.timeline", c => new {
    playing = Function.Call<bool>(Hash.IS_CUTSCENE_PLAYING),
    timeMs = Function.Call<int>(Hash.GET_CUTSCENE_TIME),
    durationMs = Function.Call<int>(Hash.GET_CUTSCENE_TOTAL_DURATION)
});
'@
& $gta state -StateJson '{"sections":["player","cutscene.timeline"]}'
```
