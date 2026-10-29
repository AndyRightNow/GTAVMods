# Simplification review: what can go, and what the game API already gives us

- **Reviewer:** DeepSeek Harness agent, `deepseek-flash` model.
- **Date:** 2026-10-20.
- **Status:** proposal only. No production code, test, project, script or package
  file was changed by this review. Nothing was built or deployed.

This document answers two questions: what in the shipped implementation can be
removed or collapsed without losing a feature, and where the mod hand-rolls
something the pinned SHVDNE (ScriptHookVDotNet Enhanced) API already provides.

## 1. Scope and method

Reviewed:

- All compiled production sources: `Thor` (11 files, 2 642 lines), `ModUtils`
  (13 files, 1 166), `DeveloperConsole` (10 files, 1 712), `GameBridge`
  (12 files, 1 170) - 6 690 lines total, excluding `Properties/AssemblyInfo`
  and the three uncompiled files.
- The test project (30 files, 8 055 lines) and its fakes and fixtures.
- The coverage gate: [coverage-gate.md](coverage-gate.md),
  [coverage-plan.md](coverage-plan.md), [mutation-review.md](mutation-review.md),
  [engine-contracts.md](engine-contracts.md), `scripts/Test-Coverage.ps1`,
  `scripts/New-CoverageInventory.ps1`, `scripts/Coverage.Inventory.targets`,
  `scripts/coverage-boundaries.json`, `scripts/tests/*`.
- Upstream API evidence: the pinned XML
  `.deps/shvdne/ScriptHookVDotNet3.xml` and the pinned assembly
  `.deps/shvdne/ScriptHookVDotNet3.dll` (3.9.0.0). API members quoted below were
  confirmed by reflection over that assembly, not from memory. For example
  `GTA.Entity.EntityType` returns the enum `GTA.EntityType` with values
  `Invalid, Ped, Vehicle, Prop`.

Verification of "dead" claims was mechanical: every member listed in Appendix A
has zero call sites outside its own declaration and its tests, checked with a
repository-wide reference count over the shipped sources. Unreachability of the
console framework was traced from `F8` through `RunCommand` to
`CommandDispatcher.Commands`, which is constructed empty and is only filled by
`RegisterCommand`, whose only caller is the deliberately uncompiled
`Example.cs`.

## 2. Baseline

| Item | Current value | Source |
| --- | ---: | --- |
| Shipped source lines | 6 690 | measured |
| Test lines | 8 055 | measured |
| Tests passing | 374 | [coverage-plan.md:151](coverage-plan.md) |
| Executable lines / branches | Thor 1 469/656, ModUtils 476/212, DeveloperConsole 765/320, GameBridge 214/6 | [mutation-review.md:54](mutation-review.md) |
| Reviewed primitive bindings | 146 (GameBridge 111, DeveloperConsole 28, Thor 4, ModUtils 3) | `scripts/coverage-boundaries.json` |
| Uncompiled legacy sources | 674 lines (`_LEGACY_PathFinder.cs` 356, `TestObjects.cs` 284, `Example.cs` 34) | measured |

The gate requires a one-to-one match between `[GamePrimitive]` methods and the
manifest (`New-CoverageInventory.ps1`, "Primitive annotation and reviewed
manifest disagree"), so removing or adding a binding always requires a manifest
edit in the same change. Coverage is measured for every other line and branch,
which is why the dead code below is expensive: each dead member carries tests
that exist only to hit it.

## 3. Recommendation summary

| Tier | Change | Production lines | Test lines | Manifest | Risk |
| --- | --- | ---: | ---: | ---: | --- |
| 1 | Delete uncompiled legacy files | -674 (not in build) | 0 | 0 | none |
| 1 | Delete dead members (Appendix A) | -250 ... -300 | -250 ... -350 | -6 | none |
| 2 | Console decision (finding 1) | -1 100 ... -1 300 | -1 500 ... -1 700 | -8 (A), -15 (B) | low-medium |
| 3 | Fold duplicated observations (finding 3) | -60 ... -90 | -200 ... -300 | -2 (plus moves) | low |
| 4 | Merge single-implementation hierarchies, drop test-only branches (finding 4) | -120 ... -160 | -100 ... -150 | 0 | low |
| 5 | Typed replacements verified live (section 9) | -100 ... -140 | -150 ... -200 | 0 (moves) | medium |

The numbers are estimates; they are the difference between deleting the listed
code and keeping today's behavior. Tier 1 and tier 3 keep the 100 % gate green
because code and tests are deleted together.

## 4. Finding 1: the F8 console is a full command framework with no commands

### Evidence

- `CommandDispatcher` initializes `Commands` empty and `RegisterCommand` is its
  only writer ([CommandDispatcher.cs:20](../gtav_console/Commands/CommandDispatcher.cs#L20),
  [CommandDispatcher.cs:35](../gtav_console/Commands/CommandDispatcher.cs#L35)).
  The only caller in the repository is the excluded
  [Example.cs:18](../gtav_console/Example.cs#L18).
- `DefaultCommands` registers nothing; it prints a debug line that is disabled
  outside a `9.9.9.9` build ([DefaultCommands.cs:8](../gtav_console/Commands/DefaultCommands.cs#L8),
  `ConsoleSettings.IsDevBuild`).
- The welcome text says "Run 'help' for commands", but no `help` command exists,
  so every entered line reaches "Command '...' not found!"
  ([DeveloperConsole.cs:82](../gtav_console/Console/DeveloperConsole.cs#L82),
  [DeveloperConsole.cs:264](../gtav_console/Console/DeveloperConsole.cs#L264)).
- The parser carries an unreachable runtime C# evaluator
  (`ICommandEnvironment.Compile` with `CSharpCodeProvider`), reachable only from
  a command callback ([CommandParser.cs:15](../gtav_console/Commands/CommandParser.cs#L15),
  [CommandParser.cs:40](../gtav_console/Commands/CommandParser.cs#L40)).
- The logger attaches to the console but never forwards events, so F8 displays
  two static lines: the welcome line and the `Logger.Init` header. The
  forwarding members are dead ([Logger.cs:29](../ModUtils/Logger.cs#L29),
  [Logger.cs:54](../ModUtils/Logger.cs#L54), [Logger.cs:57](../ModUtils/Logger.cs#L57)).
- Of the 59 methods in the seven console capability interfaces
  ([ConsoleContracts.cs](../GameBridge/ConsoleContracts.cs)), only 8 are
  reachable: `IsEnabled`, `Enable`, `DisableAll` from `IConsoleInput`, and
  `TextFont`, `TextScale`, `BeginTextWidth`, `AppendText`, `EndTextWidth` from
  `IConsoleDrawing`. `IConsoleNetwork` (11), `IConsoleStats` (3),
  `IConsoleWorld` (10), `IConsolePlayers` (8) and `IConsoleEntities` (7) have no
  reachable caller (`IConsolePlayers.Index` is referenced only by the
  unreachable expression environment); five of the seven adapter classes in
  [NativeConsoleAdapter.cs](../GameBridge/NativeConsoleAdapter.cs) are unused,
  and about 50 of their 59 native calls are unreachable.

### Shipped cost

- `CommandParser` 222 lines, `CommandDispatcher` 203, `GTAFuncs` 463 (roughly
  120 reachable), `Rectangle3D`/`Line3D` 279, `DefaultCommands` 12,
  `IDeveloperConsole` 12; ~1 100 of 1 712 console lines.
- `ConsoleContracts` plus console adapters in `GameBridge` ~180 of 239 lines.
- Console tests: `ConsoleContractTests` 562, `ConsoleBehaviorTests` 427,
  `ConsoleGameFunctionsTests` 286, `ConsoleCommandTests` 245, `ConsoleFakes`
  202, `ConsoleGeometryTests` 101 - ~1 900 of 8 055 test lines, to hold 765
  measured console lines at 100 %.
- Eight manifest entries (`LiveCommandEnvironment` x7, `DefaultCommands` ctor),
  plus seven more (keyboard x5, clipboard x2) if text entry is dropped.
- Mutation cost: the last full run recorded 32 "no coverage" and 181
  runner-ignored mutants in DeveloperConsole alone
  ([mutation-review.md:70](mutation-review.md)).

### Options

**Option A (recommended): keep F8 as a log viewer plus a small command table.**

- Keep the overlay, key capture, input editing, history, scrolling and drawing
  from `DeveloperConsole`/`ConsolePlatform`, and `Logger.Init`.
- Replace the parser/dispatcher with a table of 3-5 commands
  (`help`, `clear`, `thor on|off`, `log`), so the welcome text becomes true and
  the console gains a visible purpose (recent events) at roughly 60 lines.
- Replace the whole control capture/restore state machine
  (`CaptureEnabledControls`, `RestoreControls`, `RestorePendingControls`,
  `RestoreStates`, `_pendingControls`, `WithEnabledControls`,
  [GTAFuncs.cs:87](../gtav_console/Utils/GTAFuncs.cs#L87),
  [GTAFuncs.cs:331](../gtav_console/Utils/GTAFuncs.cs#L331)) with the frame-scoped
  API: `Game.DisableControlThisFrame`, `Game.DisableAllControlsThisFrame`,
  `Game.IsControlEnabled`. Frame-scoped disables persist for one frame only, so
  there is nothing to capture, restore or retry, and the documented
  restore-ordering contract in [engine-contracts.md:98](engine-contracts.md)
  becomes unnecessary rather than merely tested.
- Delete `CommandParser`, `CommandDispatcher`, `DefaultCommands`,
  `IDeveloperConsole`, `Rectangle3D`, the dead parts of `GTAFuncs`, the five
  unused console capability interfaces and their adapters.

**Option B: log-only overlay.** Also remove text entry, `NativeMethods`, caps
lock/clipboard translation and `ClipboardThread`; loses typing, which today has
no observable effect beyond a "not found" message.

**Option C: keep the framework and register real commands.** Preserves latent
extensibility but keeps the whole coverage and mutation cost, and should then
include a `help` command so the shipped text is truthful.

Either option preserves the user-visible F8 behavior: open/close, display, and
suppression of Thor hotkeys while open (`DeveloperConsole.Instance.IsOpen`).
Option A additionally makes the console useful; Option B is the smallest
maintenance surface.

**Verification needed:** the frame-scoped control switch is the only behavioral
change in Option A. Confirm in game that F8 opens, typed characters appear, the
game does not react to movement/attack while open, and Thor hotkeys resume after
closing.

## 5. Finding 2: dead engine capabilities in the contract layer

These capabilities have no reachable production caller. Each carries interface
members, an adapter, fake support, manifest entries and tests.

| Capability | Dead members | Natives |
| --- | --- | ---: |
| `IDrawingAccess` | all 3 ([GameplayContracts.cs:103](../GameBridge/GameplayContracts.cs#L103)); reachable only through dead `ADModUtils.NativeHelper` drawing members, dead `ADModUtils.Line` and dead `GTAFuncs` methods | 3 |
| Looped particles | `StartLoopedOnPedBone`, `StartLoopedAt`, `StopLooped`, `SetLoopedColor` ([GameplayContracts.cs:91](../GameBridge/GameplayContracts.cs#L91)); only `ParticleFxLooped` calls them, and it is unused | 4 |
| `INativeOutputs` | all 3 ([Contracts.cs:249](../GameBridge/Contracts.cs#L249)): `LastDamageBone` only via dead `GetLastDamagedBone`, `EntityQuaternion` only via dead `GTAFuncs.GetEntityQuaternion`, `WorldToScreen` only via dead `GTAFuncs.WorldToScreen`/`Rectangle3D` | 3 |
| `IPedAccess` | `IsInCombatAgainst`, `IsRagdoll` | 2 |
| `IWorldAccess` | `GetGroundHeight`; only dead `GTAFuncs.GetGroundPos` calls it | 1 |
| `IGameplayAccess` | `SetWeaponVisible` (only dead `SetPedWeaponVisible`), `SetVelocity` (only dead `SetEntityVelocity`) | 2 |

Removing `INativeOutputs` also removes `NativeOutputs`, `IRawNativeOutputs` and
`GtaOutputs` ([NativeInvoker.cs:47](../GameBridge/NativeInvoker.cs#L47),
[NativeInvoker.cs:68](../GameBridge/NativeInvoker.cs#L68)). `IDrawingAccess`
also removes `NativeDrawingAccess` ([NativeEffectsAccess.cs:51](../GameBridge/NativeEffectsAccess.cs#L51)).

Module-level dead code (full inventory in Appendix A): the dead
`ADModUtils.NativeHelper` members, the unused `ParticleFxLooped`, `Line`,
`OneShotAudio`/`Utilities.Audio.Play`, `Utilities.Math.QuaternionToEulerAngles`,
`DegreesToRadians`, `CloseTo`, the unused logger wrappers and `LogConsole`
fan-out, `Mjolnir.ConvertToHammerHeadDirection`, two never-read `Mjolnir`
fields, `WorthyAbility.thunder` (assigned, never read,
[WorthyAbility.cs:46](../Thor/WorthyAbility.cs#L46)), and
`Thor.NativeHelper.PlayThunderFx(Vector3)`.

## 6. Finding 3: the same observation is exposed twice

The contract layer duplicates capabilities that already exist next to them:

| Duplicate | Replacement | Call sites |
| --- | --- | --- |
| `IGameplayAccess.Position` (`GET_ENTITY_COORDS`) | `IEntityAccess.Position` (`Entity.Position`) | one ([GodlyWeapon.cs:154](../Thor/GodlyWeapon.cs#L154)) |
| `IGameplayAccess.Speed` (`GET_ENTITY_SPEED`) | `Entity.Speed` (new one-liner or reuse) | one ([GodlyWeapon.cs:167](../Thor/GodlyWeapon.cs#L167)) |
| `IGameplayAccess.SetVelocity` (`SET_ENTITY_VELOCITY`) | `IEntityAccess.SetVelocity` (`Entity.Velocity`) | dead |
| `IGameplayAccess.IsPed` + `IsVehicle` | one `Entity.EntityType` observation (`Invalid, Ped, Vehicle, Prop`) | via `ADModUtils.NativeHelper` only |
| `INativeOutputs.EntityQuaternion` | `IEntityAccess.Quaternion` | dead |
| `IWorldQueryBindings`/`GtaWorldQueries` (manual `ShapeTest`) | `World.Raycast` / `World.RaycastCapsule` returning `RaycastResult` (`DidHit`, `HitEntity`, `HitPosition`) | `GtaWorld` only |

The world-query seam is the clearest example: `GtaWorld` already calls
`World.Raycast` for the raycast path, then re-implements the capsule path with
`ShapeTest.StartTestCapsule` + `GetResult`
([GtaWorldQueries.cs:29](../GameBridge/GtaWorldQueries.cs#L29)). Both can map
`HitEntity` to a handle directly, which keeps the assembly logic measured and
deletes a 40-line transport plus two manifest entries.

The interaction between tiers matters: `GameServices` currently exposes
20 capabilities, of which 7 are console-facing and 1 is the three-member
`INativeOutputs` transport. After findings 1, 2 and 3 it exposes 11, and
`INativeInvoker` shrinks to the residue SHVDNE does not wrap (section 10).

## 7. Finding 4: one implementation behind two abstract layers, plus test-only branches

`WorthyAbility` (952 lines) declares 10 abstract members and four
virtual/override pairs for exactly one subclass,
[MjolnirWorthyAbility.cs](../Thor/MjolnirWorthyAbility.cs) (236 lines).
`GodlyWeapon` has exactly one subclass, `Mjolnir`. Merging each pair removes
~15 declarations, the abstract-method indirection and the `Thunder` constructor
threading, with no behavior change. Keep `IThorAbility`: the controller tests
deliberately replace the ability with a probe
([GameplayTestSupport.cs:55](../ThorTest/GameplayTestSupport.cs#L55)).

Dead or test-only parameters and branches:

- `WorthyAbility.OnTick(bool onlyHandleWeaponHolding)`: production always passes
  `false` ([Thor.cs:90](../Thor/Thor.cs#L90)); the flag exists for one test.
  Drop the parameter and the branch.
- `GodlyWeapon.Init(..., bool forceReplacingOld)`: never passed `true`
  ([GodlyWeapon.cs:189](../Thor/GodlyWeapon.cs#L189)).
- `Thunder.Shoot(jaggedness, maxSwayRate, hasDamage)`: the only caller passes
  `-1, -1, false` ([GodlyWeapon.cs:169](../Thor/GodlyWeapon.cs#L169)), so the
  sentinel mapping and the entire damage branch
  ([Thunder.cs:22](../Thor/Thunder.cs#L22)) are unreachable. Pass constants and
  delete the branch.
- `List<...>.ToArray()` plus `PickOne` over single-element lists for
  `CatchingWeapon1`/`CallingForWeapon`
  ([WorthyAbility.cs:779](../Thor/WorthyAbility.cs#L779)): two pointless RNG
  draws per summon; use the constant.
- `SetInvincible(bool)` never calls `SetInvincible(entity, true)`. It is a
  health/proof policy, and its cleanup path writes `MaxHealth = 30000` that
  `RemoveAbility` overwrites immediately
  ([WorthyAbility.cs:482](../Thor/WorthyAbility.cs#L482),
  [WorthyAbility.cs:137](../Thor/WorthyAbility.cs#L137)). Rename it to what it
  does and drop the transient write.
- `Utilities.Random.SystemRandomInstance` is a static mutable `Random` with
  `lock`s and five convenience overloads. Every production path already threads
  an explicit `Random`; requiring it removes shared state, the locks and the
  overloads ([Utilities.cs:51](../ModUtils/Utilities.cs#L51)).
- `InitHoverWhirledWeapon` creates two extra weapon objects on the first tick
  after attach even if hovering is never used
  ([MjolnirWorthyAbility.cs:129](../Thor/MjolnirWorthyAbility.cs#L129)). Create
  them on first hover and delete them if that never happens.
- Repeated per-tick queries: `CurrentObject(attachedPed)` up to six times in
  `HandleFlying` ([WorthyAbility.cs:562](../Thor/WorthyAbility.cs#L562)) and up
  to three times per target; `HeightAboveGround` two to three times
  ([GodlyWeapon.cs:73](../Thor/GodlyWeapon.cs#L73)). Cache in locals.

## 8. Finding 5: ModUtils layering

`Thor.NativeHelper` (277 lines: animation/particle tables and `PlayThunderFx`)
wraps `ADModUtils.NativeHelper` (290 lines: operations) which wraps
`GameServices`. Three classes named `NativeHelper`, with the middle one holding
five dead methods and the outer one passing nine table/scalar arguments
(including five parallel collections) into it.

- Collapse to one helper plus one animation table. The table currently uses
  `AnimationDictNames`, `AnimationNames`, `AnimationWithAngles`,
  `AnimationWithAnglesAndIncompletePlusOrMinusSign` and `AnimationWaitTime`
  (three of whose eight dictionaries are empty), all indexed by the same
  `AnimationActions` value ([NativeHelper.cs:36](../Thor/NativeHelper.cs#L36)).
  One row per action removes five parallel collections and three public getters.
- After that, `ParticleFxLooped`, `Line`, `OneShotAudio`, `Utilities.Audio`,
  `IAudioPlayback.Stopped` (only `OneShotAudio` subscribes) and the dead
  `NativeHelper` members have no remaining users and can go.

## 9. Game capability the mod is not using (and where using it simplifies)

Every member below was verified in the pinned API; the "today" column is what
the mod does instead.

| Capability | Pinned API member | Today | Effect of switching |
| --- | --- | --- | --- |
| Entity kind | `Entity.EntityType` (`Invalid, Ped, Vehicle, Prop`) | `IS_ENTITY_A_PED` + `IS_ENTITY_A_VEHICLE` | one observation, one native, one fake script |
| Speed | `Entity.Speed` | `GET_ENTITY_SPEED` | duplicate capability removed |
| Physics | `Entity.ActivatePhysics`, `Entity.SetShouldFreezeWaitingOnCollision` | two natives | typed one-liners |
| Ragdoll | `Ped.SetToRagdoll(min, max, type, forceScriptControl)` | `SET_PED_TO_RAGDOLL` | typed call, same arguments |
| Lightning | `World.ForceLightningFlash()` | `FORCE_LIGHTNING_FLASH` | typed |
| Player buffs | `Player.SetSuperJumpThisFrame()`, `Player.SetSwimSpeedMultThisFrame(float)` | two natives | typed; note the swim multiplier is documented as max 1.499 while the mod computes up to 1.5 |
| HUD | `Hud.ShowComponentThisFrame(HudComponent)` | `SHOW_HUD_COMPONENT_THIS_FRAME` | typed |
| Shape tests | `World.Raycast`, `World.RaycastCapsule` -> `RaycastResult` | `ShapeTest` plumbing | delete `IWorldQueryBindings` |
| Particles | `ParticleEffectAsset.Request()/IsLoaded`, `World.CreateParticleEffectNonLooped(asset, effect, entity\|bone\|pos, ...)` | 7 natives (request, loaded, use, 3 starts) | 2 typed calls, asset lifetime explicit |
| Animation dictionary | `CrClipDictionary.Request()/IsLoaded` | `REQUEST_ANIM_DICT` + `HAS_ANIM_DICT_LOADED` | typed; keep `TASK_PLAY_ANIM` for duration/blend speeds |
| Cameras | `World.RenderingCamera` (get/set), `World.CreateCamera` | `SET_CAM_ACTIVE` + `RENDER_SCRIPT_CAMS` + director reads | one property replaces start/stop + active |
| Blips | `Entity.AddBlip()` returning `Blip`, `Blip.Name/IsFriendly/Delete`, `Entity.AttachedBlip` | handle plus `new Blip(handle)` | typed object, no re-wrapping |
| Controls | `Game.IsControlEnabled`, `Game.DisableControlThisFrame`, `Game.DisableAllControlsThisFrame` | manual capture/restore | removes the restore state machine |
| Rope | `Rope.Attach`, `Rope.Detach`, `Rope.PinVertex`, `Rope.Length`, `Rope.GetVertexCoord` | anchor weapon object | possible anchor simplification, needs a live prototype |
| Physics damping | `Entity.SetDamping(PhysicsDampingType, float)` | included in `SET_OBJECT_PHYSICS_PARAMS` | not a replacement (no mass); optional refinement |

Note on scope: because measured behavior may not call `GTA.*` members outside
`GameBridge` ([engine-contracts.md:122](engine-contracts.md),
`New-CoverageInventory.ps1`), every item above belongs in a `GameBridge`
adapter. Replacing an annotated native wrapper with a typed one-liner does not
change the manifest count - the one-liner becomes the reviewed binding - but it
removes measured adapter code, fake hash scripting and argument-order tests.
Entries disappear only where the whole capability does (drawing, looped
particles, outputs, world queries, dead members).

## 10. What not to simplify

| Item | Why it must stay |
| --- | --- |
| `CLEAR_ENTITY_LAST_DAMAGE_ENTITY` | Not the same native as `Entity.ClearLastWeaponDamage()`; it clears the `HasBeenDamagedBy` state the melee/forced-damage logic depends on. |
| `CREATE_WEAPON_OBJECT`, `GIVE_WEAPON_OBJECT_TO_PED`, `GET_WEAPON_OBJECT_FROM_PED` | No typed SHVDNE equivalent; `Weapons.Drop()` returns nothing and creates a pickup, which cannot be owned as the hammer. |
| `SET_OBJECT_PHYSICS_PARAMS` | No typed mass equivalent; `Entity.SetDamping` covers only the six damping coefficients. |
| `TASK_PLAY_ANIM` | `Entity.PlayAnimation(CrClipAsset, ...)` has no duration or blend-in/out speeds; the 250 ms catch and `-8` playback rate would change. |
| `SET_PLAYER_LOCKON_RANGE_OVERRIDE` | No typed equivalent; `PedConfigFlagToggles.DisablePlayerLockOn` is a persistent ped flag, not a per-frame player override. |
| `SET_PED_MOVE_RATE_OVERRIDE`, `DISABLE_PED_PAIN_AUDIO`, `SET_ENTITY_CAN_BE_DAMAGED` | No typed equivalents in the pinned API. |
| PathFinder Thruster + heli mission | This is the obstacle-avoidance feature, not scaffolding. |
| Rope anchor weapon object | Legitimate workaround for bone attachment; `Rope.PinVertex` is a candidate but must be prototyped live. |
| `HandleLifetime` delete-then-confirm | The ownership contract in [engine-contracts.md:38](engine-contracts.md); it is the assurance mechanism. |
| The 100 % gate | It is the reason this repository can be changed safely; the proposals shrink its surface, they do not weaken it. |

## 11. Correctness and clarity notes found while reviewing

These are not simplifications, but they are adjacent and cheap to fix:

1. F8 advertises a `help` command that does not exist (finding 1). Either
   implement it or remove the sentence.
2. `SetInvincible` does not make the ped invincible (section 7), and the
   cleanup-time `MaxHealth = 30000` is overwritten by `RemoveAbility`
   immediately.
3. `GodlyWeapon` idle motion only randomizes when
   `GameTime % idleInterval == 0` ([GodlyWeapon.cs:54](../Thor/GodlyWeapon.cs#L54)),
   with a 1 000-2 000 ms interval, so the random re-aim is effectively sporadic
   and frame-clock dependent.
4. `Mjolnir.OnTick` returns before `HandleWeaponAvailability`/rotation while the
   rope is attached ([Mjolnir.cs:52](../Thor/Mjolnir.cs#L52)); intentional, but
   worth a comment because it silently changes hammer behavior in that state.
5. `SummonWeapon` performs a blocking `Host.Wait(1)` inside the tick
   ([WorthyAbility.cs:803](../Thor/WorthyAbility.cs#L803)); acceptable, but it
   makes tick duration data in the performance log depend on catch frames.
6. `PathFinder` spawns its Thruster at `(500, 500, 500)` and teleports it to the
   hammer on first obstruction ([PathFinder.cs:16](../Thor/PathFinder.cs#L16));
   fine, but the model request throws on failure and pauses Thor.

## 12. Suggested sequence and verification

Run `scripts/Build.ps1` after each step; each step must also update
`scripts/coverage-boundaries.json` where entries appear or disappear, and delete
the tests of removed code in the same change.

1. **Tier 1 - mechanical deletions.** Remove the three uncompiled files and the
   Appendix A members outside the console framework and the world-query
   transport, with their tests. No manifest pairing changes are needed except
   for the annotated typed adapters being deleted (outputs, peds, world
   ground); the world-query entries move in tier 3.
2. **Tier 2 - console.** Choose Option A or B, land it as one change, and run
   the live F8 checks in section 13. This is the single largest line/test
   reduction.
3. **Tier 3 - duplicate observations.** Move `Position`/`Speed`/`SetVelocity`/
   `IsPed`/`IsVehicle`/`EntityQuaternion` and the capsule query onto the
   existing typed capabilities; update the fake's scripted observations and
   remove the now-unused hash entries in
   [FakeGame.cs:35](../ThorTest/FakeGame.cs#L35).
4. **Tier 4 - structure.** Merge the two class pairs, drop the test-only
   parameters and branches, thread `Random` explicitly, collapse the helper and
   animation table, defer hover-object creation, cache repeated queries.
5. **Tier 5 - optional typed replacements.** Convert the non-looped particles,
   ragdoll, lightning, HUD, player buffs and camera calls; verify each live
   because these change when the engine is asked, not what is asked.

Documentation to update alongside: `README.md` (console description and the
"excluded test-object bindings" sentence if those files are deleted),
[engine-contracts.md](engine-contracts.md) (console contracts, drawing/particles/
outputs capabilities), [coverage-plan.md](coverage-plan.md) and
[mutation-review.md](mutation-review.md) baselines.

## 13. Live feature-parity checklist

Run after each tier, with the game closed before file changes and a fresh
process after runtime/plugin changes:

- Recall (`H`), throw (`Aim + T`), throw-and-fly (`Aim + U`), drop (`Y`), whirl
  (`Z`), rope hold and swing, catch animation and sound.
- Follow camera (`R`), flight (`X`, `W`, Sprint), air dash, hover whirl.
- Lightning, hammer trail, target markers, throw at collected targets, knockdowns.
- Health/power behavior across full power, inventory-only half power, decay and
  recovery, character switch, death/respawn.
- F8 open/close/typing/scroll/history, F4, F9 snapshot, Ctrl+A+O/F toggle.
- Insert reload: objects, ropes, cameras, audio, hidden hover/pathfinder
  entities; then a full game restart.
- Logs: `ScriptHookV.log`, `ScriptHookVDotNet.log`, newest
  `scripts/Logs/Thor-*.jsonl`, no new exceptions.

Unit coverage does not certify rendering, physics, audible output or native
runtime compatibility; treat the live checks as required evidence.

## Appendix A: dead member inventory

"Zero call sites" means no reference outside the declaration and its tests in
the shipped sources.

**Uncompiled files (delete):**

- `Thor/_LEGACY_PathFinder.cs` - 356 lines, entirely commented out.
- `Thor/TestObjects.cs` - 284 lines; calls a static `NativeHelper.CreateWeaponObject`
  that no longer exists, so it cannot compile.
- `gtav_console/Example.cs` - 34 lines; the only `RegisterCommand` caller.

**GameBridge:**

- `IDrawingAccess` and `NativeDrawingAccess` (3 natives).
- Looped particle members and their adapter methods (4 natives).
- `INativeOutputs`, `NativeOutputs`, `IRawNativeOutputs`, `GtaOutputs` (3 natives).
- `IWorldQueryBindings`, `GtaWorldQueries` (2 natives) after the typed query move.
- `IPedAccess.IsInCombatAgainst`, `IPedAccess.IsRagdoll` (`GtaPeds` x2).
- `IWorldAccess.GetGroundHeight` (`GtaWorld` x1).
- `IGameplayAccess.SetWeaponVisible`, `IGameplayAccess.SetVelocity`
  (`NativeGameplayAccess` measured lines).
- Console: `IConsoleNetwork`, `IConsoleStats`, `IConsoleWorld`, `IConsolePlayers`,
  `IConsoleEntities`, their five adapters, the unused drawing/input members, and
  the command-environment adapters.

**ModUtils:**

- `NativeHelper`: `ClearPlayerAnimation`, `GetLastDamagedBone`,
  `SetEntityVelocity`, `SetPedWeaponVisible`, `DrawLine`, `DrawBox`, `DrawLines`,
  both `PlayParticleFxLooped` overloads
  ([NativeHelper.cs:122](../ModUtils/NativeHelper.cs#L122) onward).
- `ParticleFxLooped`, `Line`, `OneShotAudio` and `Utilities.Audio`,
  `Utilities.Math.QuaternionToEulerAngles`, `DegreesToRadians`, both `CloseTo`
  overloads.
- `DiagnosticLog.Log`, `DiagnosticLog.LogConsole`, and the static
  `Logger.Log/LogConsole/Event/Throttled` wrappers
  ([Logger.cs:54](../ModUtils/Logger.cs#L54)).
- `IAudioPlayback.Stopped` and its NAudio subscription once `OneShotAudio` is
  gone ([AudioPlayback.cs:6](../ModUtils/AudioPlayback.cs#L6)).

**Thor:**

- `Mjolnir.ConvertToHammerHeadDirection`
  ([Mjolnir.cs:176](../Thor/Mjolnir.cs#L176)), `AttachedIntermediateEntOffset`,
  `hammerRopeAttachedPedBoneId`.
- `WorthyAbility.thunder` field and constructor parameter.
- `Thor.NativeHelper.PlayThunderFx(Vector3)`
  ([NativeHelper.cs:272](../Thor/NativeHelper.cs#L272)).
- `GodlyWeapon.Init`'s `forceReplacingOld`; `WorthyAbility.OnTick`'s
  `onlyHandleWeaponHolding`; `Thunder.Shoot`'s sentinels and damage branch.

**DeveloperConsole:**

- `CommandParser`, `CommandDispatcher`, `DefaultCommands`, `IDeveloperConsole`,
  `Rectangle3D`/`Line3D`, `LiveCommandEnvironment`, and the unreachable
  `GTAFuncs` members (spawning, network, stats, proofs, area clearing, geometry,
  anti-ban, cursor/stick helpers).

## Appendix B: gate and test impact

- Manifest: 146 -> ~130 with console Option A, ~123 with Option B
  (-8 console: `LiveCommandEnvironment` x7 and `DefaultCommands` ctor; -8 dead
  typed adapters: `GtaOutputs` x3, `GtaWorldQueries` x2, `GtaPeds` x2,
  `GtaWorld::GetGroundHeight` x1; -7 keyboard/clipboard for Option B).
  Replaced natives keep one entry each as typed wrappers; they do not reduce the
  count.
- `New-CoverageInventory.ps1` also hard-codes adapter type names that may not
  call the engine; prune the list when types are deleted.
- Test project: ~8 055 -> ~6 200-6 500 lines; the mutation runner's workload,
  survivors and "no coverage" entries shrink with the code.
- `ThorTest/ModUtilsTest.csproj` keeps `Threshold=100`; no gate change is
  proposed.

## Appendix C: open questions for the owner

1. Is the console's latent extensibility (third-party command registration, C#
   snippet evaluation) something you want to keep? Nothing shipped uses it, but
   it is a deliberate capability of the inherited framework.
2. Should F8 become a live log viewer, or is the current two-line overlay plus
   SHVDNE's F4 console sufficient?
3. Is the invisible Thruster pathfinder worth its runtime cost and failure mode
   (model load timeout pauses Thor), or should it be replaced by raycast steering
   along the hammer's travel? That would be a behavior change and needs live
   comparison.
4. Do you want the rope anchor prototyped against `Rope.PinVertex`? It is the one
   change that could remove a created entity per rope attach, but its semantics
   are unverified offline.
