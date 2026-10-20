[CmdletBinding(SupportsShouldProcess)]
param([Parameter(Mandatory=$true)][string]$GameDirectory,[string]$DotNet='dotnet')
$ErrorActionPreference='Stop'
$repo=Split-Path -Parent $PSScriptRoot
$game=(Resolve-Path -LiteralPath $GameDirectory).Path
if (!(Test-Path -LiteralPath (Join-Path $game 'GTA5_Enhanced.exe'))) { throw 'Enhanced game directory required.' }
if (Get-Process GTA5_Enhanced -ErrorAction SilentlyContinue) { throw 'Close GTA V before changing model archives.' }
if (!(Test-Path -LiteralPath (Join-Path $game 'OpenRPF.asi'))) { throw 'Install OpenRPF 0.3 or a compatible later version first.' }
$relative='update/x64/dlcpacks/patchday3ng/dlc.rpf'
$source=Join-Path $game $relative
$destination=[IO.Path]::GetFullPath((Join-Path (Join-Path $game 'mods') $relative))
$modsRoot=[IO.Path]::GetFullPath((Join-Path $game 'mods'))+[IO.Path]::DirectorySeparatorChar
if (!$destination.StartsWith($modsRoot,[StringComparison]::OrdinalIgnoreCase)) { throw 'Invalid destination.' }
if (Test-Path -LiteralPath $destination) { throw 'An override archive already exists. Preserve other mods and import the two Enhanced assets into its weapons.rpf using CodeWalker.' }
if ($PSCmdlet.ShouldProcess($destination,'Create an override and install the Enhanced Mjolnir assets')) {
    & (Join-Path $PSScriptRoot 'Prepare-AssetTool.ps1') -DotNet $DotNet
    New-Item -ItemType Directory -Path (Split-Path -Parent $destination) -Force | Out-Null
    Copy-Item -LiteralPath $source -Destination $destination
    & $DotNet (Join-Path $repo 'tools/EnhancedAssets/bin/Release/net10.0/EnhancedAssets.dll') replace $game $destination (Join-Path $repo 'Thor/resources/enhanced')
    if ($LASTEXITCODE -ne 0) { throw 'Import failed. The original game archive remains intact; inspect the new override before launching.' }
    Get-FileHash -LiteralPath $destination -Algorithm SHA256
}
