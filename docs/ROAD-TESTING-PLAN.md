# Plan: driving and testing circuits without a driver

A proposal, not a record of anything done. Written on 2026-10-07 from three pieces of research and
revised after three reviews, each of which rebuilt part of it in a throwaway harness. None of it has
been carried out, and none of the harness numbers come from the game.

## Why

Every session of hand-driving the roads has found a new physical fault, and each was patched without
the patch being driven. The faults are of one kind: the car and the road disagreeing about where the
road is, or about what to do when they meet. Nothing in the suite drives a car on a road, a bend, a
slope or a junction, and the code that decides these things is in `Ksa/`, where the tests cannot link
it.

The aim is that a fault of that kind is found by a machine before a person finds it.

## What is wrong today

Confirmed by two independent harnesses linking `Sim/` unless marked. Neither has KSA's hull colliders or
real terrain, so they show mechanisms and not what the game does.

### The road surface (`Sim/RoadSurface.cs`)

1. **Holes on the outside of every bend.** A point past the end of one piece and before the start of
   the next finds no road: about 10% of the outer edge at a 30 m radius, 2% at 100 m, a quarter-disc at
   a kink.
2. **A wheel follows the road it was on while another rises through it.** Leaving a junction onto a
   climbing road, it stays on the first road's shoulder, then is moved up to the second in one step:
   0.4 to 1.1 m in the cases tried.
3. **The same rule has no bound, so it can put a car on a bridge.** A wheel leaving the side of a
   ground road under a deck 12 m up is told it is 11.7 m inside the deck, and the car is lifted 12 m.
4. **A bend hole on a bridge sticks the wheel to the road below**, and the corner falls through the
   deck.
5. **A low deck grabs a car beside it.** The undrawn shoulder reaches 7.5 m from a road at any height,
   so a car on the grass beside a deck 1 to 2.8 m up is lifted onto it. Every ramp passes through that
   band.
6. **On a climbing bend the inside of the road is a sawtooth** of 4 to 16 cm, each piece being flat
   across its own up.

### The hard stop (`Ksa/Buggies.cs`)

7. **It takes away all of the car's downward speed**, not the part going into the surface. On a
   descent that speed is the car following the slope, so the stop sets it flying level. Read from the
   code; the size of the launch is from one harness only.
8. **It lifts the whole car by its deepest wheel and leaves its pitch alone**, so a car meeting a ramp
   is raised flat. One harness flipped it at speed. Unjudged in game.
9. **Under time warp it is the only thing holding a car on a road**: the springs are skipped and it is
   not.
10. **In low gravity it does the springs' work on ramp entries**, their load being capped at four
    times the car's weight there.

### The editor and the format

11. **A car's memory of its road survives the road being laid again**, which is how raising a road
    carries a car up with it.
12. **Splitting a raised road puts the new point on the ground.**
13. **Nothing limits curvature or gradient.** A corner strength of 0.5 on 30 m legs gives a 2.7 m
    radius under a 4 m half width, and the surface is not defined where the radius is under the half
    width.
14. **Height is level at every point**, so a steady climb through several points is a staircase.
15. **On a side slope the road is buried on its uphill edge**, and between two samples the ground can
    stand above the road: the car rides whichever is higher.

### The car, found on the way

- **Full steering lock asks for 75% of the F2004's grip** (`SteerOverGrip`), so no driver working the
  steering input, human or automatic, reaches its cornering limit.
- **Below about 313 km/h the F2004 pulls a flat 1.25 g whatever its power.** `PowerW` barely affects a
  lap. A reviewer's lap-time calculation put the car 4 to 5% off the 2004 laps with that cap and 2%
  without it.
- **Its top speed in air is about 94 m/s**, the 105 m/s in the profile being the rev limiter. That is
  below Monza's 2004 speeds and above what a high-downforce circuit saw.
- **A negative throttle is full brakes whatever its size**, and reverse below half a metre a second.

## The plan

### Step 0. Commit the road work, and three decisions

The road files are untracked, so there is nothing to refactor against. Commit them to `dev` as the
unfinished feature they are, saying what is unverified.

The owner decides, because the fixes depend on it:

- **A minimum bend radius**, at least the road's half width. Needed before the surface can be fixed.
- **Whether a parked car should ride a road up when it is raised under it.**
- ~~Whether a mod can add a static collider to KSA at runtime.~~ **It can, and it holds a car up**:
  `Ksa/RoadColliders.cs`, seen in game on a parked F2004. So roads get real colliders, the hard stop
  goes, and the car's body, a kitten and other craft stop passing through decks. Still to see: a kitten
  on one, the bubble's origin shifting on a long lap, a save loaded.

### Step 1. Move the road physics into `Sim/`, changing nothing

- `Sim/RoadLaying`: runs to lines with ground, lift, height and sink, through `ITerrainHeights`. It
  also hands the surface each point's height above the ground, which the shoulder fix needs.
- `Sim/WheelGround`: each wheel's ground from terrain, pad and road, the stop, and `GroundPlane.Tilt`,
  with a seam for the launch pads. It returns the lift and the change of velocity; `Buggies` keeps the
  state read and its writes.
- `RoadSurface.TryLocate`: which piece answered, its own normal, and how far out on the shoulder.

Not `RoadSlabs`, unless a test that a slab's corners lie on the surface comes with it.

### Step 2. A rig that can leave the ground: `TrackRig`

Moved up from later, because no fault of the stop can have a failing test without a free body. Beside
the flat `Rig`. A rigid body in the body-fixed frame on a sphere; each step `WheelGround`,
`BuggyDrive.Step`, integrate. Synthetic terrain: sphere, slope, side slope, sinusoid, step. Step
lengths from 4 to 100 ms, fixed, random and replayed.

### Step 3. Tests that hunt, written to fail

None needs a clever driver.

- **Geometry sweeps over `RoadSurface`**: legs, turns, corner strengths, heights, spacings.
  - Every point within half a width of the laid line finds a road.
  - With one level present, the answer with memory equals the answer without.
  - A point walked along a road through a junction is told the highest surface it can reach, with no
    step beyond slope times stride plus 2 cm.
  - A hub walked in from the side is never told it is inside a road more than 0.3 m above it.
  - A bridge bend over a road; leaving a road under a bridge.
- **Bullet runs on `TrackRig`**: steering straight, fixed throttle, fired at each fixture at 10, 30, 60
  and 100 m/s and at angles from 0 to 90 degrees. Ramps, a step on a descent, a deck alongside, a
  junction straight through, off a deck's end, past a kink; at Earth and Luna gravity; at long steps.
- **Offset runs**: a road followed at its centre, at each edge and straddling the edge, at fixed
  speeds.
- **Seeded fuzz**: random throttle and steer from random poses on and beside a road, the seed in the
  failure message.

Judged on, each with a number:

- depth of a wheel under any surface beyond its travel and bump stop;
- the stop's own work, lift times g less half the speed it removed squared, which is zero for a stop
  that neither gives nor takes;
- energy on a coast changing by the work of drag and rolling resistance and no more, **in both
  directions**;
- time with all wheels off a surface smooth enough at that speed;
- pitch and roll against the slope, and not inverted;
- how many times a wheel changes road against how many the route says.

### Step 4. Fix, in this order

1. **Rewrite the surface lookup once.** Within a run, each piece owns the ground between the planes
   bisecting its two joints, so there are no holes outside a bend and no overlaps inside; a prototype
   had no misses and steps under a millimetre at 2 m spacing. Between levels, admit surfaces no more
   than a step above where the wheel's surface was and take the highest. That replaces "nearest to
   where it was" and bounds it. Faults 1 to 4 and 6.
2. **The shoulder only where the road is within its own drop of the ground**, so it always reaches
   the ground. Fault 5. What remains is a stated minimum clearance: a car cannot pass under a road
   less than two metres above its wheels.
3. **Replace the stop with a bump-stop impulse in `BuggyDrive`**: past the end of its travel a wheel
   gets enough load to stop that corner closing this step and no more, at the wheel, outside the load
   cap. It cannot launch, it changes pitch, and it does not depend on the step. Faults 7 to 10. The
   lift stays only for a car put deep into a road. This also acts on terrain, where the hull has taken
   hard landings until now, so all three cars are driven again. **Step 0 found colliders, so this
   is replaced**: a road's strips become collider boxes, thicker than they are drawn so nothing passes
   through in a step, and the stop is taken out. The wheels still read the road's height from
   `RoadSurface`, since the wheel model is the mod's own; the colliders are for the hull and for
   everything that is not a car. The bump-stop impulse stays in reserve if the hull on a collider at
   speed proves too harsh.
4. **A generation on each laid surface**, the car's memory dropped when it changes. Fault 11.
5. **Split at the road's own height.** Fault 12.
6. **The radius limit**, in the editor. Fault 13.

Faults 14 and 15 are changes to what a road is, and wait for the owner.

Commits are the feature's, not `fix`: roads are unshipped, and a behaviour change is not a fix until
seen in game.

### Step 5. Telemetry in the game, then the same bullets

Before any automatic driver in game:

- A row a step in a preallocated buffer, flushed from the frame hook every second so a crash keeps
  it: the state read, what each hub was told by terrain, pad and road, the input, the step, the
  impulse written, whether the stop fired. A header with mass, inertia, centre of mass, gravity and
  air density.
- Replayed headless, the mod's function must give the same impulse exactly: that proves the mod's
  side. What is left between one recorded state and the next is KSA's, a step at a time, at a place on
  the track.
- The first in-game runs are bullets over a laid ramp, with the hold the bridge already has.

### Step 6. A driver that follows a route

Only now.

- **Route**: an explicit line with progress along it, never the nearest road, so a junction or a
  crossing cannot confuse it.
- **Steering**: pure pursuit, look-ahead at least 0.15 s and three steps, the steer input found by
  inverting the speed-dependent lock, which `BuggyDrive` exposes.
- **Speed**: a fixed cruise first. A curvature profile later, from the lesser of the grip and what
  full lock asks for, at 85% of it.
- It slows when far off line, and ends itself with a reason: off the road, flipped, stuck, fell
  through, no longer driven, a step over 0.1 s, its own exception.
- In game: stepped in the physics window after the pause check, its state outside the car's entry,
  started and polled through the bridge without blocking it. A pose write faces the car along the
  route. The first check is whether a car stays simulated five kilometres from where it started.

### Step 7. The car's numbers, which need no road

Independent of everything above, and can run beside step 3.

- Cornering g at 100, 200 and 300 km/h, at the tyres' limit and not at full lock.
- Braking from 300 and from 200 km/h.
- Acceleration from 100 to 200 and 200 to 300 km/h.
- Top speed in air.

Then the owner decides the launch cap, `SteerOverGrip` and one wing setting or two.

### Step 8. Real circuits, last

As an endurance test of the roads: it completes laps on the asphalt, the same at every step length.
Not a lap time.

- From TUM's racetrack-database, kept off the repository. A converter fits its line to 30 to 60
  points with handles set, judged by a speed profile over the result matching one over the source,
  one width a circuit, the line re-centred between its edges, and placed only through the converter.
- The autopilot follows the dataset's racing line inside the road, not the centre: the centre costs
  10 to 19 s a lap.
- A lap time is a sanity figure within 5% of a lap-time calculation on the same line, and that
  calculation within 5% of 2004. The 2004 laps: Monza 1:21.046, Shanghai 1:32.238, Montreal 1:13.622,
  Hungaroring 1:19.071, all Ferrari; Hockenheim's 1:13.780 is a McLaren's.
- Nothing real is shipped by this plan. One shipped later would be converted from OpenStreetMap
  directly, the file under its licence with a credit in the README and in game, under an invented
  name.

## What this plan does not do

- It does not make the rig a substitute for the game. KSA's integrator and sub-steps, its hull
  colliders, the real height field, rails, drag coming back in the air, and the drawing stay unproven
  until driven.
- It does not add elevation from real tracks, a start line or lap timing to the format.
- It does not decide banking, cuttings, or a gradient limit.

## The smallest useful piece

Steps 0 to 2 and the bullet runs of step 3. By the reviews' count that reproduces most of the faults
above with no driver, and is the base everything else stands on.
