using Brutal.Numerics;
using Xunit;

namespace KSACars.Tests;

public class HeadGearTests
{
    private static double3 Row(double3 point, double3 rowX, double3 rowY, double3 rowZ, double3 t) =>
        (rowX * point.X) + (rowY * point.Y) + (rowZ * point.Z) + t;

    public static TheoryData<double, double, double, double> Turns() => new()
    {
        { 0.0, 0.0, 1.0, 0.0 }, { 1.0, 0.0, 0.0, 1.2 }, { 0.0, 1.0, 0.0, -2.9 }, { 0.3, -0.8, 0.5, 2.1 }, { -0.6, 0.2, 0.77, 3.1 }, { 1.0, 1.0, 1.0, 2.0943951 },
    };

    // A point of the kitten's unposed mesh, in metres, is where KSA's skinning would put it: carried to
    // the model's centimetres by the matrix, which is why its rows are a hundred long, and from there
    // to the part by the seat.
    [Theory]
    [MemberData(nameof(Turns))]
    public void APointOfTheGearIsWhereTheHeadBoneCarriesIt(double ax, double ay, double az, double angle)
    {
        BuggyProfile p = BuggyProfile.F2004;
        doubleQuat turn = doubleQuat.CreateFromAxisAngle(Vec.Unit(new double3(ax, ay, az)), angle);
        double3 rowX = turn * new double3(100, 0, 0), rowY = turn * new double3(0, 100, 0), rowZ = turn * new double3(0, 0, 100);
        double3 t = new(3.0, 61.0, -4.5);

        (double3 at, doubleQuat rotation, double scale) = HeadGear.Pose(p, rowX, rowY, rowZ, t);
        Assert.Equal(1.0, scale, 9);

        foreach (double3 point in new[] { new double3(0.1, 0.75, -0.02), new double3(-0.3, 0.5, 0.2), new double3(0, 0, 0) })
        {
            double3 model = Row(point, rowX, rowY, rowZ, t);
            double3 back = SteeringGrip.ToKittenModel(p, at + (rotation * point));
            Assert.True(Vec.Len(back - model) < 1e-7, $"{point}: {back} and not {model}");
        }
    }

    [Fact]
    public void ABoneThatScalesItsMeshScalesTheGear()
    {
        (_, _, double scale) = HeadGear.Pose(BuggyProfile.F2004, new double3(50, 0, 0), new double3(0, 50, 0), new double3(0, 0, 50), default);
        Assert.Equal(0.5, scale, 9);
    }
}
