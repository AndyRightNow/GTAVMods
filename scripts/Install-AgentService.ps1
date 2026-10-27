[CmdletBinding()]
param([Parameter(Mandatory)][string]$GameDirectory, [string]$Uv = 'uv', [ValidateSet('FastApi', 'Rust')][string]$Backend = 'FastApi')
$ErrorActionPreference = 'Stop'
if (Get-Process -Name GTA5_Enhanced -ErrorAction SilentlyContinue) { throw 'Close GTA before preparing its Python environment.' }
$project = Join-Path (Resolve-Path -LiteralPath $GameDirectory).Path 'scripts/GtaUse'
if (!(Test-Path -LiteralPath (Join-Path $project 'uv.lock'))) { throw 'Extract the complete mod package before preparing the agent service.' }
& $Uv sync --project $project --frozen --python 3.14
if ($LASTEXITCODE -ne 0) { throw 'uv could not prepare the locked Python 3.14 service.' }
& $Uv run --project $project --frozen --no-sync python -c 'import sys; print(sys.executable)' | Set-Content -LiteralPath (Join-Path $project '.python-path') -Encoding UTF8
if ($LASTEXITCODE -ne 0) { throw 'Cannot locate the prepared Python interpreter.' }
$selection = Join-Path $project '.rust-server-path'
if ($Backend -eq 'Rust') {
    $server = Join-Path $project 'gta-use-server.exe'
    if (!(Test-Path -LiteralPath $server -PathType Leaf)) { throw 'Rust server missing. Extract the complete mod package first.' }
    $server | Set-Content -LiteralPath $selection -Encoding UTF8
} elseif (Test-Path -LiteralPath $selection) { Remove-Item -LiteralPath $selection }
Write-Host "GTA use is prepared. The bridge will start and supervise $Backend automatically on the next Story Mode launch. Python 3.14 runs agent policies with either backend."
