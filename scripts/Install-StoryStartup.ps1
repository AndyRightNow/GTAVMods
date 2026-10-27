[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$GameDirectory,
    [Parameter(Mandatory=$true)][string]$BackupDirectory,
    [switch]$Remove
)
$ErrorActionPreference = 'Stop'
$name = 'StraightToStoryMode.asi'
$expected = '58430C118603F8E892C9142FD9C2663BD2A8C8673E4360983B76054D39D74878'
$archiveHash = '7089BD4DFAEB592A7F10D8FCE5F4E470901F815EFB365D584299C72998A244D7'
$source = 'https://github.com/Chiheb-Bacha/StraightToStoryMode/releases/download/v1.0/StraightToStoryMode.zip'
$repo = Split-Path -Parent $PSScriptRoot
$game = (Resolve-Path -LiteralPath $GameDirectory).Path.TrimEnd('\')
$backup = [IO.Path]::GetFullPath($BackupDirectory).TrimEnd('\')
foreach ($excluded in @($game, $repo)) {
    if ($backup -eq $excluded -or $backup.StartsWith($excluded + '\', [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Choose a backup directory outside the game and repository.'
    }
}
if (Get-Process -Name GTA5_Enhanced -ErrorAction SilentlyContinue) { throw 'Close GTA V before changing startup plugins.' }
$exe = Join-Path $game 'GTA5_Enhanced.exe'
if (!(Test-Path -LiteralPath $exe -PathType Leaf)) { throw 'GTA5_Enhanced.exe not found.' }
$target = Join-Path $game $name
$manifestPath = Join-Path $backup 'story-startup.json'
if ($Remove) {
    $record = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    if ($record.game -ne $game -or $record.file -ne $name -or $record.installedHash -ne $expected) { throw 'Backup manifest does not match this installation.' }
    if (!(Test-Path -LiteralPath $target) -or (Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash -ne $expected) {
        throw 'Startup plugin changed or is missing; preserve later changes and inspect manually.'
    }
    if ($record.originalHash) {
        $original = Join-Path $backup $name
        if ((Get-FileHash -LiteralPath $original -Algorithm SHA256).Hash -ne $record.originalHash) { throw 'Original backup hash mismatch.' }
        Copy-Item -LiteralPath $original -Destination $target -Force
    } else { Remove-Item -LiteralPath $target }
    [pscustomobject]@{ Status='Removed'; GameDirectory=$game; BackupDirectory=$backup }
    return
}
$info = (Get-Item -LiteralPath $exe).VersionInfo
$version = [Version]::new($info.FileMajorPart, $info.FileMinorPart, $info.FileBuildPart, $info.FilePrivatePart)
if ($version -ne [Version]'1.0.1158.16') { throw "Unassessed game version $version. Review the startup plugin before updating this guard." }
if (!(Test-Path -LiteralPath (Join-Path $game 'xinput1_4.dll') -PathType Leaf)) { throw 'The Enhanced ASI loader xinput1_4.dll is required.' }
if ((Test-Path -LiteralPath $target) -and (Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash -eq $expected) {
    [pscustomobject]@{ Status='AlreadyInstalled'; GameDirectory=$game; SHA256=$expected }
    return
}
if (Test-Path -LiteralPath $backup) { throw 'Use a fresh backup directory; existing backups are never overwritten.' }
$cache = Join-Path $repo '.deps/story-startup'
$null = New-Item -ItemType Directory -Path $cache -Force
$archive = Join-Path $cache 'StraightToStoryMode.zip'
if (!(Test-Path -LiteralPath $archive)) { Invoke-WebRequest -Uri $source -OutFile $archive -UseBasicParsing }
if ((Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash -ne $archiveHash) { throw 'Startup archive hash mismatch; no game files changed.' }
# Read only the two named entries; do not extract arbitrary paths from an archive.
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = [IO.Compression.ZipFile]::OpenRead($archive)
try {
    $binary = $zip.GetEntry($name)
    $license = $zip.GetEntry('LICENSE/LICENSE.txt')
    if ($null -eq $binary -or $null -eq $license) { throw 'Startup archive is incomplete.' }
    $memory = [IO.MemoryStream]::new()
    $stream = $binary.Open()
    try { $stream.CopyTo($memory); $bytes = $memory.ToArray() } finally { $stream.Dispose(); $memory.Dispose() }
    $sha = [Security.Cryptography.SHA256]::Create()
    try { $actual = [BitConverter]::ToString($sha.ComputeHash($bytes)).Replace('-', '') } finally { $sha.Dispose() }
    if ($actual -ne $expected) { throw 'Startup plugin hash mismatch; no game files changed.' }
    $null = New-Item -ItemType Directory -Path $backup
    $previous = $null
    if (Test-Path -LiteralPath $target) {
        $previous = (Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash
        Copy-Item -LiteralPath $target -Destination (Join-Path $backup $name)
    }
    [IO.Compression.ZipFileExtensions]::ExtractToFile($license, (Join-Path $backup 'LICENSE.txt'))
    [pscustomobject]@{
        utc=[DateTime]::UtcNow.ToString('O'); game=$game; version=$version.ToString(); file=$name
        originalHash=$previous; installedHash=$expected; archiveHash=$archiveHash; source=$source
        sourceCommit='f1acdee8288f825c31908cb25656fb45f4c3ba1b'
    } | ConvertTo-Json | Set-Content -LiteralPath $manifestPath -Encoding UTF8
    [IO.File]::WriteAllBytes($target, $bytes)
    if ((Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash -ne $expected) { throw 'Installed startup plugin hash mismatch. Restore from the recorded backup.' }
} finally { $zip.Dispose() }
[pscustomobject]@{ Status='Installed'; GameDirectory=$game; BackupDirectory=$backup; SHA256=$expected }
