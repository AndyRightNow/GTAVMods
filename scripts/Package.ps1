[CmdletBinding()]
param([ValidateSet('Debug', 'Release')][string]$Configuration = 'Release')
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$buildRoot = Join-Path $repoRoot "Thor/bin/$Configuration/net48"
$artifactRoot = Join-Path $repoRoot 'artifacts'
# A fresh staging directory prevents stale assemblies entering a later package.
$stage = Join-Path $artifactRoot ([Guid]::NewGuid().ToString('N'))
$scripts = Join-Path $stage 'scripts'
New-Item -ItemType Directory -Path (Join-Path $scripts 'Thor') -Force | Out-Null
$assemblies = Get-Content -LiteralPath (Join-Path $buildRoot 'package-files.txt') | Sort-Object -Unique
foreach ($name in $assemblies) {
    if ([IO.Path]::GetFileName($name) -ne $name -or $name -notlike '*.dll' -or $name -like 'ScriptHook*') {
        throw "Unexpected assembly in package manifest: $name"
    }
    $source = Join-Path $buildRoot $name
    if (!(Test-Path -LiteralPath $source)) { throw "Build output missing: $source" }
    Copy-Item -LiteralPath $source -Destination $scripts
}
foreach ($name in @('catch-hammer.wav', 'hammer-close-to-player.wav', 'whirling-hammer.wav')) {
    Copy-Item -LiteralPath (Join-Path $buildRoot "Thor/$name") -Destination (Join-Path $scripts 'Thor')
}
Copy-Item -LiteralPath (Join-Path $repoRoot 'README.md'), (Join-Path $repoRoot 'THIRD-PARTY-NOTICES.md') -Destination $stage
$assets = Join-Path $stage 'Enhanced-Mjolnir-assets'
New-Item -ItemType Directory -Path $assets -Force | Out-Null
Copy-Item -Path (Join-Path $repoRoot 'Thor/resources/enhanced/*') -Destination $assets
Copy-Item -LiteralPath (Join-Path $repoRoot 'scripts/Collect-Diagnostics.ps1') -Destination $stage
Copy-Item -LiteralPath (Join-Path $repoRoot 'scripts/Invoke-Gta.ps1') -Destination $stage
Copy-Item -LiteralPath (Join-Path $repoRoot 'scripts/Install-AgentService.ps1') -Destination $stage
$agent = Join-Path $scripts 'GtaUse'
$null = New-Item -ItemType Directory -Path (Join-Path $agent 'tools/gta_use') -Force
foreach ($name in @('pyproject.toml', 'uv.lock', '.python-version', 'gta-service.py')) {
    Copy-Item -LiteralPath (Join-Path $repoRoot $name) -Destination $agent
}
Get-ChildItem -LiteralPath (Join-Path $repoRoot 'tools/gta_use') -Filter '*.py' -File |
    Copy-Item -Destination (Join-Path $agent 'tools/gta_use')
Copy-Item -LiteralPath (Join-Path $repoRoot 'tools/gta_use/examples') -Destination (Join-Path $agent 'tools/gta_use/examples') -Recurse
$guides = Join-Path $stage 'docs'
New-Item -ItemType Directory -Path $guides -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $repoRoot 'docs/gta-use.md'), (Join-Path $repoRoot 'docs/world-state.md') -Destination $guides
$zip = Join-Path $artifactRoot 'GTAVMods-Enhanced.zip'
Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $zip -Force
Write-Host "Package: $zip"
