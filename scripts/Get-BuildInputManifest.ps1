[CmdletBinding()]
param([string]$RepositoryRoot = (Split-Path -Parent $PSScriptRoot))
$ErrorActionPreference = 'Stop'
Push-Location $RepositoryRoot
try {
    $files = @(& git -c core.quotepath=false ls-files --cached --others --exclude-standard)
    if ($LASTEXITCODE -ne 0) { throw 'Cannot enumerate build inputs.' }
    foreach ($relative in ($files | Sort-Object -Unique)) {
        if ([IO.Path]::GetExtension($relative) -notin @('.cs', '.csproj', '.sln', '.props',
                '.targets', '.json', '.config', '.ps1', '.runsettings', '.py', '.toml', '.lock', '.python-version') -and
                [IO.Path]::GetFileName($relative) -ne '.python-version') { continue }
        $path = Join-Path $RepositoryRoot $relative
        if (Test-Path -LiteralPath $path -PathType Leaf) {
            [pscustomobject]@{ path = $relative; sha256 = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash }
        }
    }
} finally { Pop-Location }
