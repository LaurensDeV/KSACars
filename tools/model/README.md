# tools/model

The cars are authored in Blender over MCP, per `.claude/skills/ksa-blender/SKILL.md`, and what is
committed is the export. Their `.blend` files are the source and live outside this repository, so the
checkers here are the only gate on art nobody can rebuild from a clean checkout.

| Tool | What |
| --- | --- |
| `checkmesh.py` | unpaired node/mesh names, zero-UV-area triangles and coplanar faces in a `.glb` -- the two in-game defects that look alike as flickering speckle and no preview shows; `--compare` diffs two atlases by geometry |
| `dilate-atlas.py` | fills the empty space round a baked atlas's islands from their nearest neighbour, so mipmapping never averages an island with whatever sits beside it |
| `lens-emissive.py` | paints a car's emissive mask from the lens faces in `lenses/<Car>.json` -- UV polygons read out of the bake-source `.blend`, tagged head, tail or amber |
| `split-lenses.py` | moves the tail and amber lens triangles out of a body mesh into subparts of their own, in place; KSA colours a glowing lens per subpart, so **run it again after every export from Blender** |
| `preview-glb.py` | renders any `.glb` from a few angles, so an export can be judged before it is declared |
| `preview.sh` | runs that from WSL: Blender is a Windows binary and wants Windows paths for the script and for everywhere it writes |

```bash
./tools/model/checkmesh.py src/KSACars/Meshes/*.glb --near-max 0
./tools/model/dilate-atlas.py --key 0,255,0 <Diffuse.png> <PBR.png>
./tools/model/preview.sh src/KSACars/Meshes/KSACars_Eldorado.glb
```

Part space, which is also what the export writes with **+Y Up off**: +X up from the ground, +Y
forward, +Z the car's left. `docs/KSA-MODDING-NOTES.md` has the rest of the asset contract.
