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
- [x] A Steam Controller drives the car being flown, read through SDL with Steam closed: `gamepad: Steam Controller`
      in the log, and the car driven with it (Laurens, 2026-10-12). With Steam running its desktop layout also
      turns the buttons into keys, so Start pauses the game
- [ ] Each gamepad button on its own: A, X, Y, Start, the bumpers, D-pad up, and the D-pad and left stick in the air
- [ ] An Xbox pad, through GLFW with no SDL beside the mod
- [ ] The chase camera: behind the car, tilted with a slope the right way, looking into a corner, and the right
      stick looking round; and the view's frame put back when the panel's Chase camera is cleared
- [ ] What speed does to the chase camera: the view wider, further back and lower by 65 m/s, a shake past 55,
      and the field of view and the distance as they were on leaving the car or the chase
- [ ] The F2004's helmet gone from the first-person view out of its seat, and back in every other view
- [ ] The F2004's helmet gone from its thumbnail and from the car in the vehicle editor, and still on the
      driver's head in flight
- [ ] A hover let go of comes to rest over a place and stays over it for minutes, at 1x and under warp, and
      still moves across the ground while a lean key is held
- [ ] Space brakes the car being flown from speed without reversing, holds it on a slope, lights the brake
      lamps, and is let go when a window takes the keyboard
- [ ] The craft mover sets a car down on a road on the ground and on a raised deck, standing on the asphalt
      where the ring was drawn, from near and from kilometres off; and a craft that is no car goes to the
      ground as before
- [x] Racing online between two copies on one machine (2026.10.13.5602, `tools/two-games.sh`, joined at
      127.0.0.1): the guest laid the host's course by itself, each saw the other's F2004 with a kitten at the
      wheel, and raced at up to 66 m/s each puppet was 0.23 m on average and 0.8 m at worst from where it was
      wanted, never put there outright; over the line and after a car was moved to its box it was found again
- [x] A guest given the host's world (same games): the save and the course, 46 KB, loaded with the guest
      flying its own car in it and the course laid as the host's, the host's car on it 2 cm from where it was
      wanted; the universe times within 6 ms while both drew freely; a warp to 10x and a pause refused in both
- [ ] A guest joining a race already under way, and a third racer's car made in the worlds of those in
- [ ] Racing online between two machines, one hosting and one joining with its code across the internet
- [ ] The host's router opening the port through UPnP, and the panel saying the address it gives
- [ ] A guest let in with its own code where the host's router would not open the port
- [x] The host's start putting every car in its box and letting them go together (same games, bridge
      `online` with `start`): the lights out 6 to 13 ms apart in the two
- [ ] The racing display counting the laps from the lights
- [x] An online car touched (same games): an F2004 into the other at 9 m/s lost 3.6 m/s in its game, and
      the one it hit was shoved to 3 m/s in the other's; neither thrown, each puppet then doing what its car did
- [x] A course's start (2026.10.13.5602, bridge `road` with `course`, Club Motorsports Flat): the chequered
      line between the road's edge lines ahead of the F2004, solid white and black 2 cm over the asphalt, the
      pole's box round the car, and the racing line's arrowheads solid over it
- [x] Opponents (2026.10.13.5602, bridge `road` with `opponent`): two copies of the F2004 made from its part's
      template, stood in the second and third boxes of Club Motorsports Flat's grid, set off when the flown
      car did and lapping at up to 67 m/s, none off the asphalt in 27 s
- [x] Opponents from the panel's buttons (Laurens, 2026.10.13.5602): three added and raced by hand. Each
      collided with the flown car or another, lost up to 23 m/s in a step, ended its lap `OffRoad` and stood
      where it stopped, which is what holding back and being put back on the road were then written for
- [x] Nine opponents at once (Laurens, 2026.10.13.5602, Club Motorsports Flat): a full grid set off together
      with no opponent touching another, each on a line of its own, 3,562 to 3,570 m round, all nine still
      lapping two minutes on at 30 to 77 m/s, at steps of 21 ms on average and 33 at most
- [x] An opponent put back (same race): Opponent 9, run into from behind at 27 m/s, was `OffRoad` 1.7 s later,
      and 2.9 s after that stood on the road 16 m away and racing again
- [x] A collision between two cars is KSA's and is plastic (same race, five of them): the flown car and the
      one it ran into each changed by half their closing speed in one step, 4.4 to 13.9 m/s, and left together
- [x] F2004 opponents on a course with no lap (Laurens, 2026.10.13.5602, Club Motorsports Flat, nine of them at
      full pace): seven ran four laps of 86.7 to 88.1 s each, the rig's 87, five of them with no wheel off
      the asphalt; two were rammed or rammed the flown car where it stood, and one of those and another were
      then taken for stuck every five seconds while they waited behind it
- [x] Practice in game (Laurens, 2026.10.13.5602, Club Motorsports Flat, the F2004 as the engine has it): 82.7 s
      blind and 80.6 s practised at 1.14 of its pace, 22 laps of the rig in 2.4 s; nine opponents set off in
      the same millisecond, steps of 21 ms on average with ten cars
- [x] Opponents in a train (same race): with drivers 0.93 to 1 of the pace and in no order, laps of 89.5 to
      97.5 s, slower box by box, against the 80.6 they had practised
- [ ] Opponents gridded quickest first: laps near what they practised, and no queue behind a slow one
- [ ] Practice: the panel's line and the log's after the first opponent is added, how long it took, the lap it
      found against the laps the opponents then drive, the frame time while it runs, and a course laid a
      second time known at once from its kept file
- [ ] The driver's model 2 in game: opponents' laps against the rig's 84.7 s blind and 80.3 s practised on Club
      Motorsports in the F2004, and whether they stay on the road at the game's steps
- [ ] Nine opponents set off with no pause in the picture, and one put back with none
- [ ] Two opponents that stop together both put back on the road
- [x] Opponents queued behind the flown car across the road in a bend (Laurens, 2026.10.13.5602): none went
      round it, a pass then being for a straight only, which is what going round a standing car was written for
- [x] Practice in full in game (bridge, 2026.10.13.5602, Club Motorsports Flat, the game held to 33 ms steps): a
      lap racing blind clean, 74.5 and 74.8 s, where the panel had said it could not lap the course; 72.5 s
      practised in 32 s and, once practice was also made sure of at 33 ms, 73.1 s in 27 s; what a lap gives
      up slower measured as 1.6% at 0.95 of every bend down to 36% at 0.6; and the course laid again known at
      once from its kept file. The panel's own line and the frame time while it runs were not watched
- [x] Six opponents raced at 33 ms steps (bridge, same build): one at the edge of the practised lap at 25 ms ran
      wide into a tree, and in the rig every driver of that lap left the road at 33 ms, so practice now makes
      sure of a lap there too; two side by side braking into a bend touched and both spun; one braking for a
      car ahead at full lock spun by itself
- [x] Opponents round a car stopped at the road's edge (bridge, same build): two went round it and drove on, one
      against it was put back on the road past it 2.5 s later
- [x] Four opponents raced three times at 33 ms (bridge, same build): passes among them in the first lap each
      time; in two races a driver still behind a car it was going round ran into it as that car braked, at 18
      to 28 m/s, and one put back on the road was hit by a car coming up behind it at 40 m/s. With a passer held
      back by a car braking ahead of it, and no car put back with one coming, a third race went 2.7 laps with
      passes and no car touching another; laps of 82.6 to 85.9 s and 3 to 5 s each off the asphalt, at 33 ms
- [x] Opponents queued behind the flown car standing on the road a second time (Laurens, same build as the
      new line): the room to go round was asked of the next 150 m of a line that hugs the edges
- [ ] Opponents going round a car standing across the road, one after another from the queue behind it
- [ ] An opponent passing a slower one, and the flown car, on a straight; and dropping back in when it cannot
- [ ] An opponent drawing up in another's slipstream and passing it, a car alike; and the flown car towed
- [ ] Opponents slower by: the field's laps at 0%, 5% and 10% against the practised lap and what was measured
- [ ] A fluffed exit and laps of one opponent that differ, watched; and no opponent touching one beside it
      now that each keeps a lane from a car alongside
- [ ] The blind driver at the game's own steps of 17 to 21 ms on Club Motorsports Flat: at 33 ms, the game
      under 30 frames a second, it ran wide at 2,400 m, where the rig at 33 ms laps it cleanly. Over the first
      880 m its steering was at full lock 35% of the steps and all four wheels down 43%, the front right off
      the ground for seconds at a time; in the rig 8% and 82%. The car turns less in the game than in the rig
- [ ] The new racing line in game: the arrowheads' line against the old, and opponents' laps against the rig's
      77.3 s blind and 76.4 practised on Club Motorsports
- [ ] An opponent slowing for a car round a bend, and waiting behind a parked car without being set off again
- [ ] The racing display with the interface hidden: speed, lap, position and the map turning with the car and
      showing the others; nothing with the interface up; and gone with the panel's switch cleared
- [ ] The lap's clock, the last and best laps, and the green or red split at each checkpoint from the second lap on
- [ ] The post at the next checkpoint: standing on the road, its distance counting down, LAP and FINISH at the line
- [ ] Checkpoints: a lap counted at the line after a whole lap, the yellow dot moving on round the map, and
      MISSED CHECKPOINT after cutting across, cleared by going back through it
- [ ] Opponents at 70% and at full pace on Club Motorsports in the F2004: their lap times against the rig's 83
      to 86 s, whether they stay on the road at the game's steps, and the log's "towards a lap of" line
- [ ] F2004 opponents on a course with no lap, Club Motorsports Flat among them: the rig has them at 87 s there
- [ ] Opponents in the Eldorado and the buggy
- [ ] An opponent seen slowing behind the flown car and behind another opponent, and stopping behind a parked one
- [ ] An opponent knocked off the road driving itself back on, one on its roof set on its wheels and driving
      on, one standing still for 2.5 s put back on the road, and one waiting behind a parked car left where it is
- [ ] Opponents: their driver seen at the wheel, a whole lap, Remove opponents and Take up roads taking them
      away, and an install with no `developer` file
- [ ] A course's start: the grid's other nine boxes behind the car, on a banked or raised start, and gone
      when the roads are taken up
- [ ] KSA 2026.10.13.5602: a car spawned, seated, driven, its hands on the rim, and one kitten out with the
      other still drawn; `Roads.CheckPlacement`'s line in the log with a road laid
- [ ] The line to drive at `Push` 1 and 0.3 m inside the edges: where it goes red against where a car has to brake
- [x] A circuit laid whole is worked out beside the game: a 10 km high road placed in the editor with 32 ms of it
      in a frame, where it was 8 s (log, 2026-10-12: `prepared_ms 665, solids_ms 21, shown_ms 11`, clutter
      `found in 2957 ms beside the game, taken in 0 ms`), and Laurens found it smooth
- [ ] A course laid from the panel while the game carries on, the car stood on its start once it is laid
- [ ] Club Motorsports as regenerated, its runoff to one side: no warning in the editor, and its bends as drawn
- [ ] The editor: a length that is not 0 m over roads laid before it opened, its points gone with Take up roads,
      and Ctrl with a click picking a point without joining
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
- [x] Kerbs and barriers (2026.10.10.5554): red and white kerbs both sides of the short course's bends and
      a barrier with a red and white top along the banked bowl's deck, in the player's own capture; the
      6 km parcours lapped in 131.3 s with them, nothing hit, 44 meshes all drawn
- [x] What stood through a low deck were KSA's shrubs, which are small trees with colliders: the car was
      stopped by one in the bowl, 2 m over the ground. Under a deck only grass is kept now
- [x] A car driven into a barrier on purpose (2026.10.10.5554): its faces were drawn facing away from the
      road, so the physics let a car in from the road side and its wheels stood in the wall. Faced the
      right way, the F2004 driven along a line 6.7 m left of a 14 m road's middle round the banked bowl
      rubs the wall the whole way with its wheels on the asphalt, pushed up to 0.44 m off that line,
      and is slowed by it from 25 m/s to 3.5
- [x] A barrier rubbed with the mod's own push (2026.10.10.5554): the F2004 along a line 6.5 m left of the
      same road's middle round the banked bowl is held 0.15 to 0.39 m off that line for 200 m with no
      collider touching, and goes from 24.4 m/s to 23.4 with the driver holding 25 on a third to
      three quarters throttle. Where the deck begins the same line is outside the asphalt, and the car
      meets the barrier's end head on and is stopped by it
- [x] The line to drive (2026.10.10.5554): arrowheads on the racing line ahead of the F2004 on the short
      course, blue at 12 m/s and red into a bend on a lap of 35 s, one write of 1.8 ms for the lap and
      up to 14 runs of colour at once
- [ ] The line to drive at night, on a circuit over 3 km, where a stretch is written again, and from the
      panel's own switch: only the bridge's has been used
- [x] A circuit laid somewhere else (2026.10.10.5554): the short course put 3 km away and turned a
      quarter through the bridge, saved as a file of version 3 in 1,981 bytes for 3,661, and lapped clean
      by the F2004 in 47.8 s; the driver that leaves the original at 724 m leaves this one at 710
- [ ] The editor's Load at the craft and its turn, by hand; a circuit laid on another body
- [x] A real course (2026.10.10.5554): Club Motorsports laid raised, 3,595 m in 25 meshes and one solid of
      37,162 triangles, lapped clean by the F2004 on the racing line at push 0.3 in 92.65 s. Not looked
      at: it was dusk, and the overview the bridge took showed trees
- [x] The same course as wide as its asphalt (2026.10.10.5554): 107 points, 12 to 40 m wide, 29 meshes,
      lapped clean by the F2004 on the racing line in 93.89 s. Not looked at either
- [x] A course laid at the car (2026.10.10.5554): the shipped Club Motorsports laid through the bridge's
      `road` with `course` with the F2004 on level ground, the car left standing on its start with all
      four wheels down, and a lap set going from there. The strip at a closed run's start that was drawn
      pale, with a whole lap of its lines in it, is drawn as the rest
- [ ] The panel's Course list, Lay here and Take up roads, by hand; an install with no `developer` file
- [x] A loop (2026.10.10.5554): a road of 300 m to a loop of 110 m and a road on from it, laid by the
      bridge's `road` with `loop`; the F2004 driven at it on the throttle alone went in at 36 m/s, was
      steered round with four wheels down and its hubs on their stops, and came out on the road at 35.
      Drawn with a road's lines, in the bridge's capture of the car on its way up
- [x] The route driver round a loop (2026.10.10.5554): the F2004 lapped the `Loop` course through the
      bridge's `lap`, braked from 58 m/s to 29 for the loop, went round at 27 to 34 with four wheels down
      at every step, and finished the 660 m in 24.5 s with no wheel off the asphalt
- [x] A loop with barriers and a solid under it (2026.10.10.5554): the F2004 lapped the `Loop` course
      again in 24.6 s with its hull touching nothing, where with the look-ahead's share for turning it
      was said to touch for 0.39 s over the top; stood 20 m round the loop and tipped onto its roof it
      lay there, 1.18 m over the ground, and did not fall through
- [x] A loop put by the editor's edit (2026.10.10.5554): the bridge's `road` with `loop` of 140 m, made
      with `Circuit.PutLoop`, laid as two roads and a loop in three solids and lapped clean by the F2004
      in 24.7 s
- [ ] The editor's own loop controls, clicked: Add a loop here, its length, Other side, Seat and Remove
- [ ] A loop's barriers, seen, and a car's side against one; a kitten on a loop
- [ ] The line to drive on a loop, seen: it was night, and the captures showed nothing
- [ ] A loop with the buggy and the Eldorado, at 30 frames a second, and too slowly; a kitten at one
- [ ] A barrier's open end where a deck begins, seen from the road; a barrier hit head on at speed
- [ ] The editor's width, lean and corner sliders and its warnings, by hand: the capture does not show them
- [ ] The same looked at where a road widens, at a bank's foot and for a tree under a low deck
- [ ] A junction with a road into it shorter than its mouth; the racing line and push on the older circuits
- [ ] The helmet with the driver looking about and the wheel turned; gone when the driver gets out;
      on a car that is not the one flown; and in the vehicle editor, where there is no driver
- [ ] A car standing on a wall it cannot climb, with its hull on the road; the buggy's hull at the foot
      of a wall, where the engine still takes 4 m/s off it in a step
