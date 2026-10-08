# Blocked on KSA

What the cars cannot do, or do only by working round the engine, with the engine reason and what
would unblock it. Read against KSA build **2026.10.10.5554**.

A KSA update is the only thing that changes any of these, and none will show up in
`tools/ksa-api-diff.sh`: they are things the engine does not do rather than members that moved. So
after every update, recheck this list:

- [ ] Does KSA have wheels, or any ground-contact module a part can declare?
- [ ] Does KSA draw the seated crew of a craft that is not the one being flown?
- [ ] Is a kitten's animator reachable without reflection?
- [ ] Is terrain friction still one number for every collider?
- [ ] Can Fill Seats still seat a kitten that is already out on EVA?
- [ ] Has KSA a way of its own to keep ground clutter off a structure, a footprint or a mesh it is given?
      **If it has, hand it the roads' outline and take this mod's own masking out**: see the section below.

## No wheels

KSA has no wheel, tyre or suspension module. A car is a part with mass and colliders like any other,
and everything that makes it drive -- the springs, the tyres' grip, the engine, the gears -- is this
mod's, in `Sim/BuggyDrive.cs`. The forces are applied as one impulse through the centre of mass and
one about it, written from a Harmony prefix on `Vehicle.PrepareWorker` (`Ksa/PhysicsHook.cs`),
because that is the only window in which a write to a vehicle's state is not overwritten by the
worker's result before anything reads it.

**Would unblock it:** a wheel module, or a public per-step force hook on `Vehicle`.

## Terrain friction is one number

KSA's terrain contact uses a fixed friction of about 0.95 for every collider. A wheel box resting on
the ground would drag like a skid, so each car's colliders stand clear of the ground at rest and only
touch in a crash; the springs hold the car up.

**Would unblock it:** a per-collider friction, or a rolling contact.

## Seated crew are drawn for the flown craft alone

`Vehicle.UpdateSeatedCrewRenderData` draws seated kittens only for `Program.ControlledVehicle`. In an
open car that means the kitten left sitting disappears the moment the other gets out, because the
flown craft is now the kitten on EVA. `Ksa/SeatedCrewHook.cs` postfixes it and draws the crew of
every car that is not being flown.

**Would unblock it:** crew drawn for every craft with visible seats, or a per-part flag for it.

## The kitten's animator is private

The driver's hands are solved onto the steering wheel by an `IAnimProcessor` on the seated kitten's
model (`Ksa/DriverHands.cs`), which is the hook KSA turns its eyes with. The model is reachable only
through the private field `KittenRenderable._characterAvatar`, so the hands depend on a reflected
name. Losing it leaves the hands in the kitten's lap and logs why.

**Would unblock it:** a public accessor for a kitten's character model.

## A kitten out on EVA can be seated again

The launch window's Fill Seats can seat a kitten who is already out on EVA somewhere else. Getting
that kitten out of the car then collides with the existing EVA vehicle's id -- KSA logs an error
registering a duplicate id -- and control jumps to the other copy. Not this mod's to fix; seat
kittens who are not already out, which the bridge's `seat_kittens` lets a test do by name.

**Would unblock it:** Fill Seats skipping kittens already on EVA.

## Clutter under a road is cleared by working KSA's placement shader again by hand

KSA has no way to be told "nothing grows here". It keeps a bit per clutter instance in each cell of
its grid (`GroundClutterPlacementData`), which a mod can write, but not where each instance is: that
is worked out on the graphics card by `Generate.comp`. So `Sim/ClutterGrid.cs` is that shader's
arithmetic done again, `Roads.TakeClutter` clears the bit of every instance the roads' own outline
covers (`RoadSurface.Over`), and because a cleared bit leaves a collider that is already built
standing, `KsaWorld.RebuildClutterColliders` has KSA build them all again by holding its clutter
collisions setting off for a moment. The masks are written into a save, so a save keeps the clearing
of a road it does not keep.

Three things in that can break without a word: a change to `Generate.comp` moves every instance
(`Roads.CheckPlacement` compares with KSA's own once a body and warns in the log), a cell over the
edge of a cube face is left alone, and the settings toggle is nobody's interface.

**KSA's own developers are looking at this.** On 2026-10-08 Linx, a graphics programmer at
RocketWerkz, asked how the roads do it: their launch sites skip an instance by how far it is from the
site, which they said does not scale, and they are thinking of a coarse collision mesh or bounding
geometry whose cells are excluded.

**Would unblock it:** an exclusion a mod can register, by footprint, polygon or mesh, that also takes
away the colliders already built. **When KSA has one, use it and delete this mod's**: the roads have
an exact outline to give it (`RoadSurface.Over`, or the meshes `RoadTessellation` makes), and
`Sim/ClutterGrid.cs`, the mask writing and its restore in `Ksa/Roads.cs`, the placement check and
`KsaWorld.RebuildClutterColliders`' use for roads all go. It may also write the same masks this
does, in which case the two would fight: check that first.
