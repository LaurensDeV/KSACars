using Brutal.Numerics;
using Xunit;

namespace KSACars.Tests;

public class RoadCurveTests
{
    [Fact]
    public void TheCurveStartsAndEndsOnItsPointsAndIsSampledNoCoarserThanAsked()
    {
        double3 a = new(0, 0, 0), b = new(0, 100, 0), c = new(80, 200, 0), d = new(80, 300, 0);
        double3[] points = RoadCurve.Sample(a, b, c, d, 2.0);

        Assert.Equal(a, points[0]);
        Assert.Equal(d, points[^1]);
        for (int i = 1; i < points.Length; i++)
        {
            Assert.True(Vec.Len(points[i] - points[i - 1]) < 2.6, $"{Vec.Len(points[i] - points[i - 1]):F2} m between {i - 1} and {i}");
        }
    }
}

public class ClutterGridTests
{
    [Theory]
    [InlineData(1.0, 0.2, -0.3)]
    [InlineData(-1.0, 0.4, 0.1)]
    [InlineData(0.3, 1.0, -0.2)]
    [InlineData(0.1, -1.0, 0.6)]
    [InlineData(-0.5, 0.2, 1.0)]
    [InlineData(0.3, -0.7, -1.0)]
    public void ADirectionComesBackFromItsPlaceOnTheCube(double x, double y, double z)
    {
        double3 dir = Vec.Unit(new double3(x, y, z));
        (int face, double u, double v) = ClutterGrid.FaceUv(dir);
        Assert.True(Vec.Len(ClutterGrid.Direction(face, u, v) - dir) < 1e-12);
    }

    [Fact]
    public void WhatIsTakenFromUnderALineIsEverythingWithinReachAndNothingBeyondIt()
    {
        const double radius = 6_371_000.0, reach = 5.0;
        int resolution = (int)Math.Ceiling(radius * Math.PI / 2.0 / (16.0 * 1.45));
        double3 from = Vec.Unit(new double3(1.0, 0.31, -0.22)), along = Vec.Unit(Vec.Cross(from, new double3(0, 0, 1)));
        double3[] line = new double3[100];
        for (int i = 0; i < line.Length; i++) line[i] = Vec.Unit(from + (along * (i * 1.0 / radius)));

        var cleared = ClutterGrid.Under(line, radius, reach, resolution);

        (int face, double u, double v) = ClutterGrid.FaceUv(line[50]);
        int cx = (int)(u * resolution), cy = (int)(v * resolution), taken = 0;
        for (int y = cy - 3; y <= cy + 3; y++)
        {
            for (int x = cx - 3; x <= cx + 3; x++)
            {
                cleared.TryGetValue((face, x, y), out uint[]? bits);
                for (int slot = 0; slot < ClutterGrid.Slots; slot++)
                {
                    double3 at = ClutterGrid.Instance(face, x, y, slot, resolution);
                    double nearest = line.Min(p => Vec.Len(at - p)) * radius;
                    bool gone = bits != null && (bits[slot / 32] & (1u << (slot % 32))) != 0;
                    Assert.Equal(nearest <= reach, gone);
                    if (gone) taken++;
                }
            }
        }
        Assert.InRange(taken, 300, 700);
    }
}
