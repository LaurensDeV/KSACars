# Test checklist

What has been seen working in game, and what has not. Tick a box only for something seen in game; a
passing suite is not a tick. The headline matters less than what is still unticked.

## Since the split

Both cars were driven in the mod they were built in, and everything below that section was ticked
there. This repository renamed every part, subpart, sound and file, and rewired the entry point, so
none of it has been seen as **KSACars** yet:

- [x] The F2004 loads, textured, sits level at its modelled height on all four wheels with its uprights
      found, seats a kitten whose hands are solved onto the wheel, and pulls away and turns (bridge,
      `TARGET PRACTICE`, 2026-10-07; before its power, downforce and the game's drag were changed)
- [ ] The F2004 with KSA's drag off: a coast-down with `game_drag` true and false, and its top speed
- [ ] The Eldorado and the buggy with KSA's drag off: top speed, a boost on wheels, a flight and a landing
- [ ] The F2004's rain light, faint with the lamps on and bright under the brakes
- [ ] The F2004's uprights steering with the front wheels and not spinning
- [ ] The mod loads: `KSACars.log` has the `loading` and `a developer's install` lines, and KSA's own
      log has no asset or XML errors for `KSACarsAssets.xml`, `KSACarsGameData.xml` or
      `KSACarsSounds.xml`
- [ ] Both cars are in the editor under *Vehicles*, textured, with the Eldorado's hood ornament and
      both plates lettered
- [ ] `tools/install-testcraft.sh` craft launch, and the bridge's `spawn` parks one, answering from
      `Logs/bridge/KSACars/`
- [ ] Each settles on its springs and drives: `car physics hooked into Vehicle.PrepareWorker` is in
      the log, and the drive report moves
- [ ] The driver's hands are on the rim (`hands_solves` above zero in the drive report)
- [ ] With one kitten out on EVA, the other is still drawn in the car (the seated-crew patch)
- [ ] The engine is heard, start to key-off
- [x] An Eldorado tipped onto its roof by the bridge's `flip`, railed and scraping, is back on its
      wheels, level and off rails, after `unflip`, pointing the way it was
- [ ] The **Fast & Purrious** panel is seen while a car is flown, its **Unflip** button rights the car, and the
      keys still drive with the panel on screen
- [ ] The buggy righted, and a car righted from its side and on a slope
- [x] Headlamps: on the night side both cars throw two beams ahead from the bridge's `lights`, dipped
      and main, and go dark on `off`
- [x] The Eldorado's headlamp lenses glow white with the lamps on, its tail lenses red and its side
      markers amber, each to the lens's own outline, with red on the ground behind
- [ ] The buggy's tail lamps (it has no tail lenses), the brake lights brightening, and the lamps
      coming back on after a save and reload
- [ ] The panel's Off / Low / High buttons switch them, and the brightness and reach look right from
      the driver's seat
- [x] Rockets: through the bridge's `lift` the Eldorado climbs at 0.6, holds its climb rate at 0.5,
      stays level, leans forward to 9 m/s on the throttle key and turns on the steer key
- [x] Four flames under the Eldorado while the rockets burn, longer at full throttle than at half
- [x] The buggy has no rockets and refuses the bridge's `lift`
- [x] The irises over the thrusters, on Earth from the bridge: shut, nothing to be seen on the bonnet
      or the tail panel; the four under the floor and the four on the bonnet and boot open onto their
      nozzles, with flames
- [ ] The two irises on the tail panel seen open, any iris seen opening or closing by a player, and
      held still by a pause
- [ ] The rockets heard, the flames dying away when cut, and the flames at night and in vacuum
- [ ] The rockets on KSA's keys, by a player: Z and X light and cut them, Up and Down move the
      throttle, W / S and A / D lean and bank in the air, Q / E turn
- [x] A hard landing survived: the Eldorado dropped from 180 m at 50 m/s bounces and settles on its
      wheels, level, with no destruction
- [ ] The rockets' throttle shown on KSA's throttle gauge, moving with the Up / Down keys
- [x] The boost from the bridge on Luna: 21 m/s in under two seconds, with a flame at each of the two
      ports on the tail panel
- [ ] The boost on the Shift key, by a player, and heard
- [x] The scoop shown on the Eldorado's nose on Luna from the bridge's `scoop`, and the car driving
      with it on at 7 m/s over flat ground
- [x] The downward thrusters from the bridge on Luna: the car pulling to 12 m/s in five seconds with
      them on, against about 4 without
- [x] The hover from the bridge on Earth: climbing at 10 m/s, the downward thrusters switched on stop
      it within three seconds and hold it at 48 to 50 m for ten more, leaning forward to 9 m/s meanwhile
- [x] The rockets under time warp, from the bridge on Earth: level and not on rails at 2x, 4x, 10x and
      50x, where a step is a second long; the hover holding 42 to 51 m across all of them
- [x] The rockets above the atmosphere, from the bridge on Earth: off rails and still gaining speed at
      1,190 km, where before the car was railed above about 100 km and fell back
- [x] A car railed at 219 km with its rockets cut, woken by lighting them with the hover on: stopped
      and held at 418.95 km, within 10 m, at 1x and at 50x
- [ ] Leaning across the ground under warp, a landing under warp, and warp past 50x
- [ ] The hover by a player, on Luna, and started from the ground
- [ ] The downward thrusters on the R key, by a player
- [x] The Scoop XXL shown on the Eldorado's nose on Luna from the bridge, the car level and not
      scraping with it on, and driving with it
- [ ] The panel's Scoop size choice (None / Default / XL / XXL), a rock pushed by the XXL, and its colliders
      back inside the hull when it is switched off
- [ ] The craft mover: a car picked up with a click and set down on the ground clicked next, with
      the rings drawn at its feet and at the target
- [ ] The Rock weight slider: a boulder that would not move at 100% pushed at 2%, and KSA's weights
      back when the scoop goes off
- [ ] A rock knocked loose and pushed by the scoop; the scoop hidden again and its colliders gone
      when switched off; the panel's Scoop checkbox
- [ ] A car clipping a rock at speed is not destroyed (it was, at 46 m/s, before its crash
      tolerance was raised)
- [ ] A car driven or dropped into the sea is not destroyed (it was, by ocean impact, before the
      veto covered every cause)
- [ ] A collision between a car and another craft, and a car dropped on its roof
- [ ] A landing on the springs, and a hover held over a slope
- [ ] The engine falls silent past 4x warp, with no key-off, and comes back with no starter at 1x
- [ ] **A save holding a car built in the old mod does not load**: its part Ids are gone. Expected,
      and not fixable without the old Ids; rebuild the car

## Status: the beach buggy drives, on flat ground

Flown through the bridge on `2026.9.22.5482`, parked at the flat Chaco site (24.0 S, 62.0 W) and
driven with `drive`, at 20-26 ms frames:

- [x] Spawns from the vehicle library and stands on its springs, level, 7 mm off its rest height,
      with all four wheels found and nothing of the hull on the ground
- [x] Full throttle: 0 to 15 m/s in 5 s through three gears, squatting 5 cm at the nose
- [x] A full-lock turn at 15 m/s holds its line at ~40 deg/s and ~1 g, leaning 4 cm, no scraping
- [x] Braking: 8.9 m/s^2 (0.9 g, as the profile asks), diving at the nose, then reversing
- [x] Two seats; the bridge's `crew` fills them and both kittens are drawn aboard
- [ ] Driven by a player on the keys (W/S/A/D), and heard: the engine sound has never been listened to
- [ ] Rough ground, slopes, jumps, and a landing on the pads
- [x] Getting out by EVA: the kitten steps out beside the car, and the one left in it stays drawn
      (checked by the player). A kitten already out on EVA elsewhere can still be seated by KSA's
      Fill Seats, and getting that one out jumps control to its other copy: KSA's, any craft
- [x] The driver's hands stay on the rim across full lock, 0.00 cm from their grips (bridge report)
- [ ] Getting back in by EVA through the doors
- [ ] Anywhere but Earth, and at timewarp

## Status: the Eldorado drives, on flat ground

Flown through the bridge on `2026.9.22.5482` at the Chaco site, the same way as the buggy:

- [x] Spawns from the vehicle library, stands level on its springs with four wheels found
- [x] Full throttle pulls about 3.5 m/s^2 (0.36 g) with the nose rising, 0 to 24 m/s through two
      gears. Before its mass was a car-sized box it rocked at 13 deg/s on the spot and pulled 1 m/s^2:
      a sphere's yaw inertia was a third of a car's and the side grip overcorrected every step
- [x] A full-lock turn at 14 m/s leans about 9 cm, 25 deg/s, 0.5 m/s of slip, no scraping
- [x] Braking, then reversing
- [x] Four seats; two kittens seated and drawn, the driver's hands on the rim (0.00 cm, bridge report)
- [x] Getting out by EVA: the passenger steps out beside the car, which stays put, and the driver
      stays drawn
- [ ] Heard: the Cadillac start and idle and the Impala loop and key-off have never been listened to
- [x] Launched on a pad (2026.10.7.5541, by the player): stands on the grate at its ride height,
      0.37 m at each hub, and drives off it
- [ ] Driven by a player on the keys, rough ground, getting back in

## Roads

- [x] A road drawn as a four-sided tube shows on the ground and follows it (2026.10.10.5554, bridge
      `road`, a 300 m S-bend at -24, -62 on Earth, seen close and from 40 m up)
- [x] Grass, shrubs and trees under it are switched off through KSA's exclusion mask: 1,728 tufts,
      140 shrubs and 11 trees, the surface clear with a few tufts leaning over a 0.75 m margin
- [x] A circuit with a junction laid from `Sim/Circuit.cs` (bridge `road`): the through road smooth across
      the junction, the side road square to it, 4 roads from 4 points
- [ ] A circuit read back from a file in the library
- [ ] The asphalt's colour, last seen a pale blue-grey
- [ ] A road over uneven ground: a crater wall on Luna, and rocks cleared with their colliders
- [ ] Taking a road up puts the clutter back
- [x] The editor's free camera, set through the bridge: from 220 m at 35 degrees facing east, and from
      500 m at 80 degrees turned to 200, the circuit under it both times
- [x] A junction raised 8 m: its three roads ramp up to it
- [ ] The road editor by hand: placing, joining, splitting, dragging points and handles, the corner and
      height sliders, undo and redo, save and load, and panning, turning and zooming with the mouse
- [ ] Junctions and bends drawn as runs: no wedge where a road passes a point, no flicker where a side
      road meets a through road
- [ ] A car driven on a road: sprung against it, up a ramp onto a raised one, and under a bridge
- [ ] Nothing unseen left standing on a road: the clutter colliders rebuilt after the mask changes, and
      `probe_clutter` showing the reckoned positions within a metre of KSA's
- [ ] A car driven fast down a slope, on a road and on open ground: no bouncing, since its dampers
      measure along the slope's own up; and all three cars still settle and corner on the flat
- [ ] Coming onto a road from the grass without bobbing, meeting a steep ramp at speed without passing
      through it, and a car on a steep road standing on the surface that is drawn
- [x] Roads drawn as slab meshes (2026.10.10.5554): asphalt, lit, standing a few centimetres proud of the
      grass at the car's wheels
- [ ] A raised road as a thin deck, and bends and junctions drawn as slabs without gaps or flicker
- [ ] The height knob over the selected point: dragged along its stalk from the side, and straight up the
      screen from overhead; and a height typed in past 60 m
- [x] A collider box of the mod's own in KSA's physics (bridge `road` with `box_top`): put under a
      parked F2004 with its top 0.5 m up it lifted the car by its hull, hubs from 0.34 to 0.69 m over the
      ground, landed and still; taken away, the car was back at 0.34 m
- [ ] A kitten walking on such a box; a car driven onto one; the boxes after the bubble's origin shifts
      2 km on, after a save is loaded, and after a simulation has been recycled
- [x] A road laid 2 m from a parked F2004 and laid again at 0.5, 1, 1.5, 2, 3 and 6 m up (bridge `road`,
      2026.10.10.5554) leaves the car where it is, hubs 0.34 m over the ground throughout
- [ ] The same by dragging the height knob in the editor, and a loop of road with no gap where it closes
- [ ] Every laid road has collider boxes in KSA's physics, one a stretch and half a metre deep, handed
      over on a fine lay: a car driven onto one, a kitten on one, a railed car on a deck, an origin shift
      on a long lap, a save loaded
- [ ] The roads' hard stop is gone, so a car that bottoms on a road lands on its hull on the collider:
      a ramp at speed, a bump on a descent, time warp on a road and on a deck
- [ ] A road counts as under a wheel up to half a metre into it, so a car under any deck it fits under
      stays on the ground
- [x] A driver follows a route round a laid circuit (bridge `lap`, 2026.10.10.5554, the F2004): stood on
      the road at the route's start 0.000 m from where it was asked and facing along it, it drove and
      stopped itself at the end, the summary filed under `Logs/bridge/KSACars/laps/`
- [x] A straight road with a ramp up 8 m over 100 m and down again, driven so at 15, 30 and 50 m/s: on
      its line to 4 mm, no flight, hubs within 3 cm of rest, body never on the collider. At 65 m/s it
      leaves the crest for a second, lands 14 cm into its travel, pitches 6.5 degrees and carries on
- [x] The test circuit and the one with its junction 8 m up, driven so at up to 58 m/s: the lap, the turn
      at the junction both ways, and 2 m either side of the centre line: all finished, no wheel off the
      asphalt, no flight, within 0.85 m of the line, roll under a degree
- [ ] The driver on the buggy and the Eldorado, on a closed lap of several kilometres, and at the edge of
      a road with less than a metre to spare
- [x] A 3.3 km oval laid and lapped (2026.10.10.5554): laying it closed the game while every stretch was a
      box, KSA holding 1024 mesh instances a view; with distant stretches drawn as fewer boxes the F2004
      lapped it at 93.7 m/s
- [x] A mesh made at runtime (bridge `road` with `mesh_test`): a 10 m crowned, waved patch 3 m over a
      parked F2004 is lit, carries the asphalt, shades smoothly with no facet to be seen, and casts its
      shadow on the car and the ground; written over in place with a bent, coarser one at the same
      offsets, the free room in KSA's buffers unchanged
- [ ] The eight circuits of `tools/roads/extreme-circuits.py` lapped by all three cars: the F2004 finishes
      six of eight cleanly; the driver stops the Eldorado and the buggy short of the end, and takes the
      buggy round the figure of eight fast enough to roll it
- [x] Roads drawn as the smooth surface's own triangles (2026.10.10.5554, bridge `road`): the raised test
      circuit as one ribbon climbing to a deck, asphalt on top and earth on its verges and banks, four
      meshes in four of eight slots, no box anywhere; and one collider mesh of 6,679 triangles for the run
- [x] On triangle-mesh colliders no car flips or rolls on the eight extreme circuits, where four did on a
      box a stretch: the Eldorado laps the 3.3 km oval and the buggy the figure of eight and the grid
- [x] "Stuck short of a road's end" was the lap runner leaving each car standing where the next one
      finishes; with each car parked clear, all three finish the hairpins and the kinks
- [ ] The smooth roads driven: all eight circuits lapped by all three cars; a climb with nothing to feel;
      a car on a bank; the editor dragging a point with the mesh following; a kitten on a road
- [x] The eight first circuits on the smooth roads, all three cars: 23 of 24 laps clean; the F2004 left
      the helix's descent at 942 m, and hopped for 0.6 s on the flat oval at 93 m/s
- [x] Junctions (2026.10.10.5554): the grid of crossroads laid as five junctions, none refused, one solid
      of 12,762 triangles; all three cars at once round `1,2,5,8,9,6,5,4,1`, left and right at the Ts and
      twice over the crossroads 5 m up, finished within 0.27 m of the line with no flight
- [ ] Junctions looked at close to: the corners, the mouth lines, the deck crossroads from beneath; a
      junction that cannot be made; a T on a hillside
- [ ] The eleven harder circuits. First pass, one car at a time: every car fell over at once on the ring
      banked 30 degrees and stood stuck at the start of the lap that is never level, both where a car is
      stood level on a road that is not; the F2004 was thrown on four circuits at speed, once losing
      37 m/s in a third of a second 74 m up a descent with nothing there to hit; the Eldorado and the
      buggy drove the 5 m road 300 m up and the 20 km lap
- [x] The F2004 thrown at speed (2026.10.10.5554): it was the physics engine and not the drive, logged as
      a `jolt` each time, 10 m/s off 25 in one step two or three times a lap of the wall course; with a
      car's speculative margin held to 5 cm while a wheel is on a road, none in a lap of that, the 6 km
      road in the sky, the tower or the helix, and its hull is reported down for 0.0 s of the 95 s in the
      sky where it was 55
- [x] The eleven harder circuits again, three cars at a time, with a car stood on the asphalt's own face,
      the driver given a road's lean and its true slope, and the wheels' plane let lean to 60 degrees:
      28 of 33 laps finished clean. All three lap the ring banked 30 degrees and the one that leans 20
      the wrong way; the F2004 climbs 80% and comes down 100% with no flight. Not finished: the Eldorado
      stops on a 60% wall and at once on a 45% one from rest, the buggy on 60% and 65%, which is their
      pull; the F2004 puts wheels off a 4.5 m street's right angles; and one lap ended because the roads
      were taken up under it
- [ ] The F2004 wide in the weave of the 20 km lap at 88 m/s, by how long the game's step is there: 0.4 m
      off the centre at 19 ms, 1.2 m at 23 ms with a wheel light for 15% of it, and off the 12 m road at
      27 to 30 ms. Its springs are softened past 19 ms and the driver's speeds take no account of the
      step. With the margin left as the engine's it held 0.4 m at 22 ms, so the hold costs a little there
- [ ] The editor opened on roads the bridge laid edits those, and does not take them up
- [x] A drag's cost (2026.10.10.5554, bridge `road` with `drag_node`): laying the grid, the knot and the
      Stelvio again each took 35 to 42 ms a frame, 3,300 to 4,000 terrain reads; with the ground read off
      a lattice kept for the drag, 4 to 5 ms, and a dragged road's meshes 4 to 9 ms ten times a second
- [x] Double roads in a drag (2026.10.10.5554, bridge `road` with `drag_node` and `step_m` 4): a grid point
      dragged 160 m left the meshes of up to six runs that were runs no longer; none with them taken away
- [ ] A point with several roads dragged by hand since: does it still lag
- [x] The F2004's helmet (2026.10.10.5554): on the seated driver's head from the front, the side and
      above, shell and visor together, the livery whole and the driver's eyes seen through the visor
- [x] The helmet redrawn (2026.10.10.5554): pointed ears, the chin bar forward under the visor, a stripe
      over the crown and no stars or brow band, on the seated driver from three sides
- [x] The parcours `Insane` raced (2026.10.10.5554, `run-laps.py --circuit Insane --car F2004 --crew --racing
      --push 0.4`): 6,019 m in 132.5 s, 322 km/h on the straight, 2.7 s and 180 m through the air off the
      jump and on round the lap, no wheel off the asphalt. The landing takes 20 m/s off it in three hits
- [x] The wedge a car hit in a banked bend: where the bend's bank ended under a deck its end wall stood
      23 cm over the asphalt at the low edge, on the racing line; hung from the road's outline, the lap is finished
- [x] A personal best searched for (2026.10.10.5554, `Sprint`, the F2004's flying lap in game): 41.67 s on
      the racing line with the driver's own settings; 35.9 s with settings searched in the rig, but two
      wheels off on one run in three; 35.15 s, clean six runs of six, once a way had also to be clean
      on 6% less grip and each stretch of the lap had a pace of its own; 35.05 s with the line nudged,
      one run of three with a wheel off for 0.3 s. The rig's times are within 0.2 s of the game's
- [x] Clutter cleared by the roads' outline (2026.10.10.5554): the F2004 stood on grass 18 m off the
      crossroads on the diagonal and on grass under the road 160 m up, both of which the old reach would
      have cleared; and the log's check of where clutter stands, 228 instances against KSA's, the worst 0.00 m
- [x] Lane markings (2026.10.10.5554): a white line inside each edge and a dash down the middle, following
      the banked bowl and sharp down the length of the 900 m straight; a junction and a deck's sides in
      plain asphalt
- [x] Trees beside a road: one stood under a raised deck's edge and a car hit it; with all round each
      instance tested at the margin, and twice as far for a tree, 70 more trees go on the same circuit
- [ ] The editor's width, lean and corner sliders and its warnings, by hand: the capture does not show them
- [ ] The same looked at where a road widens, at a bank's foot and for a tree under a low deck
- [ ] A junction with a road into it shorter than its mouth; the racing line and push on the older circuits
- [ ] The helmet with the driver looking about and the wheel turned; gone when the driver gets out;
      on a car that is not the one flown; and in the vehicle editor, where there is no driver
- [ ] A car standing on a wall it cannot climb, with its hull on the road; the buggy's hull at the foot
      of a wall, where the engine still takes 4 m/s off it in a step
