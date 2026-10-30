[CmdletBinding()]
param([string]$Cargo = 'cargo')
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$previousTarget = $env:CARGO_TARGET_DIR
try {
    $env:CARGO_TARGET_DIR = Join-Path $repoRoot 'artifacts/rust'
    $manifest = Join-Path $repoRoot 'tools/gta-use-server/Cargo.toml'
    & $Cargo fmt --manifest-path $manifest --check
    if ($LASTEXITCODE -ne 0) { throw 'Rust formatting failed.' }
    & $Cargo clippy --manifest-path $manifest --locked --all-targets -- -D warnings
    if ($LASTEXITCODE -ne 0) { throw 'Rust lint failed.' }
    & $Cargo test --manifest-path $manifest --locked
    if ($LASTEXITCODE -ne 0) { throw 'Rust service scenarios failed.' }
    & $Cargo rustc --manifest-path $manifest --locked --release -- -C target-feature=+crt-static
    if ($LASTEXITCODE -ne 0) { throw 'Rust release build failed.' }
    if ($env:GTA_USE_RUST_TEST_EXECUTABLE) {
        Copy-Item -LiteralPath (Join-Path $env:CARGO_TARGET_DIR 'release/gta-use-server.exe') -Destination $env:GTA_USE_RUST_TEST_EXECUTABLE -Force
    }
    # Preserve upstream copyright and license texts alongside the distributed
    # binary. A fresh directory prevents obsolete notices from prior builds.
    $notices = Join-Path $env:CARGO_TARGET_DIR ('notices-' + [Guid]::NewGuid().ToString('N'))
    $null = New-Item -ItemType Directory -Path $notices
    $rustc = Join-Path (Split-Path -Parent (Get-Command $Cargo).Source) 'rustc.exe'
    $target = ((& $rustc -vV | Where-Object { $_ -like 'host: *' }) -replace '^host: ', '').Trim()
    $included = @(& $Cargo tree --manifest-path $manifest --locked --target $target --edges normal,build --prefix none --format '{p}' |
        ForEach-Object { ($_ -split ' ')[0..1] -join ' ' } | Sort-Object -Unique)
    if ($LASTEXITCODE -ne 0) { throw 'Cannot identify shipped Rust dependency graph.' }
    $metadata = & $Cargo metadata --manifest-path $manifest --locked --format-version 1 --filter-platform $target | ConvertFrom-Json
    if ($LASTEXITCODE -ne 0) { throw 'Cannot enumerate Rust dependency notices.' }
    $packages = @($metadata.packages | Where-Object { $_.source -and ($_.name + ' v' + $_.version) -in $included })
    foreach ($package in $packages) {
        $destination = Join-Path $notices ($package.name + '-' + $package.version)
        $null = New-Item -ItemType Directory -Path $destination
        $source = Split-Path -Parent $package.manifest_path
        $files = @(Get-ChildItem -LiteralPath $source | Where-Object { $_.Name -match '^(LICENSE|COPYING|NOTICE)' })
        if ($files.Count -eq 0) { throw "No upstream license text found for $($package.name)." }
        $files | Copy-Item -Destination $destination -Recurse
    }
    $packages | Select-Object name, version, license, source, repository | ConvertTo-Json -Depth 3 |
        Set-Content -LiteralPath (Join-Path $notices 'dependencies.json') -Encoding UTF8
    $sysroot = & $rustc --print sysroot
    if ($LASTEXITCODE -ne 0) { throw 'Cannot locate Rust runtime notices.' }
    Copy-Item -LiteralPath (Join-Path $sysroot 'share/doc/rust/COPYRIGHT-library.html') -Destination $notices
    Copy-Item -LiteralPath (Join-Path $sysroot 'share/doc/rust/licenses') -Destination $notices -Recurse
    # Crate archives can use Unix-epoch timestamps, which ZIP cannot encode.
    # Normalize only these copied notices; preserve their original contents.
    Get-ChildItem -LiteralPath $notices -File -Recurse | ForEach-Object { $_.LastWriteTimeUtc = [DateTime]'2000-01-01T00:00:00Z' }
    $notices | Set-Content -LiteralPath (Join-Path $env:CARGO_TARGET_DIR 'notices-path.txt') -Encoding UTF8
} finally { $env:CARGO_TARGET_DIR = $previousTarget }
