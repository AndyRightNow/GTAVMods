[CmdletBinding()]
param([Parameter(Mandatory = $true)][string]$GameDirectory)
$ErrorActionPreference = 'Stop'
$gameRoot = (Resolve-Path -LiteralPath $GameDirectory).Path
$executable = Join-Path $gameRoot 'GTA5_Enhanced.exe'
if (!(Test-Path -LiteralPath $executable -PathType Leaf)) {
    throw 'This folder does not contain GTA5_Enhanced.exe.'
}
$versionInfo = (Get-Item -LiteralPath $executable).VersionInfo
$version = [Version]::new($versionInfo.FileMajorPart, $versionInfo.FileMinorPart, $versionInfo.FileBuildPart, $versionInfo.FilePrivatePart)
$required = @('ScriptHookV.dll', 'xinput1_4.dll', 'ScriptHookVDotNet.asi', 'ScriptHookVDotNet.ini', 'ScriptHookVDotNet2.dll', 'ScriptHookVDotNet3.dll', 'MinHook.x64.dll')
$missing = @($required | Where-Object { !(Test-Path -LiteralPath (Join-Path $gameRoot $_) -PathType Leaf) })
$api = Join-Path $gameRoot 'ScriptHookVDotNet3.dll'
$matchesApi = (Test-Path -LiteralPath $api) -and
    (Get-FileHash -LiteralPath $api -Algorithm SHA256).Hash -eq '1e4d0bc4d34d05a6038fc87eede492d3dc4265522de473ec19cdf6746b5b0e48'
[PSCustomObject]@{
    GameDirectory = $gameRoot
    ExecutableVersion = $version.ToString()
    UpstreamAssessedEnhancedBuild = '1.0.1158.13'
    MatchesAssessedBuild = $version -eq [Version]'1.0.1158.13'
    StoryModeSmokeTestedBuild = '1.0.1158.16'
    MatchesSmokeTestedBuild = $version -eq [Version]'1.0.1158.16'
    MissingRuntimeFiles = $missing -join ', '
    MatchesPinnedEnhancedApi = $matchesApi
    StoryStartupInstalled = (Test-Path -LiteralPath (Join-Path $gameRoot 'StraightToStoryMode.asi'))
    StoryStartupGuide = 'docs/story-startup.md (optional; requires a cold-launch test)'
    InGameValidation = 'Required; file presence and version matching alone do not prove compatibility.'
}
