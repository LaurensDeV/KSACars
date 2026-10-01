#!/usr/bin/env python3
"""
Paints a car's emissive map: its lamp lenses white, everything else black.

    ./tools/model/lens-emissive.py tools/model/lenses/Eldorado.json src/KSACars/Textures/KSACars_Eldorado_Emissive.png

KSA adds a part's emissive texture to its surface unless the part's light switch is off. Its shader
reads the red channel alone, as a mask: the glow is white, or the one colour the drawn subpart carries.
So every lens is painted white here, and a red or an amber one gets its colour by being a subpart of
its own -- split-lenses.py -- which the mod colours. The lens faces are named by material in each car's
bake-source .blend, which is not in this repository; lenses/<Car>.json is their UV polygons read out
of it, in Blender's UV space (V up), and has to be re-read if the car is unwrapped again.
"""
import json
import sys

from PIL import Image, ImageDraw

SIZE = 2048
# Drawn oversize and shrunk, so a lens edge is antialiased rather than stair-stepped.
OVERSAMPLE = 2


def main() -> int:
    if len(sys.argv) != 3:
        print(__doc__.strip(), file=sys.stderr)
        return 2
    lenses = json.load(open(sys.argv[1], encoding="utf-8"))
    big = SIZE * OVERSAMPLE
    image = Image.new("RGB", (big, big), (0, 0, 0))
    draw = ImageDraw.Draw(image)
    for polygon in lenses["polygons"]:
        points = [(u * big, (1.0 - v) * big) for u, v in polygon["uv"]]
        draw.polygon(points, fill=(255, 255, 255))
    image.resize((SIZE, SIZE), Image.LANCZOS).save(sys.argv[2], optimize=True)
    print(f"{sys.argv[2]}: {len(lenses['polygons'])} lens faces")
    return 0


if __name__ == "__main__":
    sys.exit(main())
