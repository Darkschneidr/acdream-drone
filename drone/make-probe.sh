#!/bin/bash
# Write a UI probe script that cloaks the character and teleports to each stop.
# Needs an admin (or sentinel) account on your own server for @cloak and @teleloc.
# usage: make-probe.sh out.txt "0xCELL [x y z]" ["0xCELL [x y z]" ...]
# Stops are 0xLANDBLOCKCELL [x y z], the same format @teleloc takes.
out=$1; shift
{ echo "sleep 30000"; echo "command @cloak on"; echo "sleep 12000"
  for s in "$@"; do echo "command @teleloc $s"; echo "sleep 50000"; done
  echo "close-client"; } > "$out"
