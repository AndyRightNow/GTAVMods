# GTAVMods — Thor for GTA V Enhanced

C# Story Mode mod with Mjolnir throwing/recall, flight, lightning and an external
agent console. [GTA use](docs/gta-use.md) provides timed controls, pause/inspection,
screenshots, C# commands and script reload through an automatically started
HTTP/WebSocket service powered by [Rust/Axum](docs/rust-service.md),
with uv-managed Python 3.14 checkpoint scripts.

On 2026-10-27, GTA use completed the full **Repossession** Story mission using
Python segments, observed world state, cover combat, driving and normal game
controls. [Live results and limitations](docs/gta-use.md#live-verification-repossession-2026-10-27)
describe checkpoint retries, screenshot evidence and server/watchdog recovery.

On 2026-10-29, the pre-extraction version of the [single Python policy](tools/gta_use/examples/repossession.py)
completed Repossession from the original house save in **17m 4s**, with no
intermediate pause, retry or host input. [Replay evidence and conditions](docs/continuous-repossession.md)
separate that passing attempt from the failed runs used to develop it.
The current example composes reusable SDK operations for vehicle access, settled
cover/fire, continuous interception and evidence capture; its mission routes and
tactical choices remain in the policy.

Separate initial [DeepSeek 1017 Max and Terra 6.1 Max evaluations](docs/agent-evaluation.md)
did not finish Repossession. A later assisted Terra Max continuation did, with a
verified Mission Passed screenshot, one mission retry and recorded user/parent
help. Their traces informed compact inspection, aim/cover feedback, continuous
driving and bounded pursuit. The report separates completion, elapsed time,
retries and assistance; it does not claim reliable autonomous campaign play.

The repository skill at [.agents/skills/gta-use](.agents/skills/gta-use/SKILL.md)
covers operation and supervised Python policies. See [bridge profiling](docs/bridge-performance.md)
for reproducible live latency, CPU, allocation and process-memory measurements.
To skip the startup screens and resume Story automatically, install the
[optional startup helper](docs/story-startup.md).

## Compatibility status

The port targets **ScriptHookVDotNet Enhanced v1.1.0.6**, using its v3 API assembly
**3.9.0.0**. It builds as **x64 / .NET Framework 4.8**, the runtime hosted by SHVDNE.
The .NET 10 SDK is used only to build; installing the .NET 10 runtime does not
replace the game's .NET Framework requirement.

Upstream checked on **2026-10-20**:

| Component | Assessed target |
| --- | --- |
| GTA V Enhanced executable | 1.0.1158.13 |
| Script Hook V | v3889.0 / 1158.13, released July 15, 2026 |
| ScriptHookVDotNet Enhanced | v1.1.0.6 |

**A Story Mode smoke test was performed on executable 1.0.1158.16 on 2026-10-20.**
Script Hook V initialized successfully and identified it as `VER_EN_1_0_1158_13`.
SHVDNE loaded `Thor.Thor` and both developer-console scripts; the separate F4 and
F8 consoles opened in-game. A post-introduction save loaded Franklin at his house.
The converted custom Mjolnir rendered in daylight and completed a throw/recall/catch
cycle. These controlled checks used a temporary integration script to position the
hammer and invoke the existing throw/return paths; normal held-H input was not
verified. Insert reloaded the scripts and cleaned up active objects/effects.
DirectStorageFix restored SHVDNE log writes. Structured logs captured the test
with no dropped records. Flight, whirling, combat and audible sound still need
normal gameplay testing.

This does not certify every ability or future game update. The official SHV page
still lists 1.0.1158.13. A Steam build ID is not an
executable version; check the installed executable and current runtime support
before installing on another build.

Sources: [Script Hook V](https://www.dev-c.com/gtav/scripthookv/),
[SHVDNE release](https://github.com/Chiheb-Bacha/ScriptHookVDotNetEnhanced/releases/tag/v1.1.0.6),
[SHVDNE requirements and installation](https://github.com/Chiheb-Bacha/ScriptHookVDotNetEnhanced),
[Rockstar title update notes](https://support.rockstargames.com/articles/4vRqEDvjUs9h7nqRUgc8YO/gtav-title-update-1-73-notes-ps5-ps4-xbox-series-x-or-s-xbox-one-pc-enhanced-legacy).

## Build and test

On Windows with the .NET 10 SDK, .NET Framework 4.8, [uv](https://docs.astral.sh/uv/)
and rustup (Rust 1.99.1 is selected by `rust-toolchain.toml`):

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Build.ps1
# A portable SDK can be selected explicitly:
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Build.ps1 -DotNet 'C:\path\to\dotnet.exe'
```

This verifies a pinned SHVDNE download by SHA-256, restores locked NuGet packages,
prepares locked Python 3.14 dependencies, builds all projects, runs the Python and
.NET tests, and writes `artifacts/GTAVMods-Enhanced.zip`.
The API is cached under `.deps/`; it is not copied into the mod's `scripts` folder.
NAudio 2.4.0 is pinned because it supports .NET Framework. Its transitive runtime
assemblies are included using a manifest generated by MSBuild. Builds do not copy
files into a game installation or modify the global assembly cache.

For intentional NuGet dependency updates, edit the package references, run
`dotnet restore GTAVMods.sln --force-evaluate`, and review all five lock files.
To update SHVDNE, review its release notes, update the version and both checksums
in `Restore-Dependencies.ps1`, update the diagnostic/API baseline, and repeat the
build and in-game checks. CI runs the same build/test/package script on Windows.

Gameplay values that need manual fine tuning live in
[`Thor/Tuning`](Thor/Tuning/README.md): animation release timing, hammer movement,
power, combat, camera framing and effects. The tests supply different settings
to verify their behavior; selecting values that feel right still requires Story
Mode testing.

## Install for Story Mode

1. Check the executable version and current Script Hook V support. Run this
   read-only check from the source repository:

   ```powershell
   powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Test-GameInstallation.ps1 -GameDirectory 'D:\SteamLibrary\steamapps\common\Grand Theft Auto V Enhanced'
   ```

2. Install .NET Framework 4.8 and the x64 Visual C++ redistributable required by
   SHVDNE. Install a compatible Script Hook V and its Enhanced `xinput1_4.dll`
   loader beside `GTA5_Enhanced.exe`, following the official archive's instructions.
   In Steam, set Properties > General > Launch Options to `-nobattleye` for modded
   Story Mode. Preserve any other existing launch options. The bundled `args.txt`
   alone did not disable BattlEye in the tested Steam launch. Do not load these
   mods in GTA Online.
3. Install the complete, matching SHVDNE release in the same game root:
   `ScriptHookVDotNet.asi`, `ScriptHookVDotNet.ini`, `ScriptHookVDotNet2.dll`,
   `ScriptHookVDotNet3.dll`, and `MinHook.x64.dll`. Keep its runtime files together;
   remove any old SHVDN API DLL copies from `scripts` before replacing the mod.
4. Close GTA and back up every file being replaced, recording its relative path
   and hash outside the repository. Extract the package's `scripts` folder into
   the game root. Keep all packaged DLLs together, including the unified
   `GameBridge.dll`. Back up and remove obsolete `scripts/DeveloperConsole.dll`
   and `scripts/AgentBridge.dll` when upgrading, to remove F8 and avoid loading a
   second bridge script. Preserve unrelated mods. The three WAV files
   belong in `scripts/Thor/`.
   With GTA still closed, run the packaged `Install-AgentService.ps1 -GameDirectory <game path>`
   to prepare the Python SDK/policy environment and retire the old Python server.
   The bridge starts and supervises the packaged Rust server automatically.
   Startup errors are written to the bridge log directory.
5. Install [DirectStorageFix 1.2](https://github.com/alloc8or/DirectStorageFix)
   beside the executable on Enhanced builds 1013.17 and later. It prevents
   DirectStorage from locking log/configuration files, including SHVDNE's log.
6. For the custom model, install [OpenRPF 0.3](https://www.gta5-mods.com/tools/openrpf-openiv-asi-for-gta-v-enhanced),
   then follow the model instructions below.
7. Start Story Mode and inspect `ScriptHookV.log`, `ScriptHookVDotNet.log`,
   `DirectStorageFix.log`, and the newest `scripts/Logs/Thor-*.jsonl`.

Sounds and the mod log resolve from SHVDNE's configured scripts directory, not
the copied DLL location used by its assembly cache. Close GTA before replacing
mod DLLs, even when Windows would allow the copy.
SHVDNE internally reports Enhanced versions with an incremented major number;
the mod log records both that API value and the normalized executable version.
DirectStorageFix addresses the existing-file lock at its source. The script
logger also uses a unique filename per reload and retries in a new file if one
is locked. If the script directory is unwritable, it falls back to
`%LOCALAPPDATA%/GTAVMods/Logs`. Persistent failures count dropped records and
back off for five seconds instead of interrupting gameplay.

## Enhanced Mjolnir model

The original Legacy assets remain in `Thor/resources`. Converted Enhanced
assets are in `Thor/resources/enhanced` and the ZIP's `Enhanced-Mjolnir-assets`.
CodeWalker commit `485d56bec00262ed7fa472261cce7bbc6202b96e` converted the YDR
to resource version 159 and the YTD to version 5. Their hashes are recorded in
`Thor/resources/enhanced/conversion.json`.

For the tested 1158.16 installation, the active hammer override is:
`mods/update/x64/dlcpacks/patchday3ng/dlc.rpf/x64/models/cdimages/weapons.rpf`.
Replace `w_me_hammer.ydr` and `w_me_hammer.ytd` inside that nested archive.
Keep the game's original RPF files unchanged. Do not place the Legacy files in
an Enhanced archive. OpenRPF redirects the game to the `mods` copy.

From a source checkout, this command prepares the pinned CodeWalker tool,
copies the original archive into `mods`, and imports the converted assets:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/Install-HammerModel.ps1 -GameDirectory 'D:\path\to\Grand Theft Auto V Enhanced'
```

It refuses to overwrite an existing override archive: if one contains other
mods, import the two files into it using CodeWalker. Asset loading precedence
may change in future game updates. The tool's `find` command locates all hammer
entries. The game archive itself is not distributed in the package.

## Gameplay diagnostics

Logs are JSON lines with UTC time, elapsed time, session ID, sequence, process
and thread, severity, event name and structured data. Each record is flushed
on write. Rotation occurs at approximately 5 MiB, retaining the newest ten
`Thor-*.jsonl` files per directory; unrelated files are preserved.

- Lifecycle: startup/build/runtime versions, enable/disable, attach/remove,
  character changes, script abort/reload and cleanup errors.
- Gameplay: state changes, recall/catch/release/throw, targets, flight/air dash,
  grabbing, rope/whirl, camera and pathfinding events.
- Resources: weapon requests/creation, animation timeouts, audio loading/play
  and complete failure exceptions.
- Snapshots every five seconds: game time, ped and hammer handles/models,
  positions/velocities/distance, health, selected weapon, state and controls.
- Performance every ten seconds: average/max script tick time, frame time and
  dropped log records. Slow ticks and repeated failures are rate limited.

Press **F9** when a bug occurs to add a `manual-F9` snapshot. Then collect a
local diagnostic ZIP (nothing is uploaded):

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/Collect-Diagnostics.ps1 -GameDirectory 'D:\path\to\Grand Theft Auto V Enhanced'
```

The collector includes game/mod logs and binary versions/hashes. Saves and
account files are excluded. The final snapshot may precede a native crash by
up to five seconds; managed logging cannot capture every engine crash.

## Controls

| Action | Default key |
| --- | --- |
| Enable / disable Thor | Ctrl+A+O / Ctrl+A+F |
| Recall hammer | H |
| Aim, then throw / throw and fly | Aim + T / Aim + U |
| Drop hammer | Y |
| Hammer follow camera | R |
| Whirl hammer | Z |
| Flight movement | X, W, Sprint (context dependent) |
| SHVDNE console | F4 |
| Record diagnostic snapshot | F9 |
| Reload scripts | Insert or external `reload` |

Thor starts enabled. The old experimental test-object key bindings are excluded
from production builds. On an exception Thor pauses, logs the error, and allows
Ctrl+A+O to retry. Missing audio is logged once per clip and does not stop gameplay.

## GTA use: external agent controls

F8 has been replaced by an automatically started Rust/Axum HTTP/WebSocket service
and a Python 3.14 command utility/SDK. Prepare the locked environment once with
uv through `Install-AgentService.ps1`; the bridge starts, reuses and supervises
the server with the game. `describe` lists built-in and registered commands;
`step` advances a timed action, pauses and returns game state; `capture` saves
a PNG; `command` executes trusted local C#; `reload` requests a fresh managed
session. Python segments support continuous reactive control with deadlines,
progress checks and file-based checkpoint evidence. See the
[installation, protocol and examples](docs/gta-use.md).

`scripts/Invoke-Gta.ps1` remains a direct local diagnostic client for inspecting
the private game transport if the Rust service cannot start.

`state` returns a structured JSON world tree with player pose, nearby entities,
weapons/ammo, vehicle condition, navigation markers and collision probes.
Select walking/driving/combat profiles or individual sections and bounds to
avoid unnecessary scans. See [state packs and query filters](docs/world-state.md).

Requests and responses are ephemeral. Immediate actions return in one RPC;
timed sequences retain step results, screenshots and console output under
`%LOCALAPPDATA%\GTAVMods\Bridge`. Its discovery file points to the current session
and JSONL log. Diagnostics use a bounded background writer with no forced durable
flushes. Native input, batches and pause/cleanup run on the script thread. The
upstream SHVDNE F4 console remains a separate runtime feature.

## Broader gameplay checks

The controlled high-speed throw travelled below the map before recall recovered
it. Terrain collision at speed still needs investigation; the diagnostic bundle
records that trajectory. A full restart successfully loaded the post-introduction
save and wrote to the existing SHVDNE log with DirectStorageFix installed.

- After future updates, load Story Mode and check all script assemblies for API errors.
- Recall, catch, throw, drop and whirl the hammer; verify all three sound effects.
- Fly, attack targets, summon lightning and toggle the follow camera.
- Toggle the ability, change characters, die and respawn; verify health and controls.
- Reload scripts from SHVDNE while flying/whirling/viewing the follow camera;
  verify cameras, ropes, audio and hidden pathfinder entities are cleaned up.
- Exercise external `step`, `observe`, `capture`, `command`, `cancel` and `reload`.
  Check new session IDs and logs after reload and a full restart. Confirm F8 is gone.

The shared build requires **100% executable line and branch coverage** in Thor,
ModUtils and GameBridge. It fails before packaging if tests
fail, a line or branch is uncovered, or the fresh coverage report does not match
the compiled source inventory. CI preserves reports even on failure.

Agents can use the repository [gta-use skill](.agents/skills/gta-use/SKILL.md)
for session discovery, selective world observations, supervised Python policies,
mission checkpoints and recovery. The managed scheduler and game adapters ship
together in `GameBridge.dll`; the automatically started Rust companion hosts
the Python-facing HTTP and WebSocket API.

Gameplay runs against focused engine interfaces in tests. Scripted observations
and recorded primitive commands exercise the real recall, catch, throw, flight,
targeting, ropes, cameras, health, input interpretation and cleanup logic.
External console, timed batches, real named-pipe/PowerShell clients, transport
failures, file results, audio ownership, numerical helpers and diagnostics
are also covered.
Only pure utility functions have unit tests. All other cases are player
integration scenarios using the real cooperating mod components and adapters,
with controlled observations at the external game, device and storage boundaries.
The same production assemblies ship in the package, including `GameBridge.dll`.
The [engine contracts](docs/engine-contracts.md) define units, timing, failure
results and resource ownership. Tests distinguish requests from observed
completion, exercise clock rollover and cleanup retries, and verify native
argument mappings without simulating physics.

Only individually reviewed game/OS binding methods are outside the measurement;
no gameplay class or assembly is excluded. See [the coverage gate guide](docs/coverage-gate.md)
for the exact boundary manifest, deterministic test design and local workflow.
The [mutation workflow](docs/mutation-testing.md) tests whether assertions detect
deliberate behavior changes and records surviving mutations for review.
These tests do not certify GTA physics, rendered output, keyboard delivery,
audible playback or native compatibility. Keep the live checks above after
runtime or gameplay changes.
