# Resume Story automatically

GTA V Enhanced can skip the splash/legal screens and landing menu and load the
last Story save on launch. This happens before SHVDNE and the GTA use bridge
start, so it uses the separate, open-source
[StraightToStoryMode v1.0 ASI](https://github.com/Chiheb-Bacha/StraightToStoryMode/releases/tag/v1.0).
The bridge then starts its Rust service when the scripts initialize.

Close GTA and run from the repository in PowerShell:

```powershell
$game = 'D:\SteamLibrary\steamapps\common\Grand Theft Auto V Enhanced'
$backup = Join-Path $env:USERPROFILE ('Documents\GTAVMods-backups\story-startup-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Install-StoryStartup.ps1 -GameDirectory $game -BackupDirectory $backup
```

The installer supports the assessed **1.0.1158.16** executable and requires the
Enhanced `xinput1_4.dll` ASI loader. It verifies both archive and ASI SHA-256,
retains the upstream license/source revision, and records any replaced file
and its hash outside the game and repository. An exact existing installation
is a no-op. Save files, settings and Steam launch options are untouched.
The plugin is fetched separately and is not bundled in the mod ZIP.

Launch through Steam **Play**, with the existing Story Mode mod setup and
`-nobattleye`. No Story/Resume menu selection should be needed. Resource loading
and any Rockstar authentication still need to finish. Do not use
`-StraightIntoFreemode`: that selects multiplayer.

Check the new `StraightToStoryMode.log` for `Patch successful!`, then the new
SHVDNE session and GTA use discovery for the actual game process. A fresh state
pack and screenshot establish that the last Story save loaded; plugin loading
alone does not prove this. The diagnostics collector includes the startup log
and plugin hash. Repeat a cold launch after game/runtime updates, and reassess
the version guard against upstream source before changing it.

To restore the startup menu, close GTA and use the same backup directory:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Install-StoryStartup.ps1 -GameDirectory $game -BackupDirectory $backup -Remove
```

Removal restores the previous plugin if one existed, or removes only the
installed plugin. It refuses a changed target or damaged backup to preserve
later edits. Keep the backup directory until rollback is no longer needed.

## Live verification, 2026-10-27

On Enhanced 1.0.1158.16, a cold launch from Steam Play reached Franklin at his
original house without any startup-menu input. The new startup log reported
`Patch successful!`; SHVDNE started the bridge and Thor at 14:08:02 local time,
about 114 seconds after Play. That run preceded the Rust migration; its Python companion served
a fresh state pack with `playerAvailable=true`, `pauseMenu=false`,
`playerControl=true`, and the expected house coordinates. A screenshot confirmed
the scene. Only after observing successful startup did the test disable Thor and
pause for profiling. This is one observed launch, not a loading-time guarantee.

The local state, screenshot reference and fresh runtime logs are under
`C:\Users\Helen\Documents\Codex\2026-10-27\gta-use-performance\startup`.
The installed plugin's rollback record is under
`C:\Users\Helen\Documents\Codex\2026-10-27\story-startup-backup`.
