# Record the Windows desktop with ffmpeg (gdigrab) while the client flies.
param([string]$Name="clip",[int]$Sec=60,[string]$Out=".\clips",[string]$Ffmpeg="ffmpeg")
New-Item -ItemType Directory -Force $Out | Out-Null
& $Ffmpeg -hide_banner -loglevel error -y -f gdigrab -framerate 30 -draw_mouse 0 -t $Sec -i desktop `
  -c:v libx264 -preset ultrafast -crf 18 -pix_fmt yuv420p (Join-Path $Out "$Name.mp4")
