# Frames, epochs and the ecliptic carrier

**Near Earth, every position and velocity carries ~29.8 km/s of ecliptic motion.** That is ~500 m
per frame at 60 fps. Any two quantities differenced across even a fraction of a step leak a piece
of it, and the result looks like a completely different bug each time — a jitter, a constant
offset, a steering error, a drift. One cause, four disguises.

The arithmetic is always the same:

| discrepancy | error at 29.8 km/s |
| --- | --- |
| 0.1 ms | 3 m |
| 1 ms | 30 m |
| one 20 ms frame | 596 m |
| one 20 ms frame, at 10× warp | 6 km |

**A cause is identified when its magnitude divides out to a number of milliseconds that matches
something real in the frame.** A magnitude on its own identifies nothing: the division against the
carrier speed is the diagnosis.

Everything below applies to any body a mod simulates itself rather than handing to KSA's vehicle
physics. The cars are not one: they are KSA vehicles whose springs and tyres are read in the
planet-fixed frame, where the ecliptic carrier cancels. Anything drawn against them in Ecl or Ego still
obeys these rules.

## The epoch contract

This is what KSA does, read from the decompiled source rather than inferred.
**`docs/KSA-FRAME-ORDER.md` is the long form** — the whole frame in order, what stamps each
sample, and what a mod can read instead of assuming. The short version:

- `Universe.ApplyVehicleSolvers` sets `_lastSimStep = _nextSimStep` and then calls
  `CurrentSystem.UpdatePerFrameData()` back to back (`Universe.cs:1681-1683`), which walks the
  whole system parent-first and is where every `Celestial._positionEcl` (`Celestial.cs:593-608`)
  and `Vehicle._positionEcl` (`Vehicle.cs:2456-2490`) is written. **Celestials and vehicles share
  one epoch because they are advanced in one call.**
- That runs inside `PrepareFrame` (`Program.cs:2012`), 84 lines and six phases before
  `OnDrawUiViewports` (`Program.cs:2096`), which is the mod's GUI hook.
- The epoch is `Universe.GetElapsedTime()`, which *is* `_lastSimStep.NextTime`
  (`Universe.cs:2060-2062`) — so the interval and the instant are two fields of one struct and
  cannot drift apart.

Three consequences, all load-bearing:

1. **Every vehicle shares one epoch**, the *end* of the step just applied. A sample means "where
   this will be at the end of this step", not "where it is now".
2. **`GetLastSimStep().DeltaTime` is the interval that ended at that epoch** — the applied step,
   not a measurement of one. It cannot be a phase out from the world.
3. **KSA's frame order is** reset gizmos → `OnDrawUiViewports` (mod GUI hook) → render → postfix
   on `OnFrame` (mod frame hook). **The frame hook lands after the drawing it feeds.**

That third point is also a constraint on any *loader* a mod runs under, not just on where the
simulation is called from — see "Porting to a different loader" in `docs/KSA-MODDING-NOTES.md`.

## Rules that follow

**Simulate in the same pass that draws.** Run from the frame hook instead, every draw uses an
offset produced one frame earlier against an anchor sampled now: measured as 0.999 steps of
ecliptic motion along the direction of travel and 0.4 m across it. Compensating at draw time cannot
work — the drag is one step of platform motion, so any correction carries a `dt` that changes and
comes back as jitter.

**A drawn offset is `PositionEcl - anchorEcl`, measured after the step, no extrapolation.** Both
terms then advance in lockstep and the difference is the body's own motion. The two other
arrangements — differencing before the step, or extrapolating the anchor sample forward — both
leak `v·dstep`: 30 m per ms of frame wobble, 507 m when the simulation speed changes. They agree
with each other to 0.6 m, because they are one error rather than two alternatives.

**Back-date an engine sample to the simulated body's epoch before pairing them.** The sample is
end-of-step; the body's pre-step position is start-of-step. Extrapolating the sample *forward*
from an already-forward value leaves every line between them carrying `+V_ecl·dt` — measured as a
steering law flying a clean approach onto a ghost 450–680 m from the real point.

**A carry belongs to a consumer, not to a body.** Carrying a simulated body forward by
`VelocityEcl * step` is right for a consumer about to compare it against positions the engine
writes later in the frame, and wrong for one sampling gravity or air *at* the body as it stands.
Applied to the second, the body diverged from its own prediction by 2 km. Before reusing a carry,
ask which two samples the consumer is pairing and at which phase each is written; two consumers of
the same body can need opposite answers. `docs/KSA-FRAME-ORDER.md` §5 has the phase analysis.

**Sample a mod-simulated body once, for everyone, before anything steps it.** A KSA object's
position is settled by the engine before the mod's hook runs, so every reader sees the same
instant. A body the mod steps has no such authority: a live reference answers start-of-step or
end-of-step depending on who asks first.

**Consume the step; never peek at it twice.** `GetLastSimStep()` answers "the last step", not "a
step since you last asked". Deduplicate on the step's own `NextTime`.

**Gate on the applied step, never on `IsPaused`.** `Universe.IsPaused()` is
`simulationSpeed == 0.0` — a statement about the *setting*, not about whether the world moved.
On the frame the speed drops to zero the engine still applies one real step: the anchor sample
advances, and a mod that skips on the flag leaves its body behind by a full step. Because the
offset is a difference of integrated positions, that step stays in **permanently**, and every
pause adds another. The symptom is a body that jumps further from its anchor on every pause.

**Integrate a newly launched body before anything differences it.** A body integrated in its own
launch frame against an anchor sample that has not moved yet bakes one frame of ecliptic motion
into its travel for the rest of its life — measured as 658.78 m of travel at an age of 0.04 s on a
body doing 124 m/s.

**A `Sim/` entry point takes both frame-carrying terms and differences them itself. It never
accepts a difference computed in `Ksa/`.** Every rule above is a subtraction that has to happen at
the right place and the right instant, and a signature taking `relativeVelocity` moves exactly
that subtraction to a call site no test can reach. A regression test written against such a solver
asserts that the *solver* is sensitive to the common term — which it always is — and so passes
unchanged while the caller is the thing that is wrong. Take `(aPos, aVelocity, bPos, bVelocity)`
and difference inside.

Test such a function for **invariance** — add the same arbitrary velocity to both inputs and
assert the answer does not move — with a sensitivity assertion beside it. One proves the common
term is removed, the other proves the relative term still matters; neither alone is worth much.

## Diagnosing

**Measure vectors, not magnitudes.** Comparing two *separations* mixes the error with the
geometry: one constant displacement reads as anything from −0.45 to +0.99 steps depending on where
the other object is. Since Ego is a pure translation of Ecl, a vector between two objects is
identical in both frames, so differencing them isolates the drawing error with the geometry
removed — which is what turns a wandering number into `0.999 steps along the motion, 0.4 m across`.

**Check which renderer is being judged.** A body drawn twice — say a gizmo from
`AnchorEgo + offset` and a mesh as a subpart through a vehicle's part frame — can disagree by a
lot: measured as the gizmo correct to 0.000 m while the mesh was 650 m out. If a symptom and a
measurement disagree, first ask whether they are even describing the same object.

**Constant vs accumulating tells you where it lives.** A mismatched epoch gives a *fixed* offset.
Only re-applying something into an integrated quantity compounds. "It gets worse every time" is a
much stronger clue than the magnitude.

**A threshold crossing is not a distance.** A closest-approach test whose look-ahead horizon is
the integration sub-step (`Vec.TimeOfClosestApproach(r, v, h)`) is bounded by the threshold
whatever the body actually does. It read 10–15 m while the bodies missed by 450–680 m. The honest
number is a range taken at one instant from two samples of that same instant.

## Tests

A test that never varies the step cannot see any of this: at a constant `dt` the right and wrong
phases are indistinguishable, so a suite can pass against a broken implementation indefinitely and
say nothing.

- Vary the step the way changing simulation speed does.
- Advance the anchor sample *before* the update that uses it.
- Write engine samples as end-of-step values (`position + velocity * dt`).
- **Check every regression test fails against the old code.** A test kept without that check is
  not evidence of anything.

## Handing a position to something that draws for itself

The particle emitters take a world position and place themselves, so they draw without going
through a draw anchor — and the trap above then arrives from a new direction.

A simulated body's `PositionEcl` is the analytic position the simulation integrates. Anything drawn
against a vehicle is drawn against its **physics** origin, which is not the same place: on a landed
craft the two differ by metres, and deriving a draw position from `GetPositionEcl` visibly misses
the craft. Placing an effect at the analytic position therefore puts it somewhere nothing visibly
happened.

Convert the *drawn* position back instead: take the vehicle's drawn Ego position, add the body's
offset from it, and convert that to Ecl. The rule generalises: **anything
handed to the engine to place must be derived from where things are drawn, not from where the
simulation says they are** — the two frames agree on directions and differ on positions.
