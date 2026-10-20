[CmdletBinding()]
param([string]$DotNet = 'dotnet')
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repository = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$inventoryScript = Join-Path (Split-Path -Parent $PSScriptRoot) 'New-CoverageInventory.ps1'
$fixture = Join-Path ([IO.Path]::GetTempPath()) ('GTAVMods-CoverageInventory-' + [Guid]::NewGuid().ToString('N'))
$null = New-Item -ItemType Directory -Path $fixture
function Write-Json($Value, [string]$Path) {
    $Value | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $Path -Encoding UTF8
}
function Assert-Rejected([string]$Expected) {
    $message = ''
    try { & $inventoryScript -RunDirectory $runDirectory -RepositoryRoot $fixture -BoundaryManifest $boundaryPath 6>$null }
    catch { $message = $_.Exception.Message }
    if ($message -notlike "*$Expected*") { throw "Expected '$Expected', received '$message'." }
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
        @("Project|$project", "AssemblyName|$name", "TargetPath|$binary", 'IsTestProject|false',
            "NuGetPackageRoot|$packageRoot", "Compile|$source") |
            Set-Content -LiteralPath (Join-Path $runDirectory "inputs/$name.txt")
        $bindings += @{ assembly = $name; method = 'System.Int32 Example::Binding(System.Int32)'; reason = 'Branch-free synthetic binding used only to verify inventory enforcement.' }
    }
    $testProject = Join-Path $fixture 'ModUtilsTest.csproj'
    '<Project />' | Set-Content -LiteralPath $testProject
    $solutionLines += 'Project("{TEST}") = "ModUtilsTest", "ModUtilsTest.csproj", "{TEST}"'
    $solutionLines | Set-Content -LiteralPath (Join-Path $fixture 'GTAVMods.sln')
    @("Project|$testProject", 'AssemblyName|ModUtilsTest', "TargetPath|$outputDirectory/ModUtilsTest.dll",
        'IsTestProject|true', "NuGetPackageRoot|$packageRoot") |
        Set-Content -LiteralPath (Join-Path $runDirectory 'inputs/ModUtilsTest.txt')
    $boundaryPath = Join-Path $fixture 'boundaries.json'
    Write-Json @{ schemaVersion = 1; bindings = $bindings } $boundaryPath
    & $inventoryScript -RunDirectory $runDirectory -RepositoryRoot $fixture -BoundaryManifest $boundaryPath 6>$null
    $inventory = Get-Content -Raw -LiteralPath (Join-Path $runDirectory 'inventory.json') | ConvertFrom-Json
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

    [IO.File]::AppendAllText($source, '// changed after compilation')
    Assert-Rejected 'Source changed since compilation'
    [IO.File]::WriteAllText($source, $sourceText)

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
    Assert-Rejected 'Ambiguous reported branch identity'

    $branchedSource = $sourceText.Replace('return value;', 'return value > 0 ? value : -value;')
    [IO.File]::WriteAllText($source, $branchedSource)
    foreach ($name in @('Thor', 'ModUtils', 'DeveloperConsole')) {
        & $DotNet exec $compiler /nologo /noconfig /nostdlib+ /target:library /optimize+ /debug:portable `
            "/reference:$frameworkReference" "/out:$outputDirectory/$name.dll" $source
        if ($LASTEXITCODE -ne 0) { throw 'Branched fixture compilation failed.' }
    }
    Assert-Rejected 'Runtime binding contains branching'
    Write-Host 'Coverage inventory checks passed: portable PDBs, setter-lambda ownership, source freshness, exact primitive review and branch rejection.'
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
