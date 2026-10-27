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
and yaw keys, like anything else in KSA, and braked with Space.

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
- **KSA build these notes were taken against**: `2026.10.19.5638`
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
- **Nothing is written in the mod's folder, and nothing kept is found by a path of its own.** A Borea
  instance is a profile of its own: StarMap patches `KSA.Constants.DocumentsFolderPath` to answer the
  instance's folder, so everything the mod keeps is found from that, and only what the mod ships is
  read beside its DLL. A path built from `Documents/My Games` reads the shared profile in an instance.
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
./tools/two-games.sh --race                # two copies of the game on this machine, one hosting the other
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
| `Sim/BuggyDrive.cs` | a car's springs, tyres and engine stepped against the ground under each hub, **as one impulse through the centre of mass and one about it**, in as many substeps as a step longer than the car can carry needs, and the poses its wheels, arms and coil-overs are drawn in |
| `docs/F2004-REFERENCES.md` | what the F2004 was modelled from: dimensions, the rules of its year, blueprint stations, the kitten's measurements, and each photograph's author and licence |
| `Sim/GroundPlane.cs` | the ground under a car as one plane through what is under its four wheels, **whose up is what the dampers measure closing speed along**: straight up, a car running down a slope is closing on the ground as far as they know |
| `Sim/WheelGround.cs` | what is under each wheel this step — the nearest of the terrain, a launch pad and a road below its hub — as the contacts the drive is stepped against, **the wheels' alone**: a hull on a road is the road's colliders' to stop |
| `Sim/SteeringGrip.cs` | where a seated driver's hands hold the wheel, in the kitten's own model space — **anchored to its seat**, so where the car is in the world never enters it — and the two-bone elbow that puts a wrist there; past 20 deg of wheel the rim slides through the hands, which a seated kitten's 14.5 cm reach needs |
| `Sim/HeadGear.cs` | where a thing worn on the driver's head is as a subpart of the car, from the matrix KSA carries the kitten's unposed mesh to its head by — **the mesh is in the kitten's own model space, in metres, and that matrix is in centimetres** |
| `Sim/Righting.cs` | the turn and the lift that set a car on its roof or its side back on its wheels, **in the car's own frame** — the shortest turn, so it keeps its heading, and a roll rather than a somersault from flat on the roof; and the turn that stands a car on a road facing along it |
| `Sim/Lift.cs` | four rockets under the car as one push — **thrust along the car's own up, through the centre of mass**, so it is balanced wherever the crew sit, and off the ground a hold that keeps it level or leans it the way the keys ask; and the gas each flame is drawn from |
| `Sim/Barrier.cs` | a car's side against a deck's barrier, as a push of the mod's own: **what speed it has into the wall taken away and a share of that push rubbing it along**, held short of the solid wall, whose friction is the ground's and stops a car that leans on it |
| `Sim/Slipstream.cs` | the still air behind a car: **the share of its drag a car following in it is spared**, most close behind and dead astern and nothing past 50 m or 1.6 m to the side, which lets one of two cars alike draw up to the other on a straight |
| `Sim/Boost.cs` | two rockets on the tail as one push along the car's own forward, through its centre of mass — a fixed acceleration whatever the gravity, which is what moves a rock where the tyres have nothing to push against |
| `Sim/Downforce.cs` | rockets on the bonnet and the boot that press the car onto the ground — a push along its own down through the centre of mass, and **the load the springs are sized off while it is on**, so the car rides at its usual height |
| `Sim/Hover.cs` | the rockets under the car and the ones on top firing together — **thrust that carries the car's weight and brakes any climb or fall**, so it hangs where it is, and what holds it over the ground while the planet turns: the frame's own accelerations taken out and, with no lean asked, its speed across the ground braked |
| `Sim/Hatch.cs` | the iris over each thruster — **where each of its six blades is, how it is turned and how big it is drawn** as it opens, and when its rocket may fire |
| `Sim/Headlights.cs` | the dipped and main beams and the tail lamps, as the spotlights KSA is asked for — range, brightness, cone and dip — with the off/low/high setting and how it is kept in step with the part's light switch |
| `Sim/Gamepad.cs` | a gamepad as a car's controls: the triggers as throttle and brake, the left stick as steer and the right as the pitch and roll keys, **each with its dead centre taken out and the rest stretched to full**, added to the keys, and which buttons went down since the read before |
| `Sim/ChaseView.cs` | a view that stays behind a car, close, medium or far, or none, the four modes the gamepad's Back steps through: the orbit camera's heading eased towards the car's, **led into a corner by how fast the car turns and tilted as the car is on a slope**, a stick or a mouse drag to look round from there, which comes back behind the car once it is let go, and what speed does to it: a wider field of view, further back, lower, and a shake near 65 m/s |
| `Sim/TerrainRay.cs` | where a ray from the eye first goes under the ground, walked out along it — **not the mean sphere's hit**, which lands behind a hill seen side-on |
| `Sim/TerrainMask.cs` | the stretch of a line that lies below a body's highest ground, which is all of it the terrain ray has to walk |
| `Sim/CursorAim.cs` | the cursor, from window pixels to the framebuffer pixels a camera's ray is asked for in |
| `Sim/Picking.cs` | what the pointer is over: the nearest thing on screen within its own reach |
| `Sim/DrawAnchor.cs` | an ecliptic point as an offset from a craft KSA has just placed, so a gizmo lands where the craft is drawn and not where its orbit says |
| `Sim/FrameLatch.cs` | hands a frame's work out once, to whichever hook reaches it first — **the UI pass is skipped while the UI is hidden and the frame postfix is not** |
| `Sim/BridgeCommand.cs` | one command dropped into the bridge's folder, read — **text in**, so every refusal is testable here |
| `Sim/ITerrainHeights.cs` | the seam the ground under a hub is read through |
| `Sim/PadSurface.cs` | the top of a structure standing on the ground, as the first of its boxes and cylinders a line dropped from a hub meets — **the height field knows nothing of a launch pad** |
| `Sim/Circuit.cs` | **a set of roads as it is saved**: points on a body by latitude and longitude, the roads that join them, each point's corner strength and height, any handle set by hand, and a bank, a width and a shift to one side at each end of a road — its edits, each answering a new circuit so the old one is the undo, the same circuit moved to another place or body, and its JSON, **which is one place and every point as metres from it** |
| `Sim/CircuitHistory.cs` | a circuit being edited and every state it has been through, for undo and redo — **a drag is many changes and one step** |
| `Sim/RoadLayout.cs` | a circuit's roads as centre lines: one cubic curve a road, with the handle nobody set worked out from what meets at the point — **two roads go through, a side road at a junction leaves straight** |
| `Sim/GodView.cs` | a view from above that is not tied to a craft: a place on the ground looked at, from how far, from which heading and how steeply, and how it is panned, turned and zoomed |
| `Sim/RoadLaying.cs` | a circuit put on the ground: each run of its roads as a `RoadRibbon`, stopped at the mouth of each `RoadJunction` and brought to its plane there, and as a line of points along that surface, the ground under it read through a function, so a test lays the same roads the game does over ground of its own; and **which runs and junctions are joined**, which the physics is given as one solid |
| `Sim/RoadJunction.cs` | **where three or more roads meet, as one polygon of asphalt under one tilted plane**: each road's mouth, the corners between them rounded, bevelled or joined round the point, the plane that is the through road's climb and lean, what is past the edge between the mouths, what a wheel there is over and the line a driver takes across — or why the roads are too nearly alongside for one to be made |
| `Sim/RoadRibbon.cs` | **a run of roads as one surface**: where it is a distance along and across, the way it faces there, and for any place how far along and across that is — the asphalt leaning by its bank, a verge and an embankment down into the ground past its edge, or a deck with nothing beside it where it is more than 4 m up |
| `Sim/RoadTessellation.cs` | a ribbon as triangles, **the ones the game draws and collides with**: rows of vertices across the road, closer where it bends, climbs or twists, joined into meshes of about 100 m or of as much as a place in the pool holds, with the asphalt, the earth and a deck's sides apart, each vertex facing as the surface does and given twice only at a crease; and a junction as a mesh that **takes each run's last row for its own at the mouth**, several junctions to a mesh where they are near |
| `Sim/RoadLoop.cs` | **a loop, which a height over the ground cannot say**: road that leaves the ground level, goes over upside down and comes back level to one side, its bend nothing at its foot and the most over its top; for a wheel how far off its asphalt it is the way that faces, where a circuit's roads put it, its triangles and the steering that carries a car round it |
| `Sim/RoadSurface.cs` | the roads as something a wheel can be over: its height above the ribbon's own surface under it, or a junction's plane, and the way that faces, **a road more than a deck's half metre overhead being a bridge**, so the road beneath is the one answered, and asphalt answered before any road's verge or bank, and where a pointer's ray comes down on the asphalt |
| `Sim/RoadCollider.cs` | the meshes of runs and junctions that are joined as **the one solid KSA's physics is given for them**: every triangle about one origin, listed so that it is solid from the side it is drawn from, and narrowed from where each vertex was worked out so two meshes' triangles share their corners |
| `Sim/RoadDrawList.cs` | which of a circuit's meshes are drawn: how much one place in the pool holds and how many places there are, **the nearest meshes given the places when there are too few and the nearest drawn when a view has too few draws**, a mesh's triangles in the order of the two materials, and the ledger of which places are out |
| `Sim/RoadWarnings.cs` | what is wrong with a circuit as it is laid, for whoever is drawing it: a junction that could not be made or has a road too short for its corners, a bend tighter than its width turns in, and a grade past what each kind of car climbs |
| `Sim/Route.cs` | a line to drive along and how far along it a car is: the roads' centre lines through a circuit's points in order, a metre a sample, with the bend, the slope and the bend of the climb at each, a curve across each junction from the mouth come in by to the one left by, a kink rounded inside the asphalt and from its outside edge where the car turns too wide for less, and a set distance to one side. **Progress is looked for a little ahead of where it was and nowhere else**, which is what tells a route from the road it crosses |
| `Sim/Autopilot.cs` | a driver that follows a route: pure pursuit from the rear axle, **the angle it wants turned into a steer input through `BuggyDrive.SteerLock`**, a speed held to what each bend, crest and dip allows and braked for in time, and a lap that ends itself with a reason — finished, off the road, flipped, stuck, out of time — and a summary of what it saw. The same driver in the tests and in the game |
| `Sim/RaceBoard.cs` | what a race is to a display of it: how far each car has gone round a closed route, **the checkpoints it has been driven through in order, so a lap cannot be had by cutting across**, the lap and the place in the order that gives, and where a place is on a map that turns with one car |
| `Sim/LapRig.cs` | **a car lapped with nothing of the game**: `TrackRig`, a free body on a sphere stepped as `Buggies` steps a car, with `TrackWorld` its ground and `TrackCar` the car as the engine has it; what the suite drives and what an opponent practises in |
| `Sim/Practice.cs` | an opponent's practice of a course: every bend quicker until a lap goes wrong, then the driver's settings one at a time, then the pace at 24 places, **each way kept only if it is clean and quicker and tried every way a searched lap is**, some 300 laps of the rig |
| `Sim/Rival.cs` | what makes a route driver an opponent: **a driver of its own for a seed**, its pace, what it makes of each of 24 places round the lap and where it puts its car across the road, the same every time; and the speed it holds behind another opponent in its way |
| `Sim/Lanes.cs` | **where across the road an opponent runs and how fast while a player's car is near**: a handful of lanes, each tried at a few speeds and rolled 3 s ahead against every car, the cheapest in time lost, touching and leaving the road taken; an overtake of a player's car slower than the road allows, and a push past one that holds it up |
| `Sim/RaceLine.cs` | the line to drive as arrowheads on the road ahead of a car: where each lies, on the road's own surface, and **its colour from the car's speed now against what the route allows there**, as runs of one colour that are each a draw |
| `Sim/StartGrid.cs` | a course's start as paint on its asphalt: a chequered band between two white lines across the road ahead of the pole, from one edge line to the other, and behind it the boxes of a grid in two columns, **every corner on the road's own surface and a quad white or black by which half of one picture it is in** |
| `Sim/RacePose.cs` | **a car measured against a laid course rather than the planet**: how far along the route it is, where it is and how it is turned and moving in the road's own frame there, so it is put in the same place on another player's copy of the course laid somewhere else |
| `Sim/NetWire.cs` | what two games racing each other say, as UDP datagrams: a mark and a version so anything else is dropped, a hello, a welcome, the roster, a car's state, a ping, a start and a goodbye |
| `Sim/NetSession.cs` | **a race between games over one UDP socket each**: a host that lets in a guest with its build and its mods, hands it a number, a box and its world, says its universe time and the pace it really goes at, passes every car's state on to the rest and starts the race, and a guest that speaks to it alone; read on a thread of its own and handled once a frame, on the caller's clock |
| `Sim/RemoteTrack.cs` | another game's car as the states it has sent, put on this game's clock by a line under the quickest any have come, **which follows a world that runs at another rate**, and drawn 0.1 s in the past between the two either side of then |
| `Sim/WorldTransfer.cs` | **the host's world on its way to a guest**: a save and the laid course in one zip, cut into pieces that go whole in a datagram, sent at 400 KB/s, the missing ones asked for again, and nothing unpacked outside its folder |
| `Sim/ClockLock.cs` | a guest's world kept to the host's universe time by its speed alone: a gap closed over 5 s, never more than a tenth faster, and **the speed asked of KSA to make up a frame longer than its 33 ms step**, never more than 1.1 |
| `Sim/RacePace.cs` | how fast a world really goes, its universe time against the wall, **which KSA's achieved fraction does not see**; and the pace a host holds its race to, under the slowest guest's while one falls behind |
| `Sim/JoinCode.cs` | an IPv4 address and port as ten letters and digits of Crockford's base 32, and back, or a plain `host:port` |
| `Sim/Stun.cs` | a STUN binding request and its answer: **where a socket is seen from outside every router**, asked of a free public server |
| `Sim/Upnp.cs` | a UDP port opened on the home router through UPnP, and the address it is reached at, with what a router answers parsed apart from the asking |
| `Sim/RoadCurve.cs` | the cubic curve a road follows between two points, sampled |
| `Sim/RoadProfile.cs` | what a road is along its length apart from where it goes: **its height as a curve through its points' heights that climbs steadily where they do and never overshoots**, its lean and its width, an end at a junction held to the height, the climb and the lean it is given, with the steepest grade and the tightest crest or dip of each road |
| `Sim/RoadGround.cs` | the ground along a road, **smoothed and never buried**: at or above every sample of the ground and the line between two, with no step in its slope, level ground and a steady slope left as they are and a bump filled over |
| `Sim/RoadChart.cs` | **a flat chart of the ground round a circuit**, in metres east and north, so widths, offsets and lookups are plain geometry; stereographic, so every angle is kept and a length `r` from its centre is `(r/2R)^2` too long |
| `Sim/RoadLine.cs` | a road's centre line on the chart, **measured by its own length**: where it is, its heading and how hard it turns a distance along, whatever speed the curve's parameter goes at, and where it turns tighter than the road on it can |
| `Sim/ClutterGrid.cs` | **where KSA scatters grass, trees and rocks, worked out as its generation shader works it out**, so the ones under a road can be named in KSA's per-cell mask |
| `Sim/MeshPatch.cs` | **an experiment**: a curved square as vertices, normals, UVs and triangles, in a right-handed frame of its own and wound the way KSA draws, for the bridge's one mesh made while the game runs |
| `Sim/Vec.cs` | vector helpers, and `Plan`, a place or a step on a chart |
| **`src/KSACars/Ksa/`** | **everything that binds to the game** |
| `Ksa/KSACarsMod.cs` | StarMap entry point: installs the thirteen patches, and once a frame finds the cars, poses their wheels and plays their engines |
| `Ksa/PhysicsHook.cs` | **one of the thirteen places this mod patches the game** — a prefix on `Vehicle.PrepareWorker`, the only window in which a write to a vehicle's state survives the frame |
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
| `Ksa/Practising.cs` | **practice run beside the game**, for the course that is laid and the car that is to race on it: what the rig needs read in the frame hook, the search on a thread of its own, and what it finds kept under `<KSA user dir>/KSACars/Laps/` |
| `Ksa/RaceHudHook.cs` | the eleventh patch, on a private method — **the racing display drawn with KSA's interface hidden**, a postfix on `Program.DrawFps`, the one call KSA still makes inside its ImGui frame with F2 on; if KSA moves it there is no display |
| `Ksa/WarpHook.cs` | the twelfth patch, on two methods — **every world in an online race held to the race's speed**: prefixes on both of `Universe.SetSimulationSpeed`, which every warp key, auto-warp, the escape menu's pause and the editor go through, replace what is asked with `Multiplayer.HeldSpeed` while anyone has joined |
| `Ksa/ControlHook.cs` | the thirteenth patch, on two members — **nobody takes another racer's car**: a prefix on the setter of `Program.ControlledVehicle`, which clicking a craft, the map view, EVA, boarding and the editor go through, leaves control where it is, and one on `KittenEva.BoardVehicle` turns a kitten away from that car before it climbs in |
| `Ksa/RaceHud.cs` | **the racing display**: the flown car's speed, its lap out of the laps set, its place among the cars on the course and a map of the course about the car with the way it faces at the top, drawn only while KSA's interface is hidden; where every car is along the course is kept from the frame hook |
| `Ksa/RoadColliders.cs` | triangle meshes of the mod's own in KSA's physics, registered as ground clutter of infinite mass in each bubble's simulation, from four prefixes on the physics passes and one private dictionary. Each run of a laid road is one mesh, in the bubbles within three kilometres of any of it, and a road laid again has the meshes from before freed once no bubble holds one; the bridge's `road` with `box_top` puts a box under the flown craft |
| `Ksa/RoadDrawing.cs` | **the roads as they are drawn**: each mesh of a laid circuit written into a place of its own in a pool of `RuntimeMesh`es that only grows, to 56 places, and handed to KSA's renderer for each view in asphalt and in earth, turned as the body is about the mesh's own origin; the nearest drawn where there are more meshes than places |
| `Ksa/RacingLine.cs` | **the line to drive, drawn ahead of the car being flown**, from the panel's Racing line: the route driver's own racing line and speeds for that car, a stretch of arrowheads written once and each colour's material told every frame which of them to draw |
| `Ksa/StartLine.cs` | **the start painted on a laid course**: `StartGrid`'s mesh written once a laying has stood half a second, a dragged road being laid again every frame, and drawn with the roads in one material |
| `Ksa/RuntimeMesh.cs` | a mesh made while the game runs: room taken once in the three buffers KSA's static meshes share, cut into meshes that are each written over in place, any number in one submission, and drawn by KSA's own renderer as a `StaticMeshRenderable` for each material it has. **One patch has been seen in game**, the bridge's `road` with `mesh_test`; a road's meshes have not |
| `Ksa/Roads.cs` | a circuit laid on a body: every run and every junction as one surface each, answered to a wheel asking what is under it, made into the triangles `RoadDrawing` draws and `RoadColliders` gives the physics, a dragged road's meshes written no more than ten times a second, and the clutter under it switched off through KSA's exclusion mask and put back when the road is taken up |
| `Ksa/RoadEditor.cs` | **a circuit drawn on the ground with the mouse**, from the panel's Build roads: a click on the ground carries the road on from the point selected, a click on another point joins them, or with Ctrl held only picks it, a click on a road puts a point in it, and a point, a handle or the knob that sets a point's height is dragged. Its view is its own, panned, turned and zoomed with the mouse; the roads are laid again on every change, and all their meshes and their colliders when a drag ends |
| `Ksa/CircuitLibrary.cs` | the circuits a player has, one JSON file each under `<KSA user dir>/KSACars/Circuits/` — outside any save, which KSA wipes on every write — and the ones the mod ships beside its DLL, read where the player has none of that name |
| `Ksa/Courses.cs` | **a circuit laid where the car being flown is, for a player**: its first point at the car, its first road the way the car faces, and the car stood on its start; asked from the panel, worked out beside the game and laid from the frame hook once that is done |
| `Ksa/RocketSound.cs` | the rockets' roar: KSA's stock engine sound on one channel a car, fed the throttle each frame as KSA feeds an engine's |
| `Ksa/DriverHands.cs` | the driver kitten's hands on the steering wheel — **an `IAnimProcessor` on the seated kitten's model**, the hook KSA turns its eyes with, solving each arm onto the rim after the seated animation and before skinning; reached through one private field, `KittenRenderable._characterAvatar`, and losing it leaves the hands in the lap; it also reads where the head bone has the head, for the helmet |
| `Ksa/CarPanel.cs` | the panel shown while a car is flown, its Race online section **last and folded until opened**, since unfolded it is most of a screen and what is under it would want scrolling to, and told as numbered steps for a host and for a guest, a host's Tailscale code among them where it has one, every code hidden behind an eye button until asked for, with whatever would change the host's world greyed out for a guest and Build roads hidden from one, with the headlight switch, the scoop's choice and the rock weight, the craft mover's switch, a course to lay and the line to drive, the road editor's and the **Unflip** button — closable, leaving a small button that opens it again, and listed in ModMenu's menu when that mod is installed; a plain ImGui window from the GUI pass that never takes the keyboard, because KSA drops the flown craft's held keys while a window has it |
| `Ksa/ModMenuEntry.cs` | a copy of ModMenu's attribute, which that mod finds by name — **not a dependency**, and inert without it |
| `Ksa/BuggySound.cs` | a car's engine while it is being flown — an idle and a loaded loop crossfaded by throttle and re-pitched to its RPM every frame, silent past 4x warp |
| `Ksa/SoundChannels.cs` | the listener, its pressure and a held channel moved or stopped, each guarded |
| `Ksa/GamepadInput.cs` | the first gamepad SDL or GLFW knows, **read from the frame hook and handed to the physics window**, for the car being flown alone; KSA reads a gamepad for its menus only. Its buttons do what the boost, RCS and engine keys and the panel's Unflip and headlights do. **A Steam Controller has been seen read through SDL; no axis or button has been checked** |
| `Ksa/ChaseCamera.cs` | KSA's orbit camera kept behind the car being flown, at the distance of the mode chosen on the panel or with the gamepad's Back, which is said on the screen for a moment, turned by a gamepad's right stick and tilted by Home and End, which KSA leaves free, and **taking a mouse's drag as its own look-round and tilt** rather than writing over it, since KSA springs its camera towards the view and is fed the view's own rate of change, so a drag thrown away every frame threw the camera round at the speed of the mouse: the followed craft's stored view written every frame, **in KSA's surface frame**, which is put back as the player had it when the chase ends, as are the field of view and the distance that speed changes; at medium while a gamepad is read unless the panel's Camera or Back says otherwise |
| `Ksa/SdlPad.cs` | a gamepad through SDL where a `Native/SDL3` is beside the mod, for the pads GLFW cannot see, a Steam Controller among them; without it `GamepadInput` reads GLFW's. **Steam, while it runs, also turns a Steam Controller's buttons into keys**, which the mod cannot stop |
| `Ksa/TerrainHeights.cs` | one body's height field, off the engine's own height map |
| `Ksa/LaunchPads.cs` | a body's launch pads as the colliders KSA stands on each, posed as KSA poses them, so a wheel on a pad is sprung against the pad |
| `Ksa/KsaWorld.cs` | most KSA contact is funnelled here — keep it that way |
| `Ksa/KsaWorld.Pointing.cs` | the part of it the craft mover needs: the cursor's ray and the ground it meets, where a craft is on screen, and gizmo rings and lines |
| `Ksa/CraftMover.cs` | **picks a craft up with one click and sets it down with the next**, from the panel — through `Vehicle.TeleportToLocation`, so it arrives resting on the ground, and a car set down on a laid road is then stood on its asphalt. Carried over from KSArmory with its helpers |
| `Ksa/Bridge.cs` | **commands from outside the game**, read from `Logs/bridge/KSACars/` and answered beside them — load a save, park a car, drive it, set a driver going round a circuit, seat and EVA kittens, step the world, capture — so an agent can test a car in a game that stays running. Developer installs only |
| `Ksa/Laps.cs` | the laps being driven by `Autopilot`, a car each, **kept outside `Buggies.Entry`**: stepped from the physics window, and from the frame hook ended when the car is gone or the roads are laid again, and filed under the bridge's folder as a summary and a line a step |
| `Ksa/CraftSpawner.cs` | parks a craft from a vehicle library at a latitude and longitude, for the bridge's `spawn`, or a second car of the one part another is, **from the part's template**, since a player's library has no car in it |
| `Ksa/Multiplayer.cs` | **racing other players in the host's world**, from the panel's Race online: hosting or joining with a code, the host's world and course handed to each guest with its car in it, every world held to the host's universe time, this car's pose sent 30 times a second and every other racer's car put where its game says, and the host's start. **Seen between two copies on one machine; not across the internet** |
| `Ksa/NameTags.cs` | **each other online racer's name over their car**, a point above it put on the screen by KSA's camera, smaller and fainter further off, drawn behind every window, with the interface up and with it hidden |
| `Ksa/StartLights.cs` | **an online race's start on every racer's screen**: the seconds counted down large and GO! as the lights go out, and while the interface the start hid is still hidden, how to have it back |
| `Ksa/Opponents.cs` | **cars that race the one being flown**, from the panel's Add opponent: a copy of the flown car in the next box of the grid, a kitten at its wheel, driven by `Autopilot` from when the player's car sets off. A driver knows nothing of any other car |
| `Ksa/Build.cs` | what build this is, read off the assembly — and **whether it is a developer's install** |
| `Ksa/Log.cs` | the mod's own log file, which is the only debugging channel it has, **in the `logs` folder of the profile KSA was started with** (`Constants.DocumentsFolderPath`, which StarMap answers with a Borea instance's folder), which everything the mod keeps — circuits, practised laps, the bridge — sits beside; the old guesses at the user folder are only a fallback |
| `src/KSACars/KSACars*.xml` | the cars' parts, seats, colliders and sounds — at the mod root, mirroring Core |
| `src/KSACars/Meshes/`, `Textures/` | the art, **authored** in Blender over MCP; each `.blend` is the source and is not in this repository. A road has no mesh here, and its asphalt and earth are written by `tools/model/road-textures.py` |
| `src/KSACars/Native/` | `SDL3.dll`, SDL's own Windows build of release 3.4.18, under the zlib licence beside it, **the one binary in the archive that is not the mod's**; a Linux install has none and reads GLFW's gamepads |
| `src/KSACars/Circuits/Laps/` | a lap of a shipped course by a car, searched for in the rig and written by `tools/roads/personal-best.py --ship`, which an opponent's pace is a share of the way to |
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
| `docs/KSA-API-SURFACE.md` | **generated** — the 593 members an upgrade has to preserve |
| `docs/BLOCKED-ON-KSA.md` | **what the cars cannot do, or do only round the engine**, with what would unblock each |
| `docs/CAR-BACKLOG.md` | the cars to build next, what each asks of the mod beyond data and art, and the GT3's helmet as photographed |
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

**A step longer than the car can carry is taken in substeps, so the frame rate does not change how a
car drives.** The impulse is explicit, and past a step that depends on the car the roll mode overshoots:
the car rocks from side to side every step and the unloaded tyre drives nothing, which halved the
F2004's pull at KSA's 20 ms. `BuggyDrive.StableStep` is the longest step a car takes, what
`SpringRate`'s budget allows its springs and at most `LongestStep`, KSA's own longest frame: 19 ms for
the F2004 and 33 ms for the other two. `BuggyDrive.Step` takes as many substeps of it as the engine's
step needs, up to eight, carrying the car's own motion forward between them from the impulse, and
`SpringRate` softens the springs only past that. Without the substeps the F2004's springs were
2.0 Hz at 30 frames a second where they are 3.5 at 60, and the car wallowed the slower the game drew.
`BuggyDriveTests.TheSpringsKeepTheirRateAtEveryStepTheGameTakes` and
`NoCarRocksOnItsSpringsOrLosesItsDriveAtAnyStep` are the two sides of it. **Not yet seen in game.**

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

**Yaw inertia is what lets the side grip settle, so every car's mass is a box.** The grip
removes a share of each wheel's sideways slip every step; against a sphere's inertia, a third of a
car's, that share overshot, the car rocked on its wheels at 13 deg/s, and the rocking spent the grip
the drive needed. Each `<Part>` in `KSACarsGameData.xml` declares a `SolidCuboidMass`, the buggy's a box
its own size and the other two's longer and wider than the car, which is what their hubs 1.5 m out
need; `BuggyDriveTests.AYawKickDiesAwayRatherThanRockingTheDriveAway` and
`NoCarRocksWhereItStandsAtAnyStep` fail against a sphere. The buggy kept its sphere until it was found
rocking where it stood, 12 deg/s at 60 frames a second and 25 at 30. **Not yet seen in game**, and it
changes how the buggy drives: with a box it bottoms its springs at the foot of a 20% ramp at 25 m/s,
where the rocking used to slow it enough to carry it.

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

**A hover is held over the ground, which the planet would turn away under.** Near a body KSA steps a
craft in the frame that turns with it and gives one off the ground that frame's accelerations: thrown out
from the axis, 1.7 cm/s2 across the ground at 45 degrees on Earth, and turned by its own speed.
`Hover.Hold` takes both out, from `Hover.FrameAccel`, and with neither lean key held brakes the car's
speed across the ground, so a car let go of comes to rest over a place and stays there. Only in a hover
with no wheel and no collider down; a car flying on its lift rockets alone drifts as it did.
`HoverTests.ThePlanetDoesNotTurnAwayUnderAHover` slides 120 m in two minutes without it. **Not yet seen
in game.**

**Space is the brake, on all four wheels, and it holds.** The pitch key against the way a car rolls
brakes and then reverses; `DriveInput.Brake` stops the wheels and no more, cuts the engine while it is
on, and holds a standing car. `KsaWorld.BrakeKeyDown` reads the key off ImGui in the frame hook, for the
car being flown, and not while a text field has the keyboard: a vehicle's `OnKey` knows no brake, and a
key read there is never let go when a window takes the keyboard. The key is not rebindable. **Not yet
seen in game.**

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
at the wheel both are shrunk to nothing in the seat, which is also how the part declares them, so the editor and its thumbnail show none, and with the first-person view in that seat too:
KSA hides the kitten's own head from it, and its camera is at the helmet's chin bar. The visor is a `PartModelGlass`, one skin facing
out, and the driver's eyes show through it. A car has a helmet if its part has those subparts;
`docs/F2004-REFERENCES.md` has what it was modelled on and how it is fitted.

**Thirteen patches; ten are on public methods and pinned.** Each of those has a `PinTheSignature` that is
never called and only puts the patched method in this assembly's metadata, so `docs/KSA-API-SURFACE.md`
tracks it and a KSA change to it is a build error. `LensColourHook`, `RailsHook` and `RaceHudHook` each patch a private method, which
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

**A road can be wider to one side of its line than to the other.** Each end of a road has a shift, how far
the middle of its asphalt is to the left of its line travelling from its first point to its second, eased
from one end to the other as the width is, and a file with one is version 5. The line stays where it was
drawn, so a paved runoff outside a bend is width to that side and the inside edge is as far from the line
as before: laid about the middle of all its asphalt, a road swerves where a runoff starts and folds on the
inside of the bend, which is what Club Motorsports did. Every row of the surface is still square to the
line and leans about it; `RoadRibbon.Section.Edge` is where each edge is, the tight-bend rule counts what
is to the bend's inside, and a route runs down the middle of the asphalt. The line is kept a metre inside
either edge, and a road comes to a junction about its line. **No shifted road has been seen in game, and
the editor has no control for it**: a shift is the file's, and is kept through an edit.

**One surface a run, which the wheels ask and the triangles are made from.** `Sim/RoadRibbon.cs` is a run's
centre line on a flat chart of the circuit (`Sim/RoadChart.cs`), measured by its own length
(`Sim/RoadLine.cs`), with the profile along it. Past the asphalt's edge a verge falls at 1 in 15 for
1.5 m and an embankment at 1 in 2 from there to 0.3 m under the ground; where an edge is more than 4 m
above the ground the road is a deck with nothing beside it, and a dip under that height shorter than
20 m between two decks is a deck too. Past an end that is not a deck the whole section carries on ahead,
sunk by the same fall; an end at a junction stops square at its mouth.
`RoadRibbon.TooTight` says where a bend's radius is under 1.25 times what the asphalt reaches to the bend's
inside and the verge,
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
turns least, keeping `Autopilot.RaceInside` of asphalt outside the car, **less 0.8 m where a kerb is drawn**
(`Route.KerbUseM`, a bend on the ground tighter than 150 m and never a deck between barriers), so the outer
tyre runs onto the kerb at an apex and an exit as a driver's does, and a wheel on a kerb, up to 0.75 m past
the asphalt within 15 m of a kerbed bend, is not off the road (`Autopilot.KerbOnM`). Round Club Motorsports
Flat that is about 390 m of a lap on the kerbs and a blind lap 0.6 to 0.9 s quicker; further onto them the
F2004 ran wide at 33 ms steps. The line whose own bend squared,
over its own length, is least within those bounds, found by an interior-point method to within 5 mm in
12 to 19 rounds, 10 ms for 3.6 km. It is reckoned from the road's own line with the asphalt's shift a
point, not from the middle of the asphalt, which swerves where a width changes: reckoned from that, the
line had fifteen kinks round Club Motorsports that no road has, the tightest 14 m where it is now 42, and
the F2004 lapped it in 84.7 s where it now takes 77.3. A bend is then taken faster than its own radius allows, since the
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
developer's install, or the bridge's `road` with `racing_line`. The plan is the route driver's with
`Push` 1, all the grip and nearly all the brakes its model has, on a line kept 0.3 m inside the edges where
the driver keeps 0.9, and takes a crest for one to be jumped where the circuit's file has it in `jumps`. It is not lit of its own, so at night it is as dark as the road outside the headlamps.

**A laid course has a start painted on it.** `Sim/StartGrid.cs` puts a white line, two rows of 0.6 m
chequers and a second white line across the asphalt, between the white lines its own picture has inside its edges, 9.5 m along the route from its first
point, and behind that ten boxes 8 m apart in two columns, the pole's to the left; an open course has no
road behind its start and only the pole's box, and a road under 7 m wide one column. `Ksa/StartLine.cs`
draws it as one mesh in one material, 2 cm over the asphalt and under the racing line's arrowheads,
for whatever circuit is laid, and `Courses` stands the car in the pole's box, 5 m along. It is paint: a
lap is still timed from the route's own start, 9.5 m before the line.

**An opponent is the route driver in a second car.** The panel's Add opponent, while roads are laid,
has `Ksa/Opponents.cs` make a copy of the flown car (`CraftSpawner.SpawnLike`), set it down by the next
box of the grid behind the pole, seat a kitten, stand it in the box, and start a lap of
`Autopilot`'s on the racing line once the flown car passes 1 m/s; Opponents slower by, none unless
moved, is taken as each sets off. An opponent goes when its course is taken up or laid again, and has no engine sound.

**No two opponents drive alike, and the same one drives the same every race.** `Sim/Rival.cs` makes a
driver of an opponent's number: a lap up to 4% slower than the gap asked of them all, and a `Tuning.Pace`
of 0.97 to 1 at 24 places round the lap as a walk from one to the next, so it is sure through a run of
bends and not at one place. Its line is the racing line: a line of its own, 0.4 m to one side at 24
places, kinked the line a road's width changes and cost a driver 8 to 10 s a lap. **They are gridded from
the quickest to the slowest** (`Rival.Gridded`): nine raced by hand before any went round a car, with the
slowest ahead, lapped in 89.5 to 97.5 s, each as slow as the one before it and a little more.

**Nor does one lap like the last.** `Autopilot.Human` takes every bend of a lap slower by half a normal
of 1.5%, drawn for that lap from the driver's number, and one lap in four fluffs the exit of a bend, the
throttle held to 0.4 for half a second to 1.2 the next time it is wanted wide open after a place drawn
for that lap. Never quicker than the plan, so as clean as it; the same laps every race.

**Perfect opponents, on the panel, takes all of that away** (`Opponents.FlatOut`, the bridge's `road` with
`flat_out`): every opponent drives the best lap known of the course and no less, with no gap, no driver of
its own (`Rival.FlatOut`) and every lap alike. How it holds back and goes round in traffic is kept, being
what keeps it off other cars and not a slowness of its own.

**An opponent is asked to be a share slower than the best lap known of the course, and every bend is
taken slower by what gives that up.** The panel's Opponents slower by, 0 to 20%, is that share, and
`Rival.Drive` gives the driver its own on top. What a lap gives up for every bend taken slower is not the
same on any two courses or cars, so each lap known is measured: `Practice.Slowed` laps it with every bend
at 0.95 down to 0.6 of its speed, and `Rival.Slowing` reads the share for a gap between those, and at 0.4
of the lap for a share of the bends where nothing was measured. In game the F2004's practised lap of Club
Motorsports Flat gave up 1.6% at 0.95, 11.5% at 0.8 and 36% at 0.6. There are three laps it may be of,
the first there is of them being taken:

- **A lap the car has practised of this course**, here: `Ksa/Practising.cs`, below. Before practice
  finds one, the blind lap, measured the same way.
- **A lap the mod comes with**, `Circuits/Laps/<course>/<car>.json`, which `personal-best.py --ship` writes
  from a search and `CircuitLibrary.LapOf` reads if the course a player gets by that name is the one the
  lap was searched on, by its file's SHA-256: its settings, its pace at 24 places and its line, 0.55 of
  the way from the cautious driver's, past which a driver with bends of its own leaves the road. Only Club Motorsports
  has one, for the F2004, and taken whole it no longer laps cleanly: it was searched for before the
  driver was last changed.
- **Nothing**: `Rival.Racing`, the same for every car and every course.

**`Rival.Racing` is the driver with what it knows of the car in its plan.** `Autopilot.Tuning.Model` 0 is
the driver as a lap for the watch has it: a stop planned at a share of the brakes' own g, taken from what
a bend leaves of its share of the grip, and the pedals full on or off either side of the speed wanted.
Model 1 plans a stop on what presses the tyres, wings and all, on the tyres' own circle, with the air's
drag: the F2004 stops at 4.5 g at 70 m/s where its brakes' g says 1.1, and a car in a bend still has
brakes. Model 2 also presses the pedals as far as gives the acceleration the plan asks, where the car
coasted a third of a lap between full throttle and full brake. `Racing` is model 2 with 0.92 of the grip,
0.8 of the brakes and a bend's tightness averaged over 7 m, a racing line's bend spiking for a few metres
where a road's width changes, braking 0.3 s early: the pedal lags a stop by 2 m/s, and in the game, whose
gravity is 9.82 and whose air thins with height, that ran the F2004 wide off the course's first right-hander
and practice said it could not lap the course. It costs the F2004 half a second a lap. The F2004 lapped Club Motorsports in 84.7 s with it where the driver's own
settings took 94.3, on the racing line as it then was; on the line as it is those are 77.3 and 84.0, the
Eldorado's 127.2 and 129.5 and the buggy's 153.2 and 154.2, and every car laps
the suite's circuits cleanly, with 6% less grip too. A lap for the watch, the bridge's `lap` and
`personal-best.py` still drive model 0 unless told otherwise, with `model=2` in a tune.

**An opponent practises the course it is to race on.** `Sim/Practice.cs` laps a car in the rig in three
ways, round and round until a round finds nothing or 300 laps are up. First every bend 0.04 quicker than
the try before, until a lap is not clean or two have been slower. Then the driver's own settings one at a
time, of which how far ahead it looks, what a bend's tightness is averaged over, and its shares of a dip,
the brakes and the tyres are worth having, and its shares of the grip, a crest and tipping are not. Then
the pace at 24 places round the lap, all raised together and then lowered: a lap that goes wrong holds
the places round where it did, and a clean one keeps the places whose own stretches were quicker. A way
is kept only if it is clean and quicker, and one that is kept is first lapped every way a searched lap
is, five kinds of step, two with 6% less grip, one in air 6% thinner than the driver takes it to be and four
with every other place 3% slower, without which an opponent with bends of its own left the road on one of
the suite's circuits. **A way is tried at the game's uneven steps and at 33 ms**, the longest step KSA
takes, which it takes every frame while it draws under 30 a second, as it does with a field of cars: a
lap practised at 25 ms instead left the road at 515 m round at 33 ms for every driver, and in game the
quickest went off the road into a tree. **Round Club Motorsports the
F2004 comes to 73.9 s from 77.3 racing blind, the Eldorado to 120.9 from 127.2 and the buggy to 149.1
from 153.2; round the flat course the F2004 comes to 70.9 from 74.0.** A third of a minute to a minute of
one core, most of the gain in the first quarter of it.

`Ksa/Practising.cs` runs it on a thread of its own, below the game's in priority, when the first opponent
is asked for: what it needs of the game is read in the frame hook, the car's mass, centre of mass and
inertia as the engine has them, gravity and the air, and the thread is given those, the laid
`RoadSurface`, which is swapped whole and never changed, and ground a kilometre down, a car on asphalt
never meeting the ground. It gives up when roads are laid again. Each lap it finds is the opponents' at
once and is kept, in `<KSA user dir>/KSACars/Laps/`, as a `.part.json` until the practice is done, so one
that was cut short is carried on from. The file's name is a hash of the course as its file has it, how
long its roads came out and how much they climb where it is laid, the body, the car, its weight and a
`Practising.Version`, which is changed by hand when the driver, the line or the rig changes what a lap
means: not of where the course is, a course laid at the car never being in the same place twice. An
opponent takes whatever there is when it sets off, and one put back on the road has what there is then.
**Seen in game, all of it**, at 33 ms steps: on Club Motorsports Flat the F2004 laps blind in 74.8 s and
practises to 73.1 s in 27 s, the slowings measured with it, and the course laid again knows it at once.
Racing at those steps has not been clean: see `CHECKLIST.md`.

**The rig is in the mod, since practice needs it.** `Sim/LapRig.cs` is `TrackRig`, `TrackWorld` and
`TrackCar`, which the suite has always driven; the cars' own numbers for the tests are
`tests/KSACars.Tests/TrackCars.cs`. A rig that practises keeps no log of its steps.

**A route is worked out once for a laying.** A racing line is 80 to 100 ms to relax for 3.6 km of course,
on the game's thread, and every opponent asked for its own again each time it was set off, put back or
righted: `Roads.RouteOver` now keeps the routes asked of the roads that are laid by what was asked, a
route not being changed once made, and an opponent's is worked out while it waits on the grid, one car a
frame.

**An opponent holds back behind another opponent in its way, and goes round it where it can.** Each frame `Opponents`
looks 250 m ahead of each opponent for a car within 2.2 m of its side, the flown one among them, measured
along the opponent's own route (`Autopilot.Along`) and not in a straight line, by which a car round a bend
is off to one side until it is too near to stop for, and `Rival.FollowCap` gives the driver the most it
may go at (`Autopilot.CapMs`): the car's own speed and what 0.6 of its own brakes still brake off in the room there is
(`Autopilot.BrakeAt`), keeping 5 m and half a second, or 14 m behind a car that is standing, and further by
2 s of a car coming backwards; nearer than 5 m, as behind a car that cut in, it drops back. Braking for a
car at 8 m/s2, an F2004 that brakes at 16 to 45 held back so far that it was never near enough to begin
a pass, and a field raced in the rig was a procession that lost 5 s a lap to it. A driver held so is told each frame
that it is waiting, or it would take itself for stuck. That and the rest of this paragraph and the next is
how opponents race one another; a player's car near an opponent is `Sim/Lanes.cs`', below.

**A car is reckoned as wide as it is turned, where it has lately been and where it is going** (`Rival.See`,
`Rival.Reckon`). `See` gives a car's speed as its velocity along the road where it is, so one reversing is
coming at the driver, its turn against the road there, and how far from the road's middle it is; each
`Rival.Seen` is then a span across the road and not a point: out by its length turned across, to everywhere
across the road it has been, fading at 2.5 m/s, and to where it will be when the driver is up to it, up to
2.5 s on: a car keeping to the driver's line stays on it, and any other keeps to its lane, or goes on moving
across, while the racing line runs across the road under it. Every in-the-way, room and alongside test is
against that span. In `OvertakeScenarios`, a car reversing, one turned across, a racing line bending into a
car driven at the road's edge and a weaving one were each run into in the rig with it reckoned a point.
**Not yet seen in game.**

**Going round is a line to one side that the driver is given.** `Autopilot.SideM` is how far to the left
of its route a driver is to run: eased across at 2 m/s or 8% of the car's speed, 35% under 12 m/s, held
to what leaves `RaceInside` of asphalt outside the car, and with the speed a bend allows brought down
for the tighter turn. `Rival.Pass` decides it, with a `Rival.Passing` an opponent for what it is in the
middle of: behind a car at least 4 m/s slower, on a stretch straight enough for the whole pass, it goes
3 m to the side with more room and no third car in it, is no longer held by that car once it is 2.5 m
to its side or will be before it is up to it, unless that car is braking harder than 3 m/s2, and comes
back to its line 9 m past: reckoned on a car keeping its speed, one braking into a bend ahead of a driver
still behind it was run into at 18 and 24 m/s in game; if the straight, the speed or the room goes before it
is level, for 0.4 s and not a frame, it drops in behind again and waits 3 s. **A car going at under 0.6 of what the road allows the driver is crawling** (`Rival.CrawlShare`), and is
gone round wherever it is, a bend among them, as a standing one is: 3 m aside and no faster than 12 m/s
over its speed until clear of it where there is no straight for the pass. Waiting for a straight as long
as the pass takes at the driver's own speed, a field followed a car driven slowly in front of it for good.
**Its nose alongside a moving car, a driver has the place** (`Rival.AlongsideHoldM`, `HoldSideM`): less
than 4 m behind its middle and a metre to the side it is going round, it neither gives the pass up nor is
held back by that car, which a car moved across into it touches.
**No pass is begun in the first 8 s of a race**
(`Rival.Passing.Launch`): on the grid every car ahead is standing, and a field that went round them all
ran two abreast into the first bends and touched in each. Through those 8 s each driver runs as far to the
side of its line as it stood in its box, `Passing.Column`, eased onto the line over the last 4 s, and is
held back only by a car in its own column, neither the rules nor the lanes deciding: steered for its line
at once, the two columns closed into one file before the first bend. **Not yet seen in game.** **A car slower than 3 m/s is standing, and is gone
round wherever it is**, a bend among them, 3.5 m outside it or as far as the road has, driven up to at
speed and braked to 9 m/s and 8 m/s more for every metre it is clear of it to the side, and slower the
nearer it is while not yet clear of it; behind one crept up on for 200 m, a pass of a car turned across
the road lost 20 s. Round a car standing or crawling a driver may go 0.6 m nearer the edge than its racing
margin (`Rival.Squeeze`, `Autopilot.SqueezeM`). **The room to one side is asked of as much road as the
pass takes** (`Rival.RoomOver`), 15 m past a standing car, and from a little before the driver is up to it
(`Rival.RoomFrom`), 8 m short of a standing car: asked of the next 150 m,
as it first was, there is never any on a racing line, which runs out to one edge or the other somewhere
in any stretch that long, and a field raced by hand queued behind a car standing on a road 13 m wide.
**A car waiting behind another is followed, not gone round** (`Rival.Seen.Queued`, an opponent held under
3 m/s), so the car at the front of a queue goes round what stops it and the rest go after it, one by one: a
queue that was gone round stood in two columns. A pass of a standing car that cannot be finished is over,
where one of a moving car drops back behind it, which behind a car that stands is waiting for good; the car
behind a driver in its own lane does not take the side it would go to; and a standing car far enough ahead
may be gone round on either side, crossing behind it. **Cars side by side are gone round as one**
(`Rival.TogetherM`, within 8 m along the road of each other, both standing or within 2 m/s of each other): the driver runs outside the furthest of
them on its side, is held by none of them, and creeps at the pace the nearest not yet cleared sets. Going
round the nearer of two, the other was where it would go and in front of where it was, and in a race the
player drove five opponents stood in a queue behind the two for good.

**A player's car is never something an opponent waits behind** (`Sim/Lanes.cs`). While one is within what
the driver brakes in and 3 s of its speed ahead, or alongside, `Rival.Pass` hands the choice to the lanes
and for a second after: up to 11 lanes across the road and one just clear of either side of each of the
three nearest cars, each tried at free speed, at each of the two nearest cars' speed, following each, at
a push past a player's, at its own speed, at a creep and at a stop, and rolled 3 s ahead in 0.2 s steps against every car carried on
as `Rival.Reckon` reckons it, slowing for what the road allows ahead of it; the cheapest in seconds lost,
touching (50 s), coming near, running outside the road's room where the car is, and moving off the line is
taken, and kept unless something is 0.1 s better. The road's room is asked a step at a time along the
roll, since a lane is an offset from a route that runs across the road: asked as the least over the
stretch, a racing line at one edge and then the other left 0.6 m either side and no lane at all. Steering
round is cheap and a speed under the road's is what is left where no lane is clear. **An overtake**
(`Lanes.Memory.Who`) is of a player's car 3 m/s slower than the road allows the driver, now or by the next
bend it brakes for: the driver follows it 2.5 m and 0.2 s back and a touch beside it is a rub of 6 s and
2 s for every m/s, where one into its tail is still a crash; given up once the plan has held the driver
behind it for 0.6 s, with 3 s before the next. **A player's car slower than 0.9 of what the road allows
that holds the driver under that for 0.6 s is pushed past** (`Lanes.Pushing`): the driver aims at 0.6 of
the way across its tail on the side with more road, or clear of the side of one standing where the road has room and
against its corner where not, and closes on it at what still meets it at 4 m/s over its speed, since KSA's contact between two cars is plastic and each changes by half the closing speed;
stopped, crawling, blocking or driven the wrong way, the player cannot stop an opponent. A car that has swung across the road is
taken to be anywhere in that swing the driver goes only while it goes the driver's way: a car driven the
wrong way along a racing line sweeps the road as the line does, and every lane past it was a crash. A car
coming the other way is never pushed nor counted as holding the driver up, and is met with half the room
to its side and no more along it for the closing speed. In the look ahead the driver makes back for its
line once past every car it was getting past: held to its lane past the car, a lane round one met at a
bend's entry ran off where the line crossed to the apex, and only a crawl was ever clean. A car driven the
wrong way into a bend's entry now costs 0.1 to 1 s where it cost up to 3.6, and is never touched, and runs touching
a player fell from 85 to 61 of 340; but in three to five more runs a wheel is off the asphalt for 0.3 to
0.5 s going by. Another
opponent is never pushed or rubbed, and one the rules would not go round is the whole road to the lanes.
One call is 47 us with one car near, 78 with three and
154 with nine. In `OvertakeScenarios`, every run of every scenario passes the player and none is stuck:
over its 340 runs the time lost is 376 s where the rules took 898, the median time within 40 m behind a
player 1.6 s where it was 2.8 and the median held under the road's pace 0 where it was 1.4, the runs off
the asphalt 1 where they were 19, and the runs touching a player 85 where they were 120, two opponents
never touching; but the longest within 40 m behind is 12.8 s where it was 10.3, behind a player in a bend
going nearly as fast, and the longest held 7.0 s where it was 6.9, behind a blocker into a bend and one
of a queue of three behind another opponent. **Not yet seen in game.**

**A driver keeps a car's width from a car alongside** (`Rival.KeepLane`): the side it runs at comes no
nearer than 2.4 m, middle to middle, to a car within 7 m ahead or behind, and where it is nearer already,
no nearer than it is. A racing line runs across the road, and in game the car being passed followed it
into the one beside it as the two braked into a bend side by side, and both were put back on the road.

**A car in another's wake is spared some of its drag.** `Sim/Slipstream.cs` takes up to 40% of a car's
drag away right behind another, less the further back and to the side it is, and nothing past 50 m or
1.6 m to the side; `Buggies` works it out for every car on its wheels in the physics window, from where
each was last stepped, the flown one among them, and `BuggyDrive.Tow` is the share. Without it two cars
alike never drew level: a field raced in the rig had one pass in three laps. At 70 m/s an F2004's drag is
3.6 kN of the 9.2 its power pulls with, so a tow is worth 1.8 m/s2. **Not yet seen in game to let a car by.** `PassingTests` races two rigs
on one road: every pair of cars past on a straight 3 m apart, an F2004 past an Eldorado it comes upon at
60 m/s without lifting, and a standing car gone round on a straight, from 14 m behind it and in a bend.
**Seen in game**: a car stopped at the road's edge gone round by two opponents and one put back past it.
A pass of a moving car has not been watched, and a road that narrows with two cars level is not tried.

**An opponent stopped with its nose against a car standing across its way, or one coming the wrong way,
backs away from it** (`Opponents.PinnedSeconds`): after 1.5 s under 0.5 m/s with such a car within 7 m
in front of its nose the way it faces, not along its route, which off the road it faces across, and not one queued, its lap is stopped, it is held in reverse for 60 frames and set off
again, to go round from there. It cannot steer round a car against its nose, and took itself for waiting
behind it for good. In `OvertakeScenarios` a car driven slowly the wrong way is passed every time, touched
nose to nose at a creep in bends. **Not yet seen in game.**

**An opponent off the road drives itself back on, and is put back only if it cannot.** Off the asphalt
the route driver makes for its line at 5 m/s, and a lap ends `OffRoad` after `Autopilot.OffRoadFor`:
a second for a lap that is being measured, a quarter of a minute for an opponent. One on its roof is
set on its wheels where it lies (`Buggies.Right`) and drives on from there, once in twenty seconds of
racing. **One that is not moving is not left to try**: under 0.5 m/s for 2.5 s with no car in its way,
it is put back, as is one still off the road after that quarter of a minute or over a second time. It
brakes for 30 frames and is stood on the road at the nearest place of the route on from where it left
that has no car within 8 m nor one coming up to it from up to 120 m behind, twelve places being tried, and set off again: waiting for the one place to
clear, two cars that stopped together each waited for the other for good. One the driver takes for stuck behind a car that holds it under 3 m/s is
waiting, and is set off again where it stands. `RivalTests.ThrownOffTheRoadADriverGivenTheTimeDrivesItselfBackOn` is the
first of these in the rig. **Seen in game**: nine opponents raced by hand set off without touching
one another, on routes of 3,562 to 3,570 m, and one run into from behind at 27 m/s was put back on the
road and racing 4.6 s later, when an opponent had a second off the asphalt and no more. An opponent has
not been seen driving itself back on, set on its wheels, or watched slowing behind a car.

**Two cars that collide leave together.** KSA's contact between two craft is plastic: in five hits logged
in that race the flown car and the one it ran into each changed by half their closing speed in one step. `Laps.Update`
runs on every install for it, and a lap's summary is filed only on a developer's. **Seen in game**: two
F2004s on the grid of Club Motorsports Flat set off with the flown one and lapped at up to 67 m/s on the
asphalt; a car in another's way, the panel's own buttons and an install with no `developer` file have not been.

**With KSA's interface hidden, F2, a racing display is drawn in its place.** `Ksa/RaceHud.cs` shows the
flown car's speed in km/h, its lap out of the panel's Laps, the lap's time, its place among the cars on the course, and a
map 320 m about the car that turns as the car does, the other cars on it as dots. Every car's place along
a route down the middle of the laid roads is kept each frame in a `RaceBoard.Tally`.

**A lap is every checkpoint in order and the line again.** A course has a checkpoint about every 300 m,
4 at the least and 24 at the most, the painted line the first, and none of them is drawn on the road. One
counts when a car is driven over it: forwards, within 3 m of the asphalt, and by less than 60 m between
two frames, so a car found again further on, having cut across or been put there, has not been through
what is between. Its lap and its place then stand at the checkpoint it missed, the display says MISSED
CHECKPOINT, and the map marks the one it is due at in yellow, until it goes back and drives over it. The
one it is due at is also a post on the screen, a line 14 m up from the road there with how far off it is
and CHECKPOINT over it, or LAP or FINISH at the line: two places of the body put on the screen by KSA's
camera, drawn with the display and so only with the interface hidden.
`RaceBoardTests` has a car that cuts 400 m behind one that did not.

**A lap is timed from line to line, in the game's own seconds, and each checkpoint against the best lap.**
The tally keeps when the car was over each checkpoint, taken between two frames by how far through the
way between them the checkpoint is, so a lap is timed to less than a frame. The display is laid out as a
racing game's is: the laps at the top left with the race's time, the lap's and the best lap's in a box
under them, the place at the top right, the map at the bottom left, the speed and the gear at the bottom right, on a disc the map's size and as far from its corner, inside a rev counter, nearly a whole circle
clockwise from seven o'clock, numbered in thousands of revs with a needle on its rim and the last of it red, and at the top in the middle, for four seconds after a checkpoint, how much earlier or later
than on the best lap the car was there, green or red; at the line that is the whole lap against the best
before it. Nothing is kept past the session, and a pause stops the clock. No StarMap hook is inside KSA's ImGui frame while the interface is hidden, so it is
drawn from `Ksa/RaceHudHook.cs`, a postfix on the private `Program.DrawFps`, onto the foreground list.
It is on unless the panel's Racing display is cleared, shows nothing while the interface is up, and is in
a screenshot KSA takes, which hides the interface to take it. The laps are what the lap is shown out of
and nothing more: no race starts or ends. **Not yet seen in game.**

**A quad drawn over a road needs a place in its picture for each corner.** With one texture coordinate
for all four, KSA's static mesh shader drew the start, and the racing line's arrowheads, speckled with
holes at any height over the asphalt; spread over the picture they are solid. `docs/KSA-MODDING-NOTES.md`
has it.

`tools/roads/club-motorsports.py` writes a real course, Club Motorsports in New Hampshire: its centre line
from OpenStreetMap, its heights from the USGS's lidar and how far its asphalt reaches either side measured
on the USGS's aerial pictures, in `club-motorsports.csv` with where each is from, as 108 points each on
the mapped line and as high above level ground as the real track is above its lowest place, 59.5 m,
so the lap climbs as the real one does, on fill and decks; and the same line flat on the ground. The road
is as wide at each point as the asphalt is there, 12 m to 40 m, a bend's paved runoff counted in, and
shifted to the side the asphalt reaches further to; beside a
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
`tools/model/road-textures.py` writes all four materials' textures, and the start's paint.

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

**A circuit laid whole is worked out beside the game.** `Roads.Prepare` reads the ground, works out the
surfaces, the warnings and every mesh on a thread of its own, and `Roads.Lay` given that only hands them to
the wheels, the renderer and the physics: worked out in the frame, a 4 km course stopped the game for a
third of a second to a second, at every edit and at every course laid. KSA's physics workers read the
ground's height from their own threads, which is what says it may be read from this one. `Courses` and the
editor lay so; the bridge's `road` still lays in the frame, and its `course` waits for the work. A route
over laid roads is made from the surfaces as they were laid, where it used to lay the circuit a second time.
**None of this has been seen in game.**

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
as that road arrives. A file with loops is version 4 or later. `WheelGround.Read` takes the loops, and a wheel on
one is sprung along the loop's own facing under that wheel and not along one plane through all four: a
loop turns a seventh of a radian between a car's axles, and with one plane the nose closed on the road
at that share of the car's speed while its dampers saw it leaving. A loop presses a car at four to six
times its weight whatever its size, since the speed it takes grows as the loop does, and that is 8 cm of
the F2004's springs, which have 2: `BuggyDrive.BumpStops` holds each hub at the end of its travel, as a
wall holds a car's side (`Barrier.Hold`, with nothing rubbing), in `Buggies.Physics` while a wheel is on a
loop. **On a loop the mod steers**, by `RoadLoop.Steer` towards a place on its middle a little ahead, and
the keys are the throttle's: it is carried aside as it goes round. `Roads` draws it as a road is drawn,
with its lines, in a place of the pool, with a barrier outside each edge as a deck has, 5 cm high where
the loop leaves the ground and comes back and a deck's 0.9 m from 8 m round, so its end is no wall to
meet. **It is solid half a metre under its asphalt**: `RoadLoop.Solid` is a trough, a floor `ThickM` under
the asphalt and a wall either side to the barriers' tops, each from both sides, given to the physics as a
solid of its own. At the asphalt it could not be: a loop bends up under a hull's ends by 21 cm over the
F2004's length at the top, and a car on its bump stops stands 10 cm clear, so a car going round on its
wheels would scrape it. So a kitten stands in it to the shins and a car on its roof lies that deep in it.
A car's side is held off a loop's wall by the mod's own push, as off a deck's (`RoadLoop.TryBarrier`), and
a car with a wheel on a loop has the engine's look-ahead held short without the share for its turning,
since round a loop it turns as fast as the loop does and closes on nothing by it.

**A loop is put at a road's end in the editor.** With the end of a road selected the panel has Add a loop
here, which is `Circuit.PutLoop`: a point where a loop of 110 m comes down, ahead of the end as its road
arrives and to its left by the road's width and 4 m, a road of 60 m on from there, selected to carry on
from, and the loop between. With either end of a loop selected it has the loop's length, from 60 m to
400 m, Other side, Remove loop and Seat loop, which is `Circuit.SeatLoop`: the point it comes down at put
back where a loop of that length does, as a change of length does of itself and a road moved does not.
**None of it has been clicked**: the bridge's `road` with `loop` makes its course by the same edit: it lays a road to one and
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
run, KSA writes them only while the workers are parked, and a write from the GUI pass races them. **Which
instances the roads cover is worked out beside the game**, on a thread of its own that touches nothing of
KSA's and gives up once roads are laid again, and only the bits it found are written in the window: found
in the window it stopped the game for 3 to 7 seconds a laying of a 4 km course. Road more than
`Roads.ClutterUnderM`, 60 m, above the ground is left out of the search, and what stands under it stays. The bridge's
`road` with `probe_clutter` measures the reckoned positions against KSA's own. KSA writes those bits into a save, so a game saved with a road laid keeps the clearing.

## Racing online

**A race between players is one world in several games: the host's.** KSA has no multiplayer to build on:
its `KSA.Networking` connects and chats over RakNet and replicates nothing, and its terminal commands are
never registered. So `Ksa/Multiplayer.cs` hands a guest the host's world when it joins: the host makes the
guest's car in its own world, in the guest's box of the grid with a kitten at the wheel, stops its world,
saves it with `GameSaves.MakeUncompressedSave` as `Online race`, packs that save and the circuit as it is
laid into one zip (`Sim/WorldTransfer.cs`, 46 KB for a small save) and sends it over the race's socket. The
guest writes it into its own saves as `Online race`, loads it, flies its own car by its Id, lays the course
from the circuit it was sent, which is exactly where the host's is and needs no file of its own, and says it
is ready, and the host's world runs again. The course goes on being the host's: laid again or taken up while guests are in, once it
has stood a second, so a road being dragged is not sent every frame, it is sent alone to every guest racing,
who lays it as it is or takes theirs up. **Seen between two copies on one machine**: a course changed and taken
up on the host, laid and taken up on the guest within a second, and a point dragged through forty layings,
as the road editor lays a drag, sent once, 3 KB, a second after the last. Every guest has then started at the moment the host's world was
saved, with the same date, sun and craft. A guest is let in only with the host's KSA build and every mod the
host has enabled, since a save holding a part of a mod it lacks does not load; it is told which it lacks.
After that each world is simulated on its own: what is on rails stays the same, and what only physics moves
other than the racers' cars, a rock pushed or a kitten on EVA, drifts apart.

**Every world keeps the host's universe time, and nobody warps or pauses.** The host says its universe time
five times a second, and a guest measures from those how fast the host's world really goes; `Sim/ClockLock.cs`
runs the guest's world at that pace and up to a tenth faster or slower until the two agree. **KSA steps a world
by no more than 33 ms a frame** (`App.Run`, `1/MinTargetFrameRate`), so a game drawing 20 frames a second
loses a third of every frame and its world runs at two thirds of real time, which KSA's achieved fraction does
not see: in a race over the internet a guest drawing 20 fell 47 s behind, its car pulling away at two thirds
of the pace on its own screen. So every world asks KSA for the speed that makes up a frame too long
(`ClockLock.Asked`), never more than 1.1, a step of 37 ms; and **the host holds its race to the slowest
guest** (`Sim/RacePace.cs`): each guest's pace measured from the universe times its states are sent at, and how
far behind it is sent with each state, four bytes at its end that a game reading no further never sees, the
host's pace drops a little under a guest's that has been short of it for two seconds while behind, and rises a
hundredth a second while every guest keeps up, never under half. Every guest follows the host's measured pace,
so a guest of 0.1.8 is held with the rest. `RacePaceTests` races a host at 60 frames a second against a guest at
20 and holds it within 0.55 s of the host, where it fell 30 s behind before. **Seen between two copies on one
machine**, the guest held to 20 frames a second by the bridge's `online` with `fps`, which sleeps in its frame
hook since KSA 2026.10.19.5638 saves an fps limit and no longer reads one: the guest asking 1.1 and running at
0.73, the race held to 0.71 to 0.75, the guest 0.27 to 0.39 s behind through a race, and each car drawn in the
other's world at up to 83 m/s with one put outright where last night's were 5 to 11 times a second.
**Not raced across the internet.** The `pace:` line in the log says
each world's real pace, the speed asked, its frames a second and, on the host, each guest's pace and how far
behind it is. While anyone has joined,
`Ksa/WarpHook.cs` holds every world to that speed through both of KSA's `SetSimulationSpeed`, so the warp keys,
auto-warp, the escape menu's pause and the editor's do nothing. A ping slower than half a second to come back
is not used to set the clocks: the host's world stands still while it saves, and a ping answered then put a
guest's clock seconds out (`MultiplayerTests.AHostThatStandsStillAnsweringAPingDoesNotPutTheGuestsClockOut`).

**Every racer's car is a craft in every world, and each game drives its own.** The roster carries each car's
Id, and `Buggies.Puppet` hands the others' poses to the physics window, where `Buggies.Pull` writes them after
the drive is stepped: turned as told, moving as told, and drawn onto its place over 0.1 s, or put there
outright from more than 3 m. Its drive still steps with the inputs its game sent, so its wheels steer and its
springs work. A pose is sent as where the car is on the course (`Sim/RacePose.cs`), and timed and drawn on the
universe time every world shares, so a car is drawn as its world's physics moves and not as the wall clock
does. A racer who joins later has its car made, with the same Id, in the worlds of those already in. A car not
heard from for 3 s of the world's time is let go of and braked. Whoever is out of the race, by leaving, by
the host stopping or by the host's game going quiet, is left with its own car alone: the others' are taken out
of its world, found by the roster's Ids. A guest that
joins again forgets the world and the clocks it had, a new host numbering its worlds from one again; one that
has not loaded the world it was sent in two minutes is put out of the race, since the host's world waits for
it; and when the last guest goes, the host's world runs at 1 again. **Nobody takes another racer's car**: `Ksa/ControlHook.cs` refuses control of it and a kitten boarding it, and the
craft mover leaves it alone, except while a guest loads the host's world, whose save hands control to the host's
car before the guest is put in its own. **Two cars that touch each meet it in their own
game**: there one is a puppet and the other is pushed, and what the pushed car does is then sent to the other.

**Seen between two copies on one machine** (`tools/two-games.sh`, 2026.10.13.5602): the guest given the host's
world and flying its own car in it, the course laid as the host's with the host's car on it 2 cm from where it
was wanted, its universe time within 6 ms of the host's when both drew freely and following the host's real
pace when it did not, both worlds refusing a warp to 10x and a pause, the lights out 6 ms apart, and an F2004
run into the other at 9 m/s losing 3.6 m/s in its game while the one it hit was shoved to 3 m/s in the
other's. Two copies on one card is no fair test of smoothness: the copy whose window is behind the other is
given what the card has left, at 40 frames a second each with vsync off, and crawls when the card is short.
**A bad line is made on purpose for a developer**: the bridge's `online` with `delay_ms`, `jitter_ms` and
`loss` holds back, jitters and loses every datagram a game sends (`NetSession.Impaired`), sent in its next
frame. Through 75 ms each way, 20 ms either way and 5% lost on both: the world handed over in 4.4 s with its
lost pieces asked for again, the lights out 21 ms apart, and in 67 s of racing no puppet put outright, the
worst 1.5 m from where it was wanted at 48 m/s and most a tenth of that. A guest's world goes no faster than
KSA lets it: KSA runs a world slower than its speed when its physics workers take most of a frame
(`Universe._achievedSpeedFraction`), and with both copies racing on one machine a guest 0.5 s behind stayed so,
at 1.1, until the racing stopped, and then caught up. **Nothing has been raced across the internet**, nor has a
port been opened through UPnP or a guest let in by its code: the router of the machine it was tried on has UPnP off and does not take back what is sent to its
own address.

**Nobody signs in to anything.** The host's port is opened on its router through UPnP where the router
allows it (`Sim/Upnp.cs`), leased for four hours so a crash leaves it to close, and closed when the race
is — on the way out of the game by `Multiplayer.Shutdown`, which says goodbye, waits for the router and
touches nothing of the world, that being halfway closed and nobody's to read; every game asks a public STUN server where its socket is seen from outside (`Sim/Stun.cs`); and an
address is a ten-character code (`Sim/JoinCode.cs`). Where a router will not open the port, a guest gives
the host its own code and the host's Let in sends to it for 30 s, which opens the way through both
routers from inside. A code that is the guest's own public address is a host behind the same router, which
most routers do not reach by that address: the guest then says hello on this machine and to the whole local
network as well, and joins whichever host answers on the code's port. The host takes a guest on only once it
has heard more than hello from it at the address it settled on, so the one heard twice is one racer. A host
with an address of Tailscale's, 100.64.0.0/10, on any interface also shows a code of that address, which any
machine of that Tailscale network reaches whatever the routers; **not yet tried across a Tailscale network**. Every guest speaks to the host alone, which passes each state on, so only the host
has to be reached. A state is never resent, being stale before a resend would come; the pieces of a world are.

**The start is on the host's clock.** A guest measures the host's clock against its own by the quickest
ping it has had answered, so the lights go out together in every game; at a start each car is stood in
its box of the grid, which `Opponents` then leaves free, held on its brakes, and let go. Every racer's
interface is hidden at the start, as F2 hides it, so the racing display takes its place, the chase camera is
switched on, and
`Ksa/StartLights.cs` counts the seconds down large in the middle of every screen, with a note for ten seconds
that F2 has the interface back; `Ksa/NameTags.cs` puts each other racer's name over their car. **Seen between
two copies on one machine**: the count and the note with the interface hidden, the lights out 7 ms apart.

**A puppet standing where it is told to stand is left on KSA's rails.** KSA rails a craft that stands still,
and woken every step its physics is built again every step: a puppet waiting on the grid held the host's world
to a twentieth of its speed, KSA's `_achievedSpeedFraction` cutting every step, and the guest, keeping the host's
time, fell behind with it. The bridge's `online` says the achieved fraction, and each game logs it every two
seconds while racing together.

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
   saved part with its definition positionally and by Id, and a save that no longer matches loses
   the car: KSA 2026.10.19.5638 leaves it out with an alert, where earlier builds closed the game. `docs/KSA-MODDING-NOTES.md` has the loop.
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

`TrackRig`, in `Sim/LapRig.cs`, is the same car as a free body on a sphere with ground of the test's own, stepped as `Buggies`
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
`OVERTAKE_RUN=1 ./tools/test.sh --filter OvertakeScenarios` races an opponent F2004 against cars moved by
script on Club Motorsports Flat and a straight: slow on the line, the middle or an edge, weaving, turned
across the road, reversing, braking and speeding up, two abreast and one with three opponents queued
behind, at four places round the lap and at 21 and 33 ms steps, and writes what each lost, how near it came
and whether it touched to `OVERTAKE_OUT`; it asks nothing, and nothing in it touches anything.
`OVERTAKE_HELDOUT=1` runs variants nothing was tuned on, at other places, speeds and amplitudes, and
`APlayersCarOnAStraightCannotHoldAnOpponentUp` asks of four of its runs that each costs under 1.5 s.
`./tools/roads/rig-laps.sh "Z "` laps every car round the circuit files the game reads, in seconds and
with a CSV of every step for a car named, which is where a circuit is driven before the game is; it
asks nothing of a lap, because a wall no car climbs is a fair thing to draw. `tools/bepurig` is the
game's physics engine alone, for what the rig's stand-in hull cannot show.

**A behaviour change is unverified until it has been seen in game**, whatever the suite says.
`CHECKLIST.md` records what has been driven and what has not. The buggy and the Eldorado were driven
through the bridge before this repository was split out; since the split only the F2004 has been seen in
game, parked and pulling away, before its power, its downforce and the game's drag were changed.
