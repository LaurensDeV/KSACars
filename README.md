# KSACars

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

1. **Get the mod.** Download `KSACars-<version>.zip` from [Releases](../../releases), or build it with
   `./tools/package.sh`.
2. **Unzip it into your mods folder**, inside KSA's user directory (on Windows,
   `Documents\My Games\Kitten Space Agency\`). You should end up with:

   ```
   <KSA user directory>/mods/KSACars/
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
   id = "KSACars"
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
