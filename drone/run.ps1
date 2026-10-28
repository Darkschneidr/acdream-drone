# Start the client with drone-env.ps1 and session.json from this folder.
param([string]$App = "$PSScriptRoot\app\AcDream.App.exe", [string]$Session = "$PSScriptRoot\session.json")
. "$PSScriptRoot\drone-env.ps1"
$Session = (Resolve-Path $Session).Path
# The client loads its assets relative to its own folder.
Set-Location (Split-Path (Resolve-Path $App).Path)
& (Resolve-Path $App).Path --session-config $Session
