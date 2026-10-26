[CmdletBinding()]
param([Parameter(Mandatory)][string]$GameDirectory, [string]$Uv = 'uv')
$ErrorActionPreference = 'Stop'
if (Get-Process -Name GTA5_Enhanced -ErrorAction SilentlyContinue) { throw 'Close GTA before preparing its Python environment.' }
$project = Join-Path (Resolve-Path -LiteralPath $GameDirectory).Path 'scripts/GtaUse'
if (!(Test-Path -LiteralPath (Join-Path $project 'uv.lock'))) { throw 'Extract the complete mod package before preparing the agent service.' }
& $Uv sync --project $project --frozen --python 3.14
if ($LASTEXITCODE -ne 0) { throw 'uv could not prepare the locked Python 3.14 service.' }
& $Uv run --project $project --frozen --no-sync python -c 'import sys; print(sys.executable)' | Set-Content -LiteralPath (Join-Path $project '.python-path') -Encoding UTF8
if ($LASTEXITCODE -ne 0) { throw 'Cannot locate the prepared Python interpreter.' }
Write-Host 'GTA use is prepared. The bridge will start and supervise FastAPI automatically on the next Story Mode launch.'
