#!/usr/bin/env python3
"""
Writes what a road is drawn in: its asphalt, and the earth of its verges and embankments.

    ./tools/model/road-textures.py     # -> src/KSACars/Textures/KSACars_Road_*.png, KSACars_RoadLined_*.png and KSACars_RoadEarth_*.png

A road's mesh is made while the game runs, so there is nothing to model. The plain asphalt and the
earth repeat every 4 m of ground, laid by where a vertex is on the circuit's chart and not along the
road, so neither has a direction and each tiles with itself: a junction, a deck's sides and the banks
are drawn in those. A run's own asphalt is drawn in the lined one, which runs with the road.
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
    high, wide = diffuse.shape[:2]
    normal = np.full((high, wide, 3), (128.0, 128.0, 255.0))
    # R occlusion, G roughness, B metalness.
    orm = np.clip(np.dstack([np.full((high, wide), 255.0), np.broadcast_to(roughness, (high, wide)), np.zeros((high, wide))]), 0, 255)
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
    lined(rng)
    trim(rng)
    print("wrote KSACars_Road_*.png, KSACars_RoadLined_*.png, KSACars_RoadEarth_*.png and KSACars_RoadTrim_*.png")


def trim(rng):
    """What stands beside a road: down the picture is 1.5 m along it (RoadTessellation.TrimRepeatM).

    The left half is a kerb, red for half its length and white for the other. The right half is a
    barrier, pale concrete with a darker joint once a panel and a red band along its top, which is where
    across the picture its top face falls."""
    wide, high = 256, 128
    across = (np.arange(wide) + 0.5) / wide
    along = (np.arange(high) + 0.5) / high
    grain = rng.normal(0.0, 5.0, (high, wide))
    colour = np.zeros((high, wide, 3))

    red, white = np.array([176.0, 22.0, 20.0]), np.array([226.0, 224.0, 216.0])
    stripe = (along < 0.5)[:, None]
    kerb = np.where(stripe[..., None], red[None, None, :], white[None, None, :])
    concrete = np.full((high, wide, 3), (150.0, 150.0, 146.0))
    joint = np.clip(1.0 - np.abs(along - 0.02) / 0.02, 0.0, 1.0)[:, None]
    concrete = concrete * (1.0 - 0.45 * joint[..., None])

    # A barrier is laid out across its half as it is walked round: up its face, over its top and down its back.
    faces = 2.0 * 0.9 + 0.2 + 0.5
    over = (across - 0.53) / 0.44 * faces
    band = ((over > 0.9 - 0.18) & (over < 0.9 + 0.2 + 0.18))[None, :]
    concrete = np.where(band[..., None], np.where(stripe[..., None], red[None, None, :], white[None, None, :]), concrete)

    kerbside = (across < 0.5)[None, :, None]
    colour = np.where(kerbside, kerb, concrete) + grain[..., None]
    rough = np.where(across[None, :] < 0.5, 170.0, 215.0) + grain
    write("KSACars_RoadTrim", colour, rough)


def lined(rng):
    """A run's asphalt with its markings: across the picture is across the road, edge to edge whatever its
    width, and down it is 12 m along the road (RoadTessellation.MarkingsM), so it tiles along and not across.

    A white line a quarter of a metre wide just inside each edge of a 10 m road, a dash 3 m long down the middle with 9 m to the next, and the
    asphalt a little darker and smoother where the tyres run either side of the middle."""
    # Far finer across than along. KSA filters a part's texture with no regard for the angle it is seen
    # at, so a road seen along itself is drawn from whichever small copy suits its length, and a line a
    # few texels wide in a square picture is gone at thirty metres. Coarse along, the same view takes a
    # copy eight times bigger, and the lines are wide enough to be left in it.
    wide, high = 1024, 128
    across = (np.arange(wide) + 0.5) / wide
    along = (np.arange(high) + 0.5) / high
    grain = rng.normal(0.0, 6.0, (high, wide))

    # Blotches that tile down the picture: made the full width and wrapped, like the plain asphalt's.
    spectrum = np.fft.fft2(rng.normal(0.0, 1.0, (high, wide)))
    fy, fx = np.fft.fftfreq(high)[:, None], np.fft.fftfreq(wide)[None, :]
    patchy = np.real(np.fft.ifft2(spectrum * np.exp(-((fy ** 2) * 5.0 ** 2 + (fx ** 2) * 40.0 ** 2) * 2.0)))
    patchy /= np.abs(patchy).max()

    worn = np.exp(-((across - 0.27) / 0.09) ** 2) + np.exp(-((across - 0.73) / 0.09) ** 2)
    shade = (1.0 - 0.16 * worn[None, :]) * (1.0 + 0.06 * patchy)
    colour = np.array([46.0, 46.0, 50.0])[None, None, :] * shade[..., None] + grain[..., None]
    rough = 225.0 - 30.0 * worn[None, :] + grain

    def soft(x, low, high_, edge):
        return np.clip((x - low) / edge, 0.0, 1.0) * np.clip((high_ - x) / edge, 0.0, 1.0)

    edges = soft(across, 0.018, 0.044, 0.002) + soft(across, 0.956, 0.982, 0.002)
    dash = soft(across, 0.487, 0.513, 0.002)[None, :] * soft(along, 0.0, 0.25, 0.012)[:, None]
    paint = np.clip(edges[None, :] + dash, 0.0, 1.0)

    # Paint that has been driven over: thinner in patches, never quite white.
    paint *= np.clip(0.82 + 0.25 * patchy + rng.normal(0.0, 0.05, (high, wide)), 0.45, 1.0)
    colour = colour * (1.0 - paint[..., None]) + np.array([214.0, 212.0, 204.0])[None, None, :] * paint[..., None]
    rough = rough * (1.0 - paint) + 150.0 * paint
    write("KSACars_RoadLined", colour, rough)


if __name__ == "__main__":
    main()
