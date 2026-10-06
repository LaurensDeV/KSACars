# Test checklist

What has been seen working in game, and what has not. Tick a box only for something seen in game; a
passing suite is not a tick. The headline matters less than what is still unticked.

## Since the split

Both cars were driven in the mod they were built in, and everything below that section was ticked
there. This repository renamed every part, subpart, sound and file, and rewired the entry point, so
none of it has been seen as **KSACars** yet:

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
