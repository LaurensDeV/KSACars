# Fast & Purrious

Cars for **Kitten Space Agency** (RocketWerkz). KSA has no wheels, so these bring their own: springs,
tyres, an engine and a gearbox, simulated by the mod and driven with the keys you already fly with.

- **Beach Buggy** — a fibreglass dune buggy on long-travel coil-overs, with a Beetle engine behind the
  seats and two kittens up front.
- **Cadillac Eldorado** — a red 1976 convertible with the top down, four seats, a whitewall on every
  corner and a V8 that sounds like one. It floats on its springs and leans into a turn.

The driver kitten holds the steering wheel and turns it, and anyone aboard can get out on EVA and
climb back in through the doors.

> Built against KSA build `2026.9.22.5482`. KSA is pre-release and has no official code-modding
> API; this uses the community [StarMap](https://github.com/StarMapLoader/StarMap) loader and may need
> updating when the game does.

## Install

You need **Kitten Space Agency**, built against build `2026.9.22.5482`, and
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

Each car is a whole craft on its own: find **Beach Buggy** or **Cadillac Eldorado** under *Vehicles* in
the editor, or launch one straight from the vehicle list. Fill its seats in the launch window.

| Control | Default key | |
| --- | --- | --- |
| pitch | W / S | throttle, and brake then reverse |
| yaw | A / D | steer |

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
the pitch and yaw keys still lean it across the ground.


The Eldorado can carry a **scoop** on its nose, chosen from the **Fast & Purrious** panel under **Scoop size** —
**Default** at 3.4 m across, the 6 m **XL**, or the 10 m **XXL** — for shoving rocks about.
A scoop turns on KSA's experimental ground clutter collisions; with that setting on, the Eldorado shoves
rocks without a scoop too. The panel's **Rock weight** slider sets how heavy rocks are: KSA's are solid
stone, so by default they weigh 2% of that.

The panel's **Move craft with the mouse** picks a craft up with one click and sets it down wherever
you click next.

A car is not destroyed by a hard landing, a collision or the sea; it bounces.

The panel can be closed; a small **F&P** button stays in its place to open it again, and with
the ModMenu mod installed it is also under **Mods > Fast & Purrious**.

A car that ends up on its roof or its side cannot drive off. The **Fast & Purrious** panel, on screen while you
are in one, has an **Unflip** button that sets it back on its wheels where it lies.

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
