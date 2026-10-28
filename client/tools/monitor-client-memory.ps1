[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Pipe,
    [Parameter(Mandatory)][string]$Server,
    [Parameter(Mandatory)][string[]]$Accounts,
    [Parameter(Mandatory)][string]$ControlScript,
    [Parameter(Mandatory)][string]$ExpectedVersion,
    [Parameter(Mandatory)][string]$OutputDirectory,
    [ValidateRange(10, 3600)][int]$IntervalSeconds = 60,
    [ValidateRange(1, 24)][int]$DurationHours = 12,
    [ValidateRange(0, 100000)][int]$MaxSamples = 0
)

$ErrorActionPreference = 'Stop'
if (-not $IsWindows) { throw 'This process-memory sampler requires Windows.' }
if (@($Accounts | Select-Object -Unique).Count -ne $Accounts.Count) {
    throw 'Accounts must be unique.'
}
if (-not (Test-Path -LiteralPath $OutputDirectory -PathType Container)) {
    throw 'Output directory must exist.'
}

if (-not (Test-Path -LiteralPath $ControlScript -PathType Leaf)) {
    throw "Launcher control script is missing: $ControlScript"
}
$output = Join-Path $OutputDirectory 'memory.jsonl'
if (Test-Path -LiteralPath $output) { throw 'Memory log already exists; refusing to overwrite it.' }

$startedUtc = [DateTime]::UtcNow
$endUtc = $startedUtc.AddHours($DurationHours)
$sampleNumber = 0
try {
    while ([DateTime]::UtcNow -lt $endUtc) {
        $sampleStartedUtc = [DateTime]::UtcNow
        $state = & $ControlScript -Pipe $Pipe -Command status
        if (-not $state.ready -or $state.clientVersion -cne $ExpectedVersion) {
            throw "Launcher version/readiness changed: ready=$($state.ready), version=$($state.clientVersion)."
        }
        $processes = @(Get-CimInstance Win32_Process -Filter "name='acdream-headless.exe'")
        $clientRows = foreach ($account in $Accounts) {
            $sessions = @($state.sessions | Where-Object {
                $_.active -and $_.server -ceq $Server -and $_.account -ceq $account
            })
            if ($sessions.Count -ne 1) {
                [pscustomobject]@{ account=$account; status='MissingOrAmbiguousSession'; sessionCount=$sessions.Count }
                continue
            }
            $session = $sessions[0]
            $matches = @($processes | Where-Object {
                $_.CommandLine -match [regex]::Escape($session.session + '\session.json')
            })
            if ($matches.Count -ne 1) {
                [pscustomobject]@{
                    account=$account; character=$session.character; session=$session.session
                    worldState=$session.state; status='MissingOrAmbiguousProcess'; processCount=$matches.Count
                }
                continue
            }
            try {
                $process = Get-Process -Id $matches[0].ProcessId -ErrorAction Stop
                [pscustomobject]@{
                    account=$account; character=$session.character; session=$session.session
                    worldState=$session.state; status='Sampled'; pid=$process.Id
                    processStartUtc=$process.StartTime.ToUniversalTime().ToString('o')
                    privateBytes=$process.PrivateMemorySize64; workingSetBytes=$process.WorkingSet64
                    virtualBytes=$process.VirtualMemorySize64; handleCount=$process.HandleCount
                    threadCount=$process.Threads.Count
                }
            } catch [System.ArgumentException], [System.InvalidOperationException] {
                [pscustomobject]@{
                    account=$account; character=$session.character; session=$session.session
                    worldState=$session.state; status='ExitedDuringSample'; pid=$matches[0].ProcessId
                }
            }
        }
        $sampleNumber++
        $record = [pscustomobject]@{
            schemaVersion=1; sample=$sampleNumber; sampledAtUtc=$sampleStartedUtc.ToString('o')
            launcherVersion=$state.clientVersion; clients=@($clientRows)
        }
        Add-Content -LiteralPath $output -Value ($record | ConvertTo-Json -Depth 5 -Compress) -Encoding utf8
        if ($MaxSamples -gt 0 -and $sampleNumber -ge $MaxSamples) { break }
        $nextUtc = $startedUtc.AddSeconds($sampleNumber * $IntervalSeconds)
        $sleepMilliseconds = [int][Math]::Max(0, ($nextUtc - [DateTime]::UtcNow).TotalMilliseconds)
        if ($sleepMilliseconds -gt 0) { Start-Sleep -Milliseconds $sleepMilliseconds }
    }
} catch {
    [pscustomobject]@{ failedAtUtc=[DateTime]::UtcNow.ToString('o'); sample=$sampleNumber; error=$_.Exception.Message } |
        ConvertTo-Json -Compress |
        Set-Content -LiteralPath (Join-Path $OutputDirectory 'memory-error.json') -Encoding utf8
    throw
}
