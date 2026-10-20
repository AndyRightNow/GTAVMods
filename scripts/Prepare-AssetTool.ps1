[CmdletBinding()]
param([string]$DotNet='dotnet')
$ErrorActionPreference='Stop'
$repo=Split-Path -Parent $PSScriptRoot
$cw=Join-Path $repo '.deps/codewalker'
$revision='485d56bec00262ed7fa472261cce7bbc6202b96e'
if (!(Test-Path -LiteralPath $cw)) {
    & git clone https://github.com/dexyfex/CodeWalker.git $cw
    if ($LASTEXITCODE -ne 0) { throw 'CodeWalker clone failed.' }
    & git -C $cw checkout --detach $revision
    if ($LASTEXITCODE -ne 0) { throw 'CodeWalker revision checkout failed.' }
}
$head=& git -C $cw rev-parse HEAD
if ($head -ne $revision) { throw "CodeWalker must be at $revision; existing checkout was preserved." }
& $DotNet build (Join-Path $repo 'tools/EnhancedAssets/EnhancedAssets.csproj') -c Release -p:ImportDirectoryBuildProps=false
if ($LASTEXITCODE -ne 0) { throw 'Asset tool build failed.' }
