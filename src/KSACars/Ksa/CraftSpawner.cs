using Brutal.Numerics;
using KSA;

namespace KSACars;

/// <summary>
/// Parks a craft from a vehicle library on the ground at a latitude and longitude, for the bridge's
/// <c>spawn</c>: a car is tested where it was asked for rather than wherever a save happens to put one.
/// </summary>
internal static class CraftSpawner
{
    /// <summary>
    /// A craft named <paramref name="craftName"/>, from the stock library or the player's own, set down
    /// on <paramref name="body"/>. Built beside <paramref name="platform"/>, which only lends it an orbit
    /// to exist on until <see cref="Vehicle.TeleportToLocation"/> puts it on the ground.
    /// </summary>
    public static Vehicle? SpawnParked(Vehicle platform, string craftName, string id, Celestial body,
                                       double latitudeDeg, double longitudeDeg)
    {
        try
        {
            if (Universe.CurrentSystem is not { } system || platform.Parent is not { } parent) return null;
            if (LoadParts(craftName) is not { } parts) return null;

            double3 up = KsaWorld.LocalUp(platform);
            double3 spawnEcl = KsaWorld.PositionEcl(platform) + up * 500.0;
            if (!ToParentInertial(parent, spawnEcl, KsaWorld.VelocityEcl(platform), out double3 posCci, out double3 velCci))
            {
                return null;
            }

            Orbit orbit = Orbit.CreateFromStateCci(parent, Universe.GetElapsedTime(), posCci, velCci,
                                                   new byte4(255, 80, 80, 255));

            using ShapesUnlock shapes = ConstraintSim.UnlockShapesBlocking();
            Vehicle craft = Vehicle.CreateVehicle(system, platform.Body2Cce, bodyRates: default,
                                                  parent, id, parts.Root, orbit);
            parent.Children.Add(craft);
            craft.Parts.RecomputeAllDerivedData();
            craft.UpdateAfterPartTreeModification();
            craft.UpdatePerFrameData();
            craft.TeleportToLocation(body, latitudeDeg, longitudeDeg);

            Log.Info($"parked '{id}' ({craftName}) at {latitudeDeg:F4}, {longitudeDeg:F4} on {body.Id}");
            return craft;
        }
        catch (Exception e)
        {
            Log.Error("parked spawn failed", e);
            return null;
        }
    }

    // The stock library first, then the player's own, which is where tools/install-testcraft.sh puts
    // the cars. Both FindSave and Load are genuinely nullable in KSA.
    private static PartTree? LoadParts(string craftName)
    {
        try
        {
            VehicleSave? save = DefaultVehicleSaves.FindSave(craftName) ?? LibraryCraft(craftName);
            if (save?.Load(Program.MainViewport) is { } tree) return tree;

            Log.Warn($"spawn: no craft '{craftName}' in either vehicle library");
        }
        catch (Exception e)
        {
            Log.Error($"spawn: could not load '{craftName}'", e);
        }

        return null;
    }

    private static VehicleSave? LibraryCraft(string name)
    {
        VehicleSaves.Refresh();
        foreach (VehicleSave save in VehicleSaves.AsSpan())
        {
            if (string.Equals(save.Id, name, StringComparison.OrdinalIgnoreCase)
                || string.Equals(save.VehicleSaveData.Id, name, StringComparison.OrdinalIgnoreCase))
            {
                return save;
            }
        }
        return null;
    }

    // Cce is the parent-centred *ecliptic* frame, so it differs from Ecl only by the body's own position
    // and velocity; Cci is a fixed rotation away from it, and both are non-rotating, so the same
    // quaternion carries position and velocity.
    private static bool ToParentInertial(
        IParentBody parent, double3 posEcl, double3 velEcl, out double3 posCci, out double3 velCci)
    {
        posCci = default;
        velCci = default;

        if (parent is not IPosition parentPos || parent is not IVelocity parentVel) return false;

        double3 posCce = posEcl - parentPos.GetPositionEcl();
        double3 velCce = velEcl - parentVel.GetVelocityEcl();

        doubleQuat cce2Cci = parent.GetCce2Cci();
        posCci = cce2Cci * posCce;
        velCci = cce2Cci * velCce;

        return Vec.IsFinite(posCci) && Vec.IsFinite(velCci);
    }
}
