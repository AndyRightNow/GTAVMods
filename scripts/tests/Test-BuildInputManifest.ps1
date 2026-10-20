[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$manifestScript = Join-Path (Split-Path -Parent $PSScriptRoot) 'Get-BuildInputManifest.ps1'
$mutationScript = Join-Path (Split-Path -Parent $PSScriptRoot) 'Test-Mutations.ps1'
$fixture = Join-Path ([IO.Path]::GetTempPath()) ('GTAVMods-BuildInputs-' + [Guid]::NewGuid().ToString('N'))
$null = New-Item -ItemType Directory -Path $fixture
try {
    & git init --quiet $fixture
    if ($LASTEXITCODE -ne 0) { throw 'Cannot initialize the build-input fixture.' }
    'artifacts/' | Set-Content -LiteralPath (Join-Path $fixture '.gitignore')
    'class Behavior {}' | Set-Content -LiteralPath (Join-Path $fixture 'Behavior.cs')
    '<Project />' | Set-Content -LiteralPath (Join-Path $fixture 'Tests.csproj')
    & git -C $fixture add .gitignore Behavior.cs Tests.csproj
    if ($LASTEXITCODE -ne 0) { throw 'Cannot stage fixture inputs.' }
    $before = @(& $manifestScript -RepositoryRoot $fixture)
    if ($before.Count -ne 2) { throw 'Manifest omitted tracked compiler inputs.' }
    $CoverageRunDirectory = Join-Path $fixture 'artifacts'
    $null = New-Item -ItemType Directory -Path $CoverageRunDirectory
    $before | ConvertTo-Json -Depth 4 |
        Set-Content -LiteralPath (Join-Path $CoverageRunDirectory 'build-inputs.json') -Encoding UTF8
    # Execute the runner's actual JSON assignment, without starting mutation.
    # A copied reader would miss a regression in its Windows PowerShell 5.1 shape.
    $parseErrors = $null
    $tokens = $null
    $ast = [Management.Automation.Language.Parser]::ParseFile($mutationScript, [ref]$tokens, [ref]$parseErrors)
    if ($parseErrors.Count -ne 0) { throw 'Cannot parse the mutation runner.' }
    $readers = @($ast.FindAll({
        param($node)
        $node -is [Management.Automation.Language.AssignmentStatementAst] -and
            $node.Left -is [Management.Automation.Language.VariableExpressionAst] -and
            $node.Left.VariablePath.UserPath -eq 'validatedInputs'
    }, $true))
    if ($readers.Count -ne 1) { throw 'Cannot identify the mutation runner manifest reader.' }
    . ([ScriptBlock]::Create($readers[0].Extent.Text))
    if ($validatedInputs.Count -ne 2 -or
        @(Compare-Object $validatedInputs $before -Property path, sha256).Count -ne 0) {
        throw 'Mutation manifest JSON roundtrip changed the input array or rejected unchanged inputs.'
    }
    'class NewTest {}' | Set-Content -LiteralPath (Join-Path $fixture 'NewTest.cs')
    'class Generated {}' | Set-Content -LiteralPath (Join-Path $fixture 'artifacts/Generated.cs')
    'class Behavior { int Value = 1; }' | Set-Content -LiteralPath (Join-Path $fixture 'Behavior.cs')
    $after = @(& $manifestScript -RepositoryRoot $fixture)
    if ($after.Count -ne 3 -or @($after | Where-Object path -eq 'NewTest.cs').Count -ne 1) {
        throw 'Manifest must discover new test sources and ignore build outputs.'
    }
    if (($before | Where-Object path -eq 'Behavior.cs').sha256 -eq
        ($after | Where-Object path -eq 'Behavior.cs').sha256) {
        throw 'Manifest accepted changed source content.'
    }
    $changes = @(Compare-Object $validatedInputs $after -Property path, sha256)
    if ($changes.Count -ne 3 -or
        @($changes | Where-Object { $_.path -eq 'Behavior.cs' -and $_.SideIndicator -eq '<=' }).Count -ne 1 -or
        @($changes | Where-Object { $_.path -eq 'Behavior.cs' -and $_.SideIndicator -eq '=>' }).Count -ne 1 -or
        @($changes | Where-Object { $_.path -eq 'NewTest.cs' -and $_.SideIndicator -eq '=>' }).Count -ne 1) {
        throw 'JSON-restored mutation inputs must reject changed content and newly added tests.'
    }
    Remove-Item -LiteralPath (Join-Path $fixture 'Tests.csproj')
    if (@(& $manifestScript -RepositoryRoot $fixture).Count -ne 2) {
        throw 'Manifest must detect a deleted project input.'
    }
    Write-Host 'Build input checks passed: mutation JSON roundtrip, new tests, changed content, deleted projects and ignored outputs.'
} finally {
    $resolved = [IO.Path]::GetFullPath($fixture)
    $tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\') + '\'
    if (-not $resolved.StartsWith($tempRoot, [StringComparison]::OrdinalIgnoreCase) -or
        [IO.Path]::GetFileName($resolved) -notlike 'GTAVMods-BuildInputs-*') {
        throw "Refusing to clean an unexpected fixture directory: $resolved"
    }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
