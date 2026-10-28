# Reads settings.ini, asks for anything missing, writes session.json and starts the client.
$ErrorActionPreference = 'Stop'
$here = $PSScriptRoot
$cfg = @{}
Get-Content "$here\settings.ini" | Where-Object { $_ -match '^\s*([a-z]+)\s*=(.*)$' } |
  ForEach-Object { $cfg[$Matches[1]] = $Matches[2].Trim() }
foreach ($k in 'server','port','account','character','datfolder','route') {
  if (-not $cfg[$k]) { $cfg[$k] = Read-Host "Enter $k" }
}
$exe = "$here\app\AcDream.App.exe"
$env:ACDREAM_DRONE_ROUTE = $cfg.route
if ($cfg.route -eq 'list') { & $exe; Read-Host 'Press Enter to close'; exit }
$pwFile = "$here\password.txt"
if (-not (Test-Path $pwFile)) {
  $s = Read-Host 'Account password (saved to password.txt in this folder)' -AsSecureString
  [Runtime.InteropServices.Marshal]::PtrToStringAuto([Runtime.InteropServices.Marshal]::SecureStringToBSTR($s)) |
    Set-Content -NoNewline $pwFile
}
$session = @{
  version = 1
  process = @{ content = @{ datDirectory = $cfg.datfolder; preparedAssetPath = (Join-Path $cfg.datfolder 'acdream.pak') } }
  sessions = @(@{ id = 'drone'; endpoint = @{ host = $cfg.server; port = [int]$cfg.port }; account = $cfg.account
    character = @{ index = [int]$cfg.character }; credential = @{ provider = 'file'; reference = $pwFile }; plugins = @() })
}
$session | ConvertTo-Json -Depth 6 | Set-Content "$here\session.json"
$env:ACDREAM_NO_AUDIO = '1'; $env:ACDREAM_HIDE_UI_DRAW = '1'; $env:ACDREAM_DRONE_CAM = '1'
$env:ACDREAM_DRONE_KEEP_OPEN = if ($cfg.keepopen -eq '1') { '1' } else { '' }
$env:ACDREAM_WORLD_TIME = if ($cfg.worldtime) { $cfg.worldtime } else { '0.5' }
Set-Location "$here\app"
& $exe --session-config "$here\session.json"
