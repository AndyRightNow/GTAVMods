[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$gate = Join-Path (Split-Path -Parent $PSScriptRoot) 'Test-Coverage.ps1'
$fixtureRoot = Join-Path ([IO.Path]::GetTempPath()) ('GTAVMods-CoverageGate-' + [Guid]::NewGuid().ToString('N'))
$null = New-Item -ItemType Directory -Path $fixtureRoot
$passedCases = 0

function Write-Json($Value, [string]$Path) {
    $Value | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $Path -Encoding UTF8
}
function Hash([string]$Path) {
    (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}
function New-Fixture([string]$Name) {
    $folder = Join-Path $fixtureRoot $Name
    $null = New-Item -ItemType Directory -Path $folder
    $started = [DateTime]::UtcNow.AddSeconds(-2)
    $boundary = Join-Path $folder 'boundaries.json'
    Write-Json @{ schemaVersion = 1; bindings = @() } $boundary
    $modules = @()
    $report = [ordered]@{}
    foreach ($name in @('Thor', 'ModUtils', 'AgentBridge')) {
        $source = Join-Path $folder "$name.cs"
        'class Example { int Read(bool enabled) { return enabled ? 1 : 0; } }' | Set-Content -LiteralPath $source
        $binary = Join-Path $folder "$name.dll"
        $pdb = Join-Path $folder "$name.pdb"
        "fixture binary $name" | Set-Content -LiteralPath $binary
        "fixture symbols $name" | Set-Content -LiteralPath $pdb
        $signature = "System.Int32 $name.Example::Read(System.Boolean)"
        $branch = @{ document = $source; line = 1; offset = 2; endOffset = 4; path = 0; ordinal = 0 }
        $modules += @{
            name = $name; assembly = $binary; assemblySha256 = Hash $binary
            pdb = $pdb; pdbSha256 = Hash $pdb; testCopy = $binary; testPdb = $pdb
            sources = @(@{ path = $source; sha256 = Hash $source })
            methods = @(@{
                signature = $signature; binding = $false
                documents = @(@{ path = $source; lines = @(1) }); branches = @($branch)
            })
        }
        $report["$name.dll"] = @{
            $source = @{
                "$name.Example" = @{
                    $signature = @{
                        Lines = @{ '1' = 1 }
                        Branches = @(@{ Line = 1; Offset = 2; EndOffset = 4; Path = 0; Ordinal = 0; Hits = 1 })
                    }
                }
            }
        }
    }
    $testBinary = Join-Path $folder 'ModUtilsTest.dll'
    $testPdb = Join-Path $folder 'ModUtilsTest.pdb'
    $testSource = Join-Path $folder 'BehaviorTests.cs'
    'test binary fixture' | Set-Content -LiteralPath $testBinary
    'test symbols fixture' | Set-Content -LiteralPath $testPdb
    'class BehaviorTests { void Check() { } }' | Set-Content -LiteralPath $testSource
    Write-Json @{
        schemaVersion = 2; modules = $modules
        testAssembly = @{
            name = 'ModUtilsTest'; assembly = $testBinary; assemblySha256 = Hash $testBinary
            pdb = $testPdb; pdbSha256 = Hash $testPdb
            sources = @(@{ path = $testSource; sha256 = Hash $testSource })
        }
        boundaryPath = $boundary; boundarySha256 = Hash $boundary
    } (Join-Path $folder 'inventory.json')
    Write-Json @{ schemaVersion = 2; startedUtc = $started.ToString('O'); inventorySha256 = Hash (Join-Path $folder 'inventory.json') } (Join-Path $folder 'run.json')
    Write-Json $report (Join-Path $folder 'coverage.json')
    $timestamp = [long]([DateTime]::UtcNow - [DateTime]::new(1970, 1, 1)).TotalSeconds
    "<coverage timestamp='$timestamp'><packages><package name='Thor'/><package name='ModUtils'/><package name='AgentBridge'/></packages></coverage>" |
        Set-Content -LiteralPath (Join-Path $folder 'coverage.cobertura.xml')
    "<TestRun><Results><UnitTestResult outcome='Passed'/></Results><ResultSummary><Counters total='1' executed='1' passed='1'/></ResultSummary></TestRun>" |
        Set-Content -LiteralPath (Join-Path $folder 'unit-tests.trx')
    $folder
}
function Read-Report([string]$Folder) {
    Get-Content -Raw -LiteralPath (Join-Path $Folder 'coverage.json') | ConvertFrom-Json
}
function First-Method($Report, [string]$Module = 'Thor.dll') {
    $Report.PSObject.Properties[$Module].Value.PSObject.Properties.Value.PSObject.Properties.Value.PSObject.Properties.Value
}
function Assert-Gate([string]$Folder, [string]$ExpectedFailure = '') {
    $message = ''
    try { & $gate -RunDirectory $Folder 6>$null }
    catch { $message = $_.Exception.Message }
    if ($ExpectedFailure -eq '' -and $message -ne '') { throw "Valid fixture failed: $message" }
    if ($ExpectedFailure -ne '' -and $message -notlike "*$ExpectedFailure*") {
        throw "Expected '$ExpectedFailure', received '$message'."
    }
    $summary = Get-Content -Raw -LiteralPath (Join-Path $Folder 'gate-summary.json') | ConvertFrom-Json
    if ($summary.passed -ne ($ExpectedFailure -eq '')) { throw 'Gate artifact did not record its actual outcome.' }
    $script:passedCases++
}

try {
    Assert-Gate (New-Fixture 'complete')

    # A get/set pair or generated lambda may share one source line. Coverlet
    # attributes that line to the first method rather than duplicating it.
    $folder = New-Fixture 'shared-source-line'
    $inventoryPath = Join-Path $folder 'inventory.json'
    $inventory = Get-Content -Raw -LiteralPath $inventoryPath | ConvertFrom-Json
    $inventory.modules[0].methods += @{
        signature = 'System.Int32 Thor.Example::<Read>b__0()'; binding = $false
        documents = @(@{ path = $inventory.modules[0].sources[0].path; lines = @(1) }); branches = @()
    }
    Write-Json $inventory $inventoryPath
    $runPath = Join-Path $folder 'run.json'
    $run = Get-Content -Raw -LiteralPath $runPath | ConvertFrom-Json
    $run.inventorySha256 = Hash $inventoryPath
    Write-Json $run $runPath
    Assert-Gate $folder

    foreach ($case in @('covered', 'missing', 'misplaced', 'uncovered')) {
        $folder = New-Fixture "setter-lambda-$case"
        $inventoryPath = Join-Path $folder 'inventory.json'
        $inventory = Get-Content -Raw -LiteralPath $inventoryPath | ConvertFrom-Json
        $ownerSignature = $inventory.modules[0].methods[0].signature
        $source = $inventory.modules[0].sources[0].path
        $inventory.modules[0].methods += @{
            signature = 'System.Void Thor.Example/<>c__DisplayClass0_0::<Read>b__0()'; binding = $false
            documents = @(@{ path = $source; lines = @(1) })
            branches = @(@{ document = $source; line = 1; offset = 10; endOffset = 14; path = 0; ordinal = 0
                reportSignature = $ownerSignature })
        }
        Write-Json $inventory $inventoryPath
        $runPath = Join-Path $folder 'run.json'
        $run = Get-Content -Raw -LiteralPath $runPath | ConvertFrom-Json
        $run.inventorySha256 = Hash $inventoryPath
        Write-Json $run $runPath
        $report = Read-Report $folder
        $branch = @{ Line = 1; Offset = 10; EndOffset = 14; Path = 0; Ordinal = 0; Hits = 1 }
        if ($case -eq 'covered' -or $case -eq 'uncovered') {
            if ($case -eq 'uncovered') { $branch.Hits = 0 }
            (First-Method $report).Branches += $branch
        } elseif ($case -eq 'misplaced') {
            $class = $report.PSObject.Properties['Thor.dll'].Value.PSObject.Properties[$source].Value.PSObject.Properties['Thor.Example'].Value
            $class | Add-Member -NotePropertyName 'System.Void Thor.Example::Unrelated()' -NotePropertyValue @{
                Lines = @{}; Branches = @($branch)
            }
        }
        Write-Json $report (Join-Path $folder 'coverage.json')
        if ($case -eq 'covered') { Assert-Gate $folder }
        elseif ($case -eq 'uncovered') { Assert-Gate $folder 'Uncovered branch' }
        else { Assert-Gate $folder 'Executable branch is unmeasured' }
    }

    $folder = New-Fixture 'one-line-missed'
    $report = Read-Report $folder
    (First-Method $report).Lines.'1' = 0
    Write-Json $report (Join-Path $folder 'coverage.json')
    Assert-Gate $folder 'Uncovered line'

    $folder = New-Fixture 'one-branch-missed'
    $report = Read-Report $folder
    (First-Method $report).Branches[0].Hits = 0
    Write-Json $report (Join-Path $folder 'coverage.json')
    Assert-Gate $folder 'Uncovered branch'

    $folder = New-Fixture 'missing-module'
    $report = Read-Report $folder
    $report.PSObject.Properties.Remove('Thor.dll')
    Write-Json $report (Join-Path $folder 'coverage.json')
    Assert-Gate $folder 'Production coverage module is missing'

    $folder = New-Fixture 'new-executable-source'
    $inventoryPath = Join-Path $folder 'inventory.json'
    $inventory = Get-Content -Raw -LiteralPath $inventoryPath | ConvertFrom-Json
    $newSource = Join-Path $folder 'NewAbility.cs'
    'class NewAbility { public void Run() { System.Console.WriteLine("new behavior"); } }' | Set-Content -LiteralPath $newSource
    $inventory.modules[0].sources += @{ path = $newSource; sha256 = Hash $newSource }
    $inventory.modules[0].methods += @{
        signature = 'System.Void NewAbility::Run()'; binding = $false
        documents = @(@{ path = $newSource; lines = @(1) }); branches = @()
    }
    Write-Json $inventory $inventoryPath
    $runPath = Join-Path $folder 'run.json'
    $run = Get-Content -Raw -LiteralPath $runPath | ConvertFrom-Json
    $run.inventorySha256 = Hash $inventoryPath
    Write-Json $run $runPath
    Assert-Gate $folder 'Executable line is unmeasured'

    $folder = New-Fixture 'branch-erased-from-report'
    $report = Read-Report $folder
    (First-Method $report).Branches = @()
    Write-Json $report (Join-Path $folder 'coverage.json')
    Assert-Gate $folder 'Executable branch is unmeasured'

    $folder = New-Fixture 'interior-line-erased-from-report'
    $inventoryPath = Join-Path $folder 'inventory.json'
    $inventory = Get-Content -Raw -LiteralPath $inventoryPath | ConvertFrom-Json
    $inventory.modules[0].methods[0].documents[0].lines = @(1, 2, 3)
    Write-Json $inventory $inventoryPath
    $runPath = Join-Path $folder 'run.json'
    $run = Get-Content -Raw -LiteralPath $runPath | ConvertFrom-Json
    $run.inventorySha256 = Hash $inventoryPath
    Write-Json $run $runPath
    $report = Read-Report $folder
    (First-Method $report).Lines | Add-Member -NotePropertyName '3' -NotePropertyValue 1
    Write-Json $report (Join-Path $folder 'coverage.json')
    Assert-Gate $folder 'Executable line is unmeasured'

    $folder = New-Fixture 'empty-report'
    Set-Content -LiteralPath (Join-Path $folder 'coverage.json') -Value '' -NoNewline
    Assert-Gate $folder 'Required report is empty'

    $folder = New-Fixture 'stale-report'
    (Get-Item -LiteralPath (Join-Path $folder 'coverage.json')).LastWriteTimeUtc = [DateTime]::UtcNow.AddHours(-1)
    Assert-Gate $folder 'Stale report'

    $folder = New-Fixture 'zero-tests'
    "<TestRun><Results/><ResultSummary><Counters total='0' executed='0' passed='0'/></ResultSummary></TestRun>" |
        Set-Content -LiteralPath (Join-Path $folder 'unit-tests.trx')
    Assert-Gate $folder 'nonempty test run'

    $folder = New-Fixture 'changed-build'
    Add-Content -LiteralPath (Join-Path $folder 'Thor.dll') -Value 'different binary'
    Assert-Gate $folder 'Compiled artifact changed'

    $folder = New-Fixture 'changed-source'
    Add-Content -LiteralPath (Join-Path $folder 'Thor.cs') -Value '// changed'
    Assert-Gate $folder 'Source changed'

    foreach ($extension in @('dll', 'pdb')) {
        $folder = New-Fixture "changed-test-$extension"
        Add-Content -LiteralPath (Join-Path $folder "ModUtilsTest.$extension") -Value 'different test build'
        Assert-Gate $folder 'Compiled test artifact changed'
    }

    $folder = New-Fixture 'changed-test-source'
    Add-Content -LiteralPath (Join-Path $folder 'BehaviorTests.cs') -Value '// regression changed'
    Assert-Gate $folder 'Test source changed'

    $folder = New-Fixture 'missing-test-inventory'
    $inventoryPath = Join-Path $folder 'inventory.json'
    $inventory = Get-Content -Raw -LiteralPath $inventoryPath | ConvertFrom-Json
    $inventory.testAssembly = $null
    Write-Json $inventory $inventoryPath
    $runPath = Join-Path $folder 'run.json'
    $run = Get-Content -Raw -LiteralPath $runPath | ConvertFrom-Json
    $run.inventorySha256 = Hash $inventoryPath
    Write-Json $run $runPath
    Assert-Gate $folder 'requires the test assembly'

    $folder = New-Fixture 'old-schema'
    $inventoryPath = Join-Path $folder 'inventory.json'
    $inventory = Get-Content -Raw -LiteralPath $inventoryPath | ConvertFrom-Json
    $inventory.schemaVersion = 1
    Write-Json $inventory $inventoryPath
    $runPath = Join-Path $folder 'run.json'
    $run = Get-Content -Raw -LiteralPath $runPath | ConvertFrom-Json
    $run.schemaVersion = 1
    $run.inventorySha256 = Hash $inventoryPath
    Write-Json $run $runPath
    Assert-Gate $folder 'Unsupported coverage inventory format'

    $folder = New-Fixture 'missing-report'
    Remove-Item -LiteralPath (Join-Path $folder 'coverage.json')
    Assert-Gate $folder 'Required report is missing'

    Write-Host "Coverage gate checks passed: $passedCases acceptance/rejection scenarios."
}
finally {
    $resolvedRoot = [IO.Path]::GetFullPath($fixtureRoot)
    $tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\') + '\'
    if (-not $resolvedRoot.StartsWith($tempRoot, [StringComparison]::OrdinalIgnoreCase) -or
        [IO.Path]::GetFileName($resolvedRoot) -notlike 'GTAVMods-CoverageGate-*') {
        throw "Refusing to clean an unexpected fixture directory: $resolvedRoot"
    }
    Remove-Item -LiteralPath $resolvedRoot -Recurse -Force
}
