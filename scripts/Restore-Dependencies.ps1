[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
$repoRoot = Split-Path -Parent $PSScriptRoot
$dependencyRoot = Join-Path $repoRoot '.deps'
$destination = Join-Path $dependencyRoot 'shvdne'
$apiPath = Join-Path $destination 'ScriptHookVDotNet3.dll'
$apiSha256 = '1e4d0bc4d34d05a6038fc87eede492d3dc4265522de473ec19cdf6746b5b0e48'
if ((Test-Path -LiteralPath $apiPath) -and (Get-FileHash -LiteralPath $apiPath -Algorithm SHA256).Hash -eq $apiSha256) {
    Write-Host 'SHVDN Enhanced v1.1.0.6 API verified.'
    return
}
New-Item -ItemType Directory -Path $dependencyRoot -Force | Out-Null
$archive = Join-Path $dependencyRoot 'ScriptHookVDotNetEnhanced-v1.1.0.6.zip'
$archiveSha256 = 'f10da8819fa6814fb0a04553567d2516934eac96b16af06ad8f315f0da00473a'
if (!(Test-Path -LiteralPath $archive) -or (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash -ne $archiveSha256) {
    Invoke-WebRequest -UseBasicParsing -Uri 'https://github.com/Chiheb-Bacha/ScriptHookVDotNetEnhanced/releases/download/v1.1.0.6/ScriptHookVDotNetEnhanced-v1.1.0.6.zip' -OutFile $archive
}
if ((Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash -ne $archiveSha256) {
    throw 'SHVDN Enhanced archive checksum mismatch. No files were extracted.'
}
Expand-Archive -LiteralPath $archive -DestinationPath $destination -Force
if ((Get-FileHash -LiteralPath $apiPath -Algorithm SHA256).Hash -ne $apiSha256) {
    throw 'SHVDN Enhanced API checksum mismatch.'
}
Write-Host 'SHVDN Enhanced v1.1.0.6 restored and verified.'
