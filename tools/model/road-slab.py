#!/usr/bin/env python3
"""
Writes the one mesh every road is drawn with, and its asphalt.

    ./tools/model/road-slab.py     # -> src/KSACars/Meshes/KSACars_RoadSlab.glb and Textures/KSACars_Road_*.png

A box one unit across (X), one deep below its top (Y from -1 to 0) and one long (Z from 0 to 1). The mod
draws it once for each stretch between two points of a road, scaled to the road's width, its thickness and
the stretch's length, so the top face lies on the road's line. It is made here and not in Blender because
it is twelve triangles and nothing about it is a matter of taste.
"""

import json
import pathlib
import struct
import zlib

import numpy as np

REPO = pathlib.Path(__file__).resolve().parents[2]
MESH = REPO / "src" / "KSACars" / "Meshes" / "KSACars_RoadSlab.glb"
TEXTURES = REPO / "src" / "KSACars" / "Textures"


def box():
    centre = np.array([0.0, -0.5, 0.5])
    faces = [  # normal, then two axes with first x second = normal
        ((0, 1, 0), (0, 0, 1), (1, 0, 0)), ((0, -1, 0), (1, 0, 0), (0, 0, 1)),
        ((1, 0, 0), (0, 1, 0), (0, 0, 1)), ((-1, 0, 0), (0, 0, 1), (0, 1, 0)),
        ((0, 0, 1), (1, 0, 0), (0, 1, 0)), ((0, 0, -1), (0, 1, 0), (1, 0, 0)),
    ]
    positions, normals, uvs, indices = [], [], [], []
    for n, a, b in faces:
        n, a, b = (np.array(v, dtype=float) for v in (n, a, b))
        assert np.allclose(np.cross(a, b), n)
        base = len(positions)
        for sa, sb in ((-1, -1), (1, -1), (1, 1), (-1, 1)):
            positions.append(centre + 0.5 * (n + sa * a + sb * b))
            normals.append(n)
            uvs.append(((sa + 1) / 2, (sb + 1) / 2))
        indices += [base, base + 1, base + 2, base, base + 2, base + 3]
    return (np.array(positions, dtype="<f4"), np.array(normals, dtype="<f4"),
            np.array(uvs, dtype="<f4"), np.array(indices, dtype="<u2"))


def glb():
    positions, normals, uvs, indices = box()
    chunks = [positions.tobytes(), normals.tobytes(), uvs.tobytes(), indices.tobytes()]
    views, offset = [], 0
    for chunk in chunks:
        views.append({"buffer": 0, "byteOffset": offset, "byteLength": len(chunk)})
        offset += len(chunk)
    name = "KSACars_RoadSlab"
    doc = {
        "asset": {"version": "2.0", "generator": "tools/model/road-slab.py"},
        "scene": 0, "scenes": [{"nodes": [0]}],
        "nodes": [{"name": name, "mesh": 0}],
        "materials": [{"name": "KSACars_Road"}],
        "meshes": [{"name": name, "primitives": [{"attributes": {"POSITION": 0, "NORMAL": 1, "TEXCOORD_0": 2},
                                                  "indices": 3, "material": 0}]}],
        "buffers": [{"byteLength": offset}], "bufferViews": views,
        "accessors": [
            {"bufferView": 0, "componentType": 5126, "count": len(positions), "type": "VEC3",
             "min": positions.min(axis=0).tolist(), "max": positions.max(axis=0).tolist()},
            {"bufferView": 1, "componentType": 5126, "count": len(normals), "type": "VEC3"},
            {"bufferView": 2, "componentType": 5126, "count": len(uvs), "type": "VEC2"},
            {"bufferView": 3, "componentType": 5123, "count": len(indices), "type": "SCALAR"},
        ],
    }
    text = json.dumps(doc, separators=(",", ":")).encode()
    text += b" " * (-len(text) % 4)
    binary = b"".join(chunks)
    binary += b"\0" * (-len(binary) % 4)
    body = struct.pack("<II", len(text), 0x4E4F534A) + text + struct.pack("<II", len(binary), 0x004E4942) + binary
    return struct.pack("<III", 0x46546C67, 2, 12 + len(body)) + body


def png(rgb):
    h, w, _ = rgb.shape
    raw = b"".join(b"\0" + rgb[y].astype(np.uint8).tobytes() for y in range(h))

    def chunk(kind, data):
        return struct.pack(">I", len(data)) + kind + data + struct.pack(">I", zlib.crc32(kind + data))

    return (b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", w, h, 8, 2, 0, 0, 0))
            + chunk(b"IDAT", zlib.compress(raw, 9)) + chunk(b"IEND", b""))


def main():
    MESH.write_bytes(glb())
    rng = np.random.default_rng(2004)
    size = 256
    # Asphalt: a dark grey with fine grain, the same in every direction because a stretch's UVs are stretched.
    grain = rng.normal(0.0, 6.0, (size, size, 1))
    diffuse = np.clip(np.array([46.0, 46.0, 50.0]) + grain, 0, 255)
    normal = np.full((size, size, 3), (128.0, 128.0, 255.0))
    # R occlusion, G roughness, B metalness.
    orm = np.clip(np.dstack([np.full((size, size), 255.0), 225.0 + grain[..., 0], np.zeros((size, size))]), 0, 255)
    for name, image in (("Diffuse", diffuse), ("Normal", normal), ("PBR", orm)):
        (TEXTURES / f"KSACars_Road_{name}.png").write_bytes(png(image))
    print(f"wrote {MESH.relative_to(REPO)} and three KSACars_Road_*.png")


if __name__ == "__main__":
    main()
