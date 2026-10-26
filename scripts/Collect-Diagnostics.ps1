[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$GameDirectory,
    [string]$Destination = (Join-Path (Split-Path -Parent $PSScriptRoot) ('artifacts/diagnostics-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '.zip')),
    [string]$BridgeDirectory = (Join-Path $env:LOCALAPPDATA 'GTAVMods/Bridge'),
    [switch]$IncludeAgentScreenshots
)
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path -LiteralPath $GameDirectory).Path
$exe = Join-Path $root 'GTA5_Enhanced.exe'
if (!(Test-Path -LiteralPath $exe)) { throw 'GTA5_Enhanced.exe not found.' }
$destinationPath = [IO.Path]::GetFullPath($Destination)
$stage = Join-Path ([IO.Path]::GetDirectoryName($destinationPath)) ('diagnostics-stage-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $stage -Force | Out-Null
$warnings = [Collections.Generic.List[string]]::new()
$files = @(@('ScriptHookV.log','ScriptHookVDotNet.log','asiloader.log','DirectStorageFix.log','OpenRPF.log') |
    ForEach-Object { Join-Path $root $_ } | Where-Object { Test-Path -LiteralPath $_ })
$logDir = Join-Path $root 'scripts/Logs'
if (Test-Path -LiteralPath $logDir) {
    $files += @(Get-ChildItem -LiteralPath $logDir -File -Filter 'Thor-*.jsonl' | Sort-Object LastWriteTimeUtc -Descending | Select-Object -First 10 -ExpandProperty FullName)
}
$fallback = Join-Path $env:LOCALAPPDATA 'GTAVMods/Logs'
if (Test-Path -LiteralPath $fallback) {
    $files += @(Get-ChildItem -LiteralPath $fallback -File -Filter 'Thor-*.jsonl' | Sort-Object LastWriteTimeUtc -Descending | Select-Object -First 10 -ExpandProperty FullName)
}
foreach ($path in $files) {
    try { Copy-Item -LiteralPath $path -Destination $stage }
    catch { $warnings.Add([IO.Path]::GetFileName($path) + ': ' + $_.Exception.Message) }
}
if (Test-Path -LiteralPath $BridgeDirectory) {
    $agentStage = Join-Path $stage 'agent-bridge'
    $null = New-Item -ItemType Directory -Path $agentStage
    $agentFiles = @(Get-ChildItem -LiteralPath $BridgeDirectory -File -Filter 'bridge-*.json')
    foreach ($session in (Get-ChildItem -LiteralPath $BridgeDirectory -Directory -Filter 'session-*' | Sort-Object LastWriteTimeUtc -Descending | Select-Object -First 3)) {
        $null = New-Item -ItemType Directory -Path (Join-Path $agentStage $session.Name)
        $agentFiles += @(Get-ChildItem -LiteralPath $session.FullName -File | Where-Object { $_.Extension -eq '.json' -or ($IncludeAgentScreenshots -and $_.Extension -eq '.png') })
    }
    foreach ($file in $agentFiles) {
        $relative = $file.FullName.Substring((Get-Item -LiteralPath $BridgeDirectory).FullName.TrimEnd('\').Length + 1)
        try { Copy-Item -LiteralPath $file.FullName -Destination (Join-Path $agentStage $relative) }
        catch { $warnings.Add('agent-bridge/' + $relative + ': ' + $_.Exception.Message) }
    }
}
$hashes = foreach ($relative in @('GTA5_Enhanced.exe','ScriptHookV.dll','ScriptHookVDotNet.asi','ScriptHookVDotNet3.dll','DirectStorageFix.asi','OpenRPF.asi','scripts/Thor.dll','scripts/ModUtils.dll','scripts/AgentBridge.dll','scripts/GameBridge.dll')) {
    $path = Join-Path $root $relative
    if (Test-Path -LiteralPath $path) { [PSCustomObject]@{File=$relative;SHA256=(Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash;Version=(Get-Item -LiteralPath $path).VersionInfo.FileVersion} }
}
[PSCustomObject]@{CapturedUtc=[DateTime]::UtcNow.ToString('O');GameVersion=(Get-Item -LiteralPath $exe).VersionInfo.FileVersion;GameRunning=[bool](Get-Process GTA5_Enhanced -ErrorAction SilentlyContinue);Files=@($hashes);CopyWarnings=@($warnings)} |
    ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $stage 'manifest.json') -Encoding UTF8
Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $destinationPath -Force
Write-Host "Diagnostics: $destinationPath"
Write-Host 'Contains local game/mod logs and binary hashes. No saves or account files are collected. Review before sharing.'
