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
        return (seat + (ToPart(translationCm) * 0.01), Vec.FromColumns(x, y, z), length * 0.01);
    }

    // The kitten's left is the part's Z, its up the part's X and its ahead the part's Y.
    private static double3 ToPart(double3 model) => new(model.Y, model.Z, model.X);
}
