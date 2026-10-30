[CmdletBinding()]
param([ValidateSet('Debug', 'Release')][string]$Configuration = 'Release')
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$channel = [regex]::Match((Get-Content -LiteralPath (Join-Path $repo 'rust-toolchain.toml') -Raw), '(?m)^channel\s*=\s*"([^"]+)"').Groups[1].Value
$toolchain = $channel + '-x86_64-pc-windows-gnullvm'
$dotnet = Join-Path $repo '.dotnet/dotnet.exe'
$uv = Join-Path $repo '.deps/uv/bin/uv.exe'
$cargo = Join-Path $repo '.deps/cargo/bin/cargo.exe'
$linker = Join-Path $repo ".deps/rustup/toolchains/$toolchain/lib/rustlib/x86_64-pc-windows-gnullvm/bin/rust-lld.exe"
foreach ($path in @($dotnet, $uv, $cargo, $linker)) {
    if (!(Test-Path -LiteralPath $path -PathType Leaf)) { throw "Portable build tool missing: $path. Prepare the pinned tools or use Build.ps1 with explicit tool paths." }
}
$settings = @{
    DOTNET_ROOT = Join-Path $repo '.dotnet'
    DOTNET_CLI_HOME = Join-Path $repo '.deps/dotnet-home'
    NUGET_PACKAGES = Join-Path $repo '.deps/nuget'
    UV_CACHE_DIR = Join-Path $repo '.deps/uv-cache'
    UV_PYTHON_INSTALL_DIR = Join-Path $repo '.deps/python-runtime'
    UV_PROJECT_ENVIRONMENT = Join-Path $repo '.venv'
    CARGO_HOME = Join-Path $repo '.deps/cargo'
    RUSTUP_HOME = Join-Path $repo '.deps/rustup'
    RUSTUP_TOOLCHAIN = $toolchain
    CARGO_TARGET_X86_64_PC_WINDOWS_GNULLVM_LINKER = $linker
    PATH = (Join-Path $repo '.deps/cargo/bin') + ';' + (Join-Path $repo '.dotnet') + ';' + $env:PATH
}
$previous = @{}
foreach ($name in $settings.Keys) { $previous[$name] = [Environment]::GetEnvironmentVariable($name, 'Process') }
try {
    foreach ($name in $settings.Keys) { [Environment]::SetEnvironmentVariable($name, $settings[$name], 'Process') }
    & (Join-Path $PSScriptRoot 'Build.ps1') -Configuration $Configuration -DotNet $dotnet -Uv $uv -Cargo $cargo
} finally {
    foreach ($name in $settings.Keys) { [Environment]::SetEnvironmentVariable($name, $previous[$name], 'Process') }
}
