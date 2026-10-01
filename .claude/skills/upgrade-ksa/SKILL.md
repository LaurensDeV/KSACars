---
name: upgrade-ksa
description: Move this mod onto a new Kitten Space Agency build. Use when KSA has updated, when the ksa-version workflow opens an issue, when check-assemblies.sh reports the install no longer matches the lock, or when the user asks to upgrade/retarget KSA. Refreshes the assemblies and the decompiled corpus, finds the breaking changes, fixes them, and updates every place the build number is written down.
---

# Upgrading to a new KSA build

KSA is pre-release and has **no official code-modding API**. The surface this mod compiles
against genuinely moves between builds — members get renamed, and, worse, members keep their
name and change their meaning. This skill exists because the second kind is invisible to the
compiler and has to be read out of the decompiled sources.

Read `docs/KSA-MODDING-NOTES.md` and CLAUDE.md's *After a KSA update* before starting.

## What you need

- The new KSA build **installed** (`/mnt/c/Program Files/Kitten Space Agency`).
- A checkout of the private **`LaurensDeV/ksa-game-assemblies`** repository. Clone it next to
  this one so `Directory.Build.props` finds it: `git clone git@github.com:LaurensDeV/ksa-game-assemblies.git ../ksa-game-assemblies`
- `ilspycmd` (`dotnet tool install -g ilspycmd`). **Do not upgrade it as part of an upgrade** —
  a different decompiler version rewrites the whole corpus and buries the real diff.

Everything below needs `source tools/env.sh` or the `tools/*.sh` wrappers; bare `dotnet` is 8.0
and cannot build this.

## Order matters

The private repository must be pushed **before** this one, or CI here fails against a lock it
cannot satisfy yet.

## 1. Confirm the update is real

```bash
./tools/check-ksa-version.sh          # what RocketWerkz publish vs what we pin
./tools/check-assemblies.sh --game    # what is installed vs what we pin
```

If both say we are up to date, stop — there is nothing to upgrade, and the issue that sent you
here can be closed.

Write down the new build number. It is needed in seven places later (step 6).

## 2. Refresh the binaries and the corpus

```bash
./tools/sync-import.sh                                    # local Import/
./tools/sync-assemblies.sh      ../ksa-game-assemblies    # the mirror's DLLs
./tools/decompile-assemblies.sh ../ksa-game-assemblies    # the mirror's sources
```

Then, **in the private repository**, set `current/KSA_BUILD` to the new build number and commit
the DLLs and sources **together** in one commit. Committing them apart makes the next diff
straddle two commits for no reason.

Do not push yet if you want to inspect the diff first — it is local either way.

## 3. Find what actually broke

```bash
./tools/ksa-api-diff.sh ../ksa-game-assemblies
```

This reads `docs/KSA-API-SURFACE.md` — the members this mod genuinely binds to, extracted from
the compiled assembly's metadata — against the new corpus, and answers two questions:

**Missing members.** Mechanical and precise. Each one is a break you must fix. `MOVED` means it
is no longer declared on the type we use it through, which usually means a refactor into a base
class and usually still compiles — check, do not assume.

**Changed files.** The decompiled files defining types we use that this update touched. **Read
these.** This is the whole reason the corpus exists: a method that kept its name, its
parameters and its return type, and changed what it means, compiles perfectly and is wrong in
game. That class of bug — offsets measured from the wrong origin, velocities carrying 29.8 km/s
of ecliptic motion — is the one this codebase is built to avoid, and a KSA update can reintroduce
any of it silently.

Read the actual diff for each hit:

```bash
git -C ../ksa-game-assemblies diff HEAD~1 -- current/src/KSA/KSA/Vehicle.cs
```

Pay particular attention to anything touching:

- **Reference frames** — `Ecl`, `Ego`, `Asmb`, `VehicleAsmb`, `ParentAsmb`. A change in what a
  property is relative to is catastrophic and completely silent.
- **Units** — metres vs kilometres, radians vs degrees, seconds vs ticks.
- **`Part` transform plumbing** — `Asmb2ParentAsmb`, `PositionParentAsmb`, `Scale`, and
  especially `ResetCachedPosMatrixValues`. Articulating a subpart depends on the cache being
  invalidated exactly as it is now.
- **Anything the mod calls once per frame** — the cost model matters as much as the semantics.

### The named contracts

These are the specific behaviours the mod is built on that **no tool can check**. Each is a fact
about what the engine does rather than what it declares, so `ksa-api-diff.sh` sees nothing, the
build succeeds and the tests pass. Read the cited file when the diff touches it, and confirm the
claim still holds.

| Contract | Where | What breaks if it moves |
| --- | --- | --- |
| A write to a vehicle's velocity in a `Vehicle.PrepareWorker` prefix survives the frame; one from a StarMap hook is overwritten by the worker's result | `Vehicle.cs`, `Program.cs` | Every car stands still or drifts, with nothing in the log. `docs/KSA-FRAME-ORDER.md` has the order. |
| `PhysicsStates.GetStatesCcf` is the planet-fixed state, and a railed landed craft is woken by `TakeOffRails` | `Vehicle.cs`, `PhysicsStates.cs` | The springs read the ground metres off, or a parked car never wakes when driven. |
| `GetTerrainHeightFromDirCce` answers the height of the ground under a direction | `Celestial.cs` | The springs push against the wrong surface. `docs/KSA-TERRAIN.md` has what each surface is. |
| `ResetCachedPosMatrixValues` also marks the tree's `PartTreeRenderData` dirty | `Part.cs`, `PartTreeRenderData.cs` | A wheel, coil or steering wheel write is never drawn, not just drawn stale. |
| `IAnimProcessor`s run inside `KittenRenderable.UpdateRenderData`, before skinning, and `_characterAvatar` is still the field holding the model | `KittenRenderable.cs`, `AnimatedRenderable.cs` | The driver's hands fall back into its lap. |
| `Vehicle.UpdateSeatedCrewRenderData` draws seated crew for the controlled craft alone | `Vehicle.cs` | The patch in `Ksa/SeatedCrewHook.cs` draws a car's crew twice, or not at all. |
| Core's flags on `NoFaceSnapping` and the `IVASeat`/`EVADoor` elements | `Content/Core/*GameData.xml` | Seats or doors stop working with nothing failing. |

Add a row whenever a fix depends on the engine *doing* something rather than *declaring* it.

## 4. Diff Core's XML, because the compiler never sees it

`ksa-api-diff.sh` reads compiled metadata. The mod also binds to KSA's **XML serialisation
contract** — `PartGameData`, `EditorTagDef`, `Connector`/`Flags`, `MeshAtlas`, `PbrMaterial`, the
particle and sound definitions, the character attachment. None of that compiles against anything,
so a renamed element or attribute produces no error at all: the part loads with the field at its
default, or does not load, and the only trace is a line in KSA's own log.

Core's own content is the reference schema, and it ships in the install:

```bash
diff -ru "<old install>/Content/Core" "/mnt/c/Program Files/Kitten Space Agency/Content/Core" \
    --include='*.xml' | head -200
```

No old install to hand? The mirror's previous commit has the assemblies but not Core's content, so
the fallback is to read the deserialised types directly — `PartGameDataReference.cs`,
`PartTemplate.cs`, `EditorTagDefinition.cs` — and compare their `[XmlElement]` and `[XmlAttribute]`
names against what the asset XML at `src/KSACars/` actually writes.

What to look for, in order of how quietly it fails:

- **A renamed or removed attribute** the mod sets. Silently defaults.
- **A new required element** on a type the mod declares. Usually a load error, so at least loud.
- **A renamed Core Id** the mod references — a mesh, a material, an editor tag.
  `./tools/validate-parts.py` catches this class, but **only run against the install**; with
  `--offline` it cannot see Core at all and passes regardless.

## 5. Build, fix, test

```bash
./tools/build.sh
./tools/test.sh
```

Fix compile errors against the decompiled sources rather than by guessing — the definitive
answer to "what is this now" is in `../ksa-game-assemblies/current/src`. `tools/apidump` is the
faster way to ask a narrow question:

```bash
cd tools/apidump && dotnet run -- ../../Import members KSA.Vehicle
```

The tests do not touch KSA, so they passing proves the simulation still works, **not** that
the game binding does. Step 8 is where that is settled.

## 6. Record the new build everywhere it is written down

```bash
./tools/check-assemblies.sh --update      # rewrites the digests in ksa-assemblies.lock
```

Two the tooling owns:

- `ksa-assemblies.lock` — the `build` line, **by hand**. Every other check reads it, so it is the
  source of truth for the rest.
- `../ksa-game-assemblies/current/KSA_BUILD` — in the private repository.

Then **five prose files**, every one of them enforced. `tools/check-docs.sh` fails if a file
mentions any build number and does not mention the lock's, so a missed one is a red CI run rather
than a quiet lie — but only if you run it, which is step 7.

- `CLAUDE.md` — the **KSA build** line under *Environment*
- `README.md`
- `docs/KSA-MODDING-NOTES.md`
- `docs/KSA-CAMERAS.md`
- `docs/BLOCKED-ON-KSA.md`

The last three each name the build they were read against, which is what makes their claims
datable. Do not sweep the number through with `sed` and call it done: a doc that says it was read
against the new build is asserting someone read it against the new build.

`docs/KSA-CAMERAS.md`, `docs/KSA-FRAME-ORDER.md`, `docs/KSA-TERRAIN.md` and
`docs/BLOCKED-ON-KSA.md` cite `file:line` throughout, and line numbers move on every update.

**Leaving them stale does not work, because the build number cannot date them.** `check-docs.sh`
requires all five prose files to name the build in the lock, so the moment the number is updated
the file claims to be current and its citations are not. Either re-derive the
citations in the file you touched, or say in the file which build its citations are against and
why it differs from the header.

Then regenerate the surface, because fixing breakages usually changes what the mod binds to:

```bash
./tools/api-surface.sh
```

## 7. Verify the whole chain

```bash
./tools/check-all.sh                 # everything CI runs; the pre-push hook runs it anyway
./tools/validate-parts.py            # against the install, NOT --offline, so Core is readable
```

`check-all.sh` is the one that matters and is easy to skip in favour of the handful of checks that
feel related. It carries `check-docs.sh`, which is what proves step 6 was done properly — fixing
five build numbers and never running the check that reads them is the obvious way to get this
wrong.

Then work through the recheck list at the top of **`docs/BLOCKED-ON-KSA.md`** and tick what has
changed. Nothing else in this procedure will surface those: they are calls the engine does not
make and modules it does not have, so `ksa-api-diff.sh` sees nothing, and a build that succeeds
proves nothing about them. This is the one moment any of them can become possible.

## 8. Look at it in the game

**A green suite is not evidence.** The tests link `Sim/` and reference no KSA assembly at all, so
they pass identically whether the game binding works or is completely broken — which is the exact
failure an upgrade produces. Nothing above this line has run a single line of KSA.

```bash
./tools/run.sh                       # build, deploy, launch, follow the mod's log
```

The bridge drives it from here without a person at the keyboard: load a save, park a car, seat two
kittens, drive it, capture.

```bash
python3 tools/ksa-mcp/server.py cli launch '{"save":"<save>"}'
python3 tools/ksa-mcp/server.py cli spawn '{"craft":"Eldorado","name":"Eldo","lat":-24.0,"lon":-62.0}'
python3 tools/ksa-mcp/server.py cli drive '{"craft":"Eldo","focus":true,"seat_kittens":"<a>,<b>","seconds":0.1}'
python3 tools/ksa-mcp/server.py cli drive '{"craft":"Eldo","throttle":1,"steer":0.5,"seconds":6}'
python3 tools/ksa-mcp/server.py cli capture '{"label":"upgrade"}'
```

Worth a minute each, because each rests on a contract from step 3 that no tool checks: the startup
line in the mod's log; the car settles level on its springs and pulls away (the physics window and
the ground read); its wheels turn and steer (the subpart write); the driver's hands are on the rim
(`hands_solves` in the drive report, the animator hook); and with one kitten out on EVA the other is
still drawn in the car (the seated-crew patch).

Anything that misbehaves is a finding against the new build, recorded as one — not folded into the
retarget commit as a silent fix.

## 9. Push, private repository first

```bash
git -C ../ksa-game-assemblies push        # MUST be first
```

Then commit here. Use Conventional Commits — semantic-release parses them, and a message that
does not parse silently produces no release:

```
build(ksa): retarget 2026.9.1.5200
```

`build` is a patch bump. If the upgrade changed mod behaviour a player would notice, that part
belongs in its own `fix` or `feat` commit — do not bury it in the retarget.

## If it goes wrong

- **Compile errors mentioning types that clearly still exist** — `Import/` and the mirror have
  drifted. `./tools/check-assemblies.sh` says which folder the build actually resolved.
- **A huge, unreadable corpus diff** — either ilspycmd was upgraded or the mirrored assembly set
  changed. Both rewrite everything. Redo the corpus with the pinned ilspycmd and the same set.
- **CI fails on the lock but it builds locally** — the private repository was not pushed, or was
  pushed after this one.
- **Tests pass and the game misbehaves** — expected, and exactly what steps 3 and 8 are for. The
  tests never load KSA, so they cannot tell you anything about it either way.
- **`check-docs.sh` fails on a build number after you updated them** — there are five prose files,
  not three. `README.md` and `docs/BLOCKED-ON-KSA.md` are the two that get forgotten.
- **A part loads but behaves differently, with nothing in any log** — an XML attribute was renamed
  and is now sitting at its default. Step 4.
