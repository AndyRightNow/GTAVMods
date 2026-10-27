[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$collector = Join-Path (Split-Path -Parent $PSScriptRoot) 'Collect-Diagnostics.ps1'
$fixture = Join-Path ([IO.Path]::GetTempPath()) ('GTAVMods-Diagnostics-' + [Guid]::NewGuid().ToString('N'))
$originalLocalAppData = $env:LOCALAPPDATA
$null = New-Item -ItemType Directory -Path (Join-Path $fixture 'game/scripts/Logs') -Force
$null = New-Item -ItemType Directory -Path (Join-Path $fixture 'local/GTAVMods/Bridge') -Force
try {
    $env:LOCALAPPDATA = Join-Path $fixture 'local'
    $game = Join-Path $fixture 'game'
    $bridge = Join-Path $env:LOCALAPPDATA 'GTAVMods/Bridge'
    foreach ($file in @('GTA5_Enhanced.exe','scripts/AgentBridge.dll','scripts/GameBridge.dll','StraightToStoryMode.asi','StraightToStoryMode.log','ScriptHookVDotNet.log','scripts/Logs/Thor-test.jsonl')) {
        'fixture' | Set-Content -LiteralPath (Join-Path $game $file)
    }
    'private save' | Set-Content -LiteralPath (Join-Path $game 'SGTA50015')
    'private account' | Set-Content -LiteralPath (Join-Path $game 'cloudsavedata.dat')
    '{}' | Set-Content -LiteralPath (Join-Path $bridge 'bridge-123.json')
    foreach ($index in 1..4) {
        $session = Join-Path $bridge "session-$index"
        $null = New-Item -ItemType Directory -Path $session
        '{}' | Set-Content -LiteralPath (Join-Path $session 'request-test.json')
        'pixels' | Set-Content -LiteralPath (Join-Path $session 'frame-test.png')
        'unrelated' | Set-Content -LiteralPath (Join-Path $session 'account.dat')
        (Get-Item -LiteralPath $session).LastWriteTimeUtc = [DateTime]::UtcNow.AddMinutes($index - 5)
    }
    foreach ($screenshots in @($false,$true)) {
        $zip = Join-Path $fixture "capture-$screenshots.zip"
        & $collector -GameDirectory $game -BridgeDirectory $bridge -Destination $zip -IncludeAgentScreenshots:$screenshots
        $extract = Join-Path $fixture "extract-$screenshots"
        Expand-Archive -LiteralPath $zip -DestinationPath $extract
        $files = @(Get-ChildItem -LiteralPath $extract -File -Recurse)
        if ($files.Name -contains 'SGTA50015' -or $files.Name -contains 'cloudsavedata.dat' -or $files.Name -contains 'account.dat') { throw 'Private files were collected.' }
        if (@($files | Where-Object Extension -eq '.png').Count -ne (3 * [int]$screenshots)) { throw 'Screenshot opt-in was not respected.' }
        if (@($files | Where-Object Name -eq 'request-test.json').Count -ne 3 -or (Test-Path -LiteralPath (Join-Path $extract 'agent-bridge/session-1'))) { throw 'Newest three sessions were not selected.' }
        if (-not (Test-Path -LiteralPath (Join-Path $extract 'agent-bridge/bridge-123.json'))) { throw 'Discovery metadata is missing.' }
        $manifest = Get-Content -LiteralPath (Join-Path $extract 'manifest.json') -Raw | ConvertFrom-Json
        if (!(Test-Path -LiteralPath (Join-Path $extract 'StraightToStoryMode.log'))) { throw 'Startup diagnostics are missing.' }
        foreach ($binary in @('scripts/AgentBridge.dll','scripts/GameBridge.dll','StraightToStoryMode.asi')) {
            $record = @($manifest.Files | Where-Object File -eq $binary)
            if ($record.Count -ne 1 -or $record[0].SHA256 -ne (Get-FileHash -LiteralPath (Join-Path $game $binary)).Hash) { throw 'Bridge binary hash is missing or incorrect.' }
        }
        if ($manifest.CopyWarnings.Count -ne 0) { throw 'Fixture collection reported copy failures.' }
    }
    Write-Host 'Diagnostics collection checks passed: session retention, discovery, opt-in screenshots, binary hashes and private-file exclusion.'
} finally {
    $env:LOCALAPPDATA = $originalLocalAppData
    $resolved = [IO.Path]::GetFullPath($fixture)
    if (-not $resolved.StartsWith([IO.Path]::GetFullPath([IO.Path]::GetTempPath()), [StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe fixture cleanup path.' }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
