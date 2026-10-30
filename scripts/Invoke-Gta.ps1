[CmdletBinding()]
param(
    [ValidateSet('describe','status','observe','state','capture','batch','step','command','cancel','reload')][string]$Operation = 'state',
    [string]$Code,
    [string]$BatchFile,
    [string]$InputsJson = '[]',
    [string]$StateJson = '{}',
    [ValidateRange(1,30000)][int]$Milliseconds = 500,
    [ValidateSet('game','wall')][string]$Clock = 'game',
    [switch]$Capture,
    [switch]$NoWait,
    [int]$GameProcessId,
    [string]$DiscoveryDirectory,
    [ValidateRange(1,3600)][int]$TimeoutSeconds = 60
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if (!$DiscoveryDirectory) {
    $DiscoveryDirectory = if ($env:GTA_USE_DISCOVERY) { $env:GTA_USE_DISCOVERY } else {
        Join-Path (& (Join-Path $PSScriptRoot 'Get-AgentRuntimeDirectory.ps1')) 'Bridge'
    }
}

function Find-GtaBridge {
    $matches = @(Get-ChildItem -LiteralPath $DiscoveryDirectory -Filter 'bridge-*.json' -File | ForEach-Object {
        $entry = Get-Content -LiteralPath $_.FullName -Raw | ConvertFrom-Json
        if (($GameProcessId -eq 0 -or $entry.process -eq $GameProcessId) -and
            (Get-Process -Id $entry.process -ErrorAction SilentlyContinue)) { $entry }
    })
    if ($matches.Count -ne 1) { throw "Expected one running GTA bridge; found $($matches.Count). Specify -GameProcessId if necessary." }
    $matches[0]
}

function Wait-GtaIo($Task, [int]$Timeout) {
    if (-not $Task.Wait($Timeout)) { throw 'Bridge I/O timed out. The request may have executed; inspect session files before retrying mutations.' }
    $Task.GetAwaiter().GetResult()
}
function Read-GtaBytes($Stream, [int]$Count, [int]$Timeout) {
    $bytes = New-Object byte[] $Count
    $offset = 0
    while ($offset -lt $Count) {
        $read = Wait-GtaIo ($Stream.ReadAsync($bytes, $offset, $Count - $offset)) $Timeout
        if ($read -eq 0) { throw 'Bridge disconnected. Inspect game state and current session before acting.' }
        $offset += $read
    }
    ,$bytes
}
function Send-GtaRequest($Bridge, [hashtable]$Arguments) {
    $Arguments.version = 1
    $Arguments.id = [Guid]::NewGuid().ToString('N')
    $Arguments.session = $Bridge.bridge.session
    $payload = [Text.Encoding]::UTF8.GetBytes(($Arguments | ConvertTo-Json -Depth 32 -Compress))
    if ($payload.Length -gt 1048576) { throw 'Request exceeds 1048576 bytes.' }
    $client = [IO.Pipes.NamedPipeClientStream]::new('.', $Bridge.pipe, [IO.Pipes.PipeDirection]::InOut, [IO.Pipes.PipeOptions]::Asynchronous)
    try {
        $timeout = $TimeoutSeconds * 1000
        $client.Connect($timeout)
        $header = [BitConverter]::GetBytes([int]$payload.Length)
        Wait-GtaIo ($client.WriteAsync($header, 0, 4)) $timeout | Out-Null
        Wait-GtaIo ($client.WriteAsync($payload, 0, $payload.Length)) $timeout | Out-Null
        $length = [BitConverter]::ToInt32((Read-GtaBytes $client 4 $timeout), 0)
        if ($length -le 0 -or $length -gt 4194304) { throw 'Invalid response length.' }
        $result = [Text.Encoding]::UTF8.GetString((Read-GtaBytes $client $length $timeout)) | ConvertFrom-Json
        if (-not $result.ok) { throw $result.error }
        $result
    } finally { $client.Dispose() }
}

function Read-GtaRecord([string]$Path) {
    # The game atomically replaces status records. Share Delete so a reader never
    # blocks that replacement, and read one complete generation through its handle.
    $stream = [IO.File]::Open($Path, [IO.FileMode]::Open, [IO.FileAccess]::Read,
        [IO.FileShare]::ReadWrite -bor [IO.FileShare]::Delete)
    try {
        $reader = [IO.StreamReader]::new($stream)
        try { $reader.ReadToEnd() | ConvertFrom-Json } finally { $reader.Dispose() }
    } finally { $stream.Dispose() }
}

$bridge = Find-GtaBridge
$request = @{op=$Operation}
$stateOptions = $StateJson | ConvertFrom-Json
$stateRequest = @{op='state'}
foreach ($property in $stateOptions.PSObject.Properties) { $stateRequest[$property.Name] = $property.Value }
if ($stateRequest.op -ne 'state') { throw 'StateJson cannot override the operation.' }
switch ($Operation) {
    'state' { $request = $stateRequest }
    'command' {
        if ([string]::IsNullOrWhiteSpace($Code)) { throw '-Code is required.' }
        $request = @{op='batch';steps=@(@{op='command';code=$Code})}
    }
    'batch' {
        if (-not $BatchFile) { throw '-BatchFile is required (JSON array of operations).' }
        # Windows PowerShell can wrap a JSON array in a PSObject. Enumerate it
        # explicitly so ConvertTo-Json does not transmit its value/Count wrapper.
        $request = @{op='batch';steps=@(Get-Content -LiteralPath $BatchFile -Raw | ConvertFrom-Json | ForEach-Object { $_ })}
    }
    'step' {
        $inputs = @($InputsJson | ConvertFrom-Json | ForEach-Object { $_ })
        $action = @{op='wait';ms=$Milliseconds;clock=$Clock}
        if ($inputs.Count -gt 0) { $action.op='controls';$action.inputs=$inputs }
        $steps = @(@{op='resume'},$action,@{op='pause'},$stateRequest)
        if ($Capture) { $steps += @{op='capture'} }
        $request = @{op='batch';steps=$steps}
    }
}
$reply = Send-GtaRequest $bridge $request
$timer = [Diagnostics.Stopwatch]::StartNew()
if ($Operation -eq 'reload' -and -not $NoWait) {
    do {
        if ($timer.Elapsed.TotalSeconds -ge $TimeoutSeconds) { throw 'Reload was requested but a new ready session was not observed.' }
        Start-Sleep -Milliseconds 100
        $next = Find-GtaBridge
    } while ($next.bridge.session -eq $bridge.bridge.session -or $next.bridge.state -ne 'ready')
    $reply = Send-GtaRequest $next @{op='status'}
} elseif ($request.op -eq 'batch' -and -not $NoWait) {
    $batch = $reply.result.batch
    while ($true) {
        if ($timer.Elapsed.TotalSeconds -ge $TimeoutSeconds) { throw "Batch $batch has not completed. Use status/cancel; it was not resubmitted." }
        Start-Sleep -Milliseconds 50
        try { $state = Read-GtaRecord (Join-Path $bridge.bridge.directory "batch-$batch-status.json") }
        catch [IO.FileNotFoundException] { continue } # A replacement can briefly hide the directory entry on the game volume.
        catch [IO.IOException] {
            # Atomic replacement or a scanner may briefly deny this read. Retry
            # only the observation, within the existing batch deadline.
            if (($_.Exception.HResult -band 0xffff) -in 32,33) { continue }
            throw
        }
        if ($state.batch -ne $batch) { throw 'Another client replaced the current batch. Inspect saved results.' }
        if ($state.state -ne 'running') { break }
    }
    if ($state.state -ne 'completed') { throw "Batch $($state.state): $($state.error)" }
    $results = @(Get-ChildItem -LiteralPath $bridge.bridge.directory -Filter "result-$batch-*.json" -File |
        ForEach-Object { Read-GtaRecord $_.FullName } | Sort-Object step)
    $reply = [pscustomobject]@{session=$bridge.bridge.session;batch=$batch;state=$state.state;results=$results;directory=$bridge.bridge.directory;log=$bridge.bridge.log}
}
$reply | ConvertTo-Json -Depth 32
