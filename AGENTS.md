# Working on GTAVMods

These instructions apply to the whole repository. This is a C# Thor/Mjolnir
**GTA V Enhanced Story Mode** mod. Read `README.md` for the user-facing guide and
`THIRD-PARTY-NOTICES.md` for asset/runtime attribution.

## Paths and compatibility

The installation verified on **2026-10-20** used:

| Item | Verified value |
| --- | --- |
| Repository on this PC | `C:\Users\Helen\repositories\GTAVMods` |
| Game on this PC | `D:\SteamLibrary\steamapps\common\Grand Theft Auto V Enhanced` |
| Game executable | `GTA5_Enhanced.exe`, **1.0.1158.16** |
| Script Hook V | **v3889.0 / 1158.13**, released July 15, 2026 |
| ScriptHookVDotNet Enhanced (SHVDNE) | **v1.1.0.6**, v3 API assembly **3.9.0.0** |
| Extra ASI plugins | **DirectStorageFix 1.2**, **OpenRPF 0.3** |
| Managed mod target | **x64 / .NET Framework 4.8** |
| Build SDK | **.NET 10**, selected by `global.json` |

These are dated verification results, not a promise of compatibility with the
latest or a future update. SHV's official page listed Enhanced 1158.13; the
installed 1158.16 executable initialized as `VER_EN_1_0_1158_13`. SHVDNE's console
banner and API assembly have different version numbers. Inspect the executable
version and current upstream releases before changing any runtime. A Steam build
ID does not establish executable compatibility.

Use these upstream sources:

- [Script Hook V](https://www.dev-c.com/gtav/scripthookv/)
- [SHVDNE release](https://github.com/Chiheb-Bacha/ScriptHookVDotNetEnhanced/releases/tag/v1.1.0.6)
- [DirectStorageFix](https://github.com/alloc8or/DirectStorageFix)
- [OpenRPF](https://www.gta5-mods.com/tools/openrpf-openiv-asi-for-gta-v-enhanced)
- [CodeWalker](https://github.com/dexyfex/CodeWalker)

## Build and package

Run commands from the repository root in PowerShell:

```powershell
$game = 'D:\SteamLibrary\steamapps\common\Grand Theft Auto V Enhanced'
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Build.ps1
```

If `dotnet` is not on PATH, pass `-DotNet 'C:\path\to\dotnet.exe'`. The portable
SDK used on this PC is at
`C:\Users\Helen\Documents\Codex\2026-10-20\c\work\tooling\dotnet\dotnet.exe`;
check that it still exists before using it. Use process-scoped execution-policy
options; do not change the machine's PowerShell policy.

`Build.ps1` verifies the pinned SHVDNE API download, restores locked NuGet
packages, prepares Python 3.14 with uv sync --frozen, builds Release, runs the tests and creates
`artifacts/GTAVMods-Enhanced.zip`. The .NET 10 SDK builds the project; it does
not replace the game's .NET Framework 4.8 runtime. Builds must not deploy files
into the game or install assemblies into the GAC. Pass -Uv to select an explicit uv executable. After deploying a package with GTA closed, run Install-AgentService.ps1 once to prepare scripts/GtaUse; the bridge then starts and supervises FastAPI automatically. See docs/gta-use.md.

- Run the build/test/package workflow after code or dependency changes. For a
  documentation-only edit, check the documented commands and `git diff --check`.
- The shared build requires 100% executable line and branch coverage for Thor,
  ModUtils and GameBridge. See `docs/coverage-gate.md` before
  changing coverage collection or the reviewed primitive boundary. Tests execute
  the production behavior against deterministic game observations; GTA natives,
  rendering, physics, actual keyboard delivery and audible playback still need
  live verification.
- Change sources and build scripts, not generated `bin`, `obj`, `.deps` or
  `artifacts` contents. Keep dependency caches, game archives, keys, saves,
  account files and gameplay logs out of commits.
- Intentional NuGet updates require `dotnet restore GTAVMods.sln --force-evaluate`
  and review of all four `packages.lock.json` files.
- A SHVDNE update also requires reviewing the release, updating the version and
  both checksums in `scripts/Restore-Dependencies.ps1`, updating compatibility
  checks, rebuilding and repeating live testing.

## Test design

- Unit tests are reserved for pure utility/helper functions: explicit inputs
  determine the result, with no mutable global state, clock reads, randomness,
  I/O, callbacks with side effects, or changes to caller-owned state. A helper's
  name does not make a stateful component eligible for unit testing.
- All other mod tests must be integration scenarios expressed from the player's
  perspective. Drive the shipped entry points with player input, frame/lifecycle
  events and explicit engine observations, then assert the expected gameplay,
  console, presentation, recovery or diagnostic outcome. Use the real cooperating
  mod components and adapters; replace only external game, device, storage and
  host boundaries. Do not replace an ability, controller, logger or other mod
  collaborator with a probe.
- Do not retain isolated module, adapter, fake-conformance or wiring tests under
  an "integration" label. Protect their useful contracts inside the player
  scenario that depends on them. Do not invent player actions or expose internal
  state solely to make otherwise unreachable code satisfy the coverage gate.
- Every test case must specify a real behavior: a meaningful starting condition
  and trigger, an observable expected outcome, and the regression it prevents.
  This applies to gameplay, engine contracts, utilities, diagnostics and CI
  guardrails. Name and organize cases by the behavior being protected.
- Keep test folders, namespaces, classes and the review catalogue aligned with
  `ThorTest/README.md`. Group player scenarios by feature, then behavior; place
  pure helpers under `PureFunctions` and shared fixtures/fakes under `Support`.
  Give each scenario one home based on its primary expectation. Feature-specific
  fallback belongs with that feature; pausing, explicit retry and script reload
  belong under lifecycle recovery. Do not recreate catch-all module, contract or
  wiring suites, or duplicate scenarios to make them appear in several groups.
- Exercise module construction and wiring through those behavior scenarios.
  Do not add standalone tests whose only outcome is successful construction,
  concrete type identity, dependency assignment, forwarding to a mock, or
  executing lines for coverage. Do not disguise such tests by renaming them.
- Use real cooperating mod components where their interaction is part of the
  behavior. Replace the game and other external boundaries with explicit,
  maintainable observations and failures. Keep command submission separate from
  observed completion; fakes must not implement the mod's decisions or simulate
  physics to manufacture success.
- Assert independent outcomes: gameplay state, externally visible commands and
  their contract values, retained or released resources, diagnostics, or rejected
  invalid input. Exact native arguments are appropriate when they specify an
  engine contract; merely repeating implementation calls is not sufficient.
- Each case and parameter variant must protect a distinct condition or failure.
  Consolidate redundant coverage while preserving useful assertions. Avoid
  unrelated omnibus scenarios, tautologies, private implementation assertions,
  and tests of a fake's own setup with no meaningful contract guarantee.
- Keep manually calibrated gameplay settings under `Thor/Tuning`, grouped by
  feature and supplied through `GameplayTuning` when composing a session. Test
  the mechanism with deliberately different supplied settings and independent
  player outcomes: honoring a delay, crossing a threshold, scaling damage or
  preserving spatial relationships. Do not copy the default animation timing,
  camera, movement or effect tables into expected-value matrices. Those defaults
  require in-game tuning; an exact number is an appropriate assertion only when
  it follows from the scenario's input or an independent engine/math contract.
- Maintain the strict 100% line and branch gate without weakening assertions or
  adding coverage exclusions to accommodate test changes. Review test quality
  separately from coverage; a covered line does not establish correct behavior.

## Install or update the mod

1. **Close GTA V before changing DLLs, runtime files or model archives.** Confirm
   `GTA5_Enhanced` is no longer running. Preserve unrelated mods and back up every
   existing file that will be replaced, recording its relative path and hash.
   Keep backups outside the repository. Never delete the whole `scripts` or
   `mods` directory to install this mod.

2. Inspect the installation:

   ```powershell
   powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Test-GameInstallation.ps1 -GameDirectory $game
   ```

   This reports the executable version, missing runtime files and pinned API
   hash. `MatchesAssessedBuild = False` on 1158.16 is expected alongside
   `MatchesSmokeTestedBuild = True`. File checks alone do not prove runtime
   compatibility.

3. Install .NET Framework 4.8 and SHVDNE's required x64 Visual C++ redistributable.
   Install compatible SHV files next to `GTA5_Enhanced.exe` using the upstream
   archive instructions. Enhanced uses the **`xinput1_4.dll` ASI loader**;
   `dinput8.dll` alone was not the verified Enhanced setup. The tested installation
   contains both supplied loader files. Do not remove known working files or
   introduce a second competing runtime without checking the loader logs.

4. Keep the complete matching SHVDNE runtime in the game root:
   `ScriptHookVDotNet.asi`, `ScriptHookVDotNet.ini`, `ScriptHookVDotNet2.dll`,
   `ScriptHookVDotNet3.dll`, and `MinHook.x64.dll`. Do not copy API DLLs into
   `scripts`. Back up and remove obsolete SHVDN API copies there if present;
   mixing Legacy and Enhanced runtimes can break loading.

5. Build the package, extract it into a fresh staging directory, and merge its
   **`scripts`** directory into the game root after making backups. Keep all
   packaged dependency DLLs together, including `Thor.dll`, `ModUtils.dll` and
   the unified `GameBridge.dll`. Back up and remove obsolete
   `scripts/DeveloperConsole.dll` and `scripts/AgentBridge.dll` when upgrading;
   leaving them loads the old F8 console or a duplicate bridge script.
   The three WAV files belong in `scripts/Thor/`.
   Do not copy only `Thor.dll`, and do not deploy the ZIP root as `scripts`.
   SHV, SHVDNE, OpenRPF and DirectStorageFix are separate installations and are
   not bundled in the mod ZIP.

6. Install **`DirectStorageFix.asi`** beside the executable for the tested setup.
   Enhanced's DirectStorage previously locked ordinary files: SHVDNE's log
   stopped updating and F4 flooded with sharing-violation exceptions.
   DirectStorageFix 1.2 resolved this on 1158.16. Confirm its initialization in
   `DirectStorageFix.log`; clearing logs alone does not resolve the lock.

7. Install **`OpenRPF.asi`** in the game root, then install the model as below.

### Custom Mjolnir model

Use `Thor/resources/enhanced/w_me_hammer.ydr` and `.ytd`, also included in the
package's `Enhanced-Mjolnir-assets` directory. The original files directly under
`Thor/resources` are Legacy assets and must not be imported into Enhanced.
`conversion.json` records checksums and the pinned CodeWalker revision
`485d56bec00262ed7fa472261cce7bbc6202b96e`; the converted resource versions are
YDR **159** and YTD **5**.

For a fresh installation without an existing archive override:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Install-HammerModel.ps1 -GameDirectory $game
```

Add `-DotNet 'C:\path\to\dotnet.exe'` if using a portable SDK. The script prepares
the pinned CodeWalker tool, copies the original archive into `mods`, and imports
the two converted files. Allow roughly 1.7 GB for this archive copy.

The active override verified on 1158.16 is:

```text
mods/update/x64/dlcpacks/patchday3ng/dlc.rpf
  x64/models/cdimages/weapons.rpf
    w_me_hammer.ydr
    w_me_hammer.ytd
```

The installer deliberately refuses an existing destination archive, including
the one already installed on this PC. Do not delete it to bypass that guard.
Back it up and merge the two assets using CodeWalker if an update is needed.
Keep original game RPFs unchanged. Archive precedence may change after game
updates; the asset tool's `find` mode can locate all hammer entries:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Prepare-AssetTool.ps1
dotnet .\tools\EnhancedAssets\bin\Release\net10.0\EnhancedAssets.dll find $game w_me_hammer
```

This scan can take time. Derive required keys only from the locally owned game
through the tool; never commit or distribute keys or original game archives.

### Optional save that skips the introduction

The tested save is Captain_Wistful's
[Fresh Start, FINAL](https://www.gta5-mods.com/misc/fresh-start-introduction-completed-savegame).
It completes Prologue and Franklin and Lamar (1.6% completion) and loads Franklin
at his original house. Its Legacy-format `SGTA50015` loaded directly in Enhanced.
The save is installed separately and is not part of this repository or package.

With the game closed, locate the actual profile under
`Documents\Rockstar Games\GTAV Enhanced\Profiles\<profile-id>`, back it up, and
copy only the save slot. Do not replace profile/account configuration files.
The profile used on this PC was `B5FD6257`; do not assume that ID on another PC.
Do not overwrite an existing slot blindly or accept a cloud overwrite prompt
without checking which copy should be preserved. This PC already has the save;
normal code updates do not require reinstalling it.

## Launch and verify in the game

1. In Steam, open GTA V Enhanced > Properties > General > Launch Options and
   include **`-nobattleye`**, preserving any other existing options. An `args.txt`
   file alone did not disable BattlEye in the tested Steam installation.
2. Launch through Steam's **Play** button and allow Rockstar Launcher to finish.
   Let the user handle sign-in, passwords and verification codes.
3. Select **Story Mode / Resume Story**. Do not enter GTA Online with this
   modded setup. If using the imported save, verify Franklin loads at his house.
4. Inspect the new `ScriptHookV.log`, `ScriptHookVDotNet.log`,
   `DirectStorageFix.log` and newest `scripts/Logs/Thor-*.jsonl`. Confirm
   `Thor.Thor` and `GTAVMods.Agent.BridgeScript` start without load errors.
   Check timestamps and session IDs; an old log is not evidence of this run.
5. Confirm custom hammer geometry, engraved textures and lightning in daylight.
   Exercise recall, catch, throw and drop, then exercise the external bridge.
   Press F9 to capture the current state and inspect the resulting log record.
6. Test Insert reload for cleanup of objects, ropes, cameras and audio. After
   runtime/plugin changes, also exit fully and test a fresh game process:
   script reload does not reload native ASI plugins.

| Action | Default control |
| --- | --- |
| Enable / disable Thor | Ctrl+A+O / Ctrl+A+F |
| Recall hammer | H |
| Throw / throw and fly | Aim + T / Aim + U |
| Drop / follow camera / whirl | Y / R / Z |
| Flight movement | X, W, Sprint, depending on state |
| External agent console / SHVDNE console | `scripts/Invoke-Gta.ps1` / F4 |
| Diagnostic snapshot / script reload | F9 / Insert |

Thor starts enabled and changes gameplay, health and weapons. Use a backed-up
test save and avoid saving test effects over progress. Restore settings changed
for testing. Remove temporary integration scripts before declaring installation
complete; never ship automatic teleport/camera test harnesses.

## Debugging and known limits

- Press **F9** as soon as a bug occurs; it records a `manual-F9` snapshot.
  Logs normally live in `scripts/Logs/Thor-*.jsonl` under the configured scripts
  directory, with fallback `%LOCALAPPDATA%\GTAVMods\Logs`.
- JSONL records contain UTC/elapsed time, session/sequence IDs, severity, events,
  state, entity handles/positions/velocities, exceptions and dropped-record
  counts. Snapshots occur every five seconds and performance summaries every ten.
  Files rotate near 5 MiB and retain the newest ten per log directory.
- Keep logging bounded: record transitions, throttle recurring errors, and avoid
  disk writes on every frame. Agent requests/results/output are durable files
  under `%LOCALAPPDATA%\GTAVMods\Bridge`; see `docs/gta-use.md` for protocol and
  controls. Do not put console history or diagnostic-only output in memory.
  Logging failures must not crash gameplay.
- On a managed exception Thor logs the failure and pauses; Ctrl+A+O retries.
  Missing audio is logged once per clip without stopping gameplay.
- Collect diagnostics locally with:

  ```powershell
  powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Collect-Diagnostics.ps1 -GameDirectory $game
  ```

  The default ZIP is under `artifacts`; `-Destination` selects another path.
  It contains game/mod logs and binary versions/hashes, excludes saves/account
  files, and uploads nothing. Inspect `manifest.json` for copy warnings and review
  logs before sharing. The collector also leaves a staging directory next to the
  ZIP. A native crash may occur after the last five-second snapshot.
- **Known unresolved issue:** a controlled high-speed throw travelled below
  terrain before recall recovered the hammer. Do not report terrain collision
  as fixed.
- Verified on 1158.16: custom model/textures/lightning, a controlled
  throw/return/catch cycle, F4/F8, F9 with the held hammer, Insert cleanup,
  a full restart and writable SHVDNE logs. The integration test invoked existing
  ability methods; it did not verify normal held-H input.
- Flight, whirling, combat, sustained held-key controls, audible playback,
  follow-camera behavior, character changes, death/respawn and long sessions
  still require broader gameplay testing. Distinguish live observations from
  unit-test results and report these limits.

## Rollback and local evidence

Close the game before rollback. Restore backed-up files or remove only files
installed by this mod whose hashes still match the install manifest. Preserve
later user changes, other mods, saves and logs.

The 2026-10-20 installation's reports, screenshots, diagnostic ZIP and backup are
outside the repository at
`C:\Users\Helen\Documents\Codex\2026-10-20\c\outputs`.
Its `GTAVMods-install-backup/Uninstall-GTAVMods.ps1` supports `-WhatIf`, validates
paths/hashes and preserves saves. Check that backup and manifest still correspond
to the installation before using them; they are machine-specific, not a
repository-provided universal uninstaller.

Remove **`-nobattleye`** separately and restore an unmodded installation before
returning to Online. The rollback script does not change Steam launch options.
