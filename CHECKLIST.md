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
- [ ] The **Car** panel is seen while a car is flown, its **Unflip** button rights the car, and the
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
- [ ] Driven by a player on the keys, rough ground, getting back in
