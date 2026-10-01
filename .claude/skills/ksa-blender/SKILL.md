---
name: ksa-blender
description: Build a 3D asset for Kitten Space Agency by authoring it in Blender over MCP. Use when modelling a new part, editing an existing one, importing a .glb somebody authored by hand, baking textures for one, or debugging art that renders wrong in game (sparkle, flicker, a subpart in the wrong place, a part that loads invisible). Covers the authoring loop, the export contract KSA actually enforces, and the defects that are invisible outside the game.
---

# Making art for KSA

**New assets are authored in Blender, over MCP, in a live session.** Not written as a headless
Python script, not driven through the Blender CLI. If the MCP connection is down, the answer is to
fix the connection — not to fall back to batch scripting. `tools/model/README.md` lists the
checkers that go with it.

> ## Stop before you bake
>
> **Ask the human to look at the geometry in Blender, and wait for a yes, before unwrapping,
> baking or exporting.** Frame it for them with `jump_to_view3d_object_by_name`, say what you have
> built and what is next, then stop.
>
> Ask them to *look*, do not send them pictures. They have the document open, so the model is
> right there: orbitable, at any angle, lit however they like, and their view of it beats any
> render you could hand over. Framing it in their viewport is the whole of your job here.
>
> The gate is not politeness. Everything downstream of the unwrap is welded to the geometry:
> bodies sharing an atlas pack together, so changing one body afterwards forces a re-unwrap and a
> re-bake of *all* of them. A joint two primitives share and a 2.5 mm recentre both cost what a
> redesign costs, and both are free right up until the unwrap.
>
> It is also the last cheap moment to judge the shape. Whether a silhouette reads as the right
> thing is a matter of taste, and taste is not yours to sign off.

---

## 1. The authoring loop

The Blender Lab MCP addon (`blender.org/lab/mcp-server`) is an interactive channel into a session
somebody has open, with a viewport they are watching. That is the whole advantage over the old way,
and it changes how to work: build a piece, look at it, adjust, repeat.

### Prefer the dedicated tools

The server ships about twenty, with its own instructions on when to reach for which. **Read those,
and do not keep a copy of them here.** A summary is lossy in the one direction that hurts: the tools
it omits become invisible, and their jobs fall through to `execute_blender_code`, which is the
fallback for what nothing else fits rather than the interface. There is a screenshot tool, a
viewport-framing tool and a docs search in that list, and a paraphrase that leaves any of them out
costs a great deal of pointless work.

**Look things up rather than discovering them by debugging.** The Blender docs are bundled and
searchable. Every trap in §5 below was found the hard way, and at least one of them is stated
outright in the signature the API search returns for it: `bpy.ops.object.bake` defaults `use_clear`
to `False` on the operator whatever the scene setting says, which quietly wipes the body baked a
minute ago.

### What still holds

- **Read the scene back rather than assuming.** `execute_blender_code` returns stdout, and can set
  a `result` dict, so measuring is one call: bounds, vertex counts, whether a modifier is still on
  the stack, what a UV island actually covers. Never assume a number you could ask for.
- **It is somebody's open document.** Say what you are about to change before changing it, and do
  not silently delete objects they made. `bpy.data.libraries.load(path, link=True)` reads another
  `.blend` without opening it, which is how to check a file without disturbing their session.
- **Prefer `bpy.ops` for standard actions** inside the code you do run: it handles defaults and
  context. Reach into `bpy.data` for precise control or to avoid side effects.

`tools/model/preview.sh` renders an *exported* `.glb` from a few angles without touching the
live session — the check on what actually left Blender, which is not always what you think you
built. That is a different question from looking at the model, and the only one renders are still
the right tool for.

### When the connection is down

The addon listens on **127.0.0.1:9876** inside Blender's own process, and only once **Start MCP
Server** has been pressed in its sidebar. Two things reliably go wrong:

- **WSL cannot reach Windows loopback.** Across the default NAT boundary `127.0.0.1` is not shared.
  Either run the MCP server on the Windows side so the socket is local to it, or give WSL
  `networkingMode=mirrored` in `.wslconfig` and restart it.
- **Server and addon must be the same implementation.** Blender Lab's addon and the `blender-mcp`
  PyPI package are different projects that happen to share port 9876. Point the wrong one at it and
  the handshake half-succeeds, then blocks ~40 s on a command the addon has never heard of before
  failing the client's health check.

---

## 2. Where the source of truth lives

**The `.blend` is the source and it is not in the repository**; what is committed is the export —
the `.glb` and its textures. Two consequences:

- **Keep the `.blend` somewhere safe.** Losing it means the shipped asset can only ever be edited
  as raw geometry again.
- **A committed asset cannot be regenerated from a clean checkout**, so it has to be checked *at*
  the boundary. `checkmesh.py` and `validate-parts.py` are the only gate on art nobody can rebuild.

---

## 3. The export contract

What KSA reads out of a `.glb`, verified against all 44 Core atlases (1054 nodes).

| Rule | Evidence |
| --- | --- |
| **No node transform on a rendered subpart.** Translation, rotation and scale baked into the vertices. | Of 1054 Core nodes the only ones carrying a transform are `_ColPrim_*` helpers and floating-point dust (1e-16, 3e-07). |
| **Node name == mesh name.** | Holds for every rendered subpart in Core. |
| **A `_VM` twin of every subpart.** | 419 of Core's 1054 nodes. The editor's preview variant; duplicating the geometry is fine at these poly counts. |
| **`_ColPrim_*` carries the collider.** A unit box or cylinder with the volume in its node transform, never rendered, no UVs. | Core ships 58. Read `<Collider>` out of it rather than eyeballing one. |

**Author to this and there is no import step**: the export is copied in as it is and only
declared. An export that breaks it needs a tool to bake, reframe and recentre it afterwards,
and it is far cheaper to get right in Blender than to correct afterwards.

A part that loads but renders nothing is nearly always a name mismatch — `<Mesh Id="…">` must be the
atlas name exactly. Silent in game; caught by `./tools/validate-parts.py`.

### Node name == mesh name is not something you can just set

Blender will not tell you when it refuses a name. `ob.data.name = "X"` silently becomes `"X.001"`
if an orphaned mesh already owns `"X"`, and orphans are ordinary: deleting an object leaves its
mesh datablock behind, so rebuilding a body during iteration is enough to produce one. The export
then writes a node called `X` whose mesh is `X.001`, which breaks the contract above.

Two habits close it:

- **Purge before naming, name before exporting.** `bpy.ops.outliner.orphans_purge(do_local_ids=
  True, do_linked_ids=True, do_recursive=True)`, then set `ob.data.name = ob.name`, then export.
- **Read the file back and assert it**, because the only other symptom is a part that renders
  nothing in game. `checkmesh.py` does this now and fails on a mismatch, so running it after every
  export is the check — see §6.

> **A purge is not free.** It deletes every datablock nothing currently points at, which includes
> bake target images between passes and a material you have unassigned for a moment. Give images
> `use_fake_user = True` before you purge, or expect to rebake.

### Exporter settings

```
Format            glTF Binary (.glb)
+Y Up             OFF          <-- the one that matters, see §4
Apply Modifiers   ON
Include           Selected Objects (or the collection)
Data              Mesh: UVs, Normals.  No cameras, no lights
Materials         Export, never Placeholder or None  <-- a <GltfFile> built with no material throws
Compression       off
```

"+Y Up off" fixes the axes but does **not** clear object transforms.
`bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)` over the selection, or
`Object > Apply > All Transforms`.

---

## 4. Coordinate system

Exporting with **+Y Up off** means Blender coordinates *are* what KSA reads, and what `<Transform>`
and `<LocationAsmb>` mean. Leave it on and Blender swaps Y and Z: a vehicle exports three metres
long and eight wide, and nothing warns you.

**Part space:**

- **+X out of the surface the part attaches to.** Up for something that stacks; for something hung
  under a wing it points *away from the pylon*, downward.
- **+Y along the host's long axis.** A long body's nose points +Y.
- **+Z the host's right.**

The origin sits on the mounting face, so every coordinate reads as an offset from where it bolts
on. **Model in this frame from the start.** A model authored to another convention needs a rotation
baked into the vertices — it cannot be parked in the XML, because a mod that moves a subpart
rewrites its `<Transform>` each frame and overwrites it on the first update.

---

## 5. Materials and textures

```xml
<PbrMaterial Id="…">
  <Diffuse      Path="Textures/….png" Category="Vessel" />
  <Normal       Path="Textures/….png" Category="Vessel" />
  <AoRoughMetal Path="Textures/….png" Category="Vessel" />
</PbrMaterial>
```

- **PNG is fine.** KSA loads PNG and `.ktx2` and mixes them in one material. No encoder needed.
- **`AoRoughMetal` is R = occlusion, G = roughness, B = metalness** — the glTF ORM convention. Not a
  guess: Core's `default_pbr.png` is `(255, 180, 0)`.
- **Several atlases and several materials per mod are fine.** Core does it.
- **Keep the asset XML at the mod root.** `<MeshAtlas Path="…">` is relative and it is undocumented
  whether it resolves against the mod root or the XML's own directory; at the root both readings
  agree. Asset *folders* below it can be reorganised freely — only moving the XML reopens that.

**One material per part, not one per body.** Three 2048² sets for three bodies is nine files and
8.7 MB, where one unwrap across all three is three files and one material. Unwrap the
whole part into a single atlas before baking.

### Unwrapping several bodies into one atlas

Select them all, edit them together, and let Blender pack them as one set: `smart_project`, then
`average_islands_scale` so texel density is uniform across bodies, then `pack_islands`. Ask
`uv.select_overlap` afterwards and count the selected faces per object — zero, or two islands are
sharing texels and one will bake over the other.

Two bodies have packed to 95% coverage of a 1024² this way, which is the argument against a set
per body.

### Baking, and the four traps in it

Build the material out of **object-space coordinates** rather than painting in UV space. A
`ColorRamp` on constant interpolation, driven by the object's own X, gives every band on a body
at once and does not care where the islands landed — so a repack cannot smear the markings. Its
one trap is the mirror of that strength: translating the mesh afterwards moves the pattern
relative to the geometry, so a recentre means a rebake.

Then, in order:

1. **`DIFFUSE` with `pass_filter={'COLOR'}`** — no lighting baked in.
2. **`ROUGHNESS`** and **`AO`** into scratch images. AO at ~64 samples; the rest need one.
3. **Metalness has no bake pass.** Route whatever feeds Principled's Metallic into an `Emission`
   shader, point the material output at it, bake `EMIT`, then put the BSDF back.
4. **Compose ORM yourself**: R = AO, G = roughness, B = metalness. Fill the background with
   `(1.0, 0.5, 0.0)` and copy in only where roughness is non-zero, so unbaked texels are not
   fully occluded black.

The traps, all of which fail quietly:

- **`bpy.ops.object.bake(use_clear=…)` overrides `scene.render.bake.use_clear`.** Baking a second
  object into the shared image wipes the first one's islands unless you pass `use_clear=(i == 0)`
  on the operator itself. The scene setting is ignored when the operator is called from Python.
- **Bake each body with the others hidden** (`hide_render`). Ray-traced passes see the whole
  scene, and bodies sitting in their own export frames intersect each other at the origin. A body
  that moves away in game is not occluded by the one it sits against in the file, either.
- **`is` does not work on Blender RNA wrappers.** `bpy.data.materials[…].node_tree.nodes["…"]`
  returns a fresh Python object each access, so `link.to_node is bsdf` is `False` while the link
  plainly exists. Compare with `==`, or by name. The symptom is an all-zero map, baked without
  complaint.
- **A baked image lives only in memory until it is written out.** An image made with
  `bpy.data.images.new` is not saved with the `.blend`, so a crash — or simply reopening the
  file — loses every bake done so far. `image.save()` each pass the moment it finishes.
- **Keep each MCP call short.** One call baking every pass for every body outlasts the
  connection's timeout, and Blender cannot answer while it bakes. A whole bake has been lost to a
  crash inside exactly such a call; a pass per call, with the image written after each, costs
  nothing.
- **Check the result, do not assume it.** `min`, `mean` and `max` over the pixels costs one line
  and is the difference between a bad map and a bad map you shipped.

### The background of the atlas is not empty, it is a colour you are choosing

Nothing samples outside a UV island at full resolution, so an unfilled background is invisible in
a texture viewer and in a close-up. **Mipmapping is what finds it**: every lower mip averages 2×2
blocks, so whatever sits beside an island bleeds further into its edge the further away the part
is drawn. Left black, that is dark speckle crawling over the paint — *not* reproducible anywhere
except in game at range.

Three things follow, and the third is the one that is not obvious:

- **`bake(use_clear=True)` clears to black**, whatever `generated_color` said. Fill the target's
  pixels yourself and bake with `use_clear=False` throughout.
- **`bake.margin_type` defaults to `ADJACENT_FACES`**, which fills a seam from the face adjoining
  across it and leaves an island's *outer* boundary unfilled where nothing adjoins. `EXTEND`
  dilates unconditionally.
- **A margin wide enough to beat mipmapping is wide enough to ruin the atlas.** Islands sit a few
  texels apart, so dilation past that gap writes one body's colour over another body's island
  *interior*. Measured on one 2048² atlas: a 48 px margin put a neighbour's dark grey over
  **12.95%** of a white body's island — grey blotches — where 8 px puts it over 0.01%.

So: **a small bake margin, and `tools/model/dilate-atlas.py` for everything beyond it.** Each
empty texel takes the colour of its nearest baked one, so a mip only ever averages an island with
more of itself, at any radius, with no cross-contamination.

> **Bake over a key colour.** Generate every target filled with something the bake cannot produce
> (pure green, on a model with no green in it) and bake with `use_clear=False`, so whatever is still
> keyed is exactly what was never written. That is the mask, and it is *exact*.
>
> A mask rasterised from the atlas's UVs is only an approximation and the error is not harmless:
> it over-covers, which leaves unbaked texels *inside* the mask, and a nearest-neighbour fill then
> propagates them outward as a spreading stain. That happened here and was caught only by counting
> key-coloured texels afterwards — which is why the tool verifies its own output and exits
> non-zero if any key survives.

**glTF's V runs from the top and Blender's from the bottom.** Any script comparing a mesh's
exported UVs against a baked PNG must use `v` directly, not `1 - v`. Getting it backwards reads
the atlas upside down, which looks exactly like a texture full of holes — it produced a confident,
entirely false "491 of 916 triangles sample black" here before the orientation was fixed.

---

## 6. The defects that are invisible outside the game

Both read as flickering white speckle crawling over the part, and **Blender's preview reproduces
neither**, because the preview material has no normal map wired in. Diagnose with the checker, not
by eye: the symptom is identical whichever cause it is.

```bash
./tools/model/checkmesh.py <atlas.glb>                    # exits non-zero
./tools/model/checkmesh.py <a.glb> <b.glb> --near-max 0   # several at once; each is reported
./tools/model/checkmesh.py <new.glb> --compare <old.glb>  # geometry diff, not a byte diff
```

It also checks the **node/mesh name pairing** from §3, which is the one contract violation with no
visible symptom outside the game.

**Zero UV area.** A face whose loops share one UV has a zero UV derivative, so the tangent is
zero-length, `normalize()` gives NaN, and NaN survives being multiplied by zero — a flat normal map
cannot rescue it. Watch for collapsed faces from a UV sphere's poles, from booleans and from
decimation. A face with no area in 3D has none in UV either, so the fix is to delete it: no
amount of re-unwrapping gives a degenerate triangle a derivative.

**Coplanar faces.** Two surfaces on one plane make the depth buffer pick a winner per pixel per
frame. In authored work this is a modelling habit rather than a script bug: sink one surface into
the other rather than butting them, and give a cap a different facet count from the tube it caps.

> **Pass `--near-max 0`.** The default near-coplanar band was calibrated for primitives assembled
> with a modelling skin, so on a hand-built model it reports every deliberate panel step and shell
> wall — hundreds, none of them mistakes. `--near-max 0` silences that advisory while leaving
> zero-UV-area and *exact* coplanar overlaps checked in full, because neither of those is ever
> deliberate.

---

## 7. Articulation, and the part around it

Writing a subpart's transform each frame moves it. Confirmed in game.

- **`ResetCachedPosMatrixValues()` after the write**, or `Part` serves its cached matrix and the new
  value is silently ignored.
- **A subpart rotates about its own mesh origin.** So export a moving body *recentred on its pivot*
  and put the offset back with `<Position>`. The pivot then exists in the model, the XML and any code
  that drives it; keep one of them the source and check the others against it.
- **Bodies turning on one bearing share one pivot**, which is what lets an inner body sweep its
  travel without leaving its housing.
- **SubParts do not nest.** Every `<SubPart>` is placed against the `<Part>`; the mod composes the
  rotations itself.
- **Model a body at its working pose**, and apply motion as a rotation away from that reference. A
  refused write then leaves the part looking right rather than inside-out.

**A shipped part's subpart list is append-only.** KSA pairs a saved part with its definition
positionally, so removing a `<SubPart>` throws `IndexOutOfRangeException` from inside
`Popup.DrawAll` and **terminates the game** on every save holding that part. Adding and renaming are
free.

### Three editor gates that fail silently

- **`IsAllowedAsRootPart` rejects a part if *any* connector is `ToSurface`/`FromSurface`.** A vehicle
  roots; an attachment rides. Pick one.
- **A `<Connector>` with no `<Flags>` is a node connector.** `ToSurface` is the opt-in for radial.
- **Core's `Radial` tag is a `FaceSnapTargetBlacklist`, and the blacklist beats the whitelist** — so
  nothing can be mounted *on* a part carrying it. Right for an attachment, wrong for anything meant to
  carry one.

`docs/KSA-MODDING-NOTES.md` has all three with the decompiled evidence.

### Clearance

A render only shows the poses it was asked for, so check a moving body against what it could strike
across its whole travel — sweep its vertices in Blender. Its bodies can also share planes, and
`checkmesh.py` reads one mesh at a time — so the cross-body plane pass inside `validate-parts.py`,
which places each body as the part XML does and looks for faces two *different* subparts share, is
the check that sees it. It reads `<Rotation>` as well as `<Position>`, which it has to: a body placed
with a quarter turn laid out by position alone lands across its neighbour and the pass finds nothing.

**And `_ColPrim_` is a source of numbers, not something KSA reads out of your `.glb`.** The engine
takes the collider from `<Collider>` in the GameData XML. Authoring the node is Core's convention
and a convenient place to measure from; declaring the box from the mesh bounds is equally valid.

---

## 8. Checklist for a new asset

1. **Model it in Blender over MCP**, in part space (§4), to the export contract (§3).
2. **Ask the human to look at it in Blender, and wait.** The geometry is signed off or it is not
   finished. Nothing below this line is cheap to redo — see *Stop before you bake* above.
3. **Unwrap the whole part into one atlas** and bake Diffuse / Normal / ORM (§5).
4. **Export** with +Y Up off, all transforms applied, orphans purged, moving bodies recentred on
   their pivots.
5. **`checkmesh.py --near-max 0`** — node/mesh names, zero-UV-area triangles, coplanar overlaps.
6. **`preview.sh`** — look at what actually left Blender. It renders the file *naively*, node
   transforms and all, so a multi-body atlas shows each body in its own frame: a rail along +Y and
   a body along +X will cross each other, and that is correct rather than a fault.
7. **Declare it**: a `<SubPart>` per moving body, a `<Part>`, and a `<PartGameData>` with the collider
   read off `_ColPrim_` or off the mesh bounds, mass and editor tags.
8. **List the asset XML in `mod.toml`** if it is a new file — one left out is never loaded, and
   nothing reports it. A part is inert; whatever it does, the mod does by finding it by part Id.
9. **`validate-parts.py`**, then **`./tools/check-all.sh`**.
10. **Deploy it, and look at what you are replacing first.** `./tools/deploy.sh` overwrites the
    installed mod wholesale, so a build from a worktree quietly removes whatever the last build
    had that yours does not. Check the branch and the mods folder before installing over them.
11. **Look at it in game.** The suite cannot see what KSA does with the transforms, and a behaviour
    fix is not fixed until it has been seen there.
