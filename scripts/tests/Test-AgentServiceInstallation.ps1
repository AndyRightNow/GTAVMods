[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$installer = Join-Path $repo 'scripts/Install-AgentService.ps1'
$fixture = Join-Path ([IO.Path]::GetTempPath()) ('GTAVMods-ServiceInstall-' + [Guid]::NewGuid().ToString('N'))
$game = Join-Path $fixture 'game'
$project = Join-Path $game 'scripts/GtaUse'
$originalLocal = $env:LOCALAPPDATA
$running = $false
$uvFails = $false
# Replace only OS process observations and the external uv invocation. Backups,
# hashes, retirement, interpreter selection and repeat installation use real I/O.
function Get-Process { param($Name, $ErrorAction) if ($running) { [pscustomobject]@{Id=123;ProcessName='GTA5_Enhanced'} } }
function PreparedUv {
    if ($uvFails) { $global:LASTEXITCODE = 1; return }
    $global:LASTEXITCODE = 0
    if ($args[0] -eq 'run') { Join-Path $fixture 'python.exe' }
}
function Reject([scriptblock]$Action, [string]$Message) {
    $caught = $false
    try { & $Action | Out-Null } catch { if ($_.Exception.Message -notlike "*$Message*") { throw }; $caught = $true }
    if (!$caught) { throw "Expected rejection: $Message" }
}
try {
    $env:LOCALAPPDATA = Join-Path $fixture 'local'
    $null = New-Item -ItemType Directory -Path (Join-Path $project 'tools/gta_use/__pycache__'), (Join-Path $project '__pycache__') -Force
    'locked environment' | Set-Content -LiteralPath (Join-Path $project 'uv.lock')
    $retired = @('gta-service.py', 'tools/gta_use/service.py', '.rust-server-path',
        '__pycache__/gta-service.cpython-314.pyc', 'tools/gta_use/__pycache__/service.cpython-314.pyc')
    $preserved = @('tools/gta_use/client.py', 'tools/gta_use/__pycache__/client.cpython-314.pyc', 'my-policy.py')
    foreach ($relative in ($retired + $preserved)) { "original $relative" | Set-Content -LiteralPath (Join-Path $project $relative) }
    $hashes = @{}
    foreach ($relative in ($retired + $preserved)) { $hashes[$relative] = (Get-FileHash -LiteralPath (Join-Path $project $relative)).Hash }
    $running = $true
    Reject { & $installer -GameDirectory $game -Uv PreparedUv } 'Close GTA'
    $running = $false
    Reject { & $installer -GameDirectory $game -Uv PreparedUv } 'Rust server missing'
    'packaged server' | Set-Content -LiteralPath (Join-Path $project 'gta-use-server.exe')
    $uvFails = $true
    Reject { & $installer -GameDirectory $game -Uv PreparedUv } 'uv could not prepare'
    foreach ($relative in $retired) {
        if ((Get-FileHash -LiteralPath (Join-Path $project $relative)).Hash -ne $hashes[$relative]) { throw 'Failed setup changed legacy files.' }
    }
    $uvFails = $false
    & $installer -GameDirectory $game -Uv PreparedUv
    $backups = Join-Path $env:LOCALAPPDATA 'GTAVMods/Backups'
    $directories = @(Get-ChildItem -LiteralPath $backups -Directory)
    if ($directories.Count -ne 1) { throw 'Expected one recoverable upgrade backup.' }
    $backup = $directories[0].FullName
    $manifest = Get-Content -LiteralPath (Join-Path $backup 'manifest.json') -Raw | ConvertFrom-Json
    if ($manifest.project -ne $project -or $manifest.retired.Count -ne $retired.Count) { throw 'Incomplete upgrade manifest.' }
    foreach ($file in $manifest.retired) {
        if (Test-Path -LiteralPath (Join-Path $project $file.relative)) { throw 'Legacy server can still be loaded.' }
        if ((Get-FileHash -LiteralPath (Join-Path $backup $file.relative)).Hash -ne $file.sha256 -or $file.sha256 -ne $hashes[$file.relative.Replace('\', '/')]) { throw 'Original bytes were not preserved.' }
    }
    foreach ($relative in $preserved) {
        if ((Get-FileHash -LiteralPath (Join-Path $project $relative)).Hash -ne $hashes[$relative]) { throw 'Upgrade modified an unrelated file.' }
    }
    if ((Get-Content -LiteralPath (Join-Path $project '.python-path') -Raw).Trim() -ne (Join-Path $fixture 'python.exe')) { throw 'Policy interpreter was not recorded.' }
    & $installer -GameDirectory $game -Uv PreparedUv
    if (@(Get-ChildItem -LiteralPath $backups -Directory).Count -ne 1) { throw 'Repeat preparation created a redundant backup.' }
    Write-Host 'Agent service installation checks passed: running/missing/failed setup guards, verified legacy backup/removal, SDK preservation and repeat preparation.'
} finally {
    $env:LOCALAPPDATA = $originalLocal
    $resolved = [IO.Path]::GetFullPath($fixture)
    if (!$resolved.StartsWith([IO.Path]::GetFullPath([IO.Path]::GetTempPath()), [StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe fixture cleanup path.' }
    if (Test-Path -LiteralPath $resolved) { Remove-Item -LiteralPath $resolved -Recurse -Force }
}
