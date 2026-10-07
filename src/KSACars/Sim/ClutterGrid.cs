using Brutal.Numerics;

namespace KSACars;

/// <summary>
/// Where KSA scatters grass, trees and rocks, worked out as its generation shader works it out, so the
/// ones standing on a road can be named and left out.
///
/// <para>A kind of clutter is laid on a grid over each face of a cube round the body. A cell holds 16
/// by 16 slots, one thing in each, pushed up to a slot either way by a hash of the cell and the slot.
/// KSA keeps a bit per slot, and a cleared bit is a thing not drawn and not collided with.</para>
/// </summary>
public static class ClutterGrid
{
    public const int Edge = 16;
    public const int Slots = Edge * Edge;

    /// <summary>The cube face a direction from the body's centre falls on, and where on it, 0 to 1.</summary>
    public static (int Face, double U, double V) FaceUv(double3 dir)
    {
        double x = dir.X, y = dir.Y, z = dir.Z;
        double ax = Math.Abs(x), ay = Math.Abs(y), az = Math.Abs(z);
        int face = ax >= ay && ax >= az ? (x > 0.0 ? 0 : 1)
                 : ay >= ax && ay >= az ? (y > 0.0 ? 2 : 3)
                 : z > 0.0 ? 4 : 5;
        (double a, double b) = face switch
        {
            0 => (z / x, y / x),
            1 => (-z / x, y / x),
            2 => (x / y, -z / y),
            3 => (x / y, z / y),
            4 => (x / z, y / z),
            _ => (-x / z, y / z),
        };
        return (face, ((Math.Atan(a) / (Math.PI / 4.0)) + 1.0) * 0.5, ((Math.Atan(b) / (Math.PI / 4.0)) + 1.0) * 0.5);
    }

    public static double3 Direction(int face, double u, double v)
    {
        double a = Math.Tan(((u * 2.0) - 1.0) * (Math.PI / 4.0));
        double b = Math.Tan(((v * 2.0) - 1.0) * (Math.PI / 4.0));
        return Vec.Unit(face switch
        {
            0 => new double3(1.0, b, a),
            1 => new double3(-1.0, -b, a),
            2 => new double3(a, 1.0, -b),
            3 => new double3(-a, -1.0, -b),
            4 => new double3(a, b, 1.0),
            _ => new double3(a, -b, -1.0),
        });
    }

    /// <summary>
    /// Where the thing in one slot of a cell stands, as a direction from the body's centre. The slot's
    /// row runs against the face's V.
    /// </summary>
    /// <param name="jitter">Off, and the thing stands in the middle of its slot.</param>
    /// <param name="flip">Off, and the slot's row runs with the face's V. Both are for measuring this against the game.</param>
    public static double3 Instance(int face, int cellX, int cellY, int slot, int resolution, bool jitter = true, bool flip = true)
    {
        int sx = slot % Edge, sy = slot / Edge;
        (float jx, float jy) = jitter ? Jitter(cellX, cellY, sx, sy) : (0.5f, 0.5f);
        double lu = ((sx + 0.5) / Edge) + ((jx - 0.5) * 2.0 / Edge);
        double lv = ((sy + 0.5) / Edge) + ((jy - 0.5) * 2.0 / Edge);
        return Direction(face, (cellX + lu) / resolution, (cellY + (flip ? 1.0 - lv : lv)) / resolution);
    }

    /// <summary>
    /// The slots of every cell whose thing stands within <paramref name="reachM"/> of a line of points
    /// on a body of <paramref name="radiusM"/>, as the bits to clear in each cell's mask. Points are
    /// directions from the centre, close enough together that the line between two is as good as either.
    /// A cell over the edge of a cube face is left alone.
    /// </summary>
    public static Dictionary<(int Face, int X, int Y), uint[]> Under(IReadOnlyList<double3> line, double radiusM,
                                                                     double reachM, int resolution)
    {
        var near = new Dictionary<(int, int, int), List<int>>();
        double cellM = radiusM * (Math.PI / 2.0) / resolution;
        // Two slots of jitter and the tan mapping's stretch towards a face's corner.
        double pad = ((reachM / cellM) + (3.0 / Edge)) * 1.5 / resolution;
        for (int i = 0; i < line.Count; i++)
        {
            (int face, double u, double v) = FaceUv(line[i]);
            int x0 = (int)Math.Floor((u - pad) * resolution), x1 = (int)Math.Floor((u + pad) * resolution);
            int y0 = (int)Math.Floor((v - pad) * resolution), y1 = (int)Math.Floor((v + pad) * resolution);
            for (int y = Math.Max(y0, 0); y <= Math.Min(y1, resolution - 1); y++)
            {
                for (int x = Math.Max(x0, 0); x <= Math.Min(x1, resolution - 1); x++)
                {
                    if (!near.TryGetValue((face, x, y), out List<int>? points))
                    {
                        near[(face, x, y)] = points = [];
                    }
                    points.Add(i);
                }
            }
        }

        var cleared = new Dictionary<(int, int, int), uint[]>();
        double reach = reachM / radiusM;
        foreach (((int face, int x, int y) cell, List<int> points) in near)
        {
            uint[]? bits = null;
            for (int slot = 0; slot < Slots; slot++)
            {
                double3 at = Instance(cell.face, cell.x, cell.y, slot, resolution);
                foreach (int i in points)
                {
                    if (Vec.Len(at - line[i]) <= reach)
                    {
                        bits ??= new uint[Slots / 32];
                        bits[slot / 32] |= 1u << (slot % 32);
                        break;
                    }
                }
            }
            if (bits != null)
            {
                cleared[cell] = bits;
            }
        }
        return cleared;
    }

    /// <summary>How far a slot's thing is pushed from the slot's middle, each way, 0 to 1 with a half for none.</summary>
    public static (float X, float Y) Jitter(int cellX, int cellY, int slotX, int slotY)
    {
        unchecked
        {
            uint seed = ((uint)cellX * 0x1f123bb5u) ^ ((uint)cellY * 0x5bd1e995u);
            uint px = (((uint)slotX * 17u) + ((uint)slotY * 131u)) ^ seed;
            uint py = (((uint)slotX * 269u) + ((uint)slotY * 23u)) ^ (seed * 0x9e3779b9u);
            const float inv = 1.0f / 4294967295.0f;
            return (Pcg(px ^ 0x7f4a7c15u, py ^ 0x7f4a7c15u) * inv, Pcg(px ^ 0x2545f491u, py ^ 0x2545f491u) * inv);
        }
    }

    private static uint Pcg(uint vx, uint vy)
    {
        unchecked
        {
            int rot = (int)(((vx >> 28) + (vy >> 2)) & 31u);
            uint x = (vx * 1664525u) + 1013904223u;
            x ^= x >> 16;
            x *= 2246822519u;
            return rot == 0 ? x : (x << rot) | (x >> (32 - rot));
        }
    }
}
