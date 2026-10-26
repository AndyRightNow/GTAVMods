[CmdletBinding()]
param(
    [string]$DotNet = 'dotnet',
    [ValidateSet('Thor', 'ModUtils', 'AgentBridge', 'GameBridge')]
    [ValidateCount(1, 4)]
    [string[]]$Assembly = @('GameBridge', 'ModUtils', 'Thor', 'AgentBridge'),
    [string[]]$Mutate = @('**/*.cs'),
    [ValidateRange(1, 16)][int]$Concurrency = 2,
    [string]$CoverageRunDirectory
)
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
Set-StrictMode -Version Latest
function Assert-MutationBuildConfiguration($Inventory) {
    foreach ($compiled in (@($Inventory.modules) + @($Inventory.testAssembly))) {
        $configuration = $compiled.PSObject.Properties['configuration']
        if ($null -eq $configuration -or $configuration.Value -ne 'Release') {
            throw 'Mutation analysis requires a Release coverage build for every production and test assembly. Run scripts/Build.ps1 -Configuration Release.'
        }
    }
}
$repository = Split-Path -Parent $PSScriptRoot
$toolVersion = '5.0.0'
$toolDirectory = Join-Path $repository ".deps/mutation-tools/$toolVersion"
$dotnetCommand = (Get-Command $DotNet -ErrorAction Stop).Source
$oldEnvironment = @{}
foreach ($name in @('DOTNET_ROOT', 'DOTNET_CLI_HOME', 'NUGET_PACKAGES', 'PATH',
        'DOTNET_CLI_TELEMETRY_OPTOUT', 'DOTNET_NOLOGO')) {
    $oldEnvironment[$name] = [Environment]::GetEnvironmentVariable($name, 'Process')
}
Push-Location $repository
try {
    if (@($Assembly | Sort-Object -Unique).Count -ne $Assembly.Count) { throw 'Duplicate mutation assembly selection.' }
    if (-not $CoverageRunDirectory) {
        $CoverageRunDirectory = (Get-Content -LiteralPath 'artifacts/coverage/latest-run.txt' -Raw).Trim()
    }
    # Reject a stale/failed coverage run before evaluating assertion strength.
    & (Join-Path $PSScriptRoot 'Test-Coverage.ps1') -RunDirectory $CoverageRunDirectory
    $validatedInputs = @((Get-Content -LiteralPath (Join-Path $CoverageRunDirectory 'build-inputs.json') -Raw | ConvertFrom-Json))
    $currentInputs = @(& (Join-Path $PSScriptRoot 'Get-BuildInputManifest.ps1'))
    if (@(Compare-Object $validatedInputs $currentInputs -Property path, sha256).Count -ne 0) {
        throw 'Build/test inputs changed since coverage. Run Build.ps1 again.'
    }
    $inventoryPath = Join-Path $CoverageRunDirectory 'inventory.json'
    $inventory = Get-Content -LiteralPath $inventoryPath -Raw | ConvertFrom-Json
    Assert-MutationBuildConfiguration $inventory
    $runDirectory = Join-Path $repository ('artifacts/mutations/' +
        [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss') + '-' + [Guid]::NewGuid().ToString('N'))
    $snapshot = Join-Path $runDirectory 'source'
    $null = New-Item -ItemType Directory -Path $snapshot
    $runDirectory | Set-Content -LiteralPath (Join-Path $repository 'artifacts/mutations/latest-run.txt') -Encoding UTF8
    Copy-Item -LiteralPath $inventoryPath -Destination (Join-Path $runDirectory 'coverage-inventory.json')

    # Mutate an isolated snapshot of the working sources, including new files.
    # No checkout, generated binaries, game files or Git state are modified.
    $files = @(& git -c core.quotepath=false ls-files --cached --others --exclude-standard)
    if ($LASTEXITCODE -ne 0) { throw 'Cannot inventory the working sources.' }
    $sourceHashes = @()
    foreach ($relative in ($files | Sort-Object -Unique)) {
        $source = Join-Path $repository $relative
        if (-not (Test-Path -LiteralPath $source -PathType Leaf)) { continue }
        $destination = [IO.Path]::GetFullPath((Join-Path $snapshot $relative))
        if (-not $destination.StartsWith($snapshot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
            throw "Unexpected source path: $relative"
        }
        $null = New-Item -ItemType Directory -Path (Split-Path -Parent $destination) -Force
        Copy-Item -LiteralPath $source -Destination $destination
        $hash = (Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash
        if ($hash -ne (Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash) {
            throw "Source changed during mutation snapshot: $relative"
        }
        $sourceHashes += @{ path = $relative; sha256 = $hash }
    }
    $compilerSources = @($inventory.modules.sources) + @($inventory.testAssembly.sources)
    $compilerHashes = @(& (Join-Path $PSScriptRoot 'Copy-CompilerSources.ps1') `
        -Sources $compilerSources -RepositoryRoot $repository -SnapshotDirectory $snapshot)
    $sourceHashes = @($sourceHashes + $compilerHashes | Sort-Object path -Unique)
    foreach ($input in $validatedInputs) {
        if ((Get-FileHash -LiteralPath (Join-Path $snapshot $input.path) -Algorithm SHA256).Hash -ne $input.sha256) {
            throw "Snapshot differs from the validated build/test input: $($input.path)"
        }
    }
    $copiedInputs = @(& (Join-Path $PSScriptRoot 'Get-BuildInputManifest.ps1'))
    if (@(Compare-Object $currentInputs $copiedInputs -Property path, sha256).Count -ne 0) {
        throw 'Build/test inputs changed during mutation snapshot.'
    }
    # Recheck production and test artifacts/inputs after the copy for concurrent edits.
    & (Join-Path $PSScriptRoot 'Test-Coverage.ps1') -RunDirectory $CoverageRunDirectory
    $null = New-Item -ItemType Directory -Path (Join-Path $snapshot '.deps/shvdne') -Force
    foreach ($name in @('ScriptHookVDotNet3.dll', 'ScriptHookVDotNet3.xml')) {
        Copy-Item -LiteralPath (Join-Path $repository ".deps/shvdne/$name") -Destination (Join-Path $snapshot '.deps/shvdne')
    }
    $metadata = [ordered]@{
        tool = 'Stryker.NET'; version = $toolVersion; mutationLevel = 'Standard'
        completed = $false
        assemblies = $Assembly; mutate = $Mutate; sourceHashes = $sourceHashes
        coverageInventorySha256 = (Get-FileHash -LiteralPath $inventoryPath -Algorithm SHA256).Hash
        note = 'Reports retain all survivors, no-coverage and compile-error mutants. No score exclusions or survivor baseline.'
    }
    $metadata | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $runDirectory 'run.json') -Encoding UTF8

    $env:DOTNET_ROOT = Split-Path -Parent $dotnetCommand
    $env:PATH = $env:DOTNET_ROOT + [IO.Path]::PathSeparator + $env:PATH
    $env:DOTNET_CLI_HOME = Join-Path $repository '.dotnet'
    $env:NUGET_PACKAGES = Join-Path $repository '.deps/nuget'
    $env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
    $env:DOTNET_NOLOGO = '1'
    $stryker = Join-Path $toolDirectory 'dotnet-stryker.exe'
    if (-not (Test-Path -LiteralPath $stryker)) {
        & $dotnetCommand tool install dotnet-stryker --version $toolVersion --tool-path $toolDirectory
        if ($LASTEXITCODE -ne 0) { throw 'Cannot install the pinned mutation test tool.' }
    }
    $toolAssembly = Join-Path $toolDirectory ".store/dotnet-stryker/$toolVersion/dotnet-stryker/$toolVersion/tools/net10.0/any/Stryker.CLI.dll"
    if ((Get-Item -LiteralPath $toolAssembly).VersionInfo.ProductVersion -notlike "$toolVersion+*") {
        throw 'Installed mutation tool does not match its pinned version.'
    }
    & $dotnetCommand restore (Join-Path $snapshot 'GTAVMods.sln') --locked-mode
    if ($LASTEXITCODE -ne 0) { throw 'Mutation snapshot restore failed.' }
    $summaries = @()
    foreach ($name in $Assembly) {
        $module = @($inventory.modules | Where-Object name -eq $name)
        if ($module.Count -ne 1) { throw "Coverage inventory does not identify $name." }
        $relativeProject = $module[0].project.Substring($repository.Length).TrimStart('\', '/')
        $projectDirectory = Join-Path $snapshot 'ThorTest'
        $reportDirectory = Join-Path $runDirectory $name
        $config = Join-Path $runDirectory "$name.config.json"
        @{ 'stryker-config' = @{
            solution = (Join-Path $snapshot 'GTAVMods.sln')
            project = [IO.Path]::GetFileName($relativeProject)
            configuration = 'Release'; 'target-framework' = 'net48'
            'mutation-level' = 'Standard'; mutate = $Mutate
            reporters = @('Json', 'ClearText'); concurrency = $Concurrency
            'break-on-initial-test-failure' = $true
        }} | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $config -Encoding UTF8
        Push-Location $projectDirectory
        try {
            & $stryker --config-file $config --output $reportDirectory --skip-version-check --log-to-file
            if ($LASTEXITCODE -ne 0) { throw "Mutation analysis failed for $name. See $reportDirectory." }
        } finally { Pop-Location }
        $report = Get-Content -LiteralPath (Join-Path $reportDirectory 'reports/mutation-report.json') -Raw | ConvertFrom-Json
        $mutants = @($report.files.PSObject.Properties | ForEach-Object { $_.Value.mutants })
        if ($mutants.Count -eq 0) { throw "Mutation report for $name is empty." }
        $counts = [ordered]@{}
        $statuses = @('Killed', 'Timeout', 'Survived', 'NoCoverage', 'CompileError', 'Ignored')
        if (@($mutants | Where-Object { $_.status -notin $statuses }).Count -ne 0) {
            throw "Mutation report for $name contains unknown or unfinished results."
        }
        foreach ($status in $statuses) {
            $counts[$status] = @($mutants | Where-Object status -eq $status).Count
        }
        $summaries += @{ assembly = $name; counts = $counts; report = "$name/reports/mutation-report.json" }
        $summaries | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $runDirectory 'summary.json') -Encoding UTF8
    }
    $metadata.completed = $true
    $metadata | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $runDirectory 'run.json') -Encoding UTF8
    $runDirectory | Set-Content -LiteralPath (Join-Path $repository 'artifacts/mutations/latest-completed.txt') -Encoding UTF8
    Write-Host "Mutation analysis completed: $runDirectory"
    Write-Host 'Review survivors against the saved coverage inventory; primitive bindings are retained as NoCoverage, never silently removed.'
} finally {
    Pop-Location
    foreach ($name in $oldEnvironment.Keys) {
        [Environment]::SetEnvironmentVariable($name, $oldEnvironment[$name], 'Process')
    }
}
