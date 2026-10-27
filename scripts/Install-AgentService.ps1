[CmdletBinding()]
param([Parameter(Mandatory)][string]$GameDirectory, [string]$Uv = 'uv')
$ErrorActionPreference = 'Stop'
if (Get-Process -Name GTA5_Enhanced -ErrorAction SilentlyContinue) { throw 'Close GTA before preparing its Python environment.' }
$project = Join-Path (Resolve-Path -LiteralPath $GameDirectory).Path 'scripts/GtaUse'
if (!(Test-Path -LiteralPath (Join-Path $project 'uv.lock'))) { throw 'Extract the complete mod package before preparing the agent service.' }
$server = Join-Path $project 'gta-use-server.exe'
if (!(Test-Path -LiteralPath $server -PathType Leaf)) { throw 'Rust server missing. Extract the complete mod package first.' }
& $Uv sync --project $project --frozen --python 3.14
if ($LASTEXITCODE -ne 0) { throw 'uv could not prepare the locked Python 3.14 SDK and policy environment.' }
& $Uv run --project $project --frozen --no-sync python -c 'import sys; print(sys.executable)' | Set-Content -LiteralPath (Join-Path $project '.python-path') -Encoding UTF8
if ($LASTEXITCODE -ne 0) { throw 'Cannot locate the prepared Python interpreter.' }
# Retire only this project's obsolete Python server and selector. Preserve other
# scripts and record recoverable copies outside the repository before removal.
$legacy = @('gta-service.py', 'tools/gta_use/service.py', '.rust-server-path')
foreach ($cache in @('__pycache__', 'tools/gta_use/__pycache__')) {
    $directory = Join-Path $project $cache
    if (Test-Path -LiteralPath $directory -PathType Container) {
        $legacy += @(Get-ChildItem -LiteralPath $directory -File | Where-Object { $_.Name -match '^(gta-service|service)\..*\.pyc$' } |
            ForEach-Object { Join-Path $cache $_.Name })
    }
}
$retired = @($legacy | Where-Object { Test-Path -LiteralPath (Join-Path $project $_) -PathType Leaf })
if ($retired.Count) {
    $backup = Join-Path $env:LOCALAPPDATA ('GTAVMods/Backups/agent-service-' + [Guid]::NewGuid().ToString('N'))
    $manifest = @(foreach ($relative in $retired) {
        $source = Join-Path $project $relative
        $destination = Join-Path $backup $relative
        $null = New-Item -ItemType Directory -Force -Path (Split-Path -Parent $destination)
        Copy-Item -LiteralPath $source -Destination $destination
        $hash = (Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash
        if ((Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash -ne $hash) { throw 'Legacy service backup verification failed.' }
        [pscustomobject]@{relative=$relative;sha256=$hash}
    })
    [pscustomobject]@{project=$project;retired=$manifest} | ConvertTo-Json -Depth 4 |
        Set-Content -LiteralPath (Join-Path $backup 'manifest.json') -Encoding UTF8
    foreach ($relative in $retired) { Remove-Item -LiteralPath (Join-Path $project $relative) }
    Write-Host "Obsolete server files backed up to $backup"
}
Write-Host 'GTA use is prepared. The bridge will start and supervise the packaged Rust server automatically. Python 3.14 runs the SDK and agent policies.'
