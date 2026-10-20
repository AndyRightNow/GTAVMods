[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][object[]]$Sources,
    [Parameter(Mandatory = $true)][string]$RepositoryRoot,
    [Parameter(Mandatory = $true)][string]$SnapshotDirectory
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repositoryPrefix = [IO.Path]::GetFullPath($RepositoryRoot).TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
$snapshotPrefix = [IO.Path]::GetFullPath($SnapshotDirectory).TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
$copied = @{}
foreach ($source in $Sources) {
    $path = [IO.Path]::GetFullPath($source.path)
    if (-not $path.StartsWith($repositoryPrefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Compiler source is outside the repository: $path"
    }
    $relative = $path.Substring($repositoryPrefix.Length).Replace('\', '/')
    if ((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $source.sha256) {
        throw "Compiler source changed since coverage: $relative"
    }
    $destination = [IO.Path]::GetFullPath((Join-Path $snapshotPrefix $relative))
    if (-not $destination.StartsWith($snapshotPrefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Unexpected compiler source path: $relative"
    }
    # Preserve all measured compiler inputs, whether Git tracks, ignores or has
    # never seen them. This includes test regressions and generated attributes.
    if (-not (Test-Path -LiteralPath $destination -PathType Leaf)) {
        $null = New-Item -ItemType Directory -Path (Split-Path -Parent $destination) -Force
        Copy-Item -LiteralPath $path -Destination $destination
    }
    $hash = (Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($hash -ne $source.sha256) { throw "Snapshot differs from the measured compiler source: $relative" }
    $copied[$relative] = [ordered]@{ path = $relative; sha256 = $hash }
}
$copied.Values | Sort-Object { $_.path }
