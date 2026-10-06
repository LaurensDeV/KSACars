using Brutal.Numerics;
using KSA;

namespace KSACars;

/// <summary>
/// The launch pads on one body, as the colliders KSA stands on each, so a wheel over one is sprung
/// against the pad and not against the ground it is built on.
///
/// <para>Each pad is posed as <c>ConstraintSim.UpdateStaticObjectCollider</c> poses its collider: on the
/// terrain at the landmark, lifted by the static object's <c>GroundOffset</c>, with +X up, +Y east and
/// +Z north.</para>
/// </summary>
internal sealed class LaunchPads
{
    // KSA gives a craft the collider of the nearest pad within this of it, and of none further off.
    private const double ColliderRangeMetres = 300.0;

    // How far above a hub a surface still counts as under the wheel, and how far below it is looked for.
    private const double StepMetres = 0.5;
    private const double ReachMetres = 30.0;

    private static readonly LaunchPads None = new([]);
    private static readonly Dictionary<Celestial, LaunchPads> Known = [];

    private readonly record struct Pad(double3 OriginCcf, double3 UpCcf, double3 EastCcf, double3 NorthCcf, PadSurface Surface);

    private readonly Pad[] _all;

    private LaunchPads(Pad[] all) => _all = all;

    /// <summary>A body's pads, read once. A body whose pads cannot be read has none.</summary>
    public static LaunchPads On(Celestial body)
    {
        if (Known.TryGetValue(body, out LaunchPads? known)) return known;

        LaunchPads pads = None;
        try
        {
            pads = Read(body);
            Log.Info($"{body.Id}: {pads._all.Length} launch pad(s) with colliders");
        }
        catch (Exception ex)
        {
            Log.Warn($"{body.Id}: launch pads unreadable, wheels will not find them: {ex.Message}");
        }

        Known[body] = pads;
        return pads;
    }

    /// <summary>How far a point is above the pad under it (m); negative when it is inside one.</summary>
    public bool TryHeightOver(double3 atCcf, out double metres)
    {
        metres = 0.0;

        foreach (Pad pad in _all)
        {
            double3 offset = atCcf - pad.OriginCcf;
            if (Vec.Dot(offset, offset) > ColliderRangeMetres * ColliderRangeMetres) continue;

            double3 from = new(Vec.Dot(offset, pad.UpCcf) + StepMetres, Vec.Dot(offset, pad.EastCcf), Vec.Dot(offset, pad.NorthCcf));
            if (!pad.Surface.TryDrop(from, new double3(-1, 0, 0), ReachMetres, out double drop)) continue;

            metres = drop - StepMetres;
            return true;
        }

        return false;
    }

    private static LaunchPads Read(Celestial body)
    {
        List<Pad> pads = [];
        if (body.BodyTemplate is not { } template) return None;

        foreach (LocationReference location in template.Locations)
        {
            if (location is not LandmarkReference { IsLaunchPad: true } landmark) continue;
            if (landmark.GetStaticObject() is not { } pad) continue;

            List<PadSolid> solids = [];
            AddSolids(solids, pad.Template.Colliders, double3.Zero, doubleQuat.Identity);
            foreach (StaticSubObjectInstance instance in pad.Template.SubObjectInstances)
            {
                AddSolids(solids, instance.GetTemplate().Colliders,
                          instance.Transform?.PositionValue ?? double3.Zero,
                          instance.Transform?.RotationValue ?? doubleQuat.Identity);
            }
            if (solids.Count == 0) continue;

            landmark.GetAxesCcf(out double3 up, out double3 east, out double3 north);
            double3 origin = up * (body.MeanRadius + body.GetTerrainHeightFromDirCcf(up) + pad.GroundOffset);
            pads.Add(new Pad(origin, up, east, north, new PadSurface([.. solids])));
        }

        return pads.Count == 0 ? None : new LaunchPads([.. pads]);
    }

    private static void AddSolids(List<PadSolid> solids, List<ColliderModule.Template> colliders,
                                  double3 positionAsmb, doubleQuat sub2Asmb)
    {
        foreach (ColliderModule.Template group in colliders)
        {
            foreach (ColliderTemplate collider in group.Colliders)
            {
                double3 half;
                bool round;
                switch (collider)
                {
                    case BoxColliderTemplate box:
                        half = new double3((double)box.LengthX, (double)box.LengthY, (double)box.LengthZ) * 0.5;
                        round = false;
                        break;
                    case CylinderColliderTemplate cylinder:
                        half = new double3((double)cylinder.Radius, (double)cylinder.LengthY * 0.5, 0.0);
                        round = true;
                        break;
                    default:
                        continue;
                }

                doubleQuat collider2Sub = QuaternionEx.CreateFromXyzRadians(collider.Collider2Asmb.ToDouble3());
                double3 centre = collider.LocationAsmb.ToDouble3() + collider.ShapeOffsetCollider.Transform(collider2Sub);
                solids.Add(new PadSolid(
                    positionAsmb + centre.Transform(sub2Asmb),
                    Axis(new double3(1, 0, 0)), Axis(new double3(0, 1, 0)), Axis(new double3(0, 0, 1)),
                    half, round));

                double3 Axis(double3 own) => own.Transform(collider2Sub).Transform(sub2Asmb);
            }
        }
    }
}
