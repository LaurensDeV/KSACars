#!/usr/bin/env python3
"""
Moves a car's coloured lamp lenses out of its body mesh into subparts of their own.

    ./tools/model/split-lenses.py src/KSACars/Meshes/KSACars_Eldorado.glb tools/model/lenses/Eldorado.json \\
        KSACars_Subpart_EldoBody tail=KSACars_Subpart_EldoTailLens amber=KSACars_Subpart_EldoMarkerLens

KSA gives a glowing lens its colour per drawn subpart, never per texel, so a red lens can only glow
red if it is not part of the body. This takes the body's triangles whose UVs fall inside the lens
faces of each named kind (lenses/<Car>.json, read out of the bake-source .blend) and writes them as a
new mesh and its _VM twin, in place, sharing the body's atlas. Nothing moves: the lens still sits
where it was, so nothing is coplanar with it.

Run it again after every export from Blender, which writes the lenses back into the body. A second
run on a file already split changes nothing.
"""
import json
import struct
import sys

import numpy as np

COMPONENT = {5126: "<f4", 5123: "<u2", 5125: "<u4"}
WIDTH = {"VEC3": 3, "VEC2": 2, "SCALAR": 1}


def read(path):
    blob = open(path, "rb").read()
    length = struct.unpack("<I", blob[12:16])[0]
    doc = json.loads(blob[20:20 + length])
    return doc, blob[20 + length + 8:]


def array(doc, binary, index):
    accessor = doc["accessors"][index]
    view = doc["bufferViews"][accessor["bufferView"]]
    if "byteStride" in view:
        sys.exit("an interleaved buffer view: not what Blender's exporter writes, and not handled")
    dtype, width = COMPONENT[accessor["componentType"]], WIDTH[accessor["type"]]
    start = view.get("byteOffset", 0) + accessor.get("byteOffset", 0)
    return np.frombuffer(binary, dtype=dtype, count=accessor["count"] * width, offset=start).reshape(-1, width).copy()


def inside(points, polygon):
    """Which points are inside a polygon, by crossing number."""
    x, y = points[:, 0], points[:, 1]
    hit = np.zeros(len(points), dtype=bool)
    for (x0, y0), (x1, y1) in zip(polygon, polygon[1:] + polygon[:1]):
        if y0 == y1:
            continue
        crosses = ((y0 > y) != (y1 > y)) & (x < (x1 - x0) * (y - y0) / (y1 - y0) + x0)
        hit ^= crosses
    return hit


def main() -> int:
    if len(sys.argv) < 5:
        print(__doc__.strip(), file=sys.stderr)
        return 2
    path, lens_path, body = sys.argv[1:4]
    wanted = dict(pair.split("=", 1) for pair in sys.argv[4:])
    lenses = json.load(open(lens_path, encoding="utf-8"))["polygons"]
    doc, binary = read(path)

    meshes = {}
    for node in doc["nodes"]:
        if "mesh" not in node:
            continue
        primitive = doc["meshes"][node["mesh"]]["primitives"][0]
        attributes = {name: array(doc, binary, i) for name, i in primitive["attributes"].items()}
        meshes[node["name"]] = (attributes, array(doc, binary, primitive["indices"]).reshape(-1, 3))
    if body not in meshes or body + "_VM" not in meshes:
        sys.exit(f"{path} has no {body} with a _VM twin")

    order = list(meshes)
    for kind, name in wanted.items():
        polygons = [p["uv"] for p in lenses if p["kind"] == kind]
        if not polygons:
            sys.exit(f"{lens_path} has no {kind} lens faces")
        moved = 0
        for source, target in ((body, name), (body + "_VM", name + "_VM")):
            attributes, triangles = meshes[source]
            centroids = attributes["TEXCOORD_0"][triangles].mean(axis=1)
            # glTF's V runs down the image and Blender's up.
            centroids[:, 1] = 1.0 - centroids[:, 1]
            taken = np.zeros(len(triangles), dtype=bool)
            for polygon in polygons:
                taken |= inside(centroids, polygon)
            if not taken.any():
                continue
            used, remapped = np.unique(triangles[taken], return_inverse=True)
            meshes[target] = ({k: v[used] for k, v in attributes.items()}, remapped.reshape(-1, 3))
            meshes[source] = (attributes, triangles[~taken])
            if target not in order:
                order.append(target)
            moved = int(taken.sum())
        print(f"{name}: {moved} triangles" if moved else f"{name}: already split")

    out = {"asset": doc["asset"], "scene": 0, "scenes": [{"name": "Scene", "nodes": list(range(len(order)))}],
           "nodes": [], "meshes": [], "accessors": [], "bufferViews": [], "buffers": []}
    chunks, offset = [], 0

    def add(values, component, kind, target, bounds=False):
        nonlocal offset
        raw = np.ascontiguousarray(values, dtype=COMPONENT[component]).tobytes()
        raw += b"\0" * (-len(raw) % 4)
        out["bufferViews"].append({"buffer": 0, "byteLength": len(raw), "byteOffset": offset, "target": target})
        accessor = {"bufferView": len(out["bufferViews"]) - 1, "componentType": component,
                    "count": int(np.asarray(values).size // WIDTH[kind]), "type": kind}
        if bounds:
            accessor["max"] = [float(v) for v in values.max(axis=0)]
            accessor["min"] = [float(v) for v in values.min(axis=0)]
        out["accessors"].append(accessor)
        chunks.append(raw)
        offset += len(raw)
        return len(out["accessors"]) - 1

    for i, name in enumerate(order):
        attributes, triangles = meshes[name]
        primitive = {"attributes": {
            "POSITION": add(attributes["POSITION"], 5126, "VEC3", 34962, bounds=True),
            "NORMAL": add(attributes["NORMAL"], 5126, "VEC3", 34962),
            "TEXCOORD_0": add(attributes["TEXCOORD_0"], 5126, "VEC2", 34962),
        }, "indices": add(triangles.reshape(-1), 5123 if len(attributes["POSITION"]) < 65536 else 5125, "SCALAR", 34963)}
        out["meshes"].append({"name": name, "primitives": [primitive]})
        out["nodes"].append({"mesh": i, "name": name})

    binary = b"".join(chunks)
    out["buffers"].append({"byteLength": len(binary)})
    text = json.dumps(out, separators=(",", ":")).encode()
    text += b" " * (-len(text) % 4)
    with open(path, "wb") as f:
        f.write(struct.pack("<4sII", b"glTF", 2, 12 + 8 + len(text) + 8 + len(binary)))
        f.write(struct.pack("<I4s", len(text), b"JSON") + text)
        f.write(struct.pack("<I4s", len(binary), b"BIN\0") + binary)
    return 0


if __name__ == "__main__":
    sys.exit(main())
