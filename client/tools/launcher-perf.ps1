[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Pipe,
    [Parameter(Mandatory)][string]$Server,
    [Parameter(Mandatory)][ValidateCount(1,64)][string[]]$Accounts,
    [ValidateSet('Status','Start','Stop','Measure')][string]$Action = 'Status',
    [string]$ExpectedVersion,
    [string]$OutputDirectory,
    [ValidateRange(0,3600)][int]$SettleSeconds = 120,
    [ValidateRange(10,3600)][int]$DurationSeconds = 300
)

$ErrorActionPreference = 'Stop'
$control = Join-Path $PSScriptRoot 'launcher-control.ps1'
if (@($Accounts | Select-Object -Unique).Count -ne $Accounts.Count) { throw 'Accounts must be unique.' }
function Read-State { & $control -Pipe $Pipe }
function Get-Targets($state) {
    foreach ($accountName in $Accounts) {
        $matches = @($state.accounts | Where-Object { $_.server -ceq $Server -and $_.account -ceq $accountName })
        if ($matches.Count -ne 1) { throw "Saved account selection not found: $accountName" }
        if ($matches[0].mode -ne 'Headless' -or -not $matches[0].character) { throw "Select a headless character for $accountName first." }
        $matches[0]
    }
}
function Get-Active($state) {
    $activeSessions = @($state.sessions | Where-Object { $_.server -ceq $Server -and $_.account -cin $Accounts -and $_.active })
    foreach ($session in $activeSessions) {
        $selection = $targets | Where-Object account -CEQ $session.account
        if ($session.mode -ne 'Headless' -or $session.character -cne $selection.character) {
            throw "Active session differs from the saved selection for $($session.account)."
        }
    }
    $activeSessions
}
$state = Read-State
$targets = @(Get-Targets $state)
if ($ExpectedVersion -and $state.clientVersion -cne $ExpectedVersion) { throw "Expected $ExpectedVersion, launcher resolved $($state.clientVersion)." }
if ($Action -eq 'Status') { $state; return }
if ($Action -eq 'Start') {
    if (-not $state.ready) { throw 'Launcher installation is not ready.' }
    foreach ($target in $targets) {
        if (-not $target.active) { & $control -Pipe $Pipe -Command start -Server $Server -Account $target.account | Out-Null }
    }
    $end = [DateTime]::UtcNow.AddSeconds(120)
    do {
        $state = Read-State
        $active = @(Get-Active $state)
        if ($active.Count -eq $Accounts.Count -and @($active | Where-Object state -ne 'InWorld').Count -eq 0) {
            $active; return
        }
        if ([DateTime]::UtcNow -ge $end) { throw 'Not all selected clients entered world. Inspect launcher status; no launch is retried automatically.' }
        Start-Sleep -Milliseconds 500
    } while ($true)
}
if ($Action -eq 'Stop') {
    $stopping = @(Get-Active $state)
    foreach ($session in $stopping) { & $control -Pipe $Pipe -Command stop -Session $session.session | Out-Null }
    $end = [DateTime]::UtcNow.AddSeconds(60)
    do {
        $state = Read-State
        $remaining = @($state.sessions | Where-Object { $_.session -cin $stopping.session -and $_.active })
        if ($remaining.Count -eq 0) {
            $finished = @($state.sessions | Where-Object { $_.session -cin $stopping.session })
            if ($finished.Count -ne $stopping.Count -or @($finished | Where-Object { -not $_.graceful }).Count) {
                throw 'A session did not report a graceful exit. Do not relaunch until its state is understood.'
            }
            $finished; return
        }
        if ([DateTime]::UtcNow -ge $end) { throw 'Graceful logout timed out. Clients were not killed.' }
        Start-Sleep -Milliseconds 500
    } while ($true)
}

if (-not $IsWindows) { throw 'Process CPU measurement currently requires Windows; lifecycle commands are portable.' }
if (-not $OutputDirectory) { throw 'Measure requires an OutputDirectory.' }
if (Test-Path -LiteralPath $OutputDirectory) { throw 'Use a new output directory to preserve previous measurements.' }
$active = @(Get-Active $state)
if ($active.Count -ne $Accounts.Count -or @($active | Where-Object state -ne 'InWorld').Count) { throw 'All selected accounts must be in world before measurement.' }
$measuredSessions = @($active.session)
$measuredVersion = $state.clientVersion
function Assert-World {
    $current = Read-State
    $currentActive = @(Get-Active $current)
    if ($current.clientVersion -cne $measuredVersion -or $currentActive.Count -ne $measuredSessions.Count -or
        @($currentActive | Where-Object { $_.session -cnotin $measuredSessions -or $_.state -ne 'InWorld' }).Count) {
        throw 'Measured sessions or world state changed. This CPU run is invalid.'
    }
}
$running = @(Get-CimInstance Win32_Process -Filter "name='acdream-headless.exe'")
$measured = foreach ($session in $active) {
    $matches = @($running | Where-Object { $_.CommandLine -match [regex]::Escape($session.session + '\session.json') })
    if ($matches.Count -ne 1) { throw "Cannot uniquely identify process for $($session.account)." }
    $process = Get-Process -Id $matches[0].ProcessId
    [pscustomobject]@{ Account=$session.account; Character=$session.character; Session=$session.session; PID=$process.Id; Start=$process.StartTime; Executable=$matches[0].ExecutablePath }
}
New-Item -ItemType Directory -Path $OutputDirectory | Out-Null
$measured | ConvertTo-Json | Set-Content (Join-Path $OutputDirectory 'sessions.json')
# No profiler is attached during this window. Account startup and logon commands
# are complete; a separate settling interval precedes the first CPU sample.
if ($SettleSeconds) { Start-Sleep -Seconds $SettleSeconds }
Assert-World
$logicalProcessors = [Environment]::ProcessorCount
$before = @{}
$records = [Collections.Generic.List[object]]::new()
$clock = [Diagnostics.Stopwatch]::StartNew()
foreach ($session in $measured) {
    $p = Get-Process -Id $session.PID
    if ($p.StartTime -ne $session.Start) { throw 'Client identity changed before measurement.' }
    $before[$p.Id] = @{ Cpu=$p.CPU; Time=$clock.Elapsed.TotalSeconds }
}
while ($clock.Elapsed.TotalSeconds -lt $DurationSeconds) {
    Start-Sleep -Seconds ([Math]::Min(10, [Math]::Max(1, $DurationSeconds - $clock.Elapsed.TotalSeconds)))
    Assert-World
    foreach ($session in $measured) {
        $p = Get-Process -Id $session.PID
        if ($p.StartTime -ne $session.Start) { throw 'Client exited or restarted during measurement.' }
        $time = $clock.Elapsed.TotalSeconds
        $elapsed = $time - $before[$p.Id].Time
        $cpu = $p.CPU - $before[$p.Id].Cpu
        $records.Add([pscustomobject]@{Account=$session.Account; PID=$p.Id; Elapsed=$time; Seconds=$elapsed; CpuSeconds=$cpu; MachinePercent=100*$cpu/$elapsed/$logicalProcessors; PrivateMiB=$p.PrivateMemorySize64/1MB})
        $before[$p.Id] = @{Cpu=$p.CPU; Time=$time}
    }
    $records | Export-Csv (Join-Path $OutputDirectory 'samples.csv') -NoTypeInformation
}
Assert-World
$summary = $records | Group-Object Account | ForEach-Object {
    $cpu = ($_.Group.CpuSeconds | Measure-Object -Sum).Sum
    $seconds = ($_.Group.Seconds | Measure-Object -Sum).Sum
    [pscustomobject]@{Account=$_.Name; CpuSeconds=$cpu; Seconds=$seconds; CoreEquivalent=$cpu/$seconds; MachinePercent=100*$cpu/$seconds/$logicalProcessors}
}
$summary | ConvertTo-Json | Set-Content (Join-Path $OutputDirectory 'summary.json')
$summary
