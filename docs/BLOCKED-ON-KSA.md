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
