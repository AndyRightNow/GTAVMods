[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Release',
    [string]$DotNet = 'dotnet',
    [string]$Uv = 'uv',
    [string]$Cargo = 'cargo'
)
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
$repoRoot = Split-Path -Parent $PSScriptRoot
& (Join-Path $PSScriptRoot 'Restore-Dependencies.ps1')
Push-Location $repoRoot
try {
    & (Join-Path $PSScriptRoot 'Build-Rust.ps1') -Cargo $Cargo
    & $Uv sync --frozen --python 3.14
    if ($LASTEXITCODE -ne 0) { throw 'uv could not prepare the locked Python 3.14 agent environment.' }
    & $Uv run --frozen --no-sync python -c 'import sys; print(sys.executable)' | Set-Content -LiteralPath (Join-Path $repoRoot '.python-path') -Encoding UTF8
    if ($LASTEXITCODE -ne 0) { throw 'Cannot locate the uv-prepared Python interpreter.' }
    & $Uv run --frozen --no-sync python -m unittest discover -s tools/gta_use/tests -p 'test_*.py' -v
    if ($LASTEXITCODE -ne 0) { throw 'Python HTTP/WebSocket/runner scenarios failed.' }
    & (Join-Path $PSScriptRoot 'tests/Test-CoverageGate.ps1')
    & (Join-Path $PSScriptRoot 'tests/Test-BuildInputManifest.ps1')
    & (Join-Path $PSScriptRoot 'tests/Test-DiagnosticsCollection.ps1')
    & (Join-Path $PSScriptRoot 'tests/Test-StoryStartup.ps1')
    $coverageRoot = Join-Path $repoRoot 'artifacts/coverage'
    $runName = [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss') + '-' + [Guid]::NewGuid().ToString('N')
    $runDirectory = Join-Path $coverageRoot $runName
    $null = New-Item -ItemType Directory -Path $runDirectory
    $runDirectory | Set-Content -LiteralPath (Join-Path $coverageRoot 'latest-run.txt') -Encoding UTF8
    $inputs = @(& (Join-Path $PSScriptRoot 'Get-BuildInputManifest.ps1')) | ConvertTo-Json -Depth 4
    $inputs | Set-Content -LiteralPath (Join-Path $runDirectory 'build-inputs.json') -Encoding UTF8
    & $DotNet restore GTAVMods.sln --locked-mode
    if ($LASTEXITCODE -ne 0) { throw 'Dependency restore failed.' }
    & (Join-Path $PSScriptRoot 'tests/Test-CoverageInventory.ps1') -DotNet $DotNet
    $inventoryTarget = Join-Path $PSScriptRoot 'Coverage.Inventory.targets'
    & $DotNet build GTAVMods.sln --configuration $Configuration --no-restore `
        "-p:CustomAfterMicrosoftCommonTargets=$inventoryTarget" "-p:CoverageRunDirectory=$runDirectory"
    if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
    & (Join-Path $PSScriptRoot 'New-CoverageInventory.ps1') -RunDirectory $runDirectory
    $coverageOutput = Join-Path $runDirectory 'coverage'
    & $DotNet test ThorTest/ModUtilsTest.csproj --configuration $Configuration --no-build --no-restore `
        --logger 'trx;LogFileName=unit-tests.trx' --results-directory $runDirectory `
        -p:CollectCoverage=true "-p:CoverletOutput=$coverageOutput"
    $testExitCode = $LASTEXITCODE
    # Run the independent gate even when the collector/test runner fails, so the
    # artifact explains missing data, uncovered paths, or failed/zero tests.
    $gateFailure = $null
    try { & (Join-Path $PSScriptRoot 'Test-Coverage.ps1') -RunDirectory $runDirectory }
    catch { $gateFailure = $_ }
    if ($null -ne $gateFailure) { throw $gateFailure }
    if ($testExitCode -ne 0) { throw "Test/coverage collection failed (exit $testExitCode). See $runDirectory." }
    $currentInputs = @(& (Join-Path $PSScriptRoot 'Get-BuildInputManifest.ps1')) | ConvertTo-Json -Depth 4
    if ($inputs -cne $currentInputs) { throw 'Build inputs changed during validation. Run the build again.' }
    & (Join-Path $PSScriptRoot 'Package.ps1') -Configuration $Configuration
} finally {
    Pop-Location
}
