# CLAUDE.md

**Fast & Purrious**, a car mod for **Kitten Space Agency** (KSA, RocketWerkz). That is the name a player
sees, on the panel, the README and the release archive (`FastAndPurrious-<version>.zip`); and the
mod's id, which is its install folder and its `manifest.toml` entry, is `FastAndPurrious`. The assembly,
the namespace, the log, the bridge folder and every part, mesh, texture and sound stay `KSACars`: a save
pairs a part with its definition by Id, so those cannot be renamed. KSA has no wheels, so every car's springs,
tyres and engine are this mod's: a **beach buggy** on long-travel coil-overs, and a red **1976 Cadillac
Eldorado** convertible that floats on its springs and leans into a turn, and a red **2004 Ferrari F2004**
on wings and springs that move an inch. All carry kittens: the driver's
hands hold the steering wheel and turn it, and either can get out on EVA. A car is driven with the pitch
and yaw keys, like anything else in KSA.

## Read this first

**`docs/KSA-MODDING-NOTES.md` is the distilled result of reverse-engineering the game.** It has the
runtime, the loader contract, the type signatures, the reference frames and the gotchas. Read it
before touching anything KSA-facing.

**`docs/KSA-FRAME-ORDER.md` is the engine's frame order**, which is why the car physics runs where it
does (see [The cars](#the-cars)), and **`docs/FRAMES-AND-EPOCHS.md`** is what follows from it for
anything drawn or timed.

KSA has **no official code-modding API**. Everything is community tooling against a pre-release
game, so the API moves between builds.

## Comments and documentation

**Docs are part of the change, not a follow-up.** If a change makes a line in `CLAUDE.md`, a
`docs/` file, `README.md` or a comment untrue, fix it in the same commit. A stale line is worse than
a missing one: it is trusted, and nothing in a build fails when it goes wrong.

**Comment why, never what.** The code says what it does. A comment earns its place only when the
reason is not recoverable from reading it — an engine contract, a measured number, a constraint
imposed from somewhere else in the frame, an ordering that looks arbitrary and is not.

```csharp
// The physics state, not the analytic orbit position: on a landed craft those differ by metres.  ok
PhysicsStates.GetStatesCcf(in states.Origin, in states.Kinematic, out double3 positionCcf, ...);

// Get the position in the planet-fixed frame.                                                    delete
PhysicsStates.GetStatesCcf(in states.Origin, in states.Kinematic, out double3 positionCcf, ...);
```

**Keep them short.** A sentence or two. If a comment needs paragraphs, the explanation belongs in
`docs/` with a one-line pointer to it.

**State the fact, not the history.** A comment says what is true now. It does not narrate what the
code used to do, what broke, when it was reported, or which commit fixed it — that belongs in git,
and the reasoning belongs in `docs/`.

**When in doubt, delete.** An unnecessary comment is another thing that can drift out of step with
the code and mislead the next reader.

## Committing

**Every commit message must be a [Conventional Commit](https://www.conventionalcommits.org/).**
semantic-release parses these to decide the next version, so a message that does not parse silently
produces no release and never appears in the changelog.

```
feat(eldorado): put a picture on the hood ornament
fix(buggy): keep the driver's hands on the rim at full lock
docs: write an install guide
refactor(sim): split the gearbox out of BuggyDrive
```

| Type | Version effect |
| --- | --- |
| `feat`, `fix`, `perf`, `build`, `revert` | **patch** |
| `!` after the type, or a `BREAKING CHANGE:` footer | **major** |
| `docs`, `refactor`, `test`, `chore`, `ci`, `style` | no release, and none appear in the changelog |
| a **minor** | never automatic — tag it by hand |

**The type says what a change is; it does not decide how big the version bump is.** `feat` cuts a
patch like everything else, so labelling something a feature is a changelog decision. A minor is a
deliberate act for a milestone — a new car, or a KSA compatibility milestone:

```bash
git tag -a v0.2.0 -m "a third car"
git push origin v0.2.0
```

semantic-release reads the newest tag and carries on from it, so the next `fix` after that is
0.2.1. A tag is the only thing that anchors a version.

**`feat` means a player can observe the difference in the shipped archive.** Capability nothing
reachable uses yet is `refactor`; it becomes a feature in the commit that uses it. Developer tooling
is `chore`, `ci`, `test` or `refactor`, whatever its scope — `feat(tools)` still cuts a release. The
commit-msg hook warns when a `feat`/`fix`/`perf` commit touches nothing under `src/KSACars/`.

**No `Co-Authored-By` trailer, and no other attribution footer.** Commits carry the repository
owner's name and nothing else, whoever or whatever drafted them.

Split unrelated work into separate commits. **Batch a session's `docs` into one commit.**

**Commit to `dev`, not to `main`.** `main` is the release branch: a push to it cuts a release.

**Do not commit a behaviour fix as a fix until it has been verified in game.** Compiling, passing
the suite and having a plausible mechanism are not evidence — the hardest bugs live in the gap
between the maths and what KSA actually does. Ship the diagnostic, not the guess, and say in the
message what is unverified.

**A regression test only counts if it fails against the old code.** Check that it does, every time.

**This is enforced.** `tools/check-commit-msg.sh` runs both as a local `commit-msg` hook
(`./tools/install-hooks.sh`, using `core.hooksPath`) and as a CI job over every commit in a push or
PR. It skips merges, reverts, `fixup!`/`squash!` and semantic-release's own `chore(release):`.

## Environment

- **KSA install**: `/mnt/c/Program Files/Kitten Space Agency` (Windows game, WSL dev)
- **KSA build these notes were taken against**: `2026.10.10.5554`
- The system `dotnet` is 8.0 and **cannot build this** — the mod targets **net10.0**. A .NET 10 SDK
  is installed at `~/.dotnet`. **Use `tools/build.sh` / `tools/test.sh`**, which source
  `tools/env.sh`. In an interactive shell, `source tools/env.sh` once.
- `Import/` holds the game's assemblies and is **gitignored**. Repopulate with
  `./tools/sync-import.sh`. The build also finds a game install or a `ksa-game-assemblies` checkout
  on its own, see `Directory.Build.props`.
- **When KSA updates, four things have to move together**, not just `Import/`. See
  [After a KSA update](#after-a-ksa-update).
- **The game is launchable from WSL**: `tools/run.sh` starts `StarMap.exe` directly, found under the
  Windows user profile — override with `STARMAP_DIR`.
- **A developer's install is marked by a `developer` file beside the DLL**, which `tools/deploy.sh`
  writes and `tools/package.sh` never carries. `Build.Developer` reads it at load, and only then does
  the bridge start. **A new developer-only tool goes behind it**, and the startup line in the log says
  which kind of install is running.
- **The mod writes its own log** to `<KSA user dir>/Logs/KSACars.log`, with the session before kept
  as `KSACars.prev.log`; `./tools/ksa-user-dir.sh` prints that directory and `./tools/run.sh --attach`
  follows the log. KSA's own log, the newest `KittenSpaceAgency.<yymmdd-hhmmss>.<pid>.log` in the
  same folder, is still the place to look for mod discovery and asset/XML errors.

## Commands

```bash
./tools/doctor.sh                          # can this machine build, test and run it? -- start here
./tools/check-all.sh                       # everything CI runs; also the pre-push hook
./tools/build.sh                           # build the mod (handles the SDK PATH)
./tools/test.sh                            # the headless tests over Sim/; needs the assemblies, not the game
./tools/validate-parts.py                  # asset XML ids and paths; runs in deploy.sh
./tools/check-boundary.sh                  # Sim/ must not reference KSA types
./tools/check-comments.sh                  # history in comments, XML docs on privates, ratios
./tools/check-docs.sh                      # layout table, API counts and KSA build vs reality
./tools/package.sh                         # release zip into dist/ -- no symbols, no game DLLs
./tools/deploy.sh                          # build and install into the KSA mods folder
./tools/install-testcraft.sh               # put both cars in the vehicle library
./tools/run.sh                             # build, deploy, launch, show the mod's output
./tools/run.sh --attach                    # follow a game that's already running
python3 tools/ksa-mcp/server.py cli status # drive a running game through the bridge
./tools/roads/rig-laps.sh "Z "             # every car round the circuit files starting so, headless, in seconds
./tools/buggy-sounds.py                    # re-cut the buggy's engine from its recordings
./tools/eldorado-sounds.py                 # ...and the Eldorado's
./tools/f2004-sounds.py                    # ...and the F2004's, from the one recording that needs a credit
./tools/model/checkmesh.py src/KSACars/Meshes/*.glb --near-max 0   # z-fighting and degenerate UVs
./tools/ksa-user-dir.sh                    # where KSA keeps Logs/, mods/ and saves on this box
./tools/setup-starmap.sh                   # one-off: install StarMap and write its config
./tools/check-assemblies.sh --game         # has the installed game moved past the lock?
./tools/check-ksa-version.sh               # has RocketWerkz published a newer build?
./tools/api-surface.sh                     # record the KSA API this mod binds to
./tools/api-surface.sh --check             # ...and fail if the record is stale
./tools/decompile-assemblies.sh ../ksa-game-assemblies   # refresh the decompiled corpus
./tools/ksa-api-diff.sh ../ksa-game-assemblies           # which KSA changes hit this mod?

source tools/env.sh                        # then bare dotnet works in this shell
cd tools/apidump && dotnet run -- ../../Import members KSA.Vehicle   # inspect the game API
./tools/meshinfo.py "<KSA>/Content/Core/Meshes/CoreStructuralA_MeshAtlas.glb" Tube  # mesh bounds
```

## Layout

**The source is split by whether it touches KSA.** `Sim/` cannot; `Ksa/` does. The test project
links `Sim/**` wholesale and references no KSA assembly, so a `using KSA;` under `Sim/` fails the
test build, and a new file under `Sim/` is tested the moment it exists.

| Path | What |
| --- | --- |
| **`src/KSACars/Sim/`** | **no KSA types, linked into the tests wholesale** |
| `Sim/BuggyProfile.cs` | **one car, as data** — where its hubs, arms and coil-overs are, and how its springs, tyres and engine are tuned; `All` is every car the mod drives. KSA has no wheels, so every one of these numbers is the mod's |
| `Sim/BuggyDrive.cs` | a car's springs, tyres and engine stepped against the ground under each hub, **as one impulse through the centre of mass and one about it**, and the poses its wheels, arms and coil-overs are drawn in |
| `docs/F2004-REFERENCES.md` | what the F2004 was modelled from: dimensions, the rules of its year, blueprint stations, the kitten's measurements, and each photograph's author and licence |
| `Sim/GroundPlane.cs` | the ground under a car as one plane through what is under its four wheels, **whose up is what the dampers measure closing speed along**: straight up, a car running down a slope is closing on the ground as far as they know |
| `Sim/WheelGround.cs` | what is under each wheel this step — the nearest of the terrain, a launch pad and a road below its hub — as the contacts the drive is stepped against, **the wheels' alone**: a hull on a road is the road's colliders' to stop |
| `Sim/SteeringGrip.cs` | where a seated driver's hands hold the wheel, in the kitten's own model space — **anchored to its seat**, so where the car is in the world never enters it — and the two-bone elbow that puts a wrist there; past 20 deg of wheel the rim slides through the hands, which a seated kitten's 14.5 cm reach needs |
| `Sim/HeadGear.cs` | where a thing worn on the driver's head is as a subpart of the car, from the matrix KSA carries the kitten's unposed mesh to its head by — **the mesh is in the kitten's own model space, in metres, and that matrix is in centimetres** |
| `Sim/Righting.cs` | the turn and the lift that set a car on its roof or its side back on its wheels, **in the car's own frame** — the shortest turn, so it keeps its heading, and a roll rather than a somersault from flat on the roof; and the turn that stands a car on a road facing along it |
| `Sim/Lift.cs` | four rockets under the car as one push — **thrust along the car's own up, through the centre of mass**, so it is balanced wherever the crew sit, and off the ground a hold that keeps it level or leans it the way the keys ask; and the gas each flame is drawn from |
| `Sim/Barrier.cs` | a car's side against a deck's barrier, as a push of the mod's own: **what speed it has into the wall taken away and a share of that push rubbing it along**, held short of the solid wall, whose friction is the ground's and stops a car that leans on it |
| `Sim/Boost.cs` | two rockets on the tail as one push along the car's own forward, through its centre of mass — a fixed acceleration whatever the gravity, which is what moves a rock where the tyres have nothing to push against |
| `Sim/Downforce.cs` | rockets on the bonnet and the boot that press the car onto the ground — a push along its own down through the centre of mass, and **the load the springs are sized off while it is on**, so the car rides at its usual height |
| `Sim/Hover.cs` | the rockets under the car and the ones on top firing together — **thrust that carries the car's weight and brakes any climb or fall**, so it hangs where it is |
| `Sim/Hatch.cs` | the iris over each thruster — **where each of its six blades is, how it is turned and how big it is drawn** as it opens, and when its rocket may fire |
| `Sim/Headlights.cs` | the dipped and main beams and the tail lamps, as the spotlights KSA is asked for — range, brightness, cone and dip — with the off/low/high setting and how it is kept in step with the part's light switch |
| `Sim/TerrainRay.cs` | where a ray from the eye first goes under the ground, walked out along it — **not the mean sphere's hit**, which lands behind a hill seen side-on |
| `Sim/TerrainMask.cs` | the stretch of a line that lies below a body's highest ground, which is all of it the terrain ray has to walk |
| `Sim/CursorAim.cs` | the cursor, from window pixels to the framebuffer pixels a camera's ray is asked for in |
| `Sim/Picking.cs` | what the pointer is over: the nearest thing on screen within its own reach |
| `Sim/DrawAnchor.cs` | an ecliptic point as an offset from a craft KSA has just placed, so a gizmo lands where the craft is drawn and not where its orbit says |
| `Sim/FrameLatch.cs` | hands a frame's work out once, to whichever hook reaches it first — **the UI pass is skipped while the UI is hidden and the frame postfix is not** |
| `Sim/BridgeCommand.cs` | one command dropped into the bridge's folder, read — **text in**, so every refusal is testable here |
| `Sim/ITerrainHeights.cs` | the seam the ground under a hub is read through |
| `Sim/PadSurface.cs` | the top of a structure standing on the ground, as the first of its boxes and cylinders a line dropped from a hub meets — **the height field knows nothing of a launch pad** |
| `Sim/Circuit.cs` | **a set of roads as it is saved**: points on a body by latitude and longitude, the roads that join them, each point's corner strength and height, any handle set by hand, and a bank and a width at each end of a road — its edits, each answering a new circuit so the old one is the undo, the same circuit moved to another place or body, and its JSON, **which is one place and every point as metres from it** |
| `Sim/CircuitHistory.cs` | a circuit being edited and every state it has been through, for undo and redo — **a drag is many changes and one step** |
| `Sim/RoadLayout.cs` | a circuit's roads as centre lines: one cubic curve a road, with the handle nobody set worked out from what meets at the point — **two roads go through, a side road at a junction leaves straight** |
| `Sim/GodView.cs` | a view from above that is not tied to a craft: a place on the ground looked at, from how far, from which heading and how steeply, and how it is panned, turned and zoomed |
| `Sim/RoadLaying.cs` | a circuit put on the ground: each run of its roads as a `RoadRibbon`, stopped at the mouth of each `RoadJunction` and brought to its plane there, and as a line of points along that surface, the ground under it read through a function, so a test lays the same roads the game does over ground of its own; and **which runs and junctions are joined**, which the physics is given as one solid |
| `Sim/RoadJunction.cs` | **where three or more roads meet, as one polygon of asphalt under one tilted plane**: each road's mouth, the corners between them rounded, bevelled or joined round the point, the plane that is the through road's climb and lean, what is past the edge between the mouths, what a wheel there is over and the line a driver takes across — or why the roads are too nearly alongside for one to be made |
| `Sim/RoadRibbon.cs` | **a run of roads as one surface**: where it is a distance along and across, the way it faces there, and for any place how far along and across that is — the asphalt leaning by its bank, a verge and an embankment down into the ground past its edge, or a deck with nothing beside it where it is more than 4 m up |
| `Sim/RoadTessellation.cs` | a ribbon as triangles, **the ones the game draws and collides with**: rows of vertices across the road, closer where it bends, climbs or twists, joined into meshes of about 100 m or of as much as a place in the pool holds, with the asphalt, the earth and a deck's sides apart, each vertex facing as the surface does and given twice only at a crease; and a junction as a mesh that **takes each run's last row for its own at the mouth**, several junctions to a mesh where they are near |
| `Sim/RoadLoop.cs` | **a loop, which a height over the ground cannot say**: road that leaves the ground level, goes over upside down and comes back level to one side, its bend nothing at its foot and the most over its top; for a wheel how far off its asphalt it is the way that faces, where a circuit's roads put it, its triangles and the steering that carries a car round it |
| `Sim/RoadSurface.cs` | the roads as something a wheel can be over: its height above the ribbon's own surface under it, or a junction's plane, and the way that faces, **a road more than a deck's half metre overhead being a bridge**, so the road beneath is the one answered, and asphalt answered before any road's verge or bank |
| `Sim/RoadCollider.cs` | the meshes of runs and junctions that are joined as **the one solid KSA's physics is given for them**: every triangle about one origin, listed so that it is solid from the side it is drawn from, and narrowed from where each vertex was worked out so two meshes' triangles share their corners |
| `Sim/RoadDrawList.cs` | which of a circuit's meshes are drawn: how much one place in the pool holds and how many places there are, **the nearest meshes given the places when there are too few and the nearest drawn when a view has too few draws**, a mesh's triangles in the order of the two materials, and the ledger of which places are out |
| `Sim/RoadWarnings.cs` | what is wrong with a circuit as it is laid, for whoever is drawing it: a junction that could not be made or has a road too short for its corners, a bend tighter than its width turns in, and a grade past what each kind of car climbs |
| `Sim/Route.cs` | a line to drive along and how far along it a car is: the roads' centre lines through a circuit's points in order, a metre a sample, with the bend, the slope and the bend of the climb at each, a curve across each junction from the mouth come in by to the one left by, a kink rounded inside the asphalt and from its outside edge where the car turns too wide for less, and a set distance to one side. **Progress is looked for a little ahead of where it was and nowhere else**, which is what tells a route from the road it crosses |
| `Sim/Autopilot.cs` | a driver that follows a route: pure pursuit from the rear axle, **the angle it wants turned into a steer input through `BuggyDrive.SteerLock`**, a speed held to what each bend, crest and dip allows and braked for in time, and a lap that ends itself with a reason — finished, off the road, flipped, stuck, out of time — and a summary of what it saw. The same driver in the tests and in the game |
| `Sim/RaceLine.cs` | the line to drive as arrowheads on the road ahead of a car: where each lies, on the road's own surface, and **its colour from the car's speed now against what the route allows there**, as runs of one colour that are each a draw |
| `Sim/RoadCurve.cs` | the cubic curve a road follows between two points, sampled |
| `Sim/RoadProfile.cs` | what a road is along its length apart from where it goes: **its height as a curve through its points' heights that climbs steadily where they do and never overshoots**, its lean and its width, an end at a junction held to the height, the climb and the lean it is given, with the steepest grade and the tightest crest or dip of each road |
| `Sim/RoadGround.cs` | the ground along a road, **smoothed and never buried**: at or above every sample of the ground and the line between two, with no step in its slope, level ground and a steady slope left as they are and a bump filled over |
| `Sim/RoadChart.cs` | **a flat chart of the ground round a circuit**, in metres east and north, so widths, offsets and lookups are plain geometry; stereographic, so every angle is kept and a length `r` from its centre is `(r/2R)^2` too long |
| `Sim/RoadLine.cs` | a road's centre line on the chart, **measured by its own length**: where it is, its heading and how hard it turns a distance along, whatever speed the curve's parameter goes at, and where it turns tighter than the road on it can |
| `Sim/ClutterGrid.cs` | **where KSA scatters grass, trees and rocks, worked out as its generation shader works it out**, so the ones under a road can be named in KSA's per-cell mask |
| `Sim/MeshPatch.cs` | **an experiment**: a curved square as vertices, normals, UVs and triangles, in a right-handed frame of its own and wound the way KSA draws, for the bridge's one mesh made while the game runs |
| `Sim/Vec.cs` | vector helpers, and `Plan`, a place or a step on a chart |
| **`src/KSACars/Ksa/`** | **everything that binds to the game** |
| `Ksa/KSACarsMod.cs` | StarMap entry point: installs the ten patches, and once a frame finds the cars, poses their wheels and plays their engines |
| `Ksa/PhysicsHook.cs` | **one of the ten places this mod patches the game** — a prefix on `Vehicle.PrepareWorker`, the only window in which a write to a vehicle's state survives the frame |
| `Ksa/Buggies.cs` | every car in the world: its ground read **off the physics state in the planet-fixed frame**, never the analytic position, which on a landed craft is metres out; the impulse written from `PhysicsHook`'s window; the subparts posed each frame |
| `Ksa/SeatedCrewHook.cs` | the second patch — **a car's crew drawn when it is not the craft being flown**, because KSA draws seated kittens for the controlled craft alone, and in an open car the one left sitting would disappear when the other gets out |
| `Ksa/LightsHook.cs` | the third patch — **the headlamps submitted where KSA submits a craft's own lights**, a postfix on `PartTree.UpdateRenderData`; KSA clears its light list after the GUI pass, so a light from any StarMap hook is never drawn |
| `Ksa/LensColourHook.cs` | the fourth patch, and the only one on a private method — **the colour a lens subpart glows**, written into KSA's per-instance render state; if KSA moves it the patch does not apply and coloured lenses glow white |
| `Ksa/FlamesHook.cs` | the fifth patch — **a flying car's flames submitted where KSA submits a craft's own plumes**, a postfix on `Vehicle.AddVolumetricExhaustInstances`; KSA empties its plume list just before, so a plume from any StarMap hook is never drawn |
| `Ksa/CrashHook.cs` | the sixth patch — **a car spared from breaking up**, a prefix on `Universe.DestroyVehicleFromEvent` that turns away every structural failure of a car, whatever the cause |
| `Ksa/BoostHook.cs` | the seventh patch, on two methods — **the sprint key heard by a car**: a postfix on `Vehicle.OnKey` queues it as `OnKey` queues the engine keys, and one on `Vehicle.ProcessInput` records it where KSA applies the queue. A vehicle's `OnKey` does not know the key at all; only a kitten's does. The same postfix switches the downward thrusters on the RCS key. A prefix on `OnKey` also takes Shift off every other key while a car boosts, since KSA matches a vehicle's keys with their modifiers exactly |
| `Ksa/HudHook.cs` | the eighth patch, on two methods — **KSA's HUD told a flying car has engines**: its engine panel, where the throttle is read, shown on a craft with no `EngineController`, and its "No active engines" alert withheld |
| `Ksa/RailsHook.cs` | the ninth patch, on a private method — **a car whose rockets are burning kept off its orbit**: KSA rails a craft above the atmosphere unless an engine of its own fires, and on rails nothing written to its velocity is read |
| `Ksa/RoadDrawHook.cs` | the tenth patch — **the roads submitted where a static mesh's draw survives**, a postfix on `SuperMeshRenderSystem.ClearBuckets`, which KSA calls for each viewport straight before drawing into it; a draw made any earlier is cleared by that call |
| `Ksa/RoadColliders.cs` | triangle meshes of the mod's own in KSA's physics, registered as ground clutter of infinite mass in each bubble's simulation, from four prefixes on the physics passes and one private dictionary. Each run of a laid road is one mesh, in the bubbles within three kilometres of any of it, and a road laid again has the meshes from before freed once no bubble holds one; the bridge's `road` with `box_top` puts a box under the flown craft |
| `Ksa/RoadDrawing.cs` | **the roads as they are drawn**: each mesh of a laid circuit written into a place of its own in a pool of `RuntimeMesh`es that only grows, to 56 places, and handed to KSA's renderer for each view in asphalt and in earth, turned as the body is about the mesh's own origin; the nearest drawn where there are more meshes than places |
| `Ksa/RacingLine.cs` | **the line to drive, drawn ahead of the car being flown**, from the panel's Racing line: the route driver's own racing line and speeds for that car, a stretch of arrowheads written once and each colour's material told every frame which of them to draw |
| `Ksa/RuntimeMesh.cs` | a mesh made while the game runs: room taken once in the three buffers KSA's static meshes share, cut into meshes that are each written over in place, any number in one submission, and drawn by KSA's own renderer as a `StaticMeshRenderable` for each material it has. **One patch has been seen in game**, the bridge's `road` with `mesh_test`; a road's meshes have not |
| `Ksa/Roads.cs` | a circuit laid on a body: every run and every junction as one surface each, answered to a wheel asking what is under it, made into the triangles `RoadDrawing` draws and `RoadColliders` gives the physics, a dragged road's meshes written no more than ten times a second, and the clutter under it switched off through KSA's exclusion mask and put back when the road is taken up |
| `Ksa/RoadEditor.cs` | **a circuit drawn on the ground with the mouse**, from the panel's Build roads: a click on the ground carries the road on from the point selected, a click on another point joins them, a click on a road puts a point in it, and a point, a handle or the knob that sets a point's height is dragged. Its view is its own, panned, turned and zoomed with the mouse; the roads are laid again on every change, and all their meshes and their colliders when a drag ends |
| `Ksa/CircuitLibrary.cs` | the circuits a player has, one JSON file each under `<KSA user dir>/KSACars/Circuits/` — outside any save, which KSA wipes on every write — and the ones the mod ships beside its DLL, read where the player has none of that name |
| `Ksa/Courses.cs` | **a circuit laid where the car being flown is, for a player**: its first point at the car, its first road the way the car faces, and the car stood on its start; asked from the panel and done from the frame hook |
| `Ksa/RocketSound.cs` | the rockets' roar: KSA's stock engine sound on one channel a car, fed the throttle each frame as KSA feeds an engine's |
| `Ksa/DriverHands.cs` | the driver kitten's hands on the steering wheel — **an `IAnimProcessor` on the seated kitten's model**, the hook KSA turns its eyes with, solving each arm onto the rim after the seated animation and before skinning; reached through one private field, `KittenRenderable._characterAvatar`, and losing it leaves the hands in the lap; it also reads where the head bone has the head, for the helmet |
| `Ksa/CarPanel.cs` | the panel shown while a car is flown, with the headlight switch, the scoop's choice and the rock weight, the craft mover's switch, a course to lay and the line to drive, the road editor's and the **Unflip** button — closable, leaving a small button that opens it again, and listed in ModMenu's menu when that mod is installed; a plain ImGui window from the GUI pass that never takes the keyboard, because KSA drops the flown craft's held keys while a window has it |
| `Ksa/ModMenuEntry.cs` | a copy of ModMenu's attribute, which that mod finds by name — **not a dependency**, and inert without it |
| `Ksa/BuggySound.cs` | a car's engine while it is being flown — an idle and a loaded loop crossfaded by throttle and re-pitched to its RPM every frame, silent past 4x warp |
| `Ksa/SoundChannels.cs` | the listener, its pressure and a held channel moved or stopped, each guarded |
| `Ksa/TerrainHeights.cs` | one body's height field, off the engine's own height map |
| `Ksa/LaunchPads.cs` | a body's launch pads as the colliders KSA stands on each, posed as KSA poses them, so a wheel on a pad is sprung against the pad |
| `Ksa/KsaWorld.cs` | most KSA contact is funnelled here — keep it that way |
| `Ksa/KsaWorld.Pointing.cs` | the part of it the craft mover needs: the cursor's ray and the ground it meets, where a craft is on screen, and gizmo rings and lines |
| `Ksa/CraftMover.cs` | **picks a craft up with one click and sets it down with the next**, from the panel — through `Vehicle.TeleportToLocation`, so it arrives resting on the ground. Carried over from KSArmory with its helpers |
| `Ksa/Bridge.cs` | **commands from outside the game**, read from `Logs/bridge/KSACars/` and answered beside them — load a save, park a car, drive it, set a driver going round a circuit, seat and EVA kittens, step the world, capture — so an agent can test a car in a game that stays running. Developer installs only |
| `Ksa/Laps.cs` | the laps being driven by `Autopilot`, a car each, **kept outside `Buggies.Entry`**: stepped from the physics window, and from the frame hook ended when the car is gone or the roads are laid again, and filed under the bridge's folder as a summary and a line a step |
| `Ksa/CraftSpawner.cs` | parks a craft from a vehicle library at a latitude and longitude, for the bridge's `spawn` |
| `Ksa/Build.cs` | what build this is, read off the assembly — and **whether it is a developer's install** |
| `Ksa/Log.cs` | the mod's own log file, which is the only debugging channel it has |
| `src/KSACars/KSACars*.xml` | the cars' parts, seats, colliders and sounds — at the mod root, mirroring Core |
| `src/KSACars/Meshes/`, `Textures/` | the art, **authored** in Blender over MCP; each `.blend` is the source and is not in this repository. A road has no mesh here, and its asphalt and earth are written by `tools/model/road-textures.py` |
| `src/KSACars/Circuits/` | the courses the mod ships, written by `tools/roads/club-motorsports.py`: **their line is OpenStreetMap's, under the ODbL, credited in `README.md`** |
| `src/KSACars/Sounds/` | the engines, cut from recordings by `tools/buggy-sounds.py`, `tools/eldorado-sounds.py` and `tools/f2004-sounds.py`. **The F2004's four are CC BY-SA 3.0, credited in `README.md`**; the rest are CC0 |
| `src/KSACars/mod.toml` | serves as both the content-mod and StarMap manifest |
| `tests/KSACars.Tests/` | links the KSA-free sources and drives the cars headlessly |
| `tools/apidump/` | reflection dumper for the game assemblies |
| `tools/apisurface/` | reads the KSA API this mod binds to out of its own metadata |
| `tools/audio/` | the recordings the engine sounds are cut from, and the provenance of each |
| `tools/model/` | the checkers over an exported mesh, and a previewer |
| `tools/ksa-mcp/server.py` | **an MCP server over the bridge**, registered in `.mcp.json`, returning captures inline; `cli <tool>` runs one from a shell. It launches a game only if none is running and closes only one it launched |
| `tools/bepurig/` | **KSA's physics engine on its own**: a hull's box flown along a circuit's road mesh, counting the contacts that change its speed with nothing touching |
| `tools/roads/` | the circuits the roads are pushed with, `run-laps.py` that laps the cars round them in the game three at a time, and `rig-laps.sh` that does it in the headless rig in seconds |
| `tools/vis/vis.py` | what the bridge's pictures are judged with: same-instant diffs, the temporal-noise map, contact sheets and animations |
| `tools/install-testcraft.sh` | writes both cars into the vehicle library as ready-to-drive craft |
| `tools/validate-parts.py` | checks asset Ids and texture, mesh and sound paths against the files and against Core |
| `docs/KSA-MODDING-NOTES.md` | the runtime, the loader, the types and the gotchas |
| `docs/KSA-FRAME-ORDER.md` | **the engine's own frame order and what instant each sample belongs to** |
| `docs/FRAMES-AND-EPOCHS.md` | the epoch rules that follow from it, for anything drawn or timed |
| `docs/KSA-CAMERAS.md` | what the engine does with cameras and viewports |
| `docs/KSA-TERRAIN.md` | **where the engine thinks the ground is** — the height field's resolution and what `accurate` buys |
| `docs/KSA-API-SURFACE.md` | **generated** — the 526 members an upgrade has to preserve |
| `docs/BLOCKED-ON-KSA.md` | **what the cars cannot do, or do only round the engine**, with what would unblock each |
| `.claude/skills/upgrade-ksa/` | the whole KSA-update procedure, as a skill |
| `.claude/skills/ksa-blender/` | authoring art in Blender over MCP, and the export contract KSA reads |

## The cars

**KSA has no wheels, so a car is a part with colliders and the mod drives it.** The colliders stand
clear of the ground at rest and only touch in a crash, because KSA's terrain friction is one number
for every collider and a wheel box on the ground drags like a skid. Everything that makes a car drive
is `Sim/BuggyDrive.cs`: per wheel a spring and damper sized off its share of the car's weight, an
anti-roll bar and a roll damper across each axle, the side force put in at the roll centre's height, and a friction circle in which side grip is spent first, so a tyre
pushing hard in a corner gives up drive before it gives up its line. The steering lock is held to
what the front tyres can hold at the speed, which is what stops a full-lock flick scrubbing to a
standstill.

**A car with wings pulls with its power and grips with its downforce.** A profile's `PowerW` makes the
pull the lesser of the launch pull and power over speed, with `TopSpeed` as the rev limiter, and its
`DownforceAreaM2` presses the tyres with the air: one friction circle on the spring's load and the wings'.
Nothing presses the body, so the share of a tyre's force past what its weight alone could hold goes in
level with the centre of mass, where it turns the car and neither rolls nor pitches it. The steering lock
is held to the grip of the weight and the wings together, and loses its three-degree floor while wings
press, which at speed asked the F2004's tyres for more than they had. The F2004 has both;
the other two have neither and drive as they did.

**Off the ground a car with wings flies nose first.** With no wheel touching, `BuggyDrive.WingsInTheAir`
turns the car's nose into the way it is going, in pitch and in yaw, and resists either turning, as the air
on its tail would: half the wings' area 1.8 m behind the centre of mass. Without it a car keeps whatever
turn it left a lip with, and the F2004 came down from a 180 m jump on its tail. A car with no wings is
left as it was.

**The springs are softened to what a step can carry.** The impulse is explicit, and past a step that
depends on the car the roll mode overshoots: the car rocks from side to side every step and the unloaded
tyre drives nothing, which halved the F2004's pull at KSA's 20 ms. `BuggyDrive.SpringRate` scales the rate
down above that step, 19 ms for the F2004 and about 80 ms for the other two.
`BuggyDriveTests.NoCarRocksOnItsSpringsOrLosesItsDriveAtAnyStep` fails without it.

**KSA's own air drag is kept off a car on its wheels.** KSA drags every craft by the bounding box of its
colliders, a skin term on the box's whole surface included: 3.1 m2 on the F2004, 5.2 on the Eldorado, and
it held them to 37 and 31 m/s. Its drag code skips the air while the craft's `AerodynamicCdABody` is zero,
so `Buggies.ShedGameDrag` zeroes it in the physics window, every step because KSA rebuilds it when a kitten
boards, and rebuilds it from the same box once a car has been off its wheels for `BuggyDrive.FlightSeconds`
or has a rocket lit, where that drag is the only one it has. A write to it anywhere else races the worker.
With its wheels on the bottom in shallow water a car loses the water's form drag the same way.
`docs/KSA-MODDING-NOTES.md` has the mechanism. **Not yet seen in game.**

**An upright rises and steers with its wheel and does not spin.** The F2004's brake drum and duct fill
its rims, so on the body they would be cut by the rim at the first bump. They are subparts ending
`Upright<corner>`, which `Buggies.Pose` places at the hub with the steering's turn alone.

**It runs inside the engine's physics window, never from a StarMap hook.** KSA double-buffers a
vehicle's state: the worker's result is written over it, the next worker's input is snapshotted, and
*then* the GUI pass runs. A velocity written from any StarMap hook is overwritten before anything reads
it. `Ksa/PhysicsHook.cs` prefixes `Vehicle.PrepareWorker`, the one method in that window a mod can
reach, and `Buggies.Physics` writes there. **Nothing in it may throw** — it runs inside the engine's
own loop.

**The ground's up is the slope's, not the planet's.** The drive damps a wheel by how fast its hub closes
on the ground along the ground's up, and pushes along it. With straight up, a car going down a slope at
speed was closing on the ground at the slope's share of its speed, its dampers lifted it off, and it
bounced until it turned over. `Sim/GroundPlane.cs` fits a plane through the four points under the wheels,
terrain, pad or road alike, and every wheel takes its up; with a wheel over nothing or a lean past 60
degrees they keep straight up. **Not yet seen in game**, and it changes every car on every slope.

**The ground is read in the planet-fixed frame, off the physics state.** `PhysicsStates.GetStatesCcf`
is where the car actually is this step; the analytic orbit position of a landed craft is metres away
from it. In the planet-fixed frame the ecliptic's ~29.8 km/s and the planet's spin cancel, so a hub's
velocity over the ground is just a velocity. KSA rails a car that has stood still, so a driven one is
woken with `TakeOffRails`.

**A launch pad is not in the height field, so a wheel over one is sprung against its colliders.** The
field answers the levelled ground a pad is built on, 1.75 m under the grate a craft is launched from.
`Ksa/LaunchPads.cs` reads each pad's boxes and cylinders off its `StaticObjectTemplate` and poses them
as KSA does; `Sim/PadSurface.cs` drops a line from each hub onto them, and the hub's height is the
lesser of that and its height over the terrain. A surface up to half a metre above a hub counts as
under the wheel; anything higher is a roof.

**Yaw inertia is what lets the side grip settle, so the mass is a box the car's size.** The grip
removes a share of each wheel's sideways slip every step; against a sphere's inertia, a third of a
car's, that share overshot, the car rocked on its wheels at 13 deg/s, and the rocking spent the grip
the drive needed. `KSACarsGameData.xml` gives the Eldorado a `SolidCuboidMass`, and
`BuggyDriveTests.AYawKickDiesAwayRatherThanRockingTheDriveAway` fails against the sphere.

**The Eldorado can fly, on thrust the mod applies itself.** A car flies if its profile has
`RocketNozzles`; the buggy has none. `Sim/Lift.cs` adds to the same velocity and spin
the springs write: up to two weights of thrust along the car's own up, so half throttle hovers. With no
wheel on the ground the pitch keys lean the nose, the yaw keys bank it left and right and the roll keys
turn it; on the ground only the thrust is given, because the springs hold the attitude there.

**The rockets answer KSA's own engine keys, read off two private fields.** Engine start and shutdown
light and cut them, through the craft's `_manualControlInputs.EngineOn`. The throttle keys move the
mod's own throttle, read off `_engineFlags`, because KSA clamps its own to the least any engine allows
and with no engine that is 1. `KsaWorld` reads both by reflection and checks their types; if KSA renames
either, the rockets cannot be lit: nothing but the keys and the bridge's `lift` works them.

**The flames are KSA's own plumes with no engine behind them.** A real engine cannot be kept for its
flame: no propellant is no plume, and any flame is thrust. `Buggies.Flames` builds a
`VolumetricExhaustInstance` per nozzle from a Core exhaust template and hands it to KSA's renderer each
frame, drawn from the gas `Lift.Flame` gives for the throttle; `BuggyProfile.RocketNozzles` is where
they leave the floor. `docs/KSA-MODDING-NOTES.md` has the mechanism.

**Every thruster sits behind an iris that is shut, and unseen, until it is wanted.** The body mesh has a
round hole at each of `BuggyProfile.Hatches`, with a cup and nozzle behind it and six blades closing it
flush, each its own subpart drawn in the body's material from a patch of the bonnet's own texels.
`Sim/Hatch.cs` is where each blade is: half the disc, squashed towards its own side of the rim as it opens,
so the six straight edges leave a hexagon growing from the centre and no blade has to slide under the
skin round the hole, which is thin and curved. A group's rockets fire once its irises are open, a tenth
of a second after they are asked for; they shut once its flames have died, and `Buggies.Pose` moves them in the world's time, so slow motion slows them and a pause holds them.
The holes are in the body mesh in the `.blend` as they are in the `.glb`, and the cup and the blade are
objects there too, with the blades laid shut over the front-left bonnet hole as `View_IrisBlade_*`.

**A car does not break up when it comes down hard.** KSA destroys a craft past a g-load set by its
size, and a car dropped a few metres onto its bump stops exceeds it. `Ksa/CrashHook.cs` vetoes that for
a car whatever the cause: ground, sea, collision, g-load or the air. That is one of two
ways KSA destroys a craft: the other breaks a part whose contact pressure passes its crash tolerance,
and a car is one part, so each `<Part>` sets `CrashTolerance` far past anything a contact gives.

**The throttle is shown on KSA's own gauge, on a panel KSA would otherwise hide.** The readout is on
KSA's engine panel, drawn only for a craft with an `EngineController`; `Ksa/HudHook.cs` shows it for a
car that flies. `KsaWorld.ShowThrottle` writes the mod's throttle into the
craft's each frame after the GUI pass, which is before KSA reads its gauges; KSA clamps it back to full
in the next physics window, so it is never read back.

**The Eldorado has a boost: two rockets on its tail, lit while the sprint key is held.** A car has one if
its profile has `BoostNozzles`. `Sim/Boost.cs` is the push, `Buggies.Flames` draws a flame at each nozzle
as it does the lift rockets'. The key is
read with `Vehicle.GetSprintInput()`, which `Ksa/BoostHook.cs` makes true for a car.

**The Eldorado has downward thrusters, switched with the RCS key.** A car has them if
its profile has `DownNozzles`. `Sim/Downforce.cs` adds about a g towards the car's own floor, which on
Luna is what gives the tyres something to grip with; the drive is stepped with gravity plus that, so the
springs carry it. `Ksa/BoostHook.cs` hears the key.

**With the lift rockets lit as well, the car hovers.** While both sets fire, `Sim/Hover.cs` replaces both
pushes with one that cancels gravity and brakes the climb rate, so the car holds its height wherever it
is; the lift throttle does nothing then, and the keys still lean it across the ground.

**Under time warp the springs are left out and the rockets carry on.** A physics step is the frame times
the warp, and past a tenth of a second the springs would overshoot, so the car rests on its colliders.
The thrust is a plain push, and the hold that keeps the car level and the hover's braking are written as
the share of the way a step of that length covers, so neither can overshoot however long the step. KSA
takes the push at the start of its step and lets gravity act through it, so the hover brakes the step's
average climb, half a step of gravity under the sampled one; braking the sampled climb leaves the car
rising at half a step of gravity, 5 m/s at 50x. The downward thrusters alone do nothing under warp.

**Above the atmosphere KSA rails a craft with no engine firing, and a railed car cannot be pushed.** It
decides in two places in `PhysicsBubble`, and beyond the body's physics radius by a route of its own, so
`Ksa/RailsHook.cs` answers the one question both ask per craft, `KittenWantsWake`, with yes for a car
whose rockets are burning. A car already coasting on its orbit when they light is rebuilt from that
orbit with `PhysicsStates.UpdateFromAnalytic`, as KSA rebuilds a craft it is about to split. With the
rockets cut the car is KSA's to rail again, and coasts as any craft does.

**The Eldorado carries a scoop, in three sizes, chosen from the panel.** A car's profile lists its
`Scoops`, and one or none is on. Each blade is its own subpart, both in one `.glb` and one atlas; off, a
blade is shrunk to nothing inside the hull, because a subpart has no switch for being drawn. Each
scoop's three collider boxes are declared stowed inside the hull's own box, and `Buggies.SetScoop` moves
them out to the blade and back by writing `ColliderModule.PositionPartAsmb` and setting
`NeedsColliderUpdate`, as KSA's own animated parts do. The bigger scoops' boxes are taller than the hull, so they
are declared lying down and `ScoopProfile.Upright` is the turn that stands them up; the Mega's are in two
tiers, because lying down a tier is the most that fits across the hull.

**A scoop pushes rocks through KSA's own clutter physics, which it has to loosen.** A rock is a fixed
static until one hit carries enough energy, and at KSA's threshold a car has to ram it. While any scoop
is on, `KsaWorld.LoosenClutter` lowers `BubbleClutterStatics.DisplaceEnergyPerKg` for every vehicle and
switches on KSA's experimental ground clutter collisions if they were off, and the setting is left on.
With no scoop on, rocks stay loose for as long as a car that can carry one is in the world and the
player has that setting on, so the bare car shoves them too; otherwise the threshold is put back. `docs/KSA-MODDING-NOTES.md` has the mechanism.

**Rocks are made lighter while they are loose, by the panel's Rock weight.** KSA's rocks are solid stone,
a hundred times a car's weight at a car's size. `KsaWorld.WeighRocks` scales every
`ClutterObjectTemplate.MassKg` to a share of what KSA loaded, 2% unless the slider says otherwise, and
puts KSA's back when they are no longer loose. KSA reads a rock's mass only when it builds the rock's collider, so
each change holds the collisions setting off for a moment: KSA drops every rock on the first sync it
finds it off and rebuilds them, at their new weight, on the first it finds it on.

**A car on its roof is stuck, so the panel can right it.** The springs only push through the wheels,
and KSA rails a car lying still. `Buggies.Right` queues it and the next physics window writes the pose
`Sim/Righting.cs` solves: turned upright where it lies, lifted until the lowest tyre is just clear, and
left standing still. The bridge's `drive` takes `flip` and `unflip` to test it.

**The lamps are KSA spotlights, handed over every frame.** `BuggyProfile.HeadLamps` and `TailLamps` are
where they are and `Sim/Headlights.cs` what each beam is; `Buggies.LightLamps` submits them from
`LightsHook`, in the matrix KSA is drawing the craft with. The tail lamps come on with the headlamps and
burn brighter under braking.

**A lens glows through KSA's emissive map, and a coloured one has to be its own subpart.** KSA's part
shader reads one channel of the emissive texture as a mask: the glow is white, or the one colour the
drawn instance carries, which KSA only sets for a battery's status light. `Ksa/LensColourHook.cs` sets
it per subpart, from `BuggyProfile.ColouredLenses`, so the Eldorado's tail lenses glow red and its side
markers amber while the headlamps in the body stay white. `tools/model/split-lenses.py` cuts those
lenses out of the body mesh and **has to be run again after every export from Blender**;
`tools/model/lens-emissive.py` paints the mask. Both read the lens faces' UVs from `tools/model/lenses/`,
taken out of the bake-source `.blend`, which have to be re-read if a car is unwrapped again. A light
cannot stand in for this: it lights the bodywork round the lens as a blob.

**The switch is the part's own.** Each car carries a `<PowerConsumer LightSwitch="true">`, which is what
KSA darkens the emissive lenses by and saves with the craft. `Headlights.Reconcile` keeps the panel's
setting and that switch in step; dipped or main beam is the mod's and is not saved, so a saved car comes
back dipped. It draws almost nothing, because nothing on a car charges the battery.

**Seats and doors are KSA's own.** An `<IVASeat>` per seat, placed at the kitten's eye, and an
`<EVADoor>` on a mesh-less subpart beside each front seat; a kitten boards and leaves through those
like any craft's. The driver's seat is the one nearest the profile's `DriverEye`, which is where
`Sim/SteeringGrip.cs` reaches from.

**The F2004's driver wears a helmet, which is two subparts of the car put on its head each frame.** A
kitten is KSA's character and not the car's, so nothing can be parented to its head: the shell and its
visor are subparts ending `Helmet` and `HelmetVisor`, the last two in the part's list, and
`Buggies.Pose` places them with `Sim/HeadGear.cs` from the head bone's inverse bind matrix times its
pose, which `DriverHands` reads off the skeleton after the seated animation. Their mesh is in the
kitten's own model space with the head as it was bound, X to its left, Y up and Z ahead, in metres,
where KSA's matrix is in centimetres: a hundred times the identity with the head unmoved. With nobody
at the wheel both are shrunk to nothing in the seat. The visor is a `PartModelGlass`, one skin facing
out, and the driver's eyes show through it. A car has a helmet if its part has those subparts;
`docs/F2004-REFERENCES.md` has what it was modelled on and how it is fitted.

**Ten patches; eight are on public methods and pinned.** Each of those has a `PinTheSignature` that is
never called and only puts the patched method in this assembly's metadata, so `docs/KSA-API-SURFACE.md`
tracks it and a KSA change to it is a build error. `LensColourHook` and `RailsHook` each patch a private method, which
cannot be pinned: each checks what it found at install and switches itself off with a warning. Harmony ships with StarMap, so a player installs
nothing extra.

## Roads

**A player lays a course where the car is; drawing one is a developer's.** The panel's Course list is the
mod's own courses, `src/KSACars/Circuits/`, shipped beside the DLL, and whatever is in the player's
library, the player's own of a name before the mod's. Lay here asks `Ksa/Courses.cs`, which from the frame
hook moves the circuit so its first point is where the car is and its first road runs the way the car
faces (`Circuit.MovedTo`, `StartBearingDeg`), lays it, clears what stands under it and stands the car on
its start; Take up roads puts the ground back, and Racing line is there while roads are laid. The bridge's
`road` with `course` does the same, which is how it was tried. The road colliders are installed on every
install for it. **The panel's own controls and an install with no `developer` file have not been run.**

**Unfinished: no save remembers which circuits are laid, and a road grips as the ground does.** A circuit is drawn
with `Ksa/RoadEditor.cs`, switched on from the car's panel on a developer's install, or laid from the bridge. Switched on over roads
that are already laid, the editor edits that circuit: starting an empty one would take them up.
`docs/ROAD-TESTING-PLAN.md` lists the faults known in it and the order they are to be fixed in. **The editor's clicks and drags have not
been tried in game**; its camera and a raised road have, through the bridge.

**The editor's camera is KSA's fixed mode, written every frame.** `Sim/GodView.cs` is a target on the
ground, a heading, a tilt and a distance in the body's own frame; `RoadEditor.View` turns it into the
offset from the followed car and the direction `FixedController` wants, so the view stays over its place
while the body turns, and puts the player's camera mode back when the editor closes. The tilt stops short
of straight down, where that controller has no up to roll about.

**A point has a height above the ground, and a road climbs between them as steadily as they do.**
`Sim/RoadProfile.cs` puts a curve through the heights of a run's points that is a straight ramp where
they are in a line, rounds a crest or a dip and never goes above or below the two points it is between.
`Sim/RoadGround.cs` puts that on the ground along the road smoothed over the circuit's `GroundSmoothM`,
30 m unless set: the highest ground across the road at each sample, so the road is filled under and
never has the ground through it, since KSA's terrain cannot be cut. A road across a hillside is level
across and stands on fill on its low side. **A bank and a width are set at each end of a road**: the
editor's panel has a width and a lean for the point selected, which set them for every road at it, a road
keeping its lean as its left edge up from its first point to its second, so the one that runs the other
way through the point keeps the opposite; at a junction it has the corners' radius in the lean's place,
a junction being one plane.

**One surface a run, which the wheels ask and the triangles are made from.** `Sim/RoadRibbon.cs` is a run's
centre line on a flat chart of the circuit (`Sim/RoadChart.cs`), measured by its own length
(`Sim/RoadLine.cs`), with the profile along it. Past the asphalt's edge a verge falls at 1 in 15 for
1.5 m and an embankment at 1 in 2 from there to 0.3 m under the ground; where an edge is more than 4 m
above the ground the road is a deck with nothing beside it, and a dip under that height shorter than
20 m between two decks is a deck too. Past an end that is not a deck the whole section carries on ahead,
sunk by the same fall; an end at a junction stops square at its mouth.
`RoadRibbon.TooTight` says where a bend's radius is under 1.25 times the half width and the verge,
inside which the surface is close to folding over itself; nothing stops a circuit being drawn so, but
the editor says so. The surface's slope has no step on the centre line; off it there is
a crease of a few hundredths of a degree at a point where a climbing road's curvature steps, the centre
line being cubic curves that share a heading there and not a bend.

**A wheel over a road is sprung against the road, as one over a launch pad is against the pad.**
`Sim/RoadLaying.cs` builds a `Sim/RoadSurface.cs` over the ribbons, and
`Sim/WheelGround.cs` takes a hub's height as the least of its height over the terrain, a pad and a road.
A wheel is over the ribbon's own surface at the place under it, found by Newton from the nearest of the
places kept every 2 m along each ribbon, and not over a piece of anything sampled from it. A road stands
a hand above the ground, and the verge is what brings a wheel up to it instead of a step; beside a raised
road the wheel is on its embankment where that is under it and on the grass where it is not. Where roads
are over one another a wheel is on the highest
one no more than half a metre above its hub, which is how thick a deck is: a hub further down
than that is below it, under a bridge. Asphalt is answered before a verge or a bank no more than a
metre above it, because the bank of a road up a hillside comes down across the road below it. A wheel that was in a road last step, which
`Buggies.Entry.RoadOver` remembers for as long as the roads are not laid again, is still on that road
however deep, and is never put on one more than half a metre above the surface it was in.
`RoadSurface.TryLocate` also answers the way a laid road's asphalt faces; the wheels do not take it and
keep the plane through what is under all four, with which every lap in the suite is on its springs.
Inside a junction's polygon a wheel is on its plane, with the plane's own facing, and between its mouths on
its verge or bank at the nearest place of its edge; a run answers up to its mouth and no further.
`RoadSurfaceSweepTests` lays bends, junctions and bridges as the game does and asks what a wheel would.
**The lookup has not been driven in game.**

**Everything but a wheel meets a road through KSA's own physics, as the triangles it is drawn with.**
`Sim/RoadCollider.cs` makes one solid of the meshes of all the runs and junctions that are joined, and
`Ksa/RoadColliders.cs` puts it in every physics bubble within three kilometres of any of it as ground clutter of infinite mass, which is what KSA lets a
craft and a kitten collide with and count as ground. So a car that meets a ramp harder than its springs
can carry comes down on its hull, as it does on the ground, and under time warp, where the springs are
left out, it rests on its hull on the road.

**A car with a wheel on a road has the physics engine's look-ahead held short.** KSA gives a vehicle a
speculative margin as long as its step, and at speed over a mesh the engine takes a triangle ahead of the
hull for a wall and cuts the car's speed to the gap over the step: 10 m/s off 25, in one step, with the
hull 14 cm clear. `Buggies.Physics` works out a margin for each car, 5 cm and what it closes on the road
with while a wheel is on a road and the engine's own otherwise, and `RoadColliders` writes it onto the
car's body from its prefix on the collision pass. `tools/bepurig` flies a hull's box along a circuit in
the engine alone and counts them, and `Buggies` logs a `jolt` whenever the engine or the drive changes a
car's speed by 3 m/s in a step, saying which. `docs/KSA-MODDING-NOTES.md` has the mechanism.

**One solid for all that is joined, because a hull sliding from one mesh onto the next is stopped or thrown.** KSA's
physics smooths a contact at an edge inside one mesh and not where two meet: in a test of the physics
alone two meshes that only abutted stopped a sliding hull dead, and in game an F2004 left the road at
55 m/s where two met. So the triangles of every run and junction `RoadLaying.Components` finds joined are all
about one origin, where a float is exact to a tenth of a millimetre 2 km out and half a millimetre 5 km out,
and each corner is narrowed from where it was worked out, so the triangles of two drawn meshes share their
corners on the row they meet at, and a junction's with each run's at its mouth. A circuit that is all one
piece is one solid however long: a ladder of 23 km of road and 38 junctions is 52,552 triangles reaching
5 km from its origin, and nothing cuts it. A triangle is solid from
one side, and `RoadCollider` lists each so that is the side it is drawn from. A road on the ground is a
sheet with its banks' feet under the ground and nothing beneath it, which stops what falls on it and,
unlike a box, never pushes a hull that is inside it further down; a raised road is a deck half a metre
thick, closed all round. Where a junction could not be made two runs still only lie over one another. A road laid again has its
solids built again; the ones from before leave the physics once no bubble holds one. The editor lays a
road's solids when a drag ends, not while it lasts, and until then the ones from before stay where they
were. **A laid road's solids have not been seen in game**; one box under a parked car has.
`docs/KSA-MODDING-NOTES.md` has how a solid is made to collide and how the clutter mask is addressed.

**A circuit is a graph of points, and a road is a curve between two.** `Sim/Circuit.cs` keeps a point as a
latitude, a longitude and a height above the ground, so a road follows whatever ground is there. The file is versioned: one from a newer build is refused and an older one loads with what it does not say at its default.

**A circuit's file is one place and metres from it, so the same circuit can be laid anywhere.** Since
version 3 a file has `at`, the latitude and longitude of its first point, the body's `radius_m`, and every
point and handle as `east_m` and `north_m` from that place on a stereographic chart about it, to the
millimetre; `Circuit.MovedTo` is the same circuit with its first point somewhere else, on that body or
another, turned by a heading. In the game a circuit is still latitudes and longitudes, and a file of
version 1 or 2, which has those a point, is read as before and written as version 3 when it is next
saved. The bridge's `road` with `circuit`, `lat`, `lon` and `heading` lays one elsewhere and `save_as`
keeps it; the editor's Load at the craft does it by hand, **which has not been tried**. The file is JSON
with a point a line and what is at its default left out, 55% of what it was: all 26 circuits of the
library are 55 KB where they were 101. A packed binary of the same would be 10 KB, and nobody could read,
compare or mend one, for 45 KB. A file's `route` and `jumps` are kept through a save, and `jumps` is where
the line to drive is not red for a crest. A point is set no lower than the ground, because KSA's terrain cannot be cut into; one below it in an old file is kept as written. Each end of a road has a
handle, as a vector shape's has: worked out by `Sim/RoadLayout.cs` from the roads meeting at the point and
scaled by the point's corner strength, or set by hand as another place on the body.

**A driver follows a route round a circuit, the same one in the tests and in the game.** `Sim/Route.cs`
is a line along the roads with progress along it, never the nearest road, so a junction or a crossing
cannot put it on the wrong one, and across a junction a curve from the middle of one mouth to the middle of
another that reaches in only as far as keeps it 2.5 m clear of the corners; `Sim/Autopilot.cs` steers by pure pursuit through the lock
`BuggyDrive.SteerLock` gives, holds a speed the bends allow, and ends itself with a reason. `Ksa/Laps.cs`
steps it in the physics window for the bridge's `lap`, which stands the car at the route's start first,
square on the asphalt's own face so a banked start does not tip it, and files a summary of the run.
A route's sample carries the road's slope as a tangent, the bend of its climb and its lean across; a bend
that leans out of the turn is taken with the grip the lean leaves, and one that leans in with no more
than the springs carry of what the turn presses on them. Pitch, roll and a wheel's clearance are
measured against the asphalt's face, so a slope or a bank is not read as a car in the air.

**A lap can be driven for the watch.** `Route.RacingLine` moves the route across the road to where it
turns least, keeping `Autopilot.RaceInside` of asphalt outside the car, by relaxing the line's bend with
its points far apart and then closer; a bend is then taken faster than its own radius allows, since the
speeds come from the line's bends and not the road's. `Autopilot.Push` takes the shares the driver leaves
spare, of grip, brakes, crests and dips, from their cautious values towards nearly all there is, and
`JumpZones` are stretches of the route where a crest is not slowed for, for a jump that is meant; in the
air the wheels are held straight. The bridge's `lap` takes `racing`, `push` and `jump_zones`, and
`run-laps.py --racing --push 0.4 --crew` reads a circuit's zones from its file's `jumps`.
**A lap time is searched for, not tuned by hand.** Every share and margin the driver has is a setting of
`Autopilot.Tuning`, each at the constant of its name unless a lap is given another, with a `Pace` beside
them: a multiplier on what each bend allows, at 24 places round the lap, so the lap is learnt corner by
corner and not held all round to what its hardest corner bears; and the racing line can be nudged
to the left or the right at 24 places (`Route.Along`'s `nudgesM`, the bridge's `line`), still on the road. `tools/roads/personal-best.py <circuit>`
tries 28 ways of driving a round in the rig (`LapSearchTests`), near the best so far, and keeps a way
only if its flying lap is clean at four kinds of step and again with 6% less grip than the driver takes
there to be, since a lap that needs all the grip is not one the game gives twice; the best and every
round are in `tools/roads/pb/`. `run-laps.py --best` drives it in the game through the bridge's `lap`
with `tune` and `inside`. The brake is pressed as hard as the throttle is negative, which a key makes
all of it.

**The line to drive can be drawn on the road, as a racing game draws it.** `Sim/RaceLine.cs` lays an
arrowhead every 3 m along the racing line the route driver would take in that car, each corner of it on
the road's surface and 4 cm over it, and colours the 210 m ahead of the car by its speed now against the
speed the driver's plan allows at each, braking for what comes after counted in: blue under it, then
yellow, orange and red by a fifth over. `Ksa/RacingLine.cs` writes a stretch of them to the graphics card
once, the whole of a lap under 3 km, and every frame tells each of four flat materials which runs of the
indices to draw (`RuntimeMesh.Draws`), since writing a mesh waits for the graphics card, up to 33 ms when
it was done eight times a second. Each colour has six draws, a run past them is left out, and the roads
keep to 24 fewer draws a view while the line is drawn. It is switched from the panel's Racing line, on a
developer's install, or the bridge's `road` with `racing_line`. The plan is the cautious driver's with
`Push` 0.3, and takes a crest for one to be jumped where the circuit's file has it in `jumps`. It is not lit of its own, so at night it is as dark as the road outside the headlamps.

`tools/roads/club-motorsports.py` writes a real course, Club Motorsports in New Hampshire: its centre line
from OpenStreetMap, its heights from the USGS's lidar and how far its asphalt reaches either side measured
on the USGS's aerial pictures, in `club-motorsports.csv` with where each is from, as 107 points each in the
middle of the asphalt and as high above level ground as the real track is above its lowest place, 59.5 m,
so the lap climbs as the real one does, on fill and decks; and the same line flat on the ground. The road
is as wide at each point as the asphalt is there, 12 m to 40 m, a bend's paved runoff counted in; beside a
straight nothing past 8 m is, since what is wide there is the pit lane.

`tools/roads/extreme-circuits.py` writes `Insane`, a 6 km lap drawn with its `Lap` class by driving it:
straights, arcs and climbs, each point with its height, lean and width.

**The driver keeps a car on its springs, because the game's colliders are what a hard landing meets.** Over
a crest it holds the speed at which `Autopilot.CrestShare` of the weight is thrown off, and through a dip
the one that pushes the springs `DipShare` of the way to their stops, from the bend of the climb
`Route.Sample.Vertical` carries; the bridge's `lap` with `jump=true` leaves both out. A bend is taken at
no more than `TipShare` of what would lift the inside wheels, from the track and the height of the centre
of mass the hubs give it, which binds on none of the three as they ship. The last 40 m of a road are
braked at 0.3 g and the car is stopped on half its brakes, since a pad collider is 18 cm off the road and
the buggy's nose goes down 10 under all of them. **None of this has been lapped in game.**

**A road is drawn as its own surface, in meshes made while the game runs.** `Roads.Lay` has
`Sim/RoadTessellation.cs` cut each run into meshes of no more than 2,048 vertices and 400 m and make each
junction a mesh of a few hundred, which `RoadTessellation.Gather` puts several to a place where they are
within 400 m of one another, and
`Ksa/RoadDrawing.cs` writes each into a place of its own in a pool of `Ksa/RuntimeMesh.cs`'s and hands it
to KSA's renderer for each view, turned as the body is about the mesh's own origin: a run's own
asphalt in one material, a junction's asphalt and a deck's sides in a second and the verges and
embankments in a third, and kerbs and barriers in a fourth, so four draws a mesh at most. The first runs with the road: across the picture
is across the road from edge to edge whatever its width and down it is 12 m along
(`RoadTessellation.Lined`), so it carries a white line inside each edge, a dash down the middle and the
darker wear of tyres either side of it; a kink counts as the length of its outside edge, and a closed
run's dashes are stretched to come out whole. That picture is 1024 texels across and 128 along, because
KSA filters a part's texture with no regard for the angle it is seen at, and a square one loses its lines
at thirty metres. The other two have no way along them and tile by where a vertex is on the chart.
`tools/model/road-textures.py` writes all four materials' textures.

**A bend has kerbs and a deck has barriers.** Round a bend tighter than `RoadTessellation.KerbRadiusM`,
150 m, the verge either side of a road on the ground is drawn as a kerb, red and white 0.75 m each: the
verge's own triangles in the trim's material, so a kerb is nothing new to drive over. Along both edges of
a deck stands a barrier 0.9 m high and 0.2 m thick, outside the edge, so the road is as wide as it was;
it is in the solid the physics is given, and stops a car leaving a bridge. It is built in straight
panels of 3 m or more and not a row at a time: a row a panel was 104,000 triangles and 78 meshes for the
6 km parcours, more than the pool's 56 places, where panels are 69,000 and 44. A junction that is a deck
has none between its mouths. Both are the last of a mesh's triangles, `RoadMeshData.TrimIndices`.

**A car rubs along a barrier by a push of the mod's own, not by KSA's friction.** KSA has one friction for
all a craft touches, the ground's, and a car leaning on the solid wall with it was stopped from 25 m/s to
3 in 150 m. `Sim/Barrier.cs` holds each side of a car 10 cm short of the solid wall: it takes away the
speed the side has into the wall and rubs the car along it by 0.35 of that push, steel on a rail.
`RoadSurface.TryBarrier` says how far past a deck's edge a place is, and `Buggies.Physics` asks it for the
side of each wheel, low and high, because the wall is plumb and a car on a banked deck is not; the way
out is taken across the car's own floor, since the level way out of a banked deck has a share of the
car's up, along which the springs push every step. The solid wall is what is left for a kitten, a rock
and a car under time warp. A barrier's end is still blunt, and a car that meets one head on is stopped
by it. KSA never gives back room in its mesh
buffers, so the pool grows eight places at a time to 56, a quarter of what those buffers hold for the
whole game, and a place whose mesh is gone is kept for the next. That is 7 km of hairpins or 20 km of
fast road: the circuits of `ExtremeCircuits.cs` take two to nine places each, the grid with its five junctions thirteen, and a 5 km oval
fourteen. A run stops at every junction and its last mesh is as short as what is left of it, so a circuit of
many junctions takes more: the ladder of 23 km and 38 junctions is 99 meshes of road and 19 of junctions. A circuit with more than the pool holds has the meshes nearest the eye drawn, chosen again every two seconds, and
says so once. KSA has 256 draws a view and throws past them from inside its render, which closes the
game; 56 places are 224 at most, and `Sim/RoadDrawList.cs` holds a view to 200 whatever the pool is,
less what the line to drive takes.

**A mesh is written from the frame hook, never from the render hook, and all of a laying in one
submission**, because each submission waits for the graphics card. While a road is dragged the wheels'
surface is laid again every frame, its ground read between the points of a 2 m lattice of heights kept for
the drag and not from KSA each time: a circuit of a kilometre or two is 3,900 accurate reads a laying,
40 ms of every frame, and off the lattice it is 4. The bridge's `road` with `drag_node` times it. The
roads laid when the drag ends are read from KSA's own. A drag at a point where several roads meet can
change which of them carry on through it, and so which runs there are: a run that is new is made
whoever's point was dragged, and the mesh of one that is a run no longer is taken away, where it used to
stay drawn beside the new ones until the drag ended. Made again while it lasts are the meshes of the runs through the dragged point and the points
its roads go to, and of the junctions those runs stop at, ten times a second at most and for no more than a quarter of the time, a long run taking
60 ms to make; the rest, and the physics, when the drag ends, when everything is made again. A road that cannot be drawn is still driven on and still solid: if the render
hook throws, drawing stops until roads are next laid whole and nothing else is touched. The bridge's
`road` says in its reply what was drawn, how many junctions were made and at which points none could be and
why, what room the pool and KSA's buffers have left, what the physics was given and what went wrong, and
`Roads` logs the same on every whole laying. **No road's mesh has been
seen in game.**

**The triangles keep to the surface by where their rows are put.** `Sim/RoadTessellation.cs`
puts a row of vertices across a ribbon at stations along it: no further apart than keeps every triangle
within 5 mm of the surface to the side and 3 mm in height and turns the road's edge or its facing a
degree, no closer than half a metre, and 10 m apart on a straight, level road. A row is eleven vertices
on the ground, the bank's foot to the bank's foot with each crease given twice, and nine round a deck,
which is closed underneath and at its ends, and where a bank on the ground ends under a deck its end is a wall hung from the road's own outline, strip by strip, so that none of it is above the asphalt where the road leans; round a kink the rows fan about the point, and what folds on
its inside is left out. Each mesh is in the body's own frame about an origin of its own, so a float holds
it. A fast circuit has about half the rows a point every 2 m gives; one of 12 m hairpins has two and a
half times as many, the degree at its inside edge asking for less than the half metre.

**A mesh made while the game runs is drawn by KSA's own renderer.** `Ksa/RuntimeMesh.cs` takes
room once in the buffers every static mesh shares, which KSA never gives back, cuts it into meshes and
writes each over in place. The bridge's `road` with `mesh_test` draws `Sim/MeshPatch.cs`'s curved square
over the flown craft and writes another over it when asked again, **which has been seen in game**: lit,
textured, smoothly shaded and casting its shadow. `docs/KSA-MODDING-NOTES.md` has the mechanism.

**A road that goes through a point is one run, and where three or more meet every one stops at a junction.**
`RoadLayout.Runs` joins the roads that carry on through one another into one run whatever their widths,
each easing to the next one's, and a ring into a closed one. Two roads at a point are always one run, however
sharply they meet: a kink is a fan of rows in the run's own mesh, and a junction of two arms would be the same
asphalt with two seams in it.

**A junction is one polygon of asphalt under one tilted plane.** `Sim/RoadJunction.cs` sorts the roads at a
point by bearing. Between each two it finds where their facing edges cross, as the curves they are, and
rounds the corner by the point's `JunctionRadiusM`, 6 m unless set, or by as much of it as fits; each road's
mouth is half a metre past the further of its two corners, square to its own centre line, and no further
from the point than 0.45 of the road's length or six of its widths. Two roads under 20 degrees apart are
joined straight across from mouth to mouth. Where nothing crosses, on the far side of a T or where a narrow
road's edge runs into a wide road's end, each edge is followed in to its foot beside the point and the two
joined there. The plane is the through road's climb along it and its bank across it, so the through road is
the ramp it would have been; at a crossroads each road's climb is the other's lean; and where nothing goes
through it is the least squares of every arm's own climb, held to 15%. It is raised until no part of the
polygon is under the ground, so on a hillside a level junction stands on fill and the through road rises to
it. `RoadLaying.Laid` stops each run at its mouth and holds it there to the plane's height, its climb along
the run and its lean across it, and the lean's rate to what a bend on a tilted plane has, so height and
facing have no step at a mouth: 1e-15 m and 1e-16 rad over 123 junctions drawn at random. Past the asphalt between two mouths are the same verge and embankment a road has, along spokes out
from the edge that fan round a corner turning outwards and are cut short, where two edges face one another,
at the distance the other is as near; a junction with any of its edge more than 4 m up is a deck with sides
and an underside, and each of its runs is a deck where it meets it. **No junction has been seen in game.**

**Roads too nearly alongside to part make no junction, and say so.** If two edges have not crossed within
reach of the point, or the polygon crosses itself, `RoadLaying.Network.Refused` has the point and the
reason, `Roads.Refused` keeps it for the editor, `Roads` logs it and the bridge's `road` replies with it; the through road is then one run as with no
junction and any other runs to the point and ends there, the two lying over one another in one plane.
The editor says it and marks the point.

**What is wrong with a circuit is said where it is drawn.** `Sim/RoadWarnings.cs` reads a laid circuit
for a junction that could not be made, a road too short for its junction's corners, a bend tighter than
its width turns in, and a grade past what the cars were driven up: 45% for the Eldorado from rest, 60%
for the buggy with a run at it and 85% for any car. `Roads.Warnings` keeps them from each whole laying,
and the editor lists them in its panel and rings each in red on the ground. None stops a road being drawn.
**The panel's sliders and its warnings have not been seen in game**: the bridge's capture does not take
the game's own windows.

**A loop is a piece of its own, which only a car's wheels meet.** A road is a height over a chart of the
ground, and a loop is upright and then upside down over itself, so `Sim/RoadLoop.cs` is a surface of its
own in one upright plane: 110 m of road round for one 30 m high, bending as a sine squared along it,
nothing where it leaves the ground and 4 pi over its length at the top, and carried to the left on the way
so the way out clears the way in. A circuit has `loops`, each from the point one road ends at to the
point another starts at, with its length; the second point is to be `RoadLoop.Reach` ahead of the first
and to its left by more than the road's width, and `RoadLoop.Of` stands the loop on the first road's end
as that road arrives. The file is version 4 for it. `WheelGround.Read` takes the loops, and a wheel on
one is sprung along the loop's own facing under that wheel and not along one plane through all four: a
loop turns a seventh of a radian between a car's axles, and with one plane the nose closed on the road
at that share of the car's speed while its dampers saw it leaving. A loop presses a car at four to six
times its weight whatever its size, since the speed it takes grows as the loop does, and that is 8 cm of
the F2004's springs, which have 2: `BuggyDrive.BumpStops` holds each hub at the end of its travel, as a
wall holds a car's side (`Barrier.Hold`, with nothing rubbing), in `Buggies.Physics` while a wheel is on a
loop. **On a loop the mod steers**, by `RoadLoop.Steer` towards a place on its middle a little ahead, and
the keys are the throttle's: it is carried aside as it goes round. `Roads` draws it as a road is drawn,
with its lines, in a place of the pool. **It is not solid**: a hull's box is longer than a loop's bend
lets lie in it, so nothing is given to the physics, and a kitten, a rock and a car off its wheels go
through it. It has no barrier, and the editor has no control for one: the bridge's `road` with `loop` lays a road to one and
a road on from it. `RoadLoopTests` drives every car round in the rig at 25 and 30 m/s at 60 and 50 steps
a second: four wheels down all the way, 2 cm past the stops at most, and out straight on the loop's own
line; at 15 m/s a car comes off the top, and at 40 m/s and 30 steps a second the F2004 does.

**A route goes round a loop, and the driver goes into one at the speed that carries it over.** `Route.Of`
makes the roads before a loop one stretch, the loop another and the roads after it a third, each road
stretch a line of its own, and joins them; with no points named it follows the road to a loop's foot,
round, and on by the road the loop comes down on. A sample on a loop has `LoopM`, the loop's length: its
place is on the loop, its tangent the way the loop really runs, and progress along it is found in three
dimensions and not from above, where half a loop is no length. `Autopilot.LoopSpeed` is 1.12 of the least
that still presses a car to the road at the top, 29 m/s for 110 m of loop, and that is what a loop's
samples allow, so the straight before it is braked down to it; on a loop the driver does not brake, is not
slowed for facing the wrong way, and is not taken for flipped or off the road, nor for a car's length
either side of it. `RaceLine` puts its arrowheads on a loop by the route's own run there. The rig's laps
with a loop in them finish with four wheels down all the way round, the F2004 and the Eldorado, open and
closed; the buggy's top speed is under what this loop takes.

**Grass, trees and rocks under a road are switched off, not removed.** They are all KSA's ground clutter,
which keeps a bit per instance in each cell of a grid on the body; `Sim/ClutterGrid.cs` finds the instances
near a road, `RoadSurface.Over` says which of them the roads' own outline covers, as wide as each road is
there, the shape each junction is and out to a bank's foot, and `Roads.ClearClutter` clears their bits.
Under a deck the ground is still ground: the grass stays and everything else goes, since KSA's shrubs
are small trees and stand through a deck two metres up as a tree does. Where an
instance is reckoned to stand is KSA's generation shader worked again by hand, so once a body
`Roads.CheckPlacement` compares that with KSA's own instances and warns in the log if they have parted. What is
within the margin of a road all round it goes too, and anything but grass within twice the margin, since what
stands beside a deck comes up past its edge.
**All of this is to be replaced by KSA's own exclusion the day it has one**, which its developers are
considering: `docs/BLOCKED-ON-KSA.md` has what goes. A cleared bit stops a
collider being built but leaves one already standing, unseen, so every change has KSA build its clutter
colliders again, by the setting held off for a moment as a change of rock weight does. **The masks are
only written from the physics window**, by `Roads.SyncClutter`: KSA's physics workers read them while they
run, KSA writes them only while the workers are parked, and a write from the GUI pass races them. The bridge's
`road` with `probe_clutter` measures the reckoned positions against KSA's own. KSA writes those bits into a save, so a game saved with a road laid keeps the clearing.

## Adding a car

A car is **data plus art**: nothing in the drive, the sound or the hands names a particular car.

1. **Model it** in Blender over MCP, per `.claude/skills/ksa-blender/SKILL.md`: a body, one wheel mesh
   per kind of wheel, a steering wheel, and glass if it has any, each its own subpart, recentred on
   its pivot and exported in part space (+X up, +Y forward, +Z the car's left). Run
   `tools/model/checkmesh.py --near-max 0` on the export. **A human signs off on the geometry before
   it is unwrapped**, because everything after is welded to the shape.
2. **Declare it** in `KSACarsAssets.xml` and `KSACarsGameData.xml`: the part, its subparts with Ids
   ending `<Prefix>WheelFL`, `WheelFR`, `WheelRL`, `WheelRR` and `Steering` (and `Arm…` and `Coil…` if
   it has visible suspension, `Upright…` for what sits inside a rim and must not spin), its seats, doors, colliders, light switch and mass — a box the car's size, not a
   sphere. **A shipped part's subpart list is append-only and its Id is not renameable**: KSA pairs a
   saved part with its definition positionally and by Id, and a save that no longer matches closes
   the game. `docs/KSA-MODDING-NOTES.md` has the loop.
3. **Give it a `BuggyProfile`** in `Sim/BuggyProfile.cs` and add it to `All`: the hubs, the steering
   wheel's pivot and axis, the driver's eye, the head and tail lamps, the rocket nozzles if it flies, and the tuning. Add tests in `BuggyDriveTests` that it
   settles, pulls away and shrugs off a yaw kick, and one in `SteeringGripTests` that the driver
   reaches the rim; `HeadlightsTests` covers every car in `All`.
4. **Give it an engine**: four sounds named `<SoundPrefix>Start`, `Idle`, `Load` and `Stop` in
   `KSACarsSounds.xml`, cut by a script in `tools/` from recordings kept in `tools/audio/` with their
   licence. `LoadRecordedRpm` and `IdleRecordedRpm` say what RPM each loop was recorded at.
5. **Put it in `tools/install-testcraft.sh`**, then drive it: `./tools/deploy.sh`, launch, and the
   bridge's `spawn` and `drive`.

## CI and releases

Building needs KSA's own assemblies. They are RocketWerkz's copyrighted files and **must never be
committed here or published anywhere**. They live in the private repository
**`LaurensDeV/ksa-game-assemblies`**, checked out by CI with a **read-only deploy key** held in the
`KSA_ASSEMBLIES_KEY` secret. Without the secret — a fork — the build job skips with a notice.

`Directory.Build.props` resolves the folder in tiers, first match wins: `KSA_DLL_DIR` (what CI
sets), then `Import/`, then a sibling `ksa-game-assemblies` checkout, then the game install.

- **`tooling` (hosted, always runs)** — `tools/check-all.sh`: shellcheck, the Sim/Ksa boundary, the
  XML, the asset references, the comment and doc rules, the meshes, and no tracked artefacts.
- **`build` (hosted)** — the build, the tests, `validate-parts.py`, the API surface and the package,
  against the checked-out assemblies.

**Work happens on `dev`; `main` is the release branch.** Merge `dev` into `main` to release, and
**merge, do not squash** — semantic-release reads the individual commits to build the changelog.
It runs on every push to `main`: version, `CHANGELOG.md`, the `<Version>` in the csproj (via
`tools/set-version.sh`), the tag and a **draft** GitHub Release; the second job builds the archive,
attaches it and publishes, so no release is public without its archive. **Never edit a version by
hand.** The changelog is written for players, so only `feat`, `fix`, `perf` and `build` appear in it.

`spacedock.yml` publishes what is attached to SpaceDock, and skips with a notice unless
`SPACEDOCK_MOD_ID` and `SPACEDOCK_USERNAME` (repository variables) and `SPACEDOCK_PASSWORD` (a secret)
are all set. It claims compatibility with the build in `ksa-assemblies.lock`. SpaceDock refuses a
changelog over 10,000 characters, and the archive with it, so `tools/spacedock-changelog.py` cuts one
that does not fit.

Three things that will bite: **branch protection on `main`** blocks the release commit unless the
token can bypass it; **a shallow checkout** makes every push look like a first release, hence
`fetch-depth: 0`; and **semantic-release carries on from the newest tag it can see**, so the first
release needs one.

**One archive covers Windows and Linux**: the mod is a portable `net10.0` assembly. Case sensitivity
differs, which is why `validate-parts.py --offline` runs on Linux in CI against the real directory
listing. Release builds carry **no debug symbols**, and the log starts at `INFO`. `package.sh` refuses
to ship a `.pdb` or any DLL that is not ours.

### After a KSA update

**You will be told when this happens.** `./tools/check-ksa-version.sh` compares RocketWerkz's
published build to the lock, and `ksa-version.yml` does the same daily and opens an issue.
`./tools/build.sh` checks the install against the lock on every build.

The assemblies exist in two places that drift apart silently: your `Import/`, and the private repo CI
compiles against. `ksa-assemblies.lock` records the expected SHA-256 of each referenced assembly plus
the game build, and both CI and `sync-import.sh` check against it. **Run the `upgrade-ksa` skill**,
which is this written out with the reasoning attached:

```bash
./tools/sync-import.sh                                    # refresh Import/; it reports the drift
./tools/sync-assemblies.sh      ../ksa-game-assemblies    # the mirror's DLLs
./tools/decompile-assemblies.sh ../ksa-game-assemblies    # the mirror's sources
#   set current/KSA_BUILD, commit BOTH together, push there
./tools/ksa-api-diff.sh ../ksa-game-assemblies      # what actually broke — read this
./tools/check-assemblies.sh --update                # record the new digests
./tools/api-surface.sh                              # the surface moves if the fixes did
#   edit the `build` line in ksa-assemblies.lock, commit it here
```

Then **recheck `docs/BLOCKED-ON-KSA.md`**: a KSA update is the only thing that changes any of it.

**The compiler only finds half of it.** A member that keeps its name and signature and changes its
*meaning* compiles clean and is wrong in game; the decompiled corpus and `ksa-api-diff.sh` are for
that half.

The build number is written in seven places: `ksa-assemblies.lock`, `current/KSA_BUILD` in the
private repo, and the **KSA build** line under Environment above, `README.md`,
`docs/KSA-MODDING-NOTES.md`, `docs/KSA-CAMERAS.md` and `docs/BLOCKED-ON-KSA.md`. The lock is the
source of truth and `check-docs.sh` fails on any prose file that disagrees with it.

## Testing

`tests/KSACars.Tests` drives the cars headlessly: a rig steps `BuggyDrive` against flat ground with the
engine's own mass and inertia for each car. `BuggyDriveTests` covers settling, pulling away, turning,
braking, a yaw kick dying away, the steering lock held to the grip, no car rocking on its springs at any
step from 1/60 to a tenth of a second, and the F2004's times to speed, its braking and its cornering; `SteeringGripTests` that the
driver reaches the rim of each car's wheel. `RoadSurfaceSweepTests` sweeps laid roads over legs, turns, corner
strengths, spacings, widths and heights: every point of a road finds it, a level road is level round every
bend, a climbing bend has no step, a junction onto a climbing road and a deck over a road answer the
right one. `RoadJunctionTests` lays a T, a Y, a crossroads, a five-way, a narrow road on a wide one, a fork of
15 degrees, two junctions 24 m apart, the grid and 150 junctions of three to six roads drawn at random over
rolling ground: every place is on one surface and no more, a road is on the plane at its mouth in height and
in facing, a wheel walked from every arm to every other meets no step, the asphalt's triangles are whole and
each mouth's edge is the junction's triangle's and the run's in one solid, a raised junction's solid is
closed and one on the ground is one sheet with no hole; and roads too nearly alongside are refused with the
reason. `KSACARS_JUNCTIONS=<file> ./tools/test.sh` writes what it measured. `RoadLineTests`, `RoadProfileTests` and `RoadRibbonTests` hold the surface to
what it is said to be: a chart that keeps angles, a length that is the curve's own, a steady climb with
nothing to feel at 80 m/s, ground that is never through the road, a facing that is the surface's own with
no jump, and a place on it found again from where it is. `RoadTessellationTests` holds the triangles to the surface:
whole, facing out, no hole or seam in the asphalt across a mesh's border or round a closed run, a deck
closed all round, and no triangle further from the surface than it is allowed. `RoadDrawListTests` holds
what the game does with them: every mesh fits its place in the pool, the meshes drawn and the draws in a
view are the nearest, and a run's solid is every triangle of its meshes, solid from the side it is drawn
from, with no edge left open where two meshes meet.

`TrackRig` is the same car as a free body on a sphere with ground of the test's own, stepped as `Buggies`
steps it: `WheelGround`, `BuggyDrive.Step`, then gravity. On a bare sphere it
is the flat `Rig` to a micron. Its hull is four points, one under each hub at the height the car's lowest
collider ends, stopped dead at the ground or a road: it does not rub, has no nose, belly or roof, and
meets a road's top and never its edge, so a run marks each step the hull was down and how hard it came
down, and what a car does after that is the rig's and not the game's. `RoadBulletTests` fires each car,
undriven, at a level road, ramps, a step in a road, a road's edge, a deck alongside and overhead and a
deck's end, at Earth's gravity and Luna's and at three step lengths, and parks it under a step too long
for the springs. One is skipped: on Luna a car comes down on its hull at the foot of a ramp its springs
carry it up on Earth. `KSACARS_BULLETS=<file> ./tools/test.sh` writes every run out as a table. `AutopilotTests` drives each car
with the route driver round rings at five step lengths, along a bend's edges, through a junction, over a
ramp and a bridge and to a road's end. `ExtremeLapTests` laps every car round the eight circuits of
`tools/roads/extreme-circuits.py`, which `ExtremeCircuits.cs` lays again point for point, at 60 Hz and at
the game's uneven steps: finished, on the asphalt, on its wheels and no spring within 2 cm of where a
collider would touch; and round the grid by its junctions, turning left and right at four and twice straight
over the raised crossroads. `KSACARS_EXTREME_LAPS=<file> ./tools/test.sh` writes them out as a table.
`./tools/roads/rig-laps.sh "Z "` laps every car round the circuit files the game reads, in seconds and
with a CSV of every step for a car named, which is where a circuit is driven before the game is; it
asks nothing of a lap, because a wall no car climbs is a fair thing to draw. `tools/bepurig` is the
game's physics engine alone, for what the rig's stand-in hull cannot show.

**A behaviour change is unverified until it has been seen in game**, whatever the suite says.
`CHECKLIST.md` records what has been driven and what has not. The buggy and the Eldorado were driven
through the bridge before this repository was split out; since the split only the F2004 has been seen in
game, parked and pulling away, before its power, its downforce and the game's drag were changed.
