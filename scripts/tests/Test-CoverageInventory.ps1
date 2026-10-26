[CmdletBinding()]
param([string]$DotNet = 'dotnet')
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repository = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$inventoryScript = Join-Path (Split-Path -Parent $PSScriptRoot) 'New-CoverageInventory.ps1'
$copySourcesScript = Join-Path (Split-Path -Parent $PSScriptRoot) 'Copy-CompilerSources.ps1'
$fixture = Join-Path ([IO.Path]::GetTempPath()) ('GTAVMods-CoverageInventory-' + [Guid]::NewGuid().ToString('N'))
$null = New-Item -ItemType Directory -Path $fixture
function Write-Json($Value, [string]$Path) {
    $Value | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $Path -Encoding UTF8
}
function Get-CompileHash([string]$Path) {
    'CompileHash|' + $Path + '|' + (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash
}
function Update-ProductionHashes {
    foreach ($name in @('Thor', 'ModUtils', 'DeveloperConsole')) {
        $recordPath = Join-Path $runDirectory "inputs/$name.txt"
        $records = @(Get-Content -LiteralPath $recordPath | Where-Object { -not $_.StartsWith('CompileHash|') })
        $records + (Get-CompileHash $source) | Set-Content -LiteralPath $recordPath
    }
}
function Assert-Rejected([string]$Expected) {
    $message = ''
    try { & $inventoryScript -RunDirectory $runDirectory -RepositoryRoot $fixture -BoundaryManifest $boundaryPath 6>$null }
    catch { $message = $_.Exception.Message }
    if ($message -notlike "*$Expected*") { throw "Expected '$Expected', received '$message'." }
}
function Assert-SnapshotRejected([object[]]$Sources, [string]$Expected) {
    $message = ''
    try { $null = & $copySourcesScript -Sources $Sources -RepositoryRoot $fixture -SnapshotDirectory $snapshot }
    catch { $message = $_.Exception.Message }
    if ($message -notlike "*$Expected*") { throw "Expected '$Expected', received '$message'." }
}
function Assert-MutationConfigurationFixture($Inventory) {
    # Load only the runner's validation function, without taking a snapshot or
    # starting Stryker. Exercise the exact guard against the generated inventory.
    $mutationScript = Join-Path (Split-Path -Parent $PSScriptRoot) 'Test-Mutations.ps1'
    $tokens = $null
    $parseErrors = $null
    $ast = [Management.Automation.Language.Parser]::ParseFile($mutationScript, [ref]$tokens, [ref]$parseErrors)
    $guards = @($ast.FindAll({
        param($node)
        $node -is [Management.Automation.Language.FunctionDefinitionAst] -and
            $node.Name -eq 'Assert-MutationBuildConfiguration'
    }, $true))
    if ($parseErrors.Count -ne 0 -or $guards.Count -ne 1) { throw 'Cannot identify the mutation configuration guard.' }
    . ([ScriptBlock]::Create($guards[0].Extent.Text))
    Assert-MutationBuildConfiguration $Inventory
    foreach ($compiled in (@($Inventory.modules) + @($Inventory.testAssembly))) {
        $compiled.configuration = 'Debug'
        $message = ''
        try { Assert-MutationBuildConfiguration $Inventory } catch { $message = $_.Exception.Message }
        if ($message -notlike '*requires a Release coverage build*') {
            throw "Mutation accepted Debug configuration for $($compiled.name): $message"
        }
        $compiled.configuration = 'Release'
    }
    $Inventory.testAssembly.PSObject.Properties.Remove('configuration')
    $message = ''
    try { Assert-MutationBuildConfiguration $Inventory } catch { $message = $_.Exception.Message }
    if ($message -notlike '*requires a Release coverage build*') { throw 'Mutation accepted missing test configuration.' }
    $Inventory.testAssembly | Add-Member -NotePropertyName configuration -NotePropertyValue 'Release'
}
function Assert-CompileCapture {
    $capture = Join-Path $fixture 'capture'
    $null = New-Item -ItemType Directory -Path $capture
    $project = Join-Path $capture 'CaptureFixture.csproj'
    @'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
    <GenerateAssemblyInfo>false</GenerateAssemblyInfo>
    <GenerateTargetFrameworkAttribute>false</GenerateTargetFrameworkAttribute>
  </PropertyGroup>
  <ItemGroup><Compile Include="Version.cs" /></ItemGroup>
</Project>
'@ | Set-Content -LiteralPath $project
    $captureSource = Join-Path $capture 'Version.cs'
    '[assembly: System.Reflection.AssemblyVersion("1.0.0.0")]' | Set-Content -LiteralPath $captureSource
    $sourceTime = (Get-Item -LiteralPath $captureSource).LastWriteTimeUtc
    $nugetConfig = Join-Path $capture 'NuGet.Config'
    '<configuration><packageSources><clear /></packageSources></configuration>' | Set-Content -LiteralPath $nugetConfig
    # Windows PowerShell turns redirected native stderr into ErrorRecords.
    # Inspect the process exit code, including any diagnostics in the failure.
    $ErrorActionPreference = 'Continue'
    $output = & $DotNet restore $project --configfile $nugetConfig --verbosity quiet 2>&1
    $ErrorActionPreference = 'Stop'
    if ($LASTEXITCODE -ne 0) { throw "Capture fixture restore failed: $output" }
    $targetScript = Join-Path (Split-Path -Parent $PSScriptRoot) 'Coverage.Inventory.targets'
    $captureRun = Join-Path $capture 'run'
    foreach ($build in @(
        @{ Configuration = 'Debug'; Version = '1.0.0.0' },
        @{ Configuration = 'Debug'; Version = '2.0.0.0' },
        @{ Configuration = 'Release'; Version = '2.0.0.0' }
    )) {
        $version = $build.Version
        if ($version -eq '2.0.0.0') {
            ('[assembly: System.Reflection.AssemblyVersion("' + $version + '")]') | Set-Content -LiteralPath $captureSource
            # Ordinary timestamp-based incremental compilation would skip this
            # edit. Coverage validation must still compile the captured bytes.
            (Get-Item -LiteralPath $captureSource).LastWriteTimeUtc = $sourceTime
        }
        $ErrorActionPreference = 'Continue'
        $output = & $DotNet build $project --configuration $build.Configuration --no-restore --verbosity quiet `
            "-p:CustomAfterMicrosoftCommonTargets=$targetScript" "-p:CoverageRunDirectory=$captureRun" 2>&1
        $ErrorActionPreference = 'Stop'
        if ($LASTEXITCODE -ne 0) { throw "Capture fixture compilation failed: $output" }
        $records = @(Get-Content -LiteralPath (Join-Path $captureRun 'inputs/CaptureFixture.txt'))
        if (@($records | Where-Object { $_ -eq (Get-CompileHash $captureSource) }).Count -ne 1) {
            throw 'Imported coverage target did not capture the actual compiler input hash.'
        }
        if (@($records | Where-Object { $_ -eq ('Configuration|' + $build.Configuration) }).Count -ne 1) {
            throw 'Imported coverage target did not capture the actual build configuration.'
        }
        $binary = Join-Path $capture ("bin/" + $build.Configuration + "/net10.0/CaptureFixture.dll")
        if ([Reflection.AssemblyName]::GetAssemblyName($binary).Version.ToString() -ne $version) {
            throw 'Coverage target assigned fresh hashes to an incremental compilation that was skipped.'
        }
    }
}
try {
    $sdkVersion = ((& $DotNet --version) | Select-Object -Last 1).Trim()
    if ($LASTEXITCODE -ne 0) { throw 'Unable to find the selected SDK for inventory checks.' }
    $sdkLine = @((& $DotNet --list-sdks) | Where-Object { $_ -match ('^' + [regex]::Escape($sdkVersion) + ' \[') })
    if ($sdkLine.Count -ne 1 -or $sdkLine[0] -notmatch '\[(.+)\]') { throw 'Unable to locate the selected C# compiler.' }
    $compiler = Join-Path $Matches[1] "$sdkVersion/Roslyn/bincore/csc.dll"
    $frameworkReference = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/mscorlib.dll'
    $testProjectDirectory = Join-Path $fixture 'ThorTest'
    $runDirectory = Join-Path $fixture 'run'
    $outputDirectory = Join-Path $fixture 'bin'
    foreach ($folder in @($testProjectDirectory, $outputDirectory, (Join-Path $runDirectory 'inputs'))) {
        $null = New-Item -ItemType Directory -Path $folder
    }
    Copy-Item -LiteralPath (Join-Path $repository 'ThorTest/packages.lock.json') -Destination $testProjectDirectory
    $assets = Get-Content -Raw -LiteralPath (Join-Path $repository 'ThorTest/obj/project.assets.json') | ConvertFrom-Json
    $packageRoot = @($assets.packageFolders.PSObject.Properties.Name)[0]
    $source = Join-Path $fixture 'Behavior.cs'
    $sourceText = @'
using System;
namespace GTAVMods.Game {
    [AttributeUsage(AttributeTargets.Method)]
    public sealed class GamePrimitiveAttribute : Attribute { }
}
public class Example {
    public int Read(bool enabled) {
        return enabled
            ? 3
            : 7;
    }
    [GTAVMods.Game.GamePrimitive]
    public static int Binding(int value) { return value; }
    private int number;
    public int Number {
        set {
            bool changed = number != value;
            Action update = () => {
                if (changed && value != 0) number = value;
            };
            update();
        }
    }
}
'@
    [IO.File]::WriteAllText($source, $sourceText)
    $solutionLines = @()
    $bindings = @()
    foreach ($name in @('Thor', 'ModUtils', 'DeveloperConsole')) {
        $binary = Join-Path $outputDirectory "$name.dll"
        & $DotNet exec $compiler /nologo /noconfig /nostdlib+ /target:library /optimize+ /debug:portable `
            "/reference:$frameworkReference" "/out:$binary" $source
        if ($LASTEXITCODE -ne 0) { throw 'Portable-PDB fixture compilation failed.' }
        $project = Join-Path $fixture "$name.csproj"
        '<Project />' | Set-Content -LiteralPath $project
        $solutionLines += 'Project("{TEST}") = "' + $name + '", "' + $name + '.csproj", "{TEST}"'
        @("Project|$project", "AssemblyName|$name", 'Configuration|Release', "TargetPath|$binary", 'IsTestProject|false',
            "NuGetPackageRoot|$packageRoot", "Compile|$source", (Get-CompileHash $source)) |
            Set-Content -LiteralPath (Join-Path $runDirectory "inputs/$name.txt")
        $bindings += @{ assembly = $name; method = 'System.Int32 Example::Binding(System.Int32)'; reason = 'Branch-free synthetic binding used only to verify inventory enforcement.' }
    }
    $testProject = Join-Path $fixture 'ModUtilsTest.csproj'
    '<Project />' | Set-Content -LiteralPath $testProject
    $testSource = Join-Path $testProjectDirectory 'BehaviorTests.cs'
    $testSourceText = 'public class BehaviorTests { public static void Check() { if (new Example().Read(true) != 3) throw new System.Exception("enabled result"); } }'
    [IO.File]::WriteAllText($testSource, $testSourceText)
    $ignoredTestSource = Join-Path $testProjectDirectory 'IgnoredRegression.cs'
    $ignoredTestText = 'public class IgnoredRegression { public static void Check() { if (new Example().Read(false) != 7) throw new System.Exception("disabled result"); } }'
    [IO.File]::WriteAllText($ignoredTestSource, $ignoredTestText)
    $generatedDirectory = Join-Path $testProjectDirectory 'obj'
    $null = New-Item -ItemType Directory -Path $generatedDirectory
    $generatedSource = Join-Path $generatedDirectory 'AssemblyInfo.cs'
    '[assembly: System.Reflection.AssemblyVersion("1.0.0.0")]' | Set-Content -LiteralPath $generatedSource
    $testBinary = Join-Path $outputDirectory 'ModUtilsTest.dll'
    & $DotNet exec $compiler /nologo /noconfig /nostdlib+ /target:library /optimize+ /debug:portable `
        "/reference:$frameworkReference" "/reference:$outputDirectory/Thor.dll" "/out:$testBinary" `
        $testSource $ignoredTestSource $generatedSource
    if ($LASTEXITCODE -ne 0) { throw 'Test portable-PDB fixture compilation failed.' }
    $solutionLines += 'Project("{TEST}") = "ModUtilsTest", "ModUtilsTest.csproj", "{TEST}"'
    $solutionLines | Set-Content -LiteralPath (Join-Path $fixture 'GTAVMods.sln')
    @("Project|$testProject", 'AssemblyName|ModUtilsTest', 'Configuration|Release', "TargetPath|$testBinary",
        'IsTestProject|true', "NuGetPackageRoot|$packageRoot", "Compile|$testSource",
        "Compile|$ignoredTestSource", "Compile|$generatedSource", (Get-CompileHash $testSource),
        (Get-CompileHash $ignoredTestSource), (Get-CompileHash $generatedSource)) |
        Set-Content -LiteralPath (Join-Path $runDirectory 'inputs/ModUtilsTest.txt')
    $boundaryPath = Join-Path $fixture 'boundaries.json'
    Write-Json @{ schemaVersion = 1; bindings = $bindings } $boundaryPath
    & $inventoryScript -RunDirectory $runDirectory -RepositoryRoot $fixture -BoundaryManifest $boundaryPath 6>$null
    $inventory = Get-Content -Raw -LiteralPath (Join-Path $runDirectory 'inventory.json') | ConvertFrom-Json
    if ($inventory.testAssembly.configuration -ne 'Release' -or
        @($inventory.modules | Where-Object configuration -ne 'Release').Count -ne 0) {
        throw 'Inventory omitted or changed compiler build configuration.'
    }
    Assert-MutationConfigurationFixture $inventory
    $read = @($inventory.modules[0].methods | Where-Object signature -eq 'System.Int32 Example::Read(System.Boolean)')[0]
    if ($read.documents.Count -ne 1 -or $read.branches.Count -ne 2 -or $read.binding) {
        throw "Real PDB inventory did not preserve the behavior method and both branch paths: $($read | ConvertTo-Json -Depth 8 -Compress)"
    }
    if (@($read.documents[0].lines | Where-Object { $_ -ge 8 -and $_ -le 10 }).Count -ne 3) {
        throw 'Real PDB inventory omitted an interior line of a multiline statement.'
    }
    $binding = @($inventory.modules[0].methods | Where-Object signature -eq 'System.Int32 Example::Binding(System.Int32)')[0]
    if (-not $binding.binding) { throw 'Reviewed primitive was not inventoried separately.' }
    $setterLambda = @($inventory.modules[0].methods | Where-Object {
        $null -ne $_.generatedOwner -and $_.generatedOwner.name -eq 'set_Number'
    })
    if ($setterLambda.Count -ne 1 -or $setterLambda[0].branches.Count -ne 4 -or
        @($setterLambda[0].branches | Where-Object reportSignature -ne 'System.Void Example::set_Number(System.Int32)').Count -gt 0) {
        throw 'Real PDB inventory did not map all setter-lambda branches to their exact shared-line owner.'
    }

    if ($inventory.schemaVersion -ne 2 -or $inventory.testAssembly.sources.Count -ne 3 -or
        $inventory.testAssembly.sources.path -notcontains $ignoredTestSource -or
        $inventory.testAssembly.sources.path -notcontains $generatedSource -or
        $inventory.testAssembly.assemblySha256 -ne (Get-FileHash -LiteralPath $testBinary -Algorithm SHA256).Hash -or
        $inventory.testAssembly.pdbSha256 -ne (Get-FileHash -LiteralPath ([IO.Path]::ChangeExtension($testBinary, '.pdb')) -Algorithm SHA256).Hash) {
        throw 'Inventory did not retain the test build and every actual compiler input.'
    }
    # Reproduce Git omitting a compiled regression, then exercise the same copy
    # helper used by mutation runs. Generated inputs without PDB points stay too.
    @('ThorTest/IgnoredRegression.cs', 'ThorTest/obj/', 'bin/', 'run/', 'snapshot/') |
        Set-Content -LiteralPath (Join-Path $fixture '.gitignore')
    & git -C $fixture init --quiet
    if ($LASTEXITCODE -ne 0) { throw 'Cannot initialize compiler-source fixture repository.' }
    $ignored = @(& git -C $fixture check-ignore ThorTest/IgnoredRegression.cs ThorTest/obj/AssemblyInfo.cs)
    if ($LASTEXITCODE -ne 0 -or $ignored.Count -ne 2) { throw 'Fixture test inputs were not Git-ignored.' }
    $snapshot = Join-Path $fixture 'snapshot'
    $null = New-Item -ItemType Directory -Path $snapshot
    $gitFiles = @(& git -C $fixture -c core.quotepath=false ls-files --cached --others --exclude-standard)
    if ($LASTEXITCODE -ne 0) { throw 'Cannot enumerate fixture snapshot inputs.' }
    foreach ($relative in $gitFiles) {
        $destination = Join-Path $snapshot $relative
        $null = New-Item -ItemType Directory -Path (Split-Path -Parent $destination) -Force
        Copy-Item -LiteralPath (Join-Path $fixture $relative) -Destination $destination
    }
    $copiedTest = Join-Path $snapshot 'ThorTest/IgnoredRegression.cs'
    if (Test-Path -LiteralPath $copiedTest) { throw 'Git fixture unexpectedly copied the ignored regression.' }
    $compilerSources = @($inventory.modules.sources) + @($inventory.testAssembly.sources)
    $copiedSources = @(& $copySourcesScript -Sources $compilerSources -RepositoryRoot $fixture -SnapshotDirectory $snapshot)
    if ((Get-FileHash -LiteralPath $copiedTest -Algorithm SHA256).Hash -ne
        (Get-FileHash -LiteralPath $ignoredTestSource -Algorithm SHA256).Hash -or
        @($copiedSources | Where-Object path -eq 'ThorTest/IgnoredRegression.cs').Count -ne 1 -or
        @($copiedSources | Where-Object path -eq 'ThorTest/obj/AssemblyInfo.cs').Count -ne 1) {
        throw 'Mutation snapshot omitted an ignored compiled test or generated compiler input.'
    }
    $generatedText = [IO.File]::ReadAllText($generatedSource)
    [IO.File]::AppendAllText($generatedSource, '// changed after compilation')
    Assert-Rejected 'Compiler input changed since compilation'
    [IO.File]::WriteAllText($generatedSource, $generatedText)
    $testRecordPath = Join-Path $runDirectory 'inputs/ModUtilsTest.txt'
    $testInputRecords = @(Get-Content -LiteralPath $testRecordPath)
    $generatedHash = Get-CompileHash $generatedSource
    @($testInputRecords | Where-Object { $_ -ne $generatedHash }) | Set-Content -LiteralPath $testRecordPath
    Assert-Rejected 'Compiler input hashes are incomplete'
    @($testInputRecords) + $generatedHash | Set-Content -LiteralPath $testRecordPath
    Assert-Rejected 'Duplicate compiler input hash'
    $testInputRecords | Set-Content -LiteralPath $testRecordPath
    [IO.File]::AppendAllText($ignoredTestSource, '// changed after compilation')
    Assert-Rejected 'Compiler input changed since compilation'
    Assert-SnapshotRejected $compilerSources 'Compiler source changed since coverage'
    [IO.File]::WriteAllText($ignoredTestSource, $ignoredTestText)
    [IO.File]::AppendAllText($copiedTest, '// corrupted snapshot')
    Assert-SnapshotRejected $compilerSources 'Snapshot differs from the measured compiler source'
    [IO.File]::WriteAllText($copiedTest, $ignoredTestText)
    Assert-SnapshotRejected @(@{ path = (Join-Path $fixture '../outside.cs'); sha256 = 'unused' }) 'Compiler source is outside the repository'

    [IO.File]::AppendAllText($source, '// changed after compilation')
    Assert-Rejected 'Compiler input changed since compilation'
    # Fresh hashes cannot relabel old executable code: PDB checks remain an
    # independent guard as well as the complete compiler-time hash comparison.
    Update-ProductionHashes
    Assert-Rejected 'Source changed since compilation'
    [IO.File]::WriteAllText($source, $sourceText)
    Update-ProductionHashes

    Write-Json @{ schemaVersion = 1; bindings = @() } $boundaryPath
    Assert-Rejected 'Primitive annotation and reviewed manifest disagree'
    Write-Json @{ schemaVersion = 1; bindings = $bindings } $boundaryPath

    $collidingSource = $sourceText.Replace('Action update = () => {', 'Action other = () => { if (changed && value != 0) number = value; }; Action update = () => {')
    # Put both identical generated bodies on the same source line: their IL
    # offsets/path/ordinal would collide after Coverlet moves them to the setter.
    $collidingSource = [regex]::Replace($collidingSource,
        'Action other =.*?update\(\);',
        'Action other = () => { if (changed && value != 0) number = value; }; Action update = () => { if (changed && value != 0) number = value; }; other(); update();',
        [Text.RegularExpressions.RegexOptions]::Singleline)
    [IO.File]::WriteAllText($source, $collidingSource)
    foreach ($name in @('Thor', 'ModUtils', 'DeveloperConsole')) {
        & $DotNet exec $compiler /nologo /noconfig /nostdlib+ /target:library /optimize+ /debug:portable `
            "/reference:$frameworkReference" "/out:$outputDirectory/$name.dll" $source
        if ($LASTEXITCODE -ne 0) { throw 'Colliding-branch fixture compilation failed.' }
    }
    Update-ProductionHashes
    Assert-Rejected 'Ambiguous reported branch identity'

    $branchedSource = $sourceText.Replace('return value;', 'return value > 0 ? value : -value;')
    [IO.File]::WriteAllText($source, $branchedSource)
    foreach ($name in @('Thor', 'ModUtils', 'DeveloperConsole')) {
        & $DotNet exec $compiler /nologo /noconfig /nostdlib+ /target:library /optimize+ /debug:portable `
            "/reference:$frameworkReference" "/out:$outputDirectory/$name.dll" $source
        if ($LASTEXITCODE -ne 0) { throw 'Branched fixture compilation failed.' }
    }
    Update-ProductionHashes
    Assert-Rejected 'Runtime binding contains branching'
    foreach ($bypass in @(
        'namespace GTA { public static class Game { public static int Clock() { return 1; } } } public class Bypass { public int Read() { return GTA.Game.Clock(); } }',
        'namespace GTAVMods.Game { public interface INativeInvoker { void Call(); } } public class Bypass { public void Run(GTAVMods.Game.INativeInvoker native) { native.Call(); } }',
        'namespace GTAVMods.Game { public interface IRawNativeOutputs { void Call(); } } public class Bypass { public void Run(GTAVMods.Game.IRawNativeOutputs raw) { raw.Call(); } }',
        'namespace GTAVMods.Game { public interface IWorldQueryBindings { void Call(); } } public class Bypass { public void Run(GTAVMods.Game.IWorldQueryBindings raw) { raw.Call(); } }',
        'namespace GTAVMods.Game { public interface IEntityLifetimeBindings { void Call(); } } public class Bypass { public void Run(GTAVMods.Game.IEntityLifetimeBindings raw) { raw.Call(); } }'
    )) {
        [IO.File]::WriteAllText($source, $sourceText + $bypass)
        foreach ($name in @('Thor', 'ModUtils', 'DeveloperConsole')) {
            & $DotNet exec $compiler /nologo /noconfig /nostdlib+ /target:library /optimize+ /debug:portable `
                "/reference:$frameworkReference" "/out:$outputDirectory/$name.dll" $source
            if ($LASTEXITCODE -ne 0) { throw 'Contract-bypass fixture compilation failed.' }
        }
        Update-ProductionHashes
        Assert-Rejected 'Behavior bypasses the typed engine contracts'
    }
    Assert-CompileCapture
    Write-Host 'Coverage inventory checks passed: compiler-time hashes and forced compilation, Release mutation configuration, portable PDBs, test build/source freshness, ignored test snapshot retention, setter-lambda ownership, exact primitive review, branch rejection and typed-contract enforcement.'
}
finally {
    $resolved = [IO.Path]::GetFullPath($fixture)
    $tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\') + '\'
    if (-not $resolved.StartsWith($tempRoot, [StringComparison]::OrdinalIgnoreCase) -or
        [IO.Path]::GetFileName($resolved) -notlike 'GTAVMods-CoverageInventory-*') {
        throw "Refusing to clean an unexpected fixture directory: $resolved"
    }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
