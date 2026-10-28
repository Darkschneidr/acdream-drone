#!/bin/bash
# Trim a raw capture into a finished clip plus a poster frame.
# usage: enc.sh in.mp4 start_sec dur_sec out_name [crop_filter]
# The default crop removes the window border from a desktop capture.
# Set it to match your window, or pass "" for none. BR sets the bitrate.
set -e
C=${5-crop=1276:716:52:53,}
fo=$(python3 -c "print($3-0.8)"); mid=$(python3 -c "print($2+$3/2)")
ffmpeg -loglevel error -y -ss "$2" -t "$3" -i "$1" \
  -vf "${C}scale=1280:720,fps=30,fade=t=in:st=0:d=0.6,fade=t=out:st=$fo:d=0.8" -an \
  -c:v libx264 -preset slow -profile:v high -pix_fmt yuv420p \
  -b:v ${BR:-2000k} -maxrate 2400k -bufsize 4800k -movflags +faststart "$4.mp4"
ffmpeg -loglevel error -y -ss "$mid" -i "$1" -frames:v 1 -vf "${C}scale=1280:720" -q:v 4 "$4.jpg"
ls -la "$4.mp4" "$4.jpg"
