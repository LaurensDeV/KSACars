# Fast & Purrious

Cars for **Kitten Space Agency** (RocketWerkz). KSA has no wheels, so these bring their own: springs,
tyres, an engine and a gearbox, simulated by the mod and driven with the keys you already fly with.

- **Beach Buggy** — a fibreglass dune buggy on long-travel coil-overs, with a Beetle engine behind the
  seats and two kittens up front.
- **Ferrari F2004** — the red 2004 single-seater: one kitten, its head well clear of the cockpit, slicks,
  wings that hold it down, and a rain light that burns brighter under the brakes.
- **Cadillac Eldorado** — a red 1976 convertible with the top down, four seats, a whitewall on every
  corner and a V8 that sounds like one. It floats on its springs and leans into a turn.

The driver kitten holds the steering wheel and turns it, and anyone aboard can get out on EVA and
climb back in through the doors.

> Built against KSA build `2026.10.13.5602`. KSA is pre-release and has no official code-modding
> API; this uses the community [StarMap](https://github.com/StarMapLoader/StarMap) loader and may need
> updating when the game does.

## Install

You need **Kitten Space Agency**, built against build `2026.10.13.5602`, and
**[StarMap](https://github.com/StarMapLoader/StarMap/releases)**, the community mod loader. Edit
StarMap's `StarMapConfig.json` to point at your KSA install; StarMap reads that file **relative to its
own directory**, so launch it from where it lives. Windows and Linux both work: the mod is a portable
.NET assembly with no native code.

1. **Get the mod.** Download `FastAndPurrious-<version>.zip` from [Releases](../../releases), or build it with
   `./tools/package.sh`.
2. **Unzip it into your mods folder**, inside KSA's user directory (on Windows,
   `Documents\My Games\Kitten Space Agency\`). You should end up with:

   ```
   <KSA user directory>/mods/FastAndPurrious/
     KSACars.dll
     mod.toml
     KSACars{Assets,GameData,Sounds}.xml
     Meshes/*.glb
     Textures/*.png
     Sounds/*.wav
   ```

   On Linux the **case** of every name matters, so unzip rather than retyping them.
3. **Register it in `manifest.toml`**, in the same user directory. Dropping the folder in is not enough:

   ```toml
   [[mods]]
   id = "FastAndPurrious"
   enabled = true
   ```

4. **Launch through StarMap**, not the game directly: `StarMap.exe` on Windows, `dotnet StarMap.dll` on
   Linux.

The mod writes its own log to `Logs/KSACars.log` under the KSA user directory. KSA's own log, the newest
`KittenSpaceAgency.*.log` beside it, is where asset and XML errors appear.

## Drive

Each car is a whole craft on its own: find **Beach Buggy**, **Cadillac Eldorado** or **Ferrari F2004** under *Vehicles* in
the editor, or launch one straight from the vehicle list. Fill its seats in the launch window.

| Control | Default key | |
| --- | --- | --- |
| pitch | W / S | throttle, and brake then reverse |
| yaw | A / D | steer |
| | Space | brake, on all four wheels: it stops the car and holds it, and never reverses |

The car changes gear on its own. The engine is heard while you are driving it.

The same panel switches the headlights between off, dipped and main beam. The tail lights come on
with them and brighten when you brake.

The Eldorado also flies, on four rockets under its floor, worked with the keys you fly a rocket with:

| Control | Default key | |
| --- | --- | --- |
| engine start / shutdown | Z / X | light and cut the rockets |
| throttle up / down | Up / Down | half hovers, more climbs |
| pitch | W / S | in the air, lean forward or back |
| yaw | A / D | in the air, bank left or right |
| roll | Q / E | in the air, turn left or right |
| sprint | Left Shift | hold to fire two rockets on the tail and push the car forward |
| toggle RCS | R | switch the downward thrusters, which press the car onto the ground for grip |

With the rockets lit and the downward thrusters on together, the car hovers: it holds its height, and
the pitch and yaw keys still lean it across the ground. Let go of them and it comes to rest over the
place it is at.

A gamepad drives a car too, beside the keys, with the triggers and the stick as far as you move them:

| Gamepad | |
| --- | --- |
| right trigger | throttle |
| left trigger | brake, then reverse |
| left stick | steer; in the air, bank left or right |
| right stick | in the air, lean forward or back and turn left or right |
| A | hold for the tail rockets |
| X | switch the downward thrusters |
| Y | unflip |
| Start | light and cut the rockets |
| right / left bumper | rocket throttle up / down |
| D-pad up | headlights: off, dipped, main beam |

An Xbox pad needs nothing. A Steam Controller is read by the mod itself, through SDL, and wants Steam
closed: while Steam runs it also turns the controller's buttons into keys, so Start pauses the game.

The Eldorado can carry a **scoop** on its nose, chosen from the **Fast & Purrious** panel under **Scoop size** —
**Default** at 3.4 m across, the 6 m **XL**, or the 10 m **XXL** — for shoving rocks about.
A scoop turns on KSA's experimental ground clutter collisions; with that setting on, the Eldorado shoves
rocks without a scoop too. The panel's **Rock weight** slider sets how heavy rocks are: KSA's are solid
stone, so by default they weigh 2% of that.

The panel's **Move craft with the mouse** picks a craft up with one click and sets it down wherever
you click next. Click a laid road and a car is stood on its asphalt, a raised road's deck included.

A car is not destroyed by a hard landing, a collision or the sea; it bounces.

The panel can be closed; a small **F&P** button stays in its place to open it again, and with
the ModMenu mod installed it is also under **Mods > Fast & Purrious**.

A car that ends up on its roof or its side cannot drive off. The **Fast & Purrious** panel, on screen while you
are in one, has an **Unflip** button that sets it back on its wheels where it lies.

### A course to race on

The panel has a **Course** list. Choose one and press **Lay here**: the course is laid on the ground with
its start where your car is, running the way the car faces, and the car is stood on pole: a chequered
start line is painted across the road ahead of it and a grid behind.
**Add opponent** puts a copy of your car in the next box of the grid with a kitten driving; it sets off
when you do and laps the course. **Opponents slower by** sets how far off the best lap they know they
are: at 0% the quickest of them goes as fast as it can, and at 20% they are a fifth slower. Each opponent
is a driver of its own, up to 4% slower than the rest, and none of them laps the same twice: now and then
one fluffs the exit of a corner. They practise whatever course is laid, yours
included: a few seconds after you add the first opponent the panel says what lap they have found so
far, and it goes on improving for up to a minute. They drive to the best there is when they set off. What they learn of a course is kept, so the next time you lay it they know it already. An opponent slows behind a car in its way but does not
overtake unless there is a straight and room for it, though they go round a car that is standing wherever it is, one at a time. Any car
following close behind another on a straight is in its slipstream and goes faster, yours too. One that is knocked off the road drives itself back on, and one on its roof is set on its
wheels first; only an opponent that cannot get back is put back on the road.

Hide the game's interface (F2 by default) and a racing display takes its place: your speed, your lap
and its time with your last and best laps, how far up or down on your best you are at each checkpoint,
your position among the cars on the course, and a map of the course that turns as you do. **Laps** in
the panel sets how many laps the lap counter counts to. A lap only counts if you drive the whole of it:
there are checkpoints round the course, the next one a post ahead of you with its distance and a yellow dot on the map, and if you cut across and
miss one the display says so and your lap and position wait until you go back through it.
**Take up roads** removes it and puts back the grass, trees and rocks that stood under it. **Racing line**
draws arrowheads along the fastest line ahead of you: blue while you are under the speed the next corner
allows, then yellow, orange and red as you need to brake.

Two courses come with the mod, both the layout of Club Motorsports, a real 3.6 km road course in Tamworth,
New Hampshire. **Club Motorsports** climbs and falls 60 m as the real one does, so on level ground most of
it is a raised road between barriers; **Club Motorsports Flat** is the same lap on the ground. Lay either
on ground that is level for a kilometre around: a road follows whatever ground it is put on.

A course is not kept in a save, so lay it again after loading one. A save made while a course is laid
does keep the ground under it cleared; take the roads up before you save if you want that ground back.

## Build

Requires the **.NET 10 SDK** — the mod targets `net10.0` because that is what KSA runs on. The scripts
resolve an SDK from `~/.dotnet`; `source tools/env.sh` if you want bare `dotnet` in your shell.

```bash
./tools/sync-import.sh                 # copy the game's assemblies into Import/
./tools/build.sh                       # build the mod
./tools/test.sh                        # the headless car tests, no game required
./tools/deploy.sh                      # build and install into the mods folder
./tools/install-testcraft.sh           # put both cars in the vehicle library
```

`CLAUDE.md` is the developer's guide: how a car is driven, how to add one, and how releases work.

## Licence

MIT. The engine recordings are CC0; `tools/audio/README.md` has where each came from.

## Credits

Gamepads are read with [SDL](https://libsdl.org), shipped as `Native/SDL3.dll` under the zlib licence beside it.

The Ferrari F2004's engine sounds (`Sounds/KSACars_F1_*.wav`) are adapted from
[*Red Bull-Cosworth RB1 (2005)*](https://commons.wikimedia.org/wiki/File:Red_Bull-Cosworth_RB1_(2005).ogg)
by Edvvc, a recording of that car at the 2010 Goodwood Festival of Speed, used under
[CC BY-SA 3.0](https://creativecommons.org/licenses/by-sa/3.0/). The recording was cut, re-pitched,
levelled and looped, and those four files are licensed under CC BY-SA 3.0 in turn.

The Club Motorsports courses (`Circuits/Club Motorsports*.json`) follow the track's centre line as mapped
in [OpenStreetMap](https://www.openstreetmap.org/copyright), © OpenStreetMap contributors, and are made
available under the [Open Database License](https://opendatacommons.org/licenses/odbl/). Their heights
are from the U.S. Geological Survey's 3D Elevation Program and their widths were measured on its NAIP
aerial imagery, both in the public domain. The mod is not affiliated with or endorsed by Club Motorsports.

Everything else in this archive is under the licence in `LICENSE`.
