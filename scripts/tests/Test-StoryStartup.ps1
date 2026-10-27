[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$fixture = Join-Path ([IO.Path]::GetTempPath()) ('GTAVMods-Startup-' + [Guid]::NewGuid().ToString('N'))
$game = Join-Path $fixture 'game'
$testRepo = Join-Path $fixture 'repo'
$installer = Join-Path $testRepo 'scripts/Install-StoryStartup.ps1'
$backup = Join-Path $fixture 'first-backup'
$archive = Join-Path $testRepo '.deps/story-startup/StraightToStoryMode.zip'
$target = Join-Path $game 'StraightToStoryMode.asi'
$running = $false
$assessed = $true
# Replace only OS observations. Installation, hashing, archives and rollback use
# the real scripts and filesystem, including a pinned upstream archive.
function Get-Process { param($Name, $ErrorAction) if ($running) { [pscustomobject]@{Id=123;ProcessName='GTA5_Enhanced'} } }
function Get-Item {
    param($LiteralPath)
    if ($LiteralPath -eq (Join-Path $game 'GTA5_Enhanced.exe')) {
        [pscustomobject]@{ VersionInfo=[pscustomobject]@{FileMajorPart=1;FileMinorPart=0;FileBuildPart=$(if ($assessed) {1158} else {9999});FilePrivatePart=16} }
    } else { Microsoft.PowerShell.Management\Get-Item -LiteralPath $LiteralPath }
}
function Reject([scriptblock]$Action, [string]$Message) {
    $caught = $false
    try { & $Action | Out-Null } catch { if ($_.Exception.Message -notlike "*$Message*") { throw }; $caught = $true }
    if (!$caught) { throw "Expected rejection: $Message" }
}
try {
    foreach ($path in @($game, (Split-Path -Parent $installer), (Split-Path -Parent $archive))) { $null = New-Item -ItemType Directory -Path $path -Force }
    Copy-Item -LiteralPath (Join-Path $repo 'scripts/Install-StoryStartup.ps1') -Destination $installer
    'executable boundary' | Set-Content -LiteralPath (Join-Path $game 'GTA5_Enhanced.exe')
    'loader' | Set-Content -LiteralPath (Join-Path $game 'xinput1_4.dll')
    'save stays intact' | Set-Content -LiteralPath (Join-Path $game 'SGTA50015')
    $saveHash = (Get-FileHash -LiteralPath (Join-Path $game 'SGTA50015')).Hash
    $cached = Join-Path $repo '.deps/story-startup/StraightToStoryMode.zip'
    if (Test-Path -LiteralPath $cached) { Copy-Item -LiteralPath $cached -Destination $archive }
    else { Invoke-WebRequest 'https://github.com/Chiheb-Bacha/StraightToStoryMode/releases/download/v1.0/StraightToStoryMode.zip' -OutFile $archive -UseBasicParsing }
    $archiveBytes = [IO.File]::ReadAllBytes($archive)
    $running = $true
    Reject { & $installer -GameDirectory $game -BackupDirectory $backup } 'Close GTA'
    $running = $false
    $assessed = $false
    Reject { & $installer -GameDirectory $game -BackupDirectory $backup } 'Unassessed game version'
    $assessed = $true
    Reject { & $installer -GameDirectory $game -BackupDirectory (Join-Path $game 'backup') } 'outside the game and repository'
    Reject { & $installer -GameDirectory $game -BackupDirectory (Join-Path $testRepo 'backup') } 'outside the game and repository'
    [IO.File]::WriteAllText($archive, 'bad download')
    Reject { & $installer -GameDirectory $game -BackupDirectory $backup } 'archive hash mismatch'
    if ((Test-Path -LiteralPath $backup) -or (Test-Path -LiteralPath $target)) { throw 'Rejected installation changed the game or backup.' }
    [IO.File]::WriteAllBytes($archive, $archiveBytes)
    $first = & $installer -GameDirectory $game -BackupDirectory $backup
    if ($first.Status -ne 'Installed' -or (Get-FileHash -LiteralPath $target).Hash -ne $first.SHA256) { throw 'Plugin installation failed.' }
    if (!(Test-Path -LiteralPath (Join-Path $backup 'LICENSE.txt'))) { throw 'Upstream license was lost.' }
    $again = & $installer -GameDirectory $game -BackupDirectory (Join-Path $fixture 'unused')
    if ($again.Status -ne 'AlreadyInstalled' -or (Test-Path -LiteralPath (Join-Path $fixture 'unused'))) { throw 'Exact installation was not idempotent.' }
    $installed = [IO.File]::ReadAllBytes($target)
    [IO.File]::WriteAllText($target, 'later user change')
    Reject { & $installer -GameDirectory $game -BackupDirectory $backup -Remove } 'preserve later changes'
    if ([IO.File]::ReadAllText($target) -ne 'later user change') { throw 'Rollback destroyed a later change.' }
    [IO.File]::WriteAllBytes($target, $installed)
    & $installer -GameDirectory $game -BackupDirectory $backup -Remove | Out-Null
    if (Test-Path -LiteralPath $target) { throw 'Fresh-install rollback left the plugin installed.' }
    # Replacing an existing plugin records and restores its exact bytes.
    [IO.File]::WriteAllText($target, 'previous plugin')
    $replacementBackup = Join-Path $fixture 'replacement-backup'
    & $installer -GameDirectory $game -BackupDirectory $replacementBackup | Out-Null
    $original = Join-Path $replacementBackup 'StraightToStoryMode.asi'
    [IO.File]::WriteAllText($original, 'corrupt backup')
    Reject { & $installer -GameDirectory $game -BackupDirectory $replacementBackup -Remove } 'Original backup hash mismatch'
    [IO.File]::WriteAllText($original, 'previous plugin')
    & $installer -GameDirectory $game -BackupDirectory $replacementBackup -Remove | Out-Null
    if ([IO.File]::ReadAllText($target) -ne 'previous plugin') { throw 'Previous plugin was not restored.' }
    if ((Get-FileHash -LiteralPath (Join-Path $game 'SGTA50015')).Hash -ne $saveHash) { throw 'Save was changed.' }
    Write-Host 'Story startup checks passed: running/version/path guards, corrupt download, install, idempotence, preservation and rollback.'
} finally {
    $resolved = [IO.Path]::GetFullPath($fixture)
    if (!$resolved.StartsWith([IO.Path]::GetFullPath([IO.Path]::GetTempPath()), [StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe fixture cleanup path.' }
    if (Test-Path -LiteralPath $resolved) { Remove-Item -LiteralPath $resolved -Recurse -Force }
}
