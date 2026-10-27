[CmdletBinding()]
param([Parameter(Mandatory = $true)][string]$RunDirectory)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Get-Sha256([string]$Path) {
    (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}
function Read-Json([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { throw "Required report is missing: $Path" }
    Get-Content -Raw -LiteralPath $Path | ConvertFrom-Json
}
function Get-PathKey([string]$Path) {
    [IO.Path]::GetFullPath($Path).ToLowerInvariant()
}
function Get-BranchKey($Branch) {
    '{0}|{1}|{2}|{3}|{4}' -f $Branch.Line, $Branch.Offset, $Branch.EndOffset, $Branch.Path, $Branch.Ordinal
}
function Get-Count($Value, [string]$Description) {
    $number = 0L
    if (-not [long]::TryParse([string]$Value, [ref]$number) -or $number -lt 0) {
        throw "Invalid integer $Description."
    }
    $number
}

$RunDirectory = [IO.Path]::GetFullPath($RunDirectory)
$failures = [Collections.Generic.List[string]]::new()
$totals = [Collections.Generic.List[object]]::new()
try {
    $run = Read-Json (Join-Path $RunDirectory 'run.json')
    $inventoryPath = Join-Path $RunDirectory 'inventory.json'
    $inventory = Read-Json $inventoryPath
    if ($run.schemaVersion -ne 2 -or $inventory.schemaVersion -ne 2) { throw 'Unsupported coverage inventory format.' }
    if ((Get-Sha256 $inventoryPath) -ne $run.inventorySha256) { throw 'Coverage inventory changed after the test run started.' }
    if ((Get-Sha256 $inventory.boundaryPath) -ne $inventory.boundarySha256) { throw 'Runtime boundary manifest changed during the test run.' }
    $testAssembly = $inventory.testAssembly
    if ($null -eq $testAssembly -or @($testAssembly.sources).Count -eq 0) {
        throw 'Coverage inventory requires the test assembly and its compiler inputs.'
    }
    foreach ($artifact in @(
        @($testAssembly.assembly, $testAssembly.assemblySha256),
        @($testAssembly.pdb, $testAssembly.pdbSha256)
    )) {
        if ((Get-Sha256 $artifact[0]) -ne $artifact[1]) { throw "Compiled test artifact changed: $($artifact[0])" }
    }
    foreach ($source in $testAssembly.sources) {
        if ((Get-Sha256 $source.path) -ne $source.sha256) { throw "Test source changed during the test run: $($source.path)" }
    }
    $started = [DateTimeOffset]::Parse($run.startedUtc).UtcDateTime
    if ($started -gt [DateTime]::UtcNow) { throw 'Coverage run starts in the future.' }
    foreach ($name in @('coverage.json', 'coverage.cobertura.xml', 'unit-tests.trx')) {
        $path = Join-Path $RunDirectory $name
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Required report is missing: $name" }
        $file = Get-Item -LiteralPath $path
        if ($file.Length -eq 0) { throw "Required report is empty: $name" }
        if ($file.LastWriteTimeUtc -lt $started) { throw "Stale report predates this test run: $name" }
    }
    [xml]$trx = Get-Content -Raw -LiteralPath (Join-Path $RunDirectory 'unit-tests.trx')
    $counters = $trx.SelectSingleNode("/*[local-name()='TestRun']/*[local-name()='ResultSummary']/*[local-name()='Counters']")
    if ($null -eq $counters) { throw 'Test results contain no counters.' }
    $executed = Get-Count $counters.executed 'test count'
    $passed = Get-Count $counters.passed 'passed test count'
    $total = Get-Count $counters.total 'total test count'
    $results = @($trx.SelectNodes("/*[local-name()='TestRun']/*[local-name()='Results']/*[local-name()='UnitTestResult']"))
    if ($executed -eq 0 -or $passed -ne $executed -or $total -ne $executed -or
        $results.Count -ne $executed -or @($results | Where-Object outcome -ne 'Passed').Count -gt 0) {
        throw 'Coverage requires a nonempty test run with every test executed and passing.'
    }

    $coverage = Read-Json (Join-Path $RunDirectory 'coverage.json')
    [xml]$cobertura = Get-Content -Raw -LiteralPath (Join-Path $RunDirectory 'coverage.cobertura.xml')
    $timestamp = Get-Count $cobertura.coverage.timestamp 'coverage timestamp'
    $startedUnix = [Math]::Floor(($started - [DateTime]::new(1970, 1, 1, 0, 0, 0, [DateTimeKind]::Utc)).TotalSeconds)
    if ($timestamp -lt $startedUnix) { throw 'Stale coverage timestamp predates this test run.' }
    $moduleReports = @{}
    foreach ($property in $coverage.PSObject.Properties) {
        $name = [IO.Path]::GetFileNameWithoutExtension($property.Name)
        if ($moduleReports.ContainsKey($name)) { throw "Duplicate coverage module: $name" }
        $moduleReports[$name] = $property.Value
    }
    $expectedNames = @($inventory.modules | ForEach-Object name)
    foreach ($required in @('Thor', 'ModUtils', 'GameBridge')) {
        if (@($expectedNames | Where-Object { $_ -eq $required }).Count -ne 1) {
            throw "Required production module absent from inventory: $required"
        }
    }
    if (@($expectedNames | Sort-Object -Unique).Count -ne $expectedNames.Count) {
        throw 'Duplicate production module in inventory.'
    }
    foreach ($name in $moduleReports.Keys) {
        if ($expectedNames -notcontains $name) { throw "Uninventoried coverage module: $name" }
    }
    foreach ($module in $inventory.modules) {
        foreach ($artifact in @(
            @($module.assembly, $module.assemblySha256), @($module.pdb, $module.pdbSha256),
            @($module.testCopy, $module.assemblySha256), @($module.testPdb, $module.pdbSha256)
        )) {
            if ((Get-Sha256 $artifact[0]) -ne $artifact[1]) { throw "Compiled artifact changed: $($artifact[0])" }
        }
        foreach ($source in $module.sources) {
            if ((Get-Sha256 $source.path) -ne $source.sha256) { throw "Source changed during the test run: $($source.path)" }
        }
        $behavior = @($module.methods | Where-Object { -not $_.binding })
        if ($behavior.Count -eq 0) {
            # A contracts/bindings-only assembly still participates in inventory.
            # The three actual behavior assemblies may never become empty.
            if (@('Thor', 'ModUtils', 'GameBridge') -contains $module.name) {
                throw "Required behavior module has no executable methods: $($module.name)"
            }
            continue
        }
        if (-not $moduleReports.ContainsKey($module.name)) { throw "Production coverage module is missing: $($module.name)" }
        if (@($cobertura.coverage.packages.package | Where-Object name -eq $module.name).Count -ne 1) {
            throw "Cobertura module is missing or duplicated: $($module.name)"
        }
        $reported = @{}
        $reportedLines = @{}
        $lines = 0L; $hitLines = 0L; $branches = 0L; $hitBranches = 0L
        foreach ($document in $moduleReports[$module.name].PSObject.Properties) {
            $documentKey = Get-PathKey $document.Name
            foreach ($type in $document.Value.PSObject.Properties) {
                foreach ($method in $type.Value.PSObject.Properties) {
                    $key = $documentKey + '|' + $method.Name
                    if ($reported.ContainsKey($key)) { throw "Duplicate reported method: $key" }
                    $lineMap = @{}
                    foreach ($line in $method.Value.Lines.PSObject.Properties) {
                        $lineNumber = Get-Count $line.Name 'source line number'
                        if ($lineNumber -eq 0) { throw 'Coverage line numbers are one-based.' }
                        $hits = Get-Count $line.Value 'line hit count'
                        $lineMap[[string]$lineNumber] = $hits
                        $sourceLine = $documentKey + '|' + $lineNumber
                        if (-not $reportedLines.ContainsKey($sourceLine)) {
                            $lines++
                            if ($hits -gt 0) { $hitLines++ }
                            $reportedLines[$sourceLine] = $hits
                        }
                        if ($hits -eq 0) { $failures.Add("Uncovered line: $($module.name) $($document.Name):$lineNumber ($($method.Name))") }
                    }
                    $branchMap = @{}
                    foreach ($branch in $method.Value.Branches) {
                        $branchKey = Get-BranchKey $branch
                        if ($branchMap.ContainsKey($branchKey)) { throw "Duplicate reported branch: $key $branchKey" }
                        $hits = Get-Count $branch.Hits 'branch hit count'
                        $branchMap[$branchKey] = $hits
                        $branches++
                        if ($hits -gt 0) { $hitBranches++ }
                        else { $failures.Add("Uncovered branch: $($module.name) $($document.Name):$($branch.Line) ($($method.Name), path $($branch.Path), offset $($branch.Offset))") }
                    }
                    $reported[$key] = @{ Lines = $lineMap; Branches = $branchMap }
                }
            }
        }
        foreach ($method in $behavior) {
            foreach ($document in $method.documents) {
                $documentKey = Get-PathKey $document.path
                # Coverlet assigns each document line to its first method. An
                # accessor or generated lambda can share that line with another
                # method, so line completeness must use document + line identity.
                foreach ($line in $document.lines) {
                    if (-not $reportedLines.ContainsKey($documentKey + '|' + $line)) {
                        $failures.Add("Executable line is unmeasured: $($module.name) $($document.path):$line ($($method.signature))")
                    }
                }
                foreach ($branch in @($method.branches | Where-Object {
                    (Get-PathKey $_.document) -eq (Get-PathKey $document.path)
                })) {
                    $branchMethod = $method.signature
                    if ($null -ne $branch.PSObject.Properties['reportSignature']) {
                        $branchMethod = $branch.reportSignature
                    }
                    $branchMethodKey = $documentKey + '|' + $branchMethod
                    if (-not $reported.ContainsKey($branchMethodKey) -or
                        -not $reported[$branchMethodKey].Branches.ContainsKey((Get-BranchKey $branch))) {
                        $failures.Add("Executable branch is unmeasured: $($module.name) $($method.signature) $(Get-BranchKey $branch)")
                    }
                }
            }
        }
        if ($lines -eq 0) { throw "Production module has no instrumented lines: $($module.name)" }
        $totals.Add([ordered]@{
            assembly = $module.name; coveredLines = $hitLines; executableLines = $lines
            coveredBranches = $hitBranches; executableBranches = $branches
        })
        Write-Host "$($module.name): $hitLines/$lines lines; $hitBranches/$branches branches."
    }
    if ($totals.Count -lt 3) { throw 'Coverage did not measure all required behavior assemblies.' }
}
catch { $failures.Add($_.Exception.Message) }

[ordered]@{
    passed = ($failures.Count -eq 0)
    requiredLineCoverage = 100; requiredBranchCoverage = 100
    modules = $totals.ToArray(); failures = $failures.ToArray()
} | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath (Join-Path $RunDirectory 'gate-summary.json') -Encoding UTF8
if ($failures.Count -gt 0) {
    throw "Coverage gate failed with $($failures.Count) issue(s). $(@($failures | Select-Object -First 8) -join [Environment]::NewLine) See gate-summary.json."
}
Write-Host 'Coverage gate passed: every inventoried behavior line and branch is covered.'
