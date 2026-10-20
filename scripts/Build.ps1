[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Release',
    [string]$DotNet = 'dotnet'
)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
& (Join-Path $PSScriptRoot 'Restore-Dependencies.ps1')
Push-Location $repoRoot
try {
    & $DotNet restore GTAVMods.sln --locked-mode
    if ($LASTEXITCODE -ne 0) { throw 'Dependency restore failed.' }
    & $DotNet build GTAVMods.sln --configuration $Configuration --no-restore
    if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
    & $DotNet test ThorTest/ModUtilsTest.csproj --configuration $Configuration --no-build --no-restore --logger 'trx;LogFileName=unit-tests.trx'
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }
    & (Join-Path $PSScriptRoot 'Package.ps1') -Configuration $Configuration
} finally {
    Pop-Location
}
