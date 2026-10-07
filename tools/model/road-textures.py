#!/usr/bin/env python3
"""
Writes what a road is drawn in: its asphalt, and the earth of its verges and embankments.

    ./tools/model/road-textures.py     # -> src/KSACars/Textures/KSACars_Road_*.png and KSACars_RoadEarth_*.png

A road's mesh is made while the game runs, so there is nothing to model; these are made here and not
painted because they are noise and nothing about them is a matter of taste. Both repeat every 4 m of
ground, laid by where a vertex is on the circuit's chart and not along the road, so neither has a
direction and each tiles with itself.
"""

import pathlib
import struct
import zlib

import numpy as np

REPO = pathlib.Path(__file__).resolve().parents[2]
TEXTURES = REPO / "src" / "KSACars" / "Textures"
SIZE = 256


def png(rgb):
    h, w, _ = rgb.shape
    raw = b"".join(b"\0" + rgb[y].astype(np.uint8).tobytes() for y in range(h))

    def chunk(kind, data):
        return struct.pack(">I", len(data)) + kind + data + struct.pack(">I", zlib.crc32(kind + data))

    return (b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", w, h, 8, 2, 0, 0, 0))
            + chunk(b"IDAT", zlib.compress(raw, 9)) + chunk(b"IEND", b""))


def write(name, diffuse, roughness):
    normal = np.full((SIZE, SIZE, 3), (128.0, 128.0, 255.0))
    # R occlusion, G roughness, B metalness.
    orm = np.clip(np.dstack([np.full((SIZE, SIZE), 255.0), roughness, np.zeros((SIZE, SIZE))]), 0, 255)
    for kind, image in (("Diffuse", np.clip(diffuse, 0, 255)), ("Normal", normal), ("PBR", orm)):
        (TEXTURES / f"{name}_{kind}.png").write_bytes(png(image))


def blotches(rng, across):
    """Noise with nothing finer than `across` pixels in it, between -1 and 1. Made in frequency space, so it tiles."""
    spectrum = np.fft.fft2(rng.normal(0.0, 1.0, (SIZE, SIZE)))
    f = np.fft.fftfreq(SIZE)
    spectrum *= np.exp(-((f[:, None] ** 2) + (f[None, :] ** 2)) * (across ** 2) * 2.0)
    smooth = np.real(np.fft.ifft2(spectrum))
    return smooth / np.abs(smooth).max()


def main():
    rng = np.random.default_rng(2004)
    # Asphalt: a dark grey with fine grain.
    grain = rng.normal(0.0, 6.0, (SIZE, SIZE, 1))
    write("KSACars_Road", np.array([46.0, 46.0, 50.0]) + grain, 225.0 + grain[..., 0])

    # Earth: a dull brown, darker and lighter in patches a quarter of a metre across, and rougher than the road.
    patches = blotches(rng, 16.0)[..., None]
    grit = rng.normal(0.0, 5.0, (SIZE, SIZE, 1))
    write("KSACars_RoadEarth", np.array([64.0, 54.0, 38.0]) * (1.0 + 0.22 * patches) + grit, np.full((SIZE, SIZE), 245.0))
    print("wrote KSACars_Road_*.png and KSACars_RoadEarth_*.png")


if __name__ == "__main__":
    main()
