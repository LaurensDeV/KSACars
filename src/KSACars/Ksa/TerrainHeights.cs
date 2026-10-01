using Brutal.Numerics;
using KSA;

namespace KSACars;

/// <summary>
/// One body's height field, off the engine's own height map.
///
/// <para><c>accurate: false</c> is the engine's own terrain solver's choice, and the default; a car's
/// springs ask for <c>accurate: true</c>, because they are deciding exactly where the ground is under
/// each hub. <c>docs/KSA-TERRAIN.md</c> has what each buys.</para>
/// </summary>
internal sealed class TerrainHeights(Celestial body, bool accurate = false) : ITerrainHeights
{
    private readonly Celestial _body = body;
    private readonly bool _accurate = accurate;

    public bool TryHeight(double3 dirFromCentre, out double metres)
    {
        metres = 0.0;

        try
        {
            metres = _body.GetTerrainHeightFromDirCce(dirFromCentre, accurate: _accurate);

            return double.IsFinite(metres);
        }
        catch
        {
            return false;
        }
    }
}
