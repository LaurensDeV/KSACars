using Brutal.Numerics;
using KSACars.Tests;

namespace KSACars;

// The cars as the engine has them, from KSACarsGameData.xml, for the rig in Sim/LapRig.cs.
internal sealed partial record TrackCar
{
    public static readonly TrackCar Manx = new(
        "Manx", BuggyProfile.Manx, BuggyDriveTests.Rig.Com, 650.0, Cuboid(650.0, 1.1, 3.6, 1.7), 0.18, 1.10);

    public static readonly TrackCar Eldorado = new(
        "Eldorado", BuggyProfile.Eldorado, BuggyDriveTests.Rig.EldoradoCom, 2336.0, Cuboid(2336.0, 1.0, 5.6, 2.0), 0.25, 1.00);

    public static readonly TrackCar F2004 = new(
        "F2004", BuggyProfile.F2004, BuggyDriveTests.Rig.F2004Com, 605.0, Cuboid(605.0, 0.5, 5.6, 2.0), 0.14, 0.70);

    public static readonly TrackCar[] All = [Manx, Eldorado, F2004];

    public static TrackCar Of(string name) => All.First(c => c.Name == name);

    private static double3 Cuboid(double mass, double x, double y, double z) =>
        new double3((y * y) + (z * z), (x * x) + (z * z), (x * x) + (y * y)) * (mass / 12.0);
}
