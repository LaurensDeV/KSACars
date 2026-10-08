using Brutal.Numerics;

namespace KSACars;

/// <summary>
/// Where something worn on the driver kitten's head is, as a subpart of the car: placed by the head
/// bone each frame, so it nods and turns as the head does.
///
/// <para>Its mesh is in the space KSA keeps the kitten's own mesh in before any bone moves it: the
/// model's axes, X to its left, Y up and Z ahead, in metres. KSA carries a point of that space to
/// the kitten's model space, which is the same in centimetres, by the head bone's inverse bind matrix
/// and then its pose, so with the head as it was bound that matrix is a hundred times the identity;
/// and the kitten's model stands on its seat, <see cref="SteeringGrip.SeatedDrop"/> under the seat's eye.</para>
/// </summary>
public static class HeadGear
{
    /// <summary>
    /// The pose of the subpart in the part frame, from the three rows and the translation of the
    /// matrix that carries a row vector of the kitten's unposed mesh to its model space.
    /// </summary>
    public static (double3 Position, doubleQuat Rotation, double Scale) Pose(BuggyProfile p, double3 rowX, double3 rowY, double3 rowZ, double3 translationCm)
    {
        double length = (Vec.Len(rowX) + Vec.Len(rowY) + Vec.Len(rowZ)) / 3.0;
        double3 seat = p.DriverEye - new double3(SteeringGrip.SeatedDrop, 0, 0);
        if (!(length > 1e-9)) return (seat, doubleQuat.Identity, 0.0);

        // Where each of the mesh's axes ends up in the part frame: the columns of the turn.
        double3 x = ToPart(rowX / length), y = ToPart(rowY / length), z = ToPart(rowZ / length);
        return (seat + (ToPart(translationCm) * 0.01), FromColumns(x, y, z), length * 0.01);
    }

    // The kitten's left is the part's Z, its up the part's X and its ahead the part's Y.
    private static double3 ToPart(double3 model) => new(model.Y, model.Z, model.X);

    private static doubleQuat FromColumns(double3 x, double3 y, double3 z)
    {
        double trace = x.X + y.Y + z.Z;
        doubleQuat q;
        if (trace > 0.0)
        {
            double s = 2.0 * Math.Sqrt(trace + 1.0);
            q = new doubleQuat((y.Z - z.Y) / s, (z.X - x.Z) / s, (x.Y - y.X) / s, 0.25 * s);
        }
        else if (x.X > y.Y && x.X > z.Z)
        {
            double s = 2.0 * Math.Sqrt(1.0 + x.X - y.Y - z.Z);
            q = new doubleQuat(0.25 * s, (y.X + x.Y) / s, (z.X + x.Z) / s, (y.Z - z.Y) / s);
        }
        else if (y.Y > z.Z)
        {
            double s = 2.0 * Math.Sqrt(1.0 + y.Y - x.X - z.Z);
            q = new doubleQuat((y.X + x.Y) / s, 0.25 * s, (z.Y + y.Z) / s, (z.X - x.Z) / s);
        }
        else
        {
            double s = 2.0 * Math.Sqrt(1.0 + z.Z - x.X - y.Y);
            q = new doubleQuat((z.X + x.Z) / s, (z.Y + y.Z) / s, 0.25 * s, (x.Y - y.X) / s);
        }
        double n = Math.Sqrt((q.X * q.X) + (q.Y * q.Y) + (q.Z * q.Z) + (q.W * q.W));
        return new doubleQuat(q.X / n, q.Y / n, q.Z / n, q.W / n);
    }
}
