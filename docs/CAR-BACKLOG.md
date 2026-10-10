# Car backlog

The cars to build next, with what each asks of the mod beyond the data and the art that
`CLAUDE.md`'s *Adding a car* lists. Numbers marked *to check* are from memory and are to be looked up
before anything is modelled to them.

| # | Car | Beyond data and art | Suggested order |
| --- | --- | --- | --- |
| 1 | Porsche 911 GT3 (991.2), blue, `443` | a second helmet, a mirrored visor, lettering on glass | first |
| 2 | 18-wheeler | more than four wheels, a trailer that pivots | last |
| 3 | Police car | flashing beacons, a siren | second |
| 4 | Fire truck | the police car's beacons and siren, six wheels, perhaps a water cannon | third |

The order is by what each has to have built first: the GT3 is a car as the mod knows one, the
police car adds beacons and a siren, the fire truck uses those and is the first with a third axle,
and the 18-wheeler needs that axle work and a coupling besides. A new car is a minor version, tagged
by hand.

## 1. Porsche 911 GT3 (991.2)

The 2017 to 2019 car, with its fixed rear wing: metallic blue, and `443` across the rear window.

| | | |
| --- | --- | --- |
| Length, width, height | 4562, 1852, 1271 mm | *to check* |
| Wheelbase | 2457 mm | *to check* |
| Track, front and rear | 1551 and 1555 mm | *to check* |
| Weight | 1413 kg manual, 1430 kg PDK | *to check* |
| Engine | 4.0 l flat six behind the rear axle, 368 kW (500 PS) at 8250 rpm, 460 Nm, 9000 rpm limit | *to check* |
| Top speed | 320 km/h manual, 318 km/h PDK | *to check* |
| 0 to 100 km/h | 3.9 s manual, 3.4 s PDK | *to check* |
| Tyres | 245/35 ZR20 front, 305/30 ZR20 rear, on centre-lock wheels | *to check* |

**What the photograph shows of the car.** It is taken from behind, over the rear wing, in full sun:

- **Paint**: a mid-to-deep metallic blue with a fine flake that lights to a bright cobalt in the sun
  and falls to navy in shadow. It reads as Porsche's Sapphire Blue Metallic; *the paint's name is a
  guess from the picture.* The wing, the engine lid and the roof are all body colour.
- **`443`**: white, on the outside of the rear window, centred, along its top edge and close under
  the roof. Heavy, wide block numerals, slanted a little forward, about a seventh of the window's
  height and a third of its width. The window's heater lines run across behind them.
- **Rear wing**: a flat blade in body colour, wide enough to take a helmet with room either side.
- **Engine lid**: the two black ram-air intakes of the 991.2 GT3, side by side under the wing's
  blade, each a rounded slot.

**To build.**

- [ ] Reference catalogue as `docs/GT3-REFERENCES.md`: dimensions checked, blueprints, photographs
      with author and licence. The photograph here is kept as
      `Documents/KSACars/GT3_refs/helmet_and_443_rear_window.webp`, beside where the `.blend` will be.
- [ ] Model, with a human's sign-off on the geometry before it is unwrapped: body, front and rear
      wheels (two kinds, the rears wider), steering wheel, glass, and the uprights and brake discs
      that show through the spokes as `Upright…` subparts.
- [ ] `443` on the rear window. The glass is a `PartModelGlass` subpart; **whether lettering can be
      on it is not known**. If not, the numerals are a thin opaque subpart of their own just outside
      the glass.
- [ ] Profile: `PowerW` 368 kW, `TopSpeed` 88 m/s, a `DownforceAreaM2` far under the F2004's, the
      weight towards the rear, two seats and two doors. The real car steers its rear wheels a
      little; the drive does not, and it is left out.
- [ ] Engine: a flat six to 9000 rpm, four sounds cut by a `tools/gt3-sounds.py` from recordings
      with a licence that allows it.
- [ ] The helmet below, on the driver.
- [ ] Tests, `install-testcraft.sh`, and driven in game.

### The driver's helmet

A Schuberth full-face car-racing helmet in bare carbon with a blue mirrored visor, wired for radio.
It reads as an SP1 Carbon; *the model's name is a guess from the picture.* The photograph shows its
front and its right side, three-quarters on, in full sun. The back, the left and the underside are
not in it.

- **Shell**: bare carbon fibre under a high-gloss clear coat, with no paint anywhere. A 2x2 twill
  weave, each tow about 2 to 3 mm, black and charcoal, the weave running diagonally and catching
  the sun as a field of small bright squares over the crown and a hard star of a highlight at the
  top. The shape is a tall rounded dome, smooth, with no spoiler, fin or peak.
- **Crown**: four or five small bright metal studs or vent ports in a loose arc over the top, each a
  few millimetres across.
- **Brow marks**, centred over the visor, in white: a ring holding a stylised S with a bar through
  its middle, about 4 cm across, and under it a white rectangle about 9 cm wide and 1.5 cm tall with
  `SCHUBERTH` in black capitals.
- **Visor**: one wide wrap-round shield from temple to temple, about a third of the helmet's height,
  mirror-coated in a strong cobalt blue that goes to violet and purple towards its upper edge and
  its sides. It is opaque from outside: it shows the sky, the trees and the car, not the face. A
  separate darker band, purple and half see-through, runs along its top edge as a sun strip, deeper
  at the sides than in the middle, so its lower edge is a shallow arch.
- **Visor fittings**: a round black pivot cover at each temple, about 3 cm across with a small
  centre boss. A clear plastic post low on the visor's right side, a short cylinder standing out
  from it. A clear plastic latch at the centre of the visor's lower edge, a tab reaching down onto
  the chin bar where it locks.
- **Chin bar**: deep and long, standing well forward of the brow, coming to a soft point at the
  centre. Carbon like the shell. Three vents in it, each covered with fine silver-grey wire mesh
  and sunk a little into the surface: a slanted slot either side of the centre under the visor's
  edge, rising outwards, and a smaller three-sided one below them on each side of the chin's point.
- **Rim**: a black rubber bead round the bottom edge, and a black lining inside it.
- **Radio**: on the right side, a boom microphone with a black foam cover, standing off at about
  the level of the visor's top, and a black coiled cable leaving low on the same side and hanging
  down. A small bright metal button low on the right side, behind the chin bar.

Beside it on the wing lies a grey-marl fire-resistant balaclava with a white homologation label and
a gold hologram patch. A kitten cannot wear one under the helmet where it would show; it is noted
only as what the driver has.

**To build.**

- [ ] The shell and visor as `…Helmet` and `…HelmetVisor`, the last two subparts of the part, which
      is all `Buggies.Pose` and `Sim/HeadGear.cs` need: the mechanism is the F2004's.
- [ ] Shape from `Helmet_work/helmet_generate.py`, which grows a shell from the kitten's head: new
      `PARAMS` for this one's dome and chin bar, and the kitten's ears under fairings as before.
- [ ] The weave as a tiling pattern in the diffuse and the normal map, fine enough to read as carbon
      and coarse enough not to sparkle; `ksa-blender` has what sparkles.
- [ ] **The visor is the open question.** The F2004's is a `PartModelGlass` and the driver's eyes
      show through it; this one is a mirror nobody sees through. Either glass can be tinted and
      made reflective enough, or the visor is an opaque metallic subpart in blue to violet.
- [ ] The microphone and the coiled cable, as part of the shell's mesh. The cable has nothing to
      plug into and is cut short at the collar.
- [ ] Whether the maker's marks are painted. The F2004's helmet carries none of its sponsors'.

## 2. 18-wheeler

An American tractor and semi-trailer: a steer axle, two driven axles on dual tyres, and two trailer
axles on dual tyres. Which tractor is not chosen; a long-nosed conventional is the one a player
expects.

**What the mod lacks.**

- **More than four wheels.** `BuggyDrive` and `Buggies` step and pose as many corners as a profile
  has, but `Sim/GroundPlane.cs` fits its plane through four, the subpart Ids are `WheelFL` to
  `WheelRR`, the anti-roll bar pairs the wheels of one axle, and steering and drive are told front
  from rear. Each needs to take a list of axles. A pair of dual tyres is one wheel to the drive and
  two to the eye.
- **A trailer that pivots.** A craft here is one part with one rigid body. The trailer is either a
  second craft, held to the tractor's fifth wheel by an impulse of the mod's own each step, as a
  barrier holds a car's side, or a subpart posed behind a tractor that drives as one long rigid
  truck. The first is a trailer; the second only looks like one and cuts every corner.
- **Weight.** 36 t loaded against the Eldorado's 2.3. The springs are sized off the weight, so the
  drive should carry it, but the step a spring can take, the crash veto and the road colliders have
  only been tried with cars.
- **Size on a road.** 16.5 m by 2.6 m, turning inside its own length. `Route` and `Autopilot` take a
  line for a car, and a junction's corners are 6 m.

**To build.**

- [ ] The tractor alone, on three axles, driven in game. This is the axle work.
- [ ] The coupling, in the rig first: a trailer follows through a bend, backs up, and does not
      jack-knife at rest.
- [ ] The trailer, with landing gear so it stands uncoupled.
- [ ] Engine: a big diesel, with an air horn on a key and air brakes on release.
- [ ] Seats: two kittens, a metre and a half up, with a way to climb in.

## 3. Police car

A marked patrol saloon. Which car is not chosen: a Ford Crown Victoria in black and white is the
one that reads as a police car at any distance.

**What the mod lacks.**

- **Beacons that flash.** A light bar on the roof, red one side and blue the other. The lenses are
  `ColouredLenses` subparts, which `LensColourHook` already colours, and the light on the road is
  `Headlights`' spotlights; what is new is a pattern in time, and it is to run on the world's
  clock, as the irises do, so a pause holds it.
- **A siren.** A loop on a channel of its own, as `RocketSound` has one, with a key and a panel
  switch for it. `BoostHook` is how a car hears a key KSA does not give a vehicle.
- **Nothing in the drive.** It is a heavy saloon and a profile.

**To build.**

- [ ] Choose the car and the force's livery, which is the mod's own and no real force's.
- [ ] Beacons: the pattern in `Sim/`, tested, and the lenses and lights in `Ksa/`.
- [ ] Siren: the recording and its licence, the sound, the key.
- [ ] Model, profile, engine, tests, driven in game.

## 4. Fire truck

A pumper with a crew cab on two axles, or a ladder truck on three. Not chosen; the pumper is the
smaller job and the ladder truck the better toy.

**What the mod lacks.**

- **The police car's beacons and siren**, with an air horn beside them.
- **A third axle**, if it is the ladder truck, which is the 18-wheeler's axle work done first.
- **A water cannon, if it has one.** The flames are KSA's exhaust plumes with no engine behind
  them, and `Buggies.Flames` could draw a jet the same way from a template that looks like water.
  Whether any Core template does is not known. A push on what the jet meets would be the mod's own.
- **A ladder, if it has one.** It is a subpart that turns and reaches, posed each frame as a wheel
  is, with its colliders moved as a scoop's are.
- **Seats for a crew.** Four to six kittens, of which KSA draws all only because `SeatedCrewHook`
  makes it.

**To build.**

- [ ] Choose pumper or ladder truck, and whether it throws water.
- [ ] Model, profile, a diesel, tests, driven in game.
- [ ] The cannon or the ladder, each as its own piece of work after the truck drives.

## Shared work, in the order it unblocks cars

| Work | Needed by |
| --- | --- |
| A second helmet, a mirrored visor | GT3 |
| Beacons on a pattern, a siren on a key | police car, fire truck |
| A profile of axles and not four corners | fire truck with three axles, 18-wheeler |
| A coupling between two craft | 18-wheeler |
