[CmdletBinding()]
param([string]$ProjectDirectory)
$ErrorActionPreference = 'Stop'
if (!$ProjectDirectory) {
    $checkout = Split-Path -Parent $PSScriptRoot
    $ProjectDirectory = if (Test-Path -LiteralPath (Join-Path $checkout 'pyproject.toml')) { $checkout } else { Join-Path $PSScriptRoot 'scripts/GtaUse' }
}
$configuration = Join-Path $ProjectDirectory '.runtime-root'
$runtime = if (Test-Path -LiteralPath $configuration) { (Get-Content -LiteralPath $configuration -Raw).Trim() } else { 'artifacts/gta-use' }
if ([string]::IsNullOrWhiteSpace($runtime)) { throw 'GTA use .runtime-root is empty; configure its runtime directory.' }
if (![IO.Path]::IsPathRooted($runtime)) { $runtime = Join-Path $ProjectDirectory $runtime }
[IO.Path]::GetFullPath($runtime)
