# Source recordings

The recordings the cars' engine sounds are cut from. A recording has a provenance, and the provenance
has to live where the next person will find it, so the sources stay here and the cuts in
`src/KSACars/Sounds/` are made from them by a script.

| File | Source | Licence |
| --- | --- | --- |
| `buggy/734252__yfjesse__car-engine-start.wav` | freesound.org/s/734252/, by yfjesse — a '74 VW Beetle starting, idling, revving and stopping | **CC0** |
| `buggy/331707__…_148s-152s.wav` | freesound.org/s/331707/, by be_a_hero_not_a_patriot — four seconds of a '74 Beetle driving, contact mic on the hood | **CC0** |
| `eldorado/73729__galarne__cadillac-start_000s-006s.wav` | freesound.org/s/73729/, by Galarne — a 1966 Cadillac Coupe de Ville starting, the first six seconds | **CC0** |
| `eldorado/779211__…_044s-052s.wav`, `…_146s-155s.wav` | freesound.org/s/779211/, by YannSauvin — a 1980 Chevrolet Impala's V8 held at a steady RPM, and switched off | **CC0** |
| `f2004/Red_Bull-Cosworth_RB1_(2005)_017s-022s.wav` | commons.wikimedia.org/wiki/File:Red_Bull-Cosworth_RB1_(2005).ogg, by Edvvc — a 2005 Red Bull-Cosworth RB1 pulling away from the start at the 2010 Goodwood Festival of Speed, five seconds round the launch | **CC BY-SA 3.0** |

CC0 places no condition on redistribution, so those ship inside the MIT archive with nothing to carry
alongside them, and no credit for them is shipped to players, by choice — the record above is kept for
maintenance rather than obligation, so that each recording can be shown to be safe without anyone
having to re-derive where it came from.

**The F2004's recording is not CC0, and its conditions ship with it.** CC BY-SA 3.0 asks for the
author's name, the licence, and a note that the work was changed, and puts what is made from it under
the same licence. So `README.md`, which goes into the archive, credits it under *Credits*, and the four
`KSACars_F1_*.wav` files are CC BY-SA 3.0 themselves, not MIT. Removing that section, or cutting
another car's sound from this recording without adding to it, breaks the licence.

Keep the filename as the source produced it. A freesound name encodes the id, and a Commons name is the
file's title: either is the only thing that leads back to the source page and its licence.

## The buggy's engine

    ./tools/buggy-sounds.py

The start, the idle loop and the shut-off come from the engine-start recording, whose idle fires at
45 Hz: 1350 rpm on a flat-four, which is the profile's idle. The loop under load is the steadiest
1.7 s of the drive recording, found by tracking its strongest engine line; only four seconds around
it are kept here, since the other three minutes are road noise and gear changes.

## The Eldorado's engine

    ./tools/eldorado-sounds.py

The start and the idle loop are the Cadillac's. Its idle fires at 95 Hz, a cold start's fast idle of
about 1400 rpm on a V8, which the profile records as `IdleRecordedRpm` so the loop is pitched down to
the warm idle rather than played as recorded. The loop under load and the key-off are the Impala's,
held near 125 Hz (1900 rpm): the Cadillac recording drives away into the distance and ends with its
door slammed over the shutdown, so it has neither a steady held note nor a clean key-off.

## The F2004's engine

    ./tools/f2004-sounds.py

One recording, of a 3.0 litre V10 of the F2004's own formula. It has three seconds of the engine under
load and nothing else the car needs: it opens with the car already running among others and ends with
it gone. The note falls from 675 to 609 Hz as the car bogs off the line, so the take is replayed at a
rate that follows the note and holds it at 641 Hz, and levelled, since the car is driving away. The loop under load is cut from that. The idle,
the start and the key-off are the loop played slower: the same engine's timbre at revs it was never
recorded at, which is not what an idling V10 sounds like and is the nearest thing to it there was.
The 641 Hz line is one bank's firing, two and a half to a turn, so the take is at 15,380 rpm. Read as
all ten cylinders' firing it is 7690 rpm, and the car then idles like one racing and screams an octave
high at speed: heard in game, and the spectrum alone does not say which it is.
