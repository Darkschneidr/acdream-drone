# acdream-drone

A fork of the OpenAC (acdream) Asheron's Call client with a scripted drone
camera, plus the small scripts I use to record town flyover clips with it.

The character logs in, cloaks, and stands still. The camera detaches and flies
a path you write in a text file. Nothing about the camera is sent to the
server. You record the screen with ffmpeg and trim the result into a clip.

## Quick start (Windows download)

No .NET install needed.

1. Download `acdream-drone-win-x64.zip` from the
   [Releases](../../releases) page and unzip it anywhere.
2. Open `settings.ini` in Notepad and fill in your server, port, account,
   character slot, dat folder and route. Blank lines are asked for at startup.
3. Double-click `Start-Drone.bat`. It asks for the password once and saves it
   to `password.txt` in that folder.

You still need Vulkan 1.3 graphics, your own dat files, and an admin account
on your server (see What you need below). `QUICKSTART.txt` in the zip covers
recording and encoding. ffmpeg is a separate download from
https://ffmpeg.org/download.html.

## Upstream and license

The client in `client/` is based on OpenAC by Erik Nihlen and contributors,
https://github.com/eriknihlen/openac, MIT licensed. Their `LICENSE` and
`NOTICE.md` (third party notices, including WorldBuilder) are kept in
`client/`. This repo is a plain copy with the drone changes on top, not a
GitHub fork. The drone scripts are MIT too.

Main changes:

- `client/src/AcDream.App/Rendering/DroneCam.cs`: free camera and path playback
- `client/src/AcDream.App/Rendering/DroneFollow.cs`: optional follow mode
- `ACDREAM_HIDE_UI_DRAW=1` hides the UI but keeps the probe script running
- streaming follows the camera, and a few teleport and portal arrival fixes

## What you need

- An account on an ACE (or other emulator) server. `@cloak` and `@teleloc`
  need admin rights, so in practice this means your own server.
- The Asheron's Call dat files from your own install.
- .NET SDK 10.0.300 or newer (see `client/global.json`).
- A graphics card that supports Vulkan 1.3. Vulkan is a graphics standard,
  like DirectX, that the client uses to draw the 3D world. Most graphics
  cards from about 2015 onward support it with a current driver: roughly
  NVIDIA GTX 900 series and newer, AMD RX 400 series and newer, and Intel
  6th gen Core (HD 500 series) and newer integrated graphics. Older cards
  such as the NVIDIA GTX 600 and 700 series stop at Vulkan 1.2 and will not
  work. Keep your graphics driver up to date, since Vulkan support comes
  from the driver. To check, run `vulkaninfo --summary` (it comes with the
  Vulkan SDK and some drivers) and look for an apiVersion of 1.3 or higher.
  Or just start the client: if Vulkan 1.3 is missing it stops at startup
  with an error asking you to install or update a Vulkan 1.3 driver.
- ffmpeg for recording and encoding. Python 3 is used by `enc.sh`.

## Build

```
cd client
dotnet restore AcDream.slnx
dotnet build AcDream.slnx -c Release
```

The first run bakes `acdream.pak` next to your dat files. See
`client/docs/building-and-running.md` for the bake step and the rest.

## Flight path files

One directive per line. `#` starts a comment. Positions are in meters relative
to where the character stands (x east, y north, z up), unless the file has
`abs`. Angles are degrees. Segment times are seconds and eased unless you add
`linear` at the end. Any segment can take `roll=deg`.

| directive | arguments |
|---|---|
| `delay s` | wait before the path starts (lets the world load) |
| `start x y z yaw pitch` | starting pose |
| `wp x y z yaw pitch s` | fly to a pose |
| `look x y z tx ty tz s` | fly to x y z while facing the point tx ty tz |
| `orbit cx cy cz radius height fromDeg sweepDeg s` | circle around a center |
| `hold s` | stay put |
| `loop` | repeat the path |

Sample (`client/assets/drone-routes/sample.path`):

```
delay 14
orbit 8.7 8.9 1.2 13 4 -110 220 26
look -25 -20 9 8.7 8.9 1.5 12
hold 30
```

That waits 14 seconds, does a 220 degree orbit 13 m out and 4 m up over 26
seconds, pulls back while still looking at the center, then holds.
The other built-in moves are in `client/assets/drone-routes/` and make good
starting points for your own files.

## Built-in routes

The client ships with a few camera moves and a list of town stops, so you can
fly something without writing any files. Set `ACDREAM_DRONE_ROUTE` to a move,
a town, or both as `town:move`:

```
$env:ACDREAM_DRONE_ROUTE = "list"               # print the names and exit
$env:ACDREAM_DRONE_ROUTE = "holtburg"           # go to Holtburg, default move
$env:ACDREAM_DRONE_ROUTE = "shoushi:rise-high"  # go to Shoushi, rising shot
$env:ACDREAM_DRONE_ROUTE = "orbit-sky"          # fly a move where you stand
```

Moves: `dolly`, `dolly-high`, `orbit-high` (default for towns), `orbit-rev`,
`orbit-rev-high`, `orbit-sky`, `rise`, `rise-high`, `sample`.

Towns: `holtburg`, `yaraq`, `shoushi`, `arwic`, `cragstone`, `qalabar`,
`zaikhal`, `linvak`, `plateau`, `eastwatch`, `tethana`, `kara`, `bluespire`,
`greenspire`, `redspire`, `ayanbaqur`, `hebianto`, `sawato`, `baishi`,
`aljalima`, `neydisa`, `timaru`, `stonehold`, `banditcastle`, `asheron`.

A town writes a small probe script that cloaks you and runs `@teleloc` to the
stop, so it needs the same admin rights as the manual steps below. A route
turns on `ACDREAM_DRONE_CAM` for you. If `ACDREAM_DRONE_PATH` is set, that file
is used instead of the built-in move, and if `ACDREAM_UI_PROBE_SCRIPT` is set,
that script is used instead of the built-in town.

## Recording a clip

1. Make a probe script that cloaks you and teleports to a spot. Cells and
   positions use the `@teleloc` format. Some town stops are in `drone/stops.tsv`.

   ```
   drone/make-probe.sh drone/probe.txt "0xA9B40019 [84 7.1 95]"
   ```

   Point `ACDREAM_UI_PROBE_SCRIPT` at the file, or skip this step and use a
   built-in town route.

2. Copy `drone/session.example.json` to `drone/session.json` and fill in
   your server, port, account, character slot (0 is the first) and dat
   folder. Put the account password alone in a text file and point
   `credential.reference` at it. Keep both files out of git. Pick a route in
   `drone/drone-env.ps1`, then start the client:

   ```
   .\drone\run.ps1 -App client\src\AcDream.App\bin\Release\net10.0\AcDream.App.exe
   ```

   The session file is what logs in and picks the character. Without it the
   client stops at character select and the route never starts.

   `ACDREAM_WORLD_TIME` sets the time of day (0.5 is about noon).

3. While it flies, record the desktop:

   ```
   .\drone\rec.ps1 -Name holtburg -Sec 130
   ```

4. Trim and encode to 1280x720 at 30 fps with fades and a poster jpg:

   ```
   drone/enc.sh clips/holtburg.mp4 65 26 holtburg
   ```

   The default crop strips a window border. Pass your own crop as the fifth
   argument, or `""` for none. `BR=3000k` raises the bitrate.

## Notes

- `ACDREAM_DRONE_FOLLOW=<name>` trails a player instead of flying a path.
  `ACDREAM_DRONE_FOLLOW_ALLOW` lists names allowed to steer it by tell.
  It is empty by default.
- Start position settings, mostly for running offline with no server. With
  `ACDREAM_FLY_POS="x,y,z"` (meters, local to the landblock) the camera starts
  there, and the drone anchors on it. `ACDREAM_FLY_YAW`, `ACDREAM_FLY_PITCH`
  and `ACDREAM_FLY_FOV` are in degrees. `ACDREAM_OFFLINE_CENTER` is the
  landblock to load first, in hex (for example `0xA9B4FFFF`, the default,
  which is Holtburg).
- Other knobs: `ACDREAM_DRONE_SPEED`, `ACDREAM_DRONE_MIN_AGL`,
  `ACDREAM_DRONE_TETHER`, `ACDREAM_DRONE_BANK`, `ACDREAM_DRONE_START`.
