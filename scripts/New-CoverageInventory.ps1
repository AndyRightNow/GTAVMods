[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$RunDirectory,
    [string]$RepositoryRoot,
    [string]$BoundaryManifest
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if (-not $RepositoryRoot) { $RepositoryRoot = Split-Path -Parent $PSScriptRoot }
if (-not $BoundaryManifest) { $BoundaryManifest = Join-Path $PSScriptRoot 'coverage-boundaries.json' }

function Get-Sha256([string]$Path) {
    (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}
function Get-AllTypes($Types) {
    foreach ($type in $Types) {
        $type
        Get-AllTypes $type.NestedTypes
    }
}
function Get-CanonicalPath([string]$Path) {
    [IO.Path]::GetFullPath($Path)
}
function Assert-DocumentSource($Document, [string[]]$SourcePaths, $CheckedDocuments) {
    $path = Get-CanonicalPath $Document.Url
    if ($SourcePaths -notcontains $path) { throw "PDB document was not a compiler input: $path" }
    if (-not $CheckedDocuments.ContainsKey($path)) {
        $algorithm = $Document.HashAlgorithm.ToString()
        if ($algorithm -ne 'SHA256' -and $algorithm -ne 'SHA1') {
            throw "PDB source checksum is absent or unsupported: $path"
        }
        $expected = [BitConverter]::ToString($Document.Hash).Replace('-', '').ToLowerInvariant()
        $actual = (Get-FileHash -LiteralPath $path -Algorithm $algorithm).Hash.ToLowerInvariant()
        if ($expected -ne $actual) { throw "Source changed since compilation: $path" }
        $CheckedDocuments[$path] = $true
    }
}

$RepositoryRoot = Get-CanonicalPath $RepositoryRoot
$RunDirectory = Get-CanonicalPath $RunDirectory
$inputFiles = @(Get-ChildItem -LiteralPath (Join-Path $RunDirectory 'inputs') -Filter '*.txt')
if ($inputFiles.Count -eq 0) { throw 'No compiler inventories were produced by the build.' }
$records = @(
    foreach ($file in $inputFiles) {
        $record = @{ Compile = @(); CompileHashes = @{} }
        foreach ($line in Get-Content -LiteralPath $file.FullName) {
            $parts = $line.Split([char[]]@('|'), 2)
            if ($parts.Count -ne 2) { throw "Invalid compiler inventory: $($file.FullName)" }
            if ($parts[0] -eq 'Compile') { $record.Compile += Get-CanonicalPath $parts[1] }
            elseif ($parts[0] -eq 'CompileHash') {
                $hashParts = $parts[1].Split([char[]]@('|'), 2)
                if ($hashParts.Count -ne 2 -or $hashParts[1] -notmatch '^[0-9a-fA-F]{64}$') {
                    throw "Invalid compiler input hash: $($file.FullName)"
                }
                $path = Get-CanonicalPath $hashParts[0]
                if ($record.CompileHashes.ContainsKey($path)) { throw "Duplicate compiler input hash: $path" }
                $record.CompileHashes[$path] = $hashParts[1].ToLowerInvariant()
            }
            else { $record[$parts[0]] = $parts[1] }
        }
        if (-not $record.ContainsKey('Configuration') -or [string]::IsNullOrWhiteSpace($record.Configuration)) {
            throw "Missing compiler build configuration: $($file.FullName)"
        }
        $sourcePaths = @($record.Compile | Sort-Object -Unique)
        if ($record.CompileHashes.Count -ne $sourcePaths.Count) {
            throw "Compiler input hashes are incomplete: $($file.FullName)"
        }
        foreach ($path in $sourcePaths) {
            if (-not $record.CompileHashes.ContainsKey($path)) { throw "Missing compiler input hash: $path" }
            if ((Get-Sha256 $path) -ne $record.CompileHashes[$path]) {
                throw "Compiler input changed since compilation: $path"
            }
        }
        $record
    }
)

# Every solution project must participate. Newly added non-test projects are
# automatically included; only the separate, unlisted asset tool is out of scope.
$solution = Get-Content -Raw -LiteralPath (Join-Path $RepositoryRoot 'GTAVMods.sln')
$projects = @([regex]::Matches($solution, '(?m)^Project\("[^"]+"\)\s*=\s*"[^"]+",\s*"([^"]+\.csproj)"') |
    ForEach-Object { Get-CanonicalPath (Join-Path $RepositoryRoot $_.Groups[1].Value) })
foreach ($project in $projects) {
    if (@($records | Where-Object { $_.Project -eq $project }).Count -ne 1) {
        throw "Missing or duplicate compiler inventory for $project"
    }
}
$testRecords = @($records | Where-Object { $_.IsTestProject -eq 'true' })
if ($testRecords.Count -ne 1) { throw 'The build expects exactly one test assembly inventory.' }
$testDirectory = Split-Path -Parent $testRecords[0].TargetPath
$production = @($records | Where-Object { $_.IsTestProject -ne 'true' })
foreach ($required in @('Thor', 'ModUtils', 'GameBridge')) {
    if (@($production | Where-Object { $_.AssemblyName -eq $required }).Count -ne 1) {
        throw "Required production assembly is missing: $required"
    }
}

# Use the exact locked collector's symbol analysis, including its handling of
# compiler-generated branches. Updating Coverlet requires verifying this API.
$lock = Get-Content -Raw -LiteralPath (Join-Path $RepositoryRoot 'ThorTest/packages.lock.json') | ConvertFrom-Json
$coverletEntries = @($lock.dependencies.PSObject.Properties |
    ForEach-Object { $_.Value.PSObject.Properties | Where-Object Name -eq 'coverlet.msbuild' })
if ($coverletEntries.Count -ne 1) { throw 'Cannot identify the locked Coverlet version.' }
$coverletVersion = $coverletEntries[0].Value.resolved
$coverletDirectory = Join-Path $testRecords[0].NuGetPackageRoot "coverlet.msbuild/$coverletVersion/tasks/netstandard2.0"
Add-Type -Path (Join-Path $coverletDirectory 'Mono.Cecil.dll')
$coverletAssembly = [Reflection.Assembly]::LoadFrom((Join-Path $coverletDirectory 'coverlet.core.dll'))
$symbolType = $coverletAssembly.GetType('Coverlet.Core.Symbols.CecilSymbolHelper', $true)
$symbols = [Activator]::CreateInstance($symbolType, $true)

# The test binary and every actual Compile input participate in freshness,
# including ignored regressions and generated sources without sequence points.
$testRecord = $testRecords[0]
$testAssemblyPath = Get-CanonicalPath $testRecord.TargetPath
$testPdbPath = [IO.Path]::ChangeExtension($testAssemblyPath, '.pdb')
foreach ($path in @($testAssemblyPath, $testPdbPath)) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Missing compiled test artifact: $path" }
}
$testSourcePaths = @($testRecord.Compile | Sort-Object -Unique)
if ($testSourcePaths.Count -eq 0) { throw 'The test assembly has no compiler inputs.' }
$testSources = @(
    foreach ($path in $testSourcePaths) {
        [ordered]@{ path = $path; sha256 = $testRecord.CompileHashes[$path] }
    }
)
$testParameters = [Mono.Cecil.ReaderParameters]::new()
$testParameters.ReadSymbols = $true
$testDefinition = [Mono.Cecil.AssemblyDefinition]::ReadAssembly($testAssemblyPath, $testParameters)
try {
    if ($testDefinition.MainModule.SymbolReader.GetType().Name -ne 'PortablePdbReader') {
        throw "Portable PDB required for $($testRecord.AssemblyName)."
    }
    $checkedTestDocuments = @{}
    foreach ($type in (Get-AllTypes $testDefinition.MainModule.Types)) {
        foreach ($method in $type.Methods) {
            foreach ($point in $method.DebugInformation.SequencePoints) {
                if (-not $point.IsHidden) {
                    Assert-DocumentSource $point.Document $testSourcePaths $checkedTestDocuments
                }
            }
        }
    }
    if ($checkedTestDocuments.Count -eq 0) { throw 'The test assembly has no executable source documents.' }
}
finally { $testDefinition.Dispose() }
$testAssembly = [ordered]@{
    name = $testRecord.AssemblyName; project = $testRecord.Project
    configuration = $testRecord.Configuration
    assembly = $testAssemblyPath; assemblySha256 = Get-Sha256 $testAssemblyPath
    pdb = $testPdbPath; pdbSha256 = Get-Sha256 $testPdbPath
    sources = $testSources
}

$bindingsPath = Get-CanonicalPath $BoundaryManifest
$boundary = Get-Content -Raw -LiteralPath $bindingsPath | ConvertFrom-Json
if ($boundary.schemaVersion -ne 1) { throw 'Unsupported runtime boundary manifest.' }
$bindings = @{}
foreach ($binding in $boundary.bindings) {
    $key = $binding.assembly + '|' + $binding.method
    if ($bindings.ContainsKey($key) -or [string]::IsNullOrWhiteSpace($binding.reason)) {
        throw "Duplicate or unexplained runtime binding: $key"
    }
    $bindings[$key] = $binding
}
$usedBindings = @{}
$modules = @()
foreach ($record in $production) {
    $assemblyPath = Get-CanonicalPath $record.TargetPath
    $pdbPath = [IO.Path]::ChangeExtension($assemblyPath, '.pdb')
    $testCopy = Join-Path $testDirectory ([IO.Path]::GetFileName($assemblyPath))
    $testPdb = [IO.Path]::ChangeExtension($testCopy, '.pdb')
    foreach ($path in @($assemblyPath, $pdbPath, $testCopy, $testPdb)) {
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Missing compiled artifact: $path" }
    }
    if ((Get-Sha256 $assemblyPath) -ne (Get-Sha256 $testCopy) -or
        (Get-Sha256 $pdbPath) -ne (Get-Sha256 $testPdb)) {
        throw "The test directory contains a different build of $($record.AssemblyName)."
    }
    $sourcePaths = @($record.Compile | Sort-Object -Unique)
    $sources = @(
        foreach ($source in $sourcePaths) {
            [ordered]@{ path = $source; sha256 = $record.CompileHashes[$source] }
        }
    )
    $parameters = [Mono.Cecil.ReaderParameters]::new()
    $parameters.ReadSymbols = $true
    $assembly = [Mono.Cecil.AssemblyDefinition]::ReadAssembly($assemblyPath, $parameters)
    try {
        if ($assembly.MainModule.SymbolReader.GetType().Name -ne 'PortablePdbReader') {
            throw "Portable PDB required for $($record.AssemblyName)."
        }
        $methods = @()
        $checkedDocuments = @{}
        foreach ($type in (Get-AllTypes $assembly.MainModule.Types)) {
            foreach ($method in $type.Methods) {
                $points = @($method.DebugInformation.SequencePoints | Where-Object { -not $_.IsHidden })
                if ($points.Count -eq 0) { continue }
                $key = $record.AssemblyName + '|' + $method.FullName
                $exempt = $bindings.ContainsKey($key)
                $annotated = @($method.CustomAttributes | Where-Object {
                    $_.AttributeType.FullName -eq 'GTAVMods.Game.GamePrimitiveAttribute'
                }).Count -gt 0
                if ($annotated -ne $exempt) {
                    throw "Primitive annotation and reviewed manifest disagree: $key"
                }
                # Assembly consolidation must not let agent orchestration bypass
                # the typed engine contracts. Nested/lambda types inherit the
                # declaring namespace rather than acquiring a blanket DLL exemption.
                $owner = $type
                while ($null -ne $owner.DeclaringType) { $owner = $owner.DeclaringType }
                $engineContract = $record.AssemblyName -eq 'GameBridge' -and $owner.Namespace -eq 'GTAVMods.Game'
                if (-not $exempt -and -not $engineContract) {
                    foreach ($instruction in $method.Body.Instructions) {
                        if ($instruction.Operand -isnot [Mono.Cecil.MethodReference]) { continue }
                        $targetType = $instruction.Operand.DeclaringType.FullName
                        if (($targetType.StartsWith('GTA.') -and -not $targetType.StartsWith('GTA.Math.')) -or
                            $targetType -in @('GTAVMods.Game.INativeInvoker', 'GTAVMods.Game.IRawNative',
                                'GTAVMods.Game.NativeInvoker', 'GTAVMods.Game.GtaNative', 'GTAVMods.Game.GtaArgumentMarshaller',
                                'GTAVMods.Game.IRawNativeOutputs', 'GTAVMods.Game.GtaOutputs',
                                'GTAVMods.Game.IWorldQueryBindings', 'GTAVMods.Game.GtaWorldQueries',
                                'GTAVMods.Game.IHandleLifetimeBindings', 'GTAVMods.Game.IEntityLifetimeBindings',
                                'GTAVMods.Game.ICameraLifetimeBindings', 'GTAVMods.Game.IRopeLifetimeBindings',
                                'GTAVMods.Game.GtaEntityLifetimeBindings', 'GTAVMods.Game.GtaCameraLifetimeBindings',
                                'GTAVMods.Game.GtaRopeLifetimeBindings')) {
                            throw "Behavior bypasses the typed engine contracts: $key calls $($instruction.Operand.FullName)"
                        }
                    }
                }
                if ($exempt) {
                    if ($method.Body.HasExceptionHandlers -or
                        @($method.Body.Instructions | Where-Object {
                            $_.OpCode.FlowControl.ToString() -eq 'Cond_Branch' -or
                            ($_.OpCode.FlowControl.ToString() -eq 'Branch' -and $_.Operand.Offset -le $_.Offset)
                        }).Count -gt 0) {
                        throw "Runtime binding contains branching or exception handling: $key"
                    }
                    if (@($method.Body.Instructions | Where-Object {
                        $_.OpCode.Name -match '^(add|sub|mul|div|rem|neg|shl|shr)(\.|$)'
                    }).Count -gt 0) {
                        throw "Runtime binding contains argument calculations: $key"
                    }
                    $usedBindings[$key] = $true
                }
                $documents = @()
                foreach ($group in ($points | Group-Object { $_.Document.Url })) {
                    $path = Get-CanonicalPath $group.Name
                    Assert-DocumentSource $group.Group[0].Document $sourcePaths $checkedDocuments
                    $lines = @($group.Group | ForEach-Object {
                        $instruction = $method.Body.Instructions | Where-Object Offset -eq $_.Offset | Select-Object -First 1
                        if ($null -ne $instruction -and -not $symbols.SkipNotCoverableInstruction($method, $instruction)) {
                            # The pinned collector instruments the inclusive source
                            # span, not only the first line of a statement.
                            $_.StartLine..$_.EndLine
                        }
                    } | Sort-Object -Unique)
                    if ($lines.Count -gt 0) {
                        $documents += [ordered]@{ path = $path; lines = $lines }
                    }
                }
                $branches = @($symbols.GetBranchPoints($method) | Where-Object {
                    $_.StartLine -gt 0 -and $_.StartLine -ne 0xFEEFEE
                } | ForEach-Object {
                    [ordered]@{
                        document = Get-CanonicalPath $_.Document
                        line = $_.StartLine; offset = $_.Offset
                        endOffset = $_.EndOffset; path = $_.Path; ordinal = $_.Ordinal
                    }
                })
                if ($documents.Count -gt 0) {
                    $generatedOwner = $null
                    if ($type.FullName.Contains('<>c') -and
                        @($type.CustomAttributes | Where-Object {
                            $_.AttributeType.FullName -eq 'System.Runtime.CompilerServices.CompilerGeneratedAttribute'
                        }).Count -gt 0 -and $method.Name -match '^<([^<>]+)>b__') {
                        $ownerName = $Matches[1]
                        $generatedSignature = $method.FullName
                        $owners = @($type.DeclaringType.Methods | Where-Object {
                            $_.Name -eq $ownerName -and $_.HasBody -and
                            @($_.Body.Instructions | Where-Object {
                                $_.OpCode.Name -eq 'ldftn' -and $_.Operand.FullName -eq $generatedSignature
                            }).Count -gt 0
                        })
                        if ($owners.Count -eq 1) {
                            $generatedOwner = @{ name = $ownerName; signature = $owners[0].FullName }
                        }
                    }
                    $methods += [ordered]@{
                        signature = $method.FullName; type = $type.FullName; name = $method.Name
                        generatedOwner = $generatedOwner
                        binding = $exempt; documents = $documents; branches = $branches
                    }
                }
            }
        }
        if ($methods.Count -eq 0) { throw "No executable methods found in $($record.AssemblyName)." }
        # Coverlet's GetCoverageResult moves compiler-generated-class branches to
        # the method owning the same document line. Permit that attribution only
        # when compiler metadata also identifies that exact enclosing method.
        $lineOwners = @{}
        foreach ($method in @($methods | Where-Object { -not $_.binding })) {
            foreach ($document in $method.documents) {
                foreach ($line in $document.lines) {
                    $lineKey = $document.path.ToLowerInvariant() + '|' + $line
                    if (-not $lineOwners.ContainsKey($lineKey)) { $lineOwners[$lineKey] = $method }
                }
            }
        }
        $branchOwners = @{}
        foreach ($method in @($methods | Where-Object { -not $_.binding })) {
            foreach ($branch in $method.branches) {
                $branch.reportSignature = $method.signature
                $lineKey = $branch.document.ToLowerInvariant() + '|' + $branch.line
                if ($null -ne $method.generatedOwner -and $lineOwners.ContainsKey($lineKey)) {
                    $owner = $lineOwners[$lineKey]
                    if ($owner.signature -eq $method.generatedOwner.signature) {
                        $branch.reportSignature = $owner.signature
                    }
                }
                $branchKey = '{0}|{1}|{2}|{3}|{4}|{5}|{6}' -f $branch.document.ToLowerInvariant(),
                    $branch.reportSignature, $branch.line, $branch.offset, $branch.endOffset, $branch.path, $branch.ordinal
                if ($branchOwners.ContainsKey($branchKey)) {
                    throw "Ambiguous reported branch identity: $($record.AssemblyName) $branchKey"
                }
                $branchOwners[$branchKey] = $method.signature
            }
        }
        $modules += [ordered]@{
            name = $record.AssemblyName; project = $record.Project
            configuration = $record.Configuration
            assembly = $assemblyPath; assemblySha256 = Get-Sha256 $assemblyPath
            pdb = $pdbPath; pdbSha256 = Get-Sha256 $pdbPath
            testCopy = $testCopy; testPdb = $testPdb
            sources = $sources; methods = $methods
        }
    }
    finally { $assembly.Dispose() }
}
foreach ($key in $bindings.Keys) {
    if (-not $usedBindings.ContainsKey($key)) { throw "Stale or unknown runtime binding: $key" }
}
$inventory = [ordered]@{
    schemaVersion = 2; createdUtc = [DateTime]::UtcNow.ToString('O')
    collectorVersion = $coverletVersion
    boundaryPath = $bindingsPath; boundarySha256 = Get-Sha256 $bindingsPath
    modules = $modules; testAssembly = $testAssembly
}
$inventoryPath = Join-Path $RunDirectory 'inventory.json'
$inventory | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $inventoryPath -Encoding UTF8
[ordered]@{
    schemaVersion = 2; startedUtc = [DateTime]::UtcNow.ToString('O')
    inventorySha256 = Get-Sha256 $inventoryPath
} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $RunDirectory 'run.json') -Encoding UTF8
Write-Host "Inventoried $($modules.Count) production assemblies, one test assembly and $($usedBindings.Count) audited runtime bindings."
