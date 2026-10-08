# Ferrari F2004: reference catalogue

What the F1 car is modelled from. The images are not in this repository: they are kept beside the
`.blend` in `Documents/KSACars/F2004_refs/`, and `commons_index.json` there records each photo's
author and licence.

## Dimensions

| | | Source |
| --- | --- | --- |
| Length | 4545 mm | Wikipedia, f1technical |
| Width | 1796 mm | " |
| Height | 959 mm | " |
| Wheelbase | 3050 mm | f1technical |
| Front track | 1470 mm | " |
| Rear track | 1405 mm | " |
| Weight | 605 kg with driver | Wikipedia |
| Wheels | BBS, 13 in | " |
| Tyres | Bridgestone Potenza, four grooves front and rear | Bridgestone |
| Engine | Tipo 053, 3.0 l 90 deg V10, 865 hp at 18,300 rpm | Wikipedia |
| Suspension | pushrod and torsion bar, front and rear | " |

## What the rules fix

From the FIA technical regulations as published for 2005. Every number below is unchanged from the
2004 text except where noted, and the F2004 was built to them.

| Rule | Value |
| --- | --- |
| Overall width, wheels included | 1800 mm |
| Bodywork width ahead of the rear axle | 1400 mm |
| Bodywork width behind the rear axle | 1000 mm, which is the rear wing's span |
| Highest bodywork | 950 mm over the reference plane |
| Front overhang | 1200 mm ahead of the front axle at most |
| Rear overhang | 600 mm behind the rear axle at most |
| Step plane | 50 mm over the reference plane, outboard of a reference surface 300 to 500 mm wide |
| Plank | 300 mm wide, from 330 mm behind the front axle to the rear axle, 10 mm thick |
| Complete front wheel | 305 to 355 mm wide, 660 mm diameter at most |
| Complete rear wheel | 365 to 380 mm wide, 660 mm diameter at most |
| Roll hoop | 940 mm over the reference plane, 30 mm behind the cockpit template |
| Rear light | 325 to 400 mm over the reference plane, on the centre line: 0.365 to 0.440 off the ground. The model's lens is 0.355 to 0.455 |

The front wing's height changed for 2005 (raised 50 mm) and the rear wing moved forward, so those
two are taken from the blueprint and the 2004 photographs, not from this text.

## How the wheels are held

Double wishbones with a pushrod at each corner, the springs and dampers inboard. Read off the
head-on and rear photographs below, and Craig Scarborough's analysis of the launch car.

| | |
| --- | --- |
| Wishbones | carbon blades, close to level. The fore leg of each runs straight across the car and only the aft leg sweeps back; the upper fore leg is the fattest member, about 100 mm of chord |
| Front upper | to the tub's flank at about 0.47 up, level with its outboard joint |
| Front lower | to a **single keel**, a fin under the nose on the centre line; the car kept one where its rivals went to twin keels |
| Front pushrod | from the foot of the upright up into the tub's shoulder at 0.59, above the wishbone's pickup |
| Track rod | a blade as broad as the upper wishbone's fore leg, lying just ahead of it and nearly touching |
| Rear | both wishbones, the pushrod and the toe link into the gearbox fairing; the driveshaft through a slot in it |
| Outboard | every arm ends on the upright **inside the rim**, behind a carbon brake drum with a duct scoop on its inboard face |

The upright, drum and scoop are a body of their own at each corner, `F1_Upright_*`: they have to
rise and steer with the wheel, and cannot spin with it. The drum fills the rim to 12 mm, so one
left on the car body would be cut by the rim at the first bump or turn of the wheel. Each blade is necked to 26 mm
where it passes the rim's mouth, and the track rod's outer end is kept close to the steering axis,
because it cannot swing with the upright as a real one does. Measured by moving the wheel against
the arms, a front wheel has 24 mm of bump and 27 mm of droop before an arm meets the rim, and a rear
20 mm and 24 mm; a front wheel turns 24 deg toe-in and 23 deg toe-out. Those are the travel and the
lock the profile can be given.

## Blueprints

| File | What | Source |
| --- | --- | --- |
| `bp_tbp_italy.gif` | Top and side, Monza 2004 trim. 121.9 px/m, front axle at column 129 | [the-blueprints.com](https://the-blueprints.com/blueprints/cars/ferrari/22265/view/ferrari_f2004_italy_gp_2004), Dr Dan Saranga |
| `bp_top.png`, `bp_side.png` | The two halves of it, loaded into the `.blend` as `REF_Blueprint_Top` and `REF_Blueprint_Side` | cropped from the above |
| `bp_getoutlines.png` | Colour side elevation, 481 px: the livery's blocks | [getoutlines.com](https://getoutlines.com/blueprints/12124/2004-ferrari-f2004-f1-formula-blueprints), CC BY 4.0 |

The blueprint draws the tyres at 0.60 m, so it is trusted for plan and for stations along the car
and the published 660 mm is used for the wheels.

**The rules' heights are over the reference plane, not the ground.** The model puts that plane 40 mm
up: the plank's face at 30 mm, the step plane at 90 mm, the rear wing's top at 840 mm and the airbox
at 985 mm. The published 959 mm does not survive that: the blueprint and the Goodwood photograph
both put the airbox at 1.0 m or more off the ground.

## Stations read off the blueprint

Car frame: Y forward from the middle of the wheelbase, Z up from the ground, X across. Metres.

| Y | What |
| --- | --- |
| +2.47 | nose tip, 0.34 up, 0.16 wide and blunt; the nose is 0.28 wide and near parallel back to the axle |
| +2.40 to +1.87 | front wing, 1.40 wide; endplates 0.135 to 0.315 up, curling inboard ahead of the tyre |
| +1.525 | front axle; tyres 0.57 to 0.90 out |
| +1.20 to +0.43 | bargeboards, 0.12 to 0.43 up: the tall edge leads, at the back of the front tyre; the top is level for a third, then falls in a long curve to a low tail clear of the sidepod |
| +0.62 to -0.10 | cockpit opening, rim 0.62 to 0.68 up |
| +0.35 | mirrors, 0.50 out |
| +0.35 | sidepod inlets: a rounded trapezoid leaning inboard toward its foot. The pod is a slab-sided shoulder of the body: a near-flat deck running up into the tub, a tight outer shoulder, a vertical flank; bodywork 1.40 wide from here to -0.80 |
| -0.18 to -0.40 | roll hoop and airbox |
| -0.40 to -1.45 | engine cover spine: one straight line from 0.985 at the airbox to about 0.63 ahead of the rear axle, on a thin flat-sided fin no wider than the airbox; twin-lobed gearbox fairing behind |
| -1.00 | exhaust chimneys, 0.27 out |
| -0.61 to -0.74 | winglet over the sidepod's shoulder, 0.55 to 0.69 out, 0.62 up, endplate outboard, on a single raked blade whose foot is at -0.42. There is no duct under it |
| -0.80 to -1.50 | coke-bottle waist: full width held to -0.80, then a concave tuck to 1.04 wide at -1.10, 0.58 at -1.30 and 0.34 at -1.50 |
| -1.525 | rear axle; tyres 0.50 to 0.90 out |
| -1.66 to -2.08 | rear wing, 1.00 wide, top 0.80 over the reference plane; the endplate's front edge is one straight line behind the axle and its foot is at the beam wing, 0.345 up |

## The kitten

Measured off KSA's own `KSA_Cat.gltf` posed with `ANI_CHA_KSA_Kitten_Seated_Idle.glb`, which is in
the `.blend` as `REF_Kitten`. Heights are over the seated model's origin, 0.63 m under the seat's eye.

| | |
| --- | --- |
| Head | 0.68 wide, 0.57 deep, from 0.51 to 1.06 up |
| Body | 0.42 deep, from 0.09 to 0.53 up; 0.26 across the shoulders |
| Shoulders | 0.44 up, 0.13 either side |
| Arm | 0.075 upper and 0.074 fore: a 0.145 m reach |

The head is wider than the tub (0.56 m) and a third again as wide as the cockpit opening, so it
cannot go down into the cockpit as a driver's helmet does. The seat is raised until the head sits
over the rim, and set forward of a driver's so the back of the head clears the roll hoop.

## The driver's helmet

Modelled on the Schuberth RF1 Michael Schumacher wore in 2004: a full-face shell with a forward chin
bar, a wide visor on a pivot each side, and a livery of red with a white brow band, a black panel
running back from the visor and seven gold stars on the crown. **None of its sponsors' marks are
painted.** The shell is not a scaled helmet: it is grown from the kitten's own head mesh with 2.8 cm
of clearance, so the ears, which stand 14 cm past where a plain shell would be, are under two fairings
moulded into it, and the opening underneath is as wide as the suit's collar and 6 cm more.

The photographs are beside the `.blend` in `Helmet_refs/`, all from Wikimedia Commons, and were
looked at, not copied: nothing of them is in a texture.

| File | Author, licence | Page |
| --- | --- | --- |
| `helmet_Ferrari_helmet_of_Michael_Schumacher_2001.jpg` | Cyberwolf, CC BY 4.0 | [Commons](https://commons.wikimedia.org/wiki/File:Ferrari_helmet_of_Michael_Schumacher,_2001.jpg) |
| `helmet_Michael_Schumacher_2004_Italian_GP_helmet_front_right_2019_Michael_Sch.jpg` | Morio, CC BY-SA 4.0 | [Commons](https://commons.wikimedia.org/wiki/File:Michael_Schumacher_2004_Italian_GP_helmet_front-right_2019_Michael_Schumacher_Private_Collection.jpg) |
| `helmet_Michael_Schumacher_2004_Italian_GP_helmet_top_2019_Michael_Schumacher_.jpg` | Morio, CC BY-SA 4.0 | [Commons](https://commons.wikimedia.org/wiki/File:Michael_Schumacher_2004_Italian_GP_helmet_top_2019_Michael_Schumacher_Private_Collection.jpg) |
| `helmet_Michael_Schumacher_Integralhelm_2000.jpg` | Auge=mit, CC BY-SA 4.0 | [Commons](https://commons.wikimedia.org/wiki/File:Michael_Schumacher_Integralhelm_2000.jpg) |
| `helmet_Michael_Schumacher_Integralhelm_2001.jpg` | Auge=mit, CC BY-SA 4.0 | [Commons](https://commons.wikimedia.org/wiki/File:Michael_Schumacher_Integralhelm_2001.jpg) |
| `helmet_Michael_Schumacher_Integralhelm_2002.jpg` | Auge=mit, CC BY-SA 4.0 | [Commons](https://commons.wikimedia.org/wiki/File:Michael_Schumacher_Integralhelm_2002.jpg) |
| `helmet_Michael_Schumacher_Integralhelm_2003.jpg` | Auge=mit, CC BY-SA 4.0 | [Commons](https://commons.wikimedia.org/wiki/File:Michael_Schumacher_Integralhelm_2003.jpg) |
| `helmet_Michael_Schumacher_Integralhelm_2004.jpg` | Auge=mit, CC BY-SA 4.0 | [Commons](https://commons.wikimedia.org/wiki/File:Michael_Schumacher_Integralhelm_2004.jpg) |
| `helmet_Michael_Schumacher_helmet_Museo_Ferrari.jpg` | Morio, CC BY-SA 3.0 | [Commons](https://commons.wikimedia.org/wiki/File:Michael_Schumacher_helmet_Museo_Ferrari.jpg) |
| `helmet_Michael_Schumacher_helmet.jpg` | pelican-actor, CC BY 2.0 | [Commons](https://commons.wikimedia.org/wiki/File:Michael_Schumacher_helmet.jpg) |

## Photographs

All from Wikimedia Commons.

| File | Shows | Author, licence | Page |
| --- | --- | --- | --- |
| `photo_2006FOS_Ferrari_F2005_001.jpg` | **An F2005, not an F2004.** Nose and front wing differ; do not copy | Mark Woodbury from Southampton, England, CC BY 2.0 | [Commons](https://commons.wikimedia.org/wiki/File:2006FOS_-_Ferrari_F2005_-_001.jpg) |
| `photo_2006FOS_Ferrari_F2005_002.jpg` | **An F2005, not an F2004.** | Mark Woodbury from Southampton, England, CC BY 2.0 | [Commons](https://commons.wikimedia.org/wiki/File:2006FOS_-_Ferrari_F2005_-_002.jpg) |
| `photo_Fale_F1_Monza_2004_127.jpg` | Distant; pit stop or track context only | Fabio Alessandro Locati, CC BY-SA 3.0 | [Commons](https://commons.wikimedia.org/wiki/File:Fale_F1_Monza_2004_127.jpg) |
| `photo_Fale_F1_Monza_2004_128.jpg` | Distant; pit stop or track context only | Fabio Alessandro Locati, CC BY-SA 3.0 | [Commons](https://commons.wikimedia.org/wiki/File:Fale_F1_Monza_2004_128.jpg) |
| `photo_Fale_F1_Monza_2004_129.jpg` | Distant; pit stop or track context only | Fabio Alessandro Locati, CC BY-SA 3.0 | [Commons](https://commons.wikimedia.org/wiki/File:Fale_F1_Monza_2004_129.jpg) |
| `photo_Fale_F1_Monza_2004_130.jpg` | Distant; pit stop or track context only | Fabio Alessandro Locati, CC BY-SA 3.0 | [Commons](https://commons.wikimedia.org/wiki/File:Fale_F1_Monza_2004_130.jpg) |
| `photo_Fale_F1_Monza_2004_131.jpg` | Distant; pit stop or track context only | Fabio Alessandro Locati, CC BY-SA 3.0 | [Commons](https://commons.wikimedia.org/wiki/File:Fale_F1_Monza_2004_131.jpg) |
| `photo_Fale_F1_Monza_2004_132.jpg` | Near plan view from the grandstand, small | Fabio Alessandro Locati, CC BY-SA 3.0 | [Commons](https://commons.wikimedia.org/wiki/File:Fale_F1_Monza_2004_132.jpg) |
| `photo_Fale_F1_Monza_2004_29.jpg` | 3/4 from above, right | Fabio Alessandro Locati, CC BY-SA 3.0 | [Commons](https://commons.wikimedia.org/wiki/File:Fale_F1_Monza_2004_29.jpg) |
| `photo_Fale_F1_Monza_2004_30.jpg` | 3/4 from above, right | Fabio Alessandro Locati, CC BY-SA 3.0 | [Commons](https://commons.wikimedia.org/wiki/File:Fale_F1_Monza_2004_30.jpg) |
| `photo_Fale_F1_Monza_2004_43.jpg` | Distant; pit stop or track context only | Fabio Alessandro Locati, CC BY-SA 3.0 | [Commons](https://commons.wikimedia.org/wiki/File:Fale_F1_Monza_2004_43.jpg) |
| `photo_Ferrari_F1.jpg` | **Primary side view.** Pure profile, Goodwood. Airbox, engine-cover spine, sidepod undercut, bargeboard, rear-wing endplate | Brian Snelson from Hockley, Essex, Engla, CC BY 2.0 | [Commons](https://commons.wikimedia.org/wiki/File:Ferrari_F1.jpg) |
| `photo_Ferrari_F2004_8925204549_.jpg` | High 3/4 from the front left: nose droop, front-wing planform, cockpit opening | emperornie, CC BY-SA 2.0 | [Commons](https://commons.wikimedia.org/wiki/File:Ferrari_F2004_(8925204549).jpg) |
| `photo_Ferrari_F2004_8925316574_.jpg` | **Primary high 3/4 from the left.** Cockpit rim, mirrors, sidepod top, chimney and winglet, coke-bottle waist | emperornie, CC BY-SA 2.0 | [Commons](https://commons.wikimedia.org/wiki/File:Ferrari_F2004_(8925316574).jpg) |
| `photo_Ferrari_f2004.jpg` | 3/4 front, on track, small | emperornie, CC BY-SA 2.0 | [Commons](https://commons.wikimedia.org/wiki/File:Ferrari_f2004.jpg) |
| `photo_FerrariF2004.jpg` | Thumbnail, rear 3/4 from above | Own work, CC BY-SA 3.0 | [Commons](https://commons.wikimedia.org/wiki/File:FerrariF2004.jpg) |
| `photo_Rubens_Barrichello_Ferrari_F2004_at_the_2004_Briti.jpg` | Side view from the left, Silverstone, non-tobacco livery | Martin Lee from London, UK, CC BY-SA 2.0 | [Commons](https://commons.wikimedia.org/wiki/File:Rubens_Barrichello_-_Ferrari_F2004_at_the_2004_British_Grand_Prix_(50838015736).jpg) |
| `photo_Scuderia_Ferrari_F2004.jpg` | **Primary front 3/4, static.** Front wing and endplates, nose pylons, pushrod and wishbones, BBS wheel, sidepod inlet | PagniDD, CC BY-SA 4.0 | [Commons](https://commons.wikimedia.org/wiki/File:Scuderia_Ferrari_F2004.jpg) |
| `photo_Xcvhjewkrjei0098.jpg` | Distant; pit stop or track context only | ?, CC BY-SA 2.5 | [Commons](https://commons.wikimedia.org/wiki/File:Xcvhjewkrjei0098.JPG) |
| `photo_Fale_F1_Monza_2004_31.jpg` | Side view from the right, Monza low-downforce rear wing | Fabio Alessandro Locati, CC BY-SA 3.0 | [Commons](https://commons.wikimedia.org/wiki/File:Fale_F1_Monza_2004_31.jpg) |
| `photo_Ferrari_F2004.jpg` | Duplicate of the above | storem, CC BY-SA 2.0 | [Commons](https://commons.wikimedia.org/wiki/File:Ferrari_F2004.jpg) |
| `photo_Inside_the_Galleria_Ferrari.jpg` | Museum, front 3/4 from above | Jazon88, CC BY-SA 3.0 | [Commons](https://commons.wikimedia.org/wiki/File:Inside_the_Galleria_Ferrari.jpg) |
| `photo_Michael_Schumacher_2004_Monaco.jpg` | Head-on from above: front wing span against track, tyre width | Cord Rodefeld from Ulm, Germany, CC BY 2.0 | [Commons](https://commons.wikimedia.org/wiki/File:Michael_Schumacher_2004_Monaco.jpg) |
| `photo_Michael_Schumacher_Ferrari_2004.jpg` | Low 3/4 from the right, Indianapolis: bargeboard, lower sidepod, floor edge | Rick Dikeman, CC BY-SA 3.0 | [Commons](https://commons.wikimedia.org/wiki/File:Michael_Schumacher_Ferrari_2004.jpg) |
| `photo_Michael_Schumacher_win_2004.jpg` | Close on the cockpit side: headrest collar, roll hoop, sidepod shoulder | Rick Dikeman, CC BY-SA 3.0 | [Commons](https://commons.wikimedia.org/wiki/File:Michael_Schumacher_win_2004.jpg) |
| `photo_Schmacher_and_Sato_at_Monaco_2004.jpg` | Distant; pit stop or track context only | Cord Rodefeld from Ulm, Germany, CC BY 2.0 | [Commons](https://commons.wikimedia.org/wiki/File:Schmacher_and_Sato_at_Monaco_2004.jpg) |
| `photo_Schumacher_at_Monaco_2004.jpg` | **Primary high front 3/4.** Top surfaces: sidepod shoulders, mirrors, rear wing in Monaco trim, grooved tyres | Cord Rodefeld from Ulm, Germany, CC BY 2.0 | [Commons](https://commons.wikimedia.org/wiki/File:Schumacher_at_Monaco_2004.jpg) |
| `photo_2004_Ferrari_F2004_Museo_Nazionale_dell_Automobile.jpg` | Head-on, low. **Not an F2004**: number 5, a later livery and a front wing raised in the middle. Do not copy | Rahil Rupawala, CC BY 2.0 | [Commons](https://commons.wikimedia.org/wiki/File:2004_Ferrari_F2004_Museo_Nazionale_dell%27Automobile_Torino.jpg) |
| `photo_FInaliMondiali_Ferrari_2017_13.jpg` | Row of showcars, front 3/4 low | mirco.81, CC BY 2.0 | [Commons](https://commons.wikimedia.org/wiki/File:FInaliMondiali_Ferrari_2017_13.jpg) |
| `susp_2003_Ferrari_F2003_GA.jpg` | F2003-GA rear corner close up: rear wheel, endplate, flip-up | pelican-actor, CC BY 2.0 | [Commons](https://commons.wikimedia.org/wiki/File:2003_Ferrari_F2003-GA.jpg) |
| `susp_2003_Ferrari_F2003_GA_52037261647.jpg` | **Primary for the rear suspension.** F2003-GA from behind: wishbones into the gearbox fairing, beam wing, rain light, diffuser fences | pelican-actor, CC BY 2.0 | [Commons](https://commons.wikimedia.org/wiki/File:2003_Ferrari_F2003-GA_-_52037261647.jpg) |
| `susp_2003_Ferrari_F2003_GA_52037261677.jpg` | F2003-GA front 3/4 from above | pelican-actor, CC BY 2.0 | [Commons](https://commons.wikimedia.org/wiki/File:2003_Ferrari_F2003-GA_-_52037261677.jpg) |
| `susp_2003_Ferrari_F2003_GA_52038303076.jpg` | F2003-GA sidepod inlet and bargeboard close up | pelican-actor, CC BY 2.0 | [Commons](https://commons.wikimedia.org/wiki/File:2003_Ferrari_F2003-GA_-_52038303076.jpg) |
| `susp_2003_Ferrari_F2003_GA_52038355153.jpg` | F2003-GA from behind and above | pelican-actor, CC BY 2.0 | [Commons](https://commons.wikimedia.org/wiki/File:2003_Ferrari_F2003-GA_-_52038355153.jpg) |
| `susp_2003_Ferrari_F2003_GA_52038550219.jpg` | **Primary for the front suspension.** F2003-GA head-on, level: wishbone heights against the tyre | pelican-actor, CC BY 2.0 | [Commons](https://commons.wikimedia.org/wiki/File:2003_Ferrari_F2003-GA_-_52038550219.jpg) |
| `susp_Ferrari_053_engine_rear_Museo_Ferrari.jpg` | The Tipo 053 engine alone | Morio, CC BY-SA 3.0 | [Commons](https://commons.wikimedia.org/wiki/File:Ferrari_053_engine_rear_Museo_Ferrari.jpg) |
| `susp_Ferrari_2_Mondial_de_l_Automobile_de_Paris_2004.jpg` | Show stand, small | Arnaud Ligny from Paris, France, CC BY-SA 2.0 | [Commons](https://commons.wikimedia.org/wiki/File:Ferrari_2_Mondial_de_l%E2%80%99Automobile_de_Paris_2004.jpg) |
| `susp_Ferrari_F2003_GA_2003_52864385234_.jpg` | **Primary for the front suspension.** F2003-GA head-on from above: both wishbones, keel, pushrod, bargeboards | Charles from Port Chester, New York, CC BY 2.0 | [Commons](https://commons.wikimedia.org/wiki/File:Ferrari_F2003-GA_(2003)_(52864385234).jpg) |
| `susp_Ferrari_F2004_F1_Michael_Schumachers_2004_AboveL.jpg` | F2004 rear 3/4 from above | Valder137, CC BY 2.0 | [Commons](https://commons.wikimedia.org/wiki/File:Ferrari_F2004_F1_Michael_Schumachers_2004_AboveLSideRear_CECF_9April2011_(14598931104).jpg) |
| `susp_Ferrari_F2004_F1_Michael_Schumachers_2004_AboveR.jpg` | F2004 from behind and above: engine cover spine, chimneys, flip-ups in plan | Valder137, CC BY 2.0 | [Commons](https://commons.wikimedia.org/wiki/File:Ferrari_F2004_F1_Michael_Schumachers_2004_AboveRear_CECF_9April2011_(14597612471).jpg) |
| `susp_Ferrari_F2004_F1_Michael_Schumachers_2004_LSideR.jpg` | F2004 rear 3/4, left | Valder137, CC BY 2.0 | [Commons](https://commons.wikimedia.org/wiki/File:Ferrari_F2004_F1_Michael_Schumachers_2004_LSideRear_CECF_9April2011_(14414251570).jpg) |
| `susp_Ferrari_F2004_F1_Michael_Schumachers_2004_Rear_C.jpg` | **Primary for the rear.** F2004 from behind | Valder137, CC BY 2.0 | [Commons](https://commons.wikimedia.org/wiki/File:Ferrari_F2004_F1_Michael_Schumachers_2004_Rear_CECF_9April2011_(14600268412).jpg) |
| `susp_SAG2004_193_Ferrari_F1_Schumacher.jpg` | Head-on, show stand | Semnoz at French Wikipedia, CC BY-SA 3.0 | [Commons](https://commons.wikimedia.org/wiki/File:SAG2004_193_Ferrari_F1_Schumacher.JPG) |
| `susp_SAG2004_194_Ferrari_F1_Schumacher.jpg` | Front wheel and pushrod, close | Semnoz at French Wikipedia, CC BY-SA 3.0 | [Commons](https://commons.wikimedia.org/wiki/File:SAG2004_194_Ferrari_F1_Schumacher.JPG) |
| `susp_SAG2004_195_Ferrari_F1_Schumacher.jpg` | Cockpit side and mirror, close | Semnoz at French Wikipedia, CC BY-SA 3.0 | [Commons](https://commons.wikimedia.org/wiki/File:SAG2004_195_Ferrari_F1_Schumacher.JPG) |
| `susp_SAG2004_196_Ferrari_F1_Schumacher.jpg` | Rear 3/4 | Semnoz at French Wikipedia, CC BY-SA 3.0 | [Commons](https://commons.wikimedia.org/wiki/File:SAG2004_196_Ferrari_F1_Schumacher.JPG) |

## Not found

No photograph of the underside, or of an upright with its wheel off. The floor is modelled from the
rules, and the drum inside each rim from what shows through the spokes.
