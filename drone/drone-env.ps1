# Camera and route settings. run.ps1 dot-sources this file.
# Server, account, character and dat folder live in session.json
# (copy session.example.json). The password goes in its own text file.
$env:ACDREAM_NO_AUDIO     = "1"
$env:ACDREAM_HIDE_UI_DRAW = "1"
$env:ACDREAM_WORLD_TIME   = "0.5"
$env:ACDREAM_DRONE_CAM    = "1"
# Built-in route: a town, a camera move, or town:move. See the README.
$env:ACDREAM_DRONE_ROUTE  = "holtburg"
# Your own files take priority over the built-in route:
# $env:ACDREAM_DRONE_PATH       = "$PSScriptRoot\my.path"
# $env:ACDREAM_UI_PROBE_SCRIPT  = "$PSScriptRoot\probe.txt"
