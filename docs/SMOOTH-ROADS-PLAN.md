# Plan: roads as one smooth surface

A proposal from two pieces of research on 2026-10-07. Nothing here is built, and nothing in it has been
seen in game.

## Why

A road is drawn, and collides, as one box for each 2 m stretch. Boxes end square, so they overlap on
the inside of a bend and are stretched to cover the outside; each is flat across and has its own slope,
so a climbing bend is a staircase of facets; and each is shaded flat. A row of box colliders also throws
a hull sliding over its seams. The wheel surface, the drawing and the collider are three approximations
of the road, and most of the faults found in the roads came from the three disagreeing.

## What was found

### KSA can draw a mesh made while it runs

Read in the decompiled game, not run.

- The static renderer draws whatever is in three shared buffers owned by
  `SuperMeshRenderSystem.MeshIndirectSystem`: positions, normal with UV, and indices. Its `AddMesh` is
  public and takes arrays. A glTF file is only one way of filling it.
- Nothing in those buffers is ever freed, so a mesh is not added for each edit. A slot of fixed size is
  reserved once and its bytes written over, the index count set to what is used.
- A `StaticMeshRenderable` over such a slot is lit, shadowed and depth-tested as the slab is. Normals
  are per vertex; tangents are not needed.
- **KSA holds 1024 mesh instances a view and throws past that**, from inside its render, which closes the
  game. A box a stretch reached it at about 2 km of road in view: seen, laying a 3.3 km oval. Roads now
  draw distant stretches as fewer boxes and stop at 800.

Dead ends: the swept tube (its normals are radial), one instanced triangle a triangle (the instance limit,
and one normal a triangle), a file written and reloaded (no reload exists).

### A triangle mesh is the better collider

Measured in BepuPhysics on its own, with KSA's contact settings.

- A hull sliding at 80 m/s through a dip stays within a centimetre of a mesh, and is thrown metres into
  the air by a row of boxes at their seams.
- 5 km of road as one mesh costs a fifth of what 2,500 boxes do each step.
- A thin sheet does not let a falling body through at 200 m/s. From below it is open, so a raised road is
  a closed deck.
- Shapes can be freed; that the mod's are not is the mod's doing.

### The road's own shape has two faults

- **Height is level at every point**, so a steady 5% climb through points 100 m apart is a staircase:
  at 80 m/s, 19 m/s2 up and down with a step at each point, and 7.5% at its steepest.
- **A road takes the ground's height at each sample**, so every bump in the ground is in the road.

## The design

One definition of the surface, which the drawing, the wheels and the collider all sample.

- **A flat chart a circuit**, so widths, junctions and lookups are plain geometry in metres.
- **The centre line stays the chain of cubic curves**, measured by its own length, with a least radius
  of 1.25 times the half width and verge so the inside edge never folds.
- **Height by a monotone cubic through the points' heights**: a steady climb is a straight ramp, crests
  and dips are rounded, nothing overshoots.
- **The ground under a road is smoothed and never buried**: the highest ground across the section,
  taken over a window and filtered. A road can only be filled, never cut, because KSA's terrain cannot
  be cut and a wheel rides whichever is higher.
- **Level across by default, with a bank set at each end of a road**, and a width at each end.
- **Past the edge**: a verge, then an embankment down into the ground; where the edge is more than 4 m up,
  a deck half a metre thick.
- **Stations chosen by curvature**, not every 2 m: to 5 mm in plan, 3 mm in height and a degree a step.
  Fewer than today on most circuits. One row of shared vertices a station, so no piece has ends.
- **A junction is one tilted plane** inside a polygon of the roads' edges with rounded corners; each road
  stops at a square mouth and is brought to the plane's height and slope there.
- **Wheels ask the surface itself**, not the mesh: the mesh is within 3 mm of it by test, and its facets
  would kick a car with 2 cm of travel.
- **The collider is the drawn triangles**, a mesh a 100 m chunk, added only to the bubbles near it.

## Order of work

Each step leaves the game working. Only steps 5 and 6 need the game.

0. **Proof**: one patch of mesh made at runtime, drawn over a parked car and replaced in place.
1. The chart and the centre line by its own length.
2. Height, bank and width profiles and the smoothed ground. Circuit format 2, old files still loading.
3. The surface evaluated and inverted, behind the lookup the wheels use now. Boxes still drawn.
4. Tessellation: ribbons, verge, embankment, deck.
5. Colliders as meshes.
6. The mesh drawn. The slab and its maths go.
7. Junctions.
8. The editor: bank, width at each end, junction radius, warnings, and only the changed chunks rebuilt.

## Not to be built

Clothoids, cutting into terrain, level-of-detail meshes, pillars, automatic banking, lane markings, slip
roads and roundabouts.
