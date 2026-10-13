using Brutal.Numerics;
using Xunit;

namespace KSACars.Tests;

public class StartGridTests
{
    private static double3 Corner(RaceLine.Mesh mesh, int vertex) =>
        mesh.Origin + new double3(mesh.Positions[vertex].X, mesh.Positions[vertex].Y, mesh.Positions[vertex].Z);

    [Fact]
    public void EveryCornerOfTheStartLiesOnTheAsphaltAndEveryQuadFacesUp()
    {
        foreach (AutopilotTests.Track track in new[] { AutopilotTests.Ring(60.0), AutopilotTests.Leaning(80.0, 15.0), AutopilotTests.Ramp() })
        {
            Route route = AutopilotTests.RouteOn(track);
            RaceLine.Mesh mesh = StartGrid.Lay(route, track.Road)!;
            Assert.Equal(0, mesh.Positions.Length % 4);
            Assert.Equal(mesh.Positions.Length / 4 * 6, mesh.Indices.Length);

            for (int v = 0; v < mesh.Positions.Length; v++)
            {
                Assert.True(track.Road.TryLocate(Corner(mesh, v), null, out double over, out double outM), "a corner of the start is over no road");
                Assert.InRange(over, StartGrid.LiftM - 0.005, StartGrid.LiftM + 0.005);
                Assert.True(outM <= RoadSurface.EdgeM + 0.01, $"a corner of the start is {outM:F2} m off the asphalt");
            }
            for (int i = 0; i < mesh.Indices.Length; i += 3)
            {
                double3 a = Corner(mesh, mesh.Indices[i]), b = Corner(mesh, mesh.Indices[i + 1]), c = Corner(mesh, mesh.Indices[i + 2]);
                Assert.True(Vec.Dot(Vec.Cross(b - a, c - a), Vec.Unit(a)) > 0.0, "a triangle of the start faces down");
            }
        }
    }

    [Fact]
    public void TheLineIsChequeredBetweenTheRoadsEdgeLinesAheadOfThePole()
    {
        AutopilotTests.Track ring = AutopilotTests.Ring(60.0);
        Route route = AutopilotTests.RouteOn(ring);
        RaceLine.Mesh mesh = StartGrid.Lay(route, ring.Road)!;
        Route.Sample at = route.Samples[route.IndexAt(StartGrid.PoleM + StartGrid.LineAheadM)];
        Route.Sample pole = route.Samples[route.IndexAt(StartGrid.PoleM)];

        // the first quad is the near white line: as wide as the asphalt, and past the nose of the car on pole
        double wide = Vec.Len(Corner(mesh, 1) - Corner(mesh, 0));
        double between = 2.0 * at.HalfWidth * (1.0 - (2.0 * StartGrid.EdgeShare));
        Assert.InRange(wide, between - 0.05, between + 0.05);
        Assert.True(Vec.Dot(Corner(mesh, 0) - pole.At, pole.Tangent) > 3.0, "the line is not ahead of the pole");

        // then two rows of squares, as many white as black to within one a row, and next to one another never the same
        int columns = Math.Max(2, (int)Math.Round(2.0 * at.HalfWidth * (1.0 - (2.0 * StartGrid.EdgeShare)) / StartGrid.SquareM));
        int white = 0, black = 0;
        for (int q = 1; q <= 2 * columns; q++)
        {
            bool isWhite = mesh.Uvs[q * 4].X < 0.5f;
            if (isWhite) white++; else black++;
            if (q > 1 && q != columns + 1) Assert.NotEqual(isWhite, mesh.Uvs[(q - 1) * 4].X < 0.5f);
        }
        Assert.InRange(white - black, -2, 2);
        Assert.NotEqual(mesh.Uvs[1 * 4].X < 0.5f, mesh.Uvs[(columns + 1) * 4].X < 0.5f);
    }

    [Fact]
    public void AClosedCourseHasAGridInTwoColumnsAndAnOpenOneOnlyItsPole()
    {
        AutopilotTests.Track ring = AutopilotTests.Ring(60.0);
        Route round = AutopilotTests.RouteOn(ring);
        Assert.Equal(StartGrid.Boxes, StartGrid.BoxesOn(round));
        Assert.Equal(1, StartGrid.BoxesOn(AutopilotTests.RouteOn(AutopilotTests.Ramp())));

        Assert.True(StartGrid.Column(0, 5.0) > 0.0 && StartGrid.Column(1, 5.0) < 0.0, "the pole is to the left and the next box to the right");
        Assert.Equal(0.0, StartGrid.Column(0, 3.0));
        Assert.True(StartGrid.Column(0, 5.0) + (0.5 * StartGrid.BoxWideM) < 5.0, "a box reaches past the asphalt's edge");

        // three quads a box, after the two lines and the two rows of squares
        RaceLine.Mesh mesh = StartGrid.Lay(round, ring.Road)!;
        Route.Sample at = round.Samples[round.IndexAt(StartGrid.PoleM + StartGrid.LineAheadM)];
        int columns = Math.Max(2, (int)Math.Round(2.0 * at.HalfWidth * (1.0 - (2.0 * StartGrid.EdgeShare)) / StartGrid.SquareM));
        Assert.Equal(2 + (2 * columns) + (3 * StartGrid.Boxes), mesh.Positions.Length / 4);
    }
}
