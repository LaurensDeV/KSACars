using Brutal.ImGuiApi;
using Brutal.Numerics;
using KSA;

namespace KSACars;

// What the pointer is over and where things are on screen, and the gizmos drawn there: the part of
// KsaWorld the craft mover needs.
internal static partial class KsaWorld
{
    /// <summary>
    /// Whether the skyline of the body under the eye hides the target, against the real height
    /// field.
    ///
    /// <para>Terrain only, and for one body. <see cref="IsOccluded"/> runs over every body in the
    /// system and stays in front of this: a ridge on another world only matters when that world is
    /// between the two points, which the sphere has already caught. So this is asked once the
    /// cheap rejects have all passed, and never for a contact they threw out.</para>
    /// </summary>
    /// <param name="samples">Height lookups this look may cost. Zero asks none.</param>
    // Each modifier contributes its amplitude times a weight, a lookup and a noise value all in
    // [0, 1], so the sum of the declared amplitudes is a supremum rather than an estimate. Earth's
    // total 7,525 m.
    private const double TerrainModifierHeadroomMetres = 8_000.0;

    // How high the terrain can possibly reach, as a bound the cheap reject may stand in front of
    // the exact test with.
    //
    // Celestial.MaxTerrainHeightApprox is NOT such a bound, and using it gives false negatives. It
    // is computed in the Celestial constructor, before Universe.SetupRenderData populates the
    // modifiers, so erosion, dunes and detail contribute nothing -- and it samples a 16,384-point
    // Fibonacci spiral, about 176 km apart on Earth. Measured against the shipped height texture it
    // returns ~5,692 m where the base field alone reaches 8,011. A sightline six kilometres over the
    // Himalayas is then declared unmasked without a single sample, which is the false negative
    // CLAUDE.md's "a sphere containing the terrain cannot produce a false negative" forbids.
    //
    // Astronomical.MaxTerrainRadius is exact for the base field, straight off the template. Over-
    // padding costs one thing: a contact high above the ground pays for samples it did not need.
    private static double MaxTerrainHeightMetres(Celestial body)
    {
        try
        {
            double exact = body.MaxTerrainRadius - body.MeanRadius;

            return double.IsFinite(exact) && exact > 0.0
                       ? exact + TerrainModifierHeadroomMetres
                       : body.MaxTerrainHeightApprox;
        }
        catch
        {
            return body.MaxTerrainHeightApprox;
        }
    }

    // The views this mod may drive, in the order its viewport numbers index. GameViews rather than
    // Views because only a game viewport has a camera and controllers -- which also keeps the
    // part-thumbnail renderer out, since it is not one.
    private static ReadOnlySpan<IGameViewport> GameViewports
    {
        get
        {
            try { return ViewportRegistry.GameViews; }
            catch { return default; }
        }
    }

    public static bool TryCursorRayEcl(out double3 originEcl, out double3 directionEcl)
    {
        originEcl = default;
        directionEcl = default;
        try
        {
            float2 cursor = ImGui.GetMousePos();

            ReadOnlySpan<IGameViewport> viewports = GameViewports;
            for (int i = 0; i < viewports.Length; i++)
            {
                IGameViewport v = viewports[i];
                if (!v.Visible) continue;

                if (v.GetCamera() is not { } camera) continue;

                // Framebuffer pixels, not viewport pixels: ScreenToEgoRay divides by the camera's
                // own framebuffer, and a render or display scale makes those different sizes.
                if (!CursorAim.TryToFramebuffer(cursor, v.Position, v.Width, v.Height,
                                                camera.FramebufferSize.X, camera.FramebufferSize.Y,
                                                out float2 local))
                {
                    continue;
                }

                double3 direction = camera.ScreenToEgoRay(local).Direction;
                if (!CursorAim.IsUsableDirection(direction)) continue;

                originEcl = camera.EgoToEcl(Vec.Zero);
                directionEcl = Vec.Unit(direction);
                return Vec.IsFinite(originEcl);
            }

            return false;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Where the cursor's ray first meets a celestial's ground, as a place a craft can be put or a
    /// round can be sent.
    ///
    /// <para>Nearest body hit, not the one being orbited: pointing at a moon on the horizon should
    /// mean the moon. Walked out from the eye by <see cref="TerrainRay"/>, because the ray meets the
    /// ground where it first goes under it — the mean sphere's hit refined by the height under that
    /// answer lands behind a hill seen side-on, on the terrain beyond what the pointer is on.</para>
    /// </summary>
    public static bool TryCursorGroundPoint(out double3 groundEcl,
                                            out double latitudeDeg, out double longitudeDeg,
                                            out string bodyName)
    {
        groundEcl = default;
        latitudeDeg = 0.0;
        longitudeDeg = 0.0;
        bodyName = string.Empty;

        try
        {
            if (!TryCursorRayEcl(out double3 eye, out double3 direction)) return false;
            if (Universe.CurrentSystem is not { } system) return false;

            Celestial? nearest = null;
            double nearestRange = double.MaxValue;

            for (int i = 0; i < system.Count; i++)
            {
                if (system.GetIndex(i) is not Celestial body) continue;

                double3 centre = body.GetPositionEcl();
                double top = MaxTerrainHeightMetres(body);

                // Far enough to cross the whole body. Only the part below its highest ground is
                // walked, and the walk stops at the first place it is under it.
                double reach = 2.0 * (Vec.Len(eye - centre) + body.MeanRadius + top);

                // Terrain only. A launch pad is 8 m of pedestal 40 m across, and adding it here
                // models it as an 8 m thicker planet: at 5 km the resolved point moves 2.8 km, and
                // sweeping the cursor over the pad edge swings the bearing from the mount through
                // 168 degrees between one pixel and the next. Where a structure's surface is has
                // no answer in this engine -- see docs/BLOCKED-ON-KSA.md.
                if (!TerrainRay.TryFirstHit(eye, direction, reach, centre, body.MeanRadius, top,
                                            new TerrainHeights(body, accurate: true), out double range)
                    || range >= nearestRange)
                {
                    continue;
                }

                nearest = body;
                nearestRange = range;
            }

            if (nearest is null) return false;

            groundEcl = eye + (Vec.Unit(direction) * nearestRange);

            double3 cce = groundEcl - nearest.GetPositionEcl();
            latitudeDeg = nearest.GetLatitudeFromCce(cce);
            longitudeDeg = nearest.GetLongitudeFromCce(cce);
            bodyName = nearest.Id ?? string.Empty;
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Where a craft stands: the surface point under it, and its latitude and longitude.
    ///
    /// <para>Placing something at a craft has to use the craft's own position rather than the
    /// ground the cursor ray reaches. A ray through a vehicle's middle carries on and meets the
    /// ground <em>behind</em> it, so aiming at a craft and using the ray puts the answer a
    /// vehicle-height's worth of parallax past it.</para>
    /// </summary>
    public static bool TryCraftSurfacePoint(Vehicle craft, out double3 groundEcl,
                                            out double latitudeDeg, out double longitudeDeg,
                                            out string bodyName)
    {
        groundEcl = default;
        latitudeDeg = 0.0;
        longitudeDeg = 0.0;
        bodyName = string.Empty;

        if (!IsAlive(craft)) return false;

        try
        {
            if (craft.Parent is not Celestial body) return false;

            double3 cce = PositionEcl(craft) - body.GetPositionEcl();
            if (!Vec.IsFinite(cce) || Vec.Len(cce) < 1.0) return false;

            groundEcl = body.GetSurfacePositionEclFromCce(cce);
            latitudeDeg = body.GetLatitudeFromCce(cce);
            longitudeDeg = body.GetLongitudeFromCce(cce);
            bodyName = body.Id ?? string.Empty;
            return Vec.IsFinite(groundEcl);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Projects a world point onto the main viewport, culling anything behind the camera.
    ///
    /// <para>Distinct from <see cref="TryProjectIntoViewport"/>, which passes
    /// <c>ignoreBehind: false</c>. That is right for the gunner's sight, whose head is pointed at
    /// its target and so cannot be looking away from it, and wrong for a marker over an arbitrary
    /// craft: <c>EgoToScreen</c> only tests the point against the camera's forward when asked, so
    /// without it a site *behind* the camera draws a bracket in front of it.</para>
    /// </summary>
    public static bool TryProjectAhead(double3 pointEcl, out float2 screen)
    {
        screen = default;
        try
        {
            if (Program.MainViewport is not { } viewport) return false;
            if (viewport.GetCamera() is not { } camera) return false;
            if (viewport.Width <= 0 || viewport.Height <= 0) return false;

            float2 local = camera.EclToScreen(pointEcl, ignoreBehind: true);
            if (!float.IsFinite(local.X) || !float.IsFinite(local.Y)) return false;
            if (local.X < 0f || local.Y < 0f || local.X > viewport.Width || local.Y > viewport.Height)
            {
                return false;
            }

            screen = new float2(viewport.Position.X + local.X, viewport.Position.Y + local.Y);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// How large something at <paramref name="atEcl"/> appears on screen, in pixels.
    ///
    /// <para>Measured by projecting a point one radius to the camera's right rather than by
    /// reconstructing the field of view: the projection already knows the lens, and asking it
    /// twice cannot disagree with itself.</para>
    /// </summary>
    public static bool TryApparentRadiusPixels(double3 atEcl, double metres, out float pixels)
    {
        pixels = 0f;
        if (!double.IsFinite(metres) || metres <= 0.0) return false;

        try
        {
            if (Program.GetMainCamera() is not { } camera) return false;

            double3 right = camera.GetRightEcl();
            if (!Vec.IsFinite(right) || Vec.Len(right) < 0.5) return false;

            if (!TryProjectAhead(atEcl, out float2 centre)) return false;
            if (!TryProjectAhead(atEcl + Vec.Unit(right) * metres, out float2 edge)) return false;

            float dx = edge.X - centre.X, dy = edge.Y - centre.Y;
            pixels = MathF.Sqrt(dx * dx + dy * dy);
            return float.IsFinite(pixels);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Rough size of a vehicle, used to scale hit and blast checks.</summary>
    public static double MeanRadius(Vehicle v)
    {
        double r = v.MeanRadius;
        return double.IsFinite(r) && r > 0.0 ? r : 5.0;
    }

    // The overlay is drawn relative to an anchor vehicle rather than by converting absolute
    // positions. See BeginDraw for why.
    private static DrawAnchor _anchor;

    private static bool _anchored;

    /// <summary>
    /// Establishes the frame for this draw pass. Call once before any Draw*Ecl call.
    ///
    /// <para>Naive conversion — <c>camera.EclToEgo(v.GetPositionEcl())</c> — puts the overlay in
    /// the wrong place. <see cref="Vehicle.GetPositionEcl"/> returns a value computed in
    /// <c>UpdatePerFrameData</c> from <c>Orbit.StateVectors</c>, i.e. the analytic on-rails
    /// position. A landed craft is held where physics puts it, not where its degenerate Kepler
    /// orbit says, and the two differ by enough to be obvious on screen.</para>
    ///
    /// <para>KSA renders vehicles via <c>camera.GetPositionEgo(vehicle)</c>, which returns
    /// <c>-PositionCce</c> for the followed craft and uses <c>KinematicStates.PositionPhys</c>
    /// for others in the same bubble — the physics position in both cases. Anchoring to that and
    /// adding Ecl offsets (exact, since Ego is a pure translation of Ecl) puts the overlay
    /// exactly where the game draws the craft.</para>
    /// </summary>
    /// <param name="anchorEcl">
    /// The anchor's Ecl position **captured at the same instant as everything else being
    /// drawn** — not re-read here. Ecliptic positions near Earth sweep past at ~29.8 km/s, so
    /// a reference taken one frame later than the geometry it is differenced against is about
    /// 500 m stale at 60 fps, and the whole overlay lands that far from the craft.
    /// </param>
    public static bool BeginDraw(Vehicle anchor, double3 anchorEcl)
    {
        _anchored = false;
        if (!IsAlive(anchor)) return false;

        try
        {
            // The camera the frame will actually be rendered with, not the main viewport's.
            Camera camera = Program.GetRenderCamera() ?? Program.GetMainCamera();
            if (camera is null) return false;

            // See DrawAnchor for why these are sampled at different instants, and why
            // collapsing them into one puts the whole overlay beside the craft.
            //
            // GetPositionEgo, not EclToEgo. Its branching on what the camera follows is the
            // engine answering correctly per case — exact for the followed craft, physics-based
            // for others in its bubble — and it is the same call KSA renders vehicles with.
            // EclToEgo instead measures against camera.PositionEcl, which only agrees with the
            // rendered scene when the followed craft's analytic and physics positions coincide.
            // That holds for a landed launcher and fails once the camera follows something in
            // flight, which is when anything drawn to a vehicle stops lining up.
            _anchor = new DrawAnchor(camera.GetPositionEgo(anchor), anchorEcl);

            if (!_anchor.IsValid) return false;


            _anchored = true;
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Converts an Ecl position into the anchored Ego frame.</summary>
    public static bool TryEclToEgo(double3 posEcl, out double3 posEgo)
    {
        if (_anchored)
        {
            posEgo = _anchor.ToEgo(posEcl);
            return true;
        }

        posEgo = Vec.Zero;
        return false;
    }

    // The body whose centre is nearest a point, by a walk of the whole system. Found once for a
    // whole draped ring rather than once a point: every point of a ring is over the same body.
    private static Celestial? NearestCelestial(double3 nearEcl)
    {
        try
        {
            if (Universe.CurrentSystem is not { } system) return null;

            Celestial? nearest = null;
            double best = double.MaxValue;

            for (int i = 0; i < system.Count; i++)
            {
                if (system.GetIndex(i) is not Celestial body) continue;

                double distance = Vec.Len(nearEcl - body.GetPositionEcl());
                if (distance >= best) continue;

                best = distance;
                nearest = body;
            }

            return nearest;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Puts a point on the ground beneath it: same direction from the body's centre, radius taken
    /// from the terrain there.
    ///
    /// <para>What makes a ring drawn on a slope follow the slope. A ring at one radius is flat in
    /// space, so on anything but level ground half of it is buried and the other half floats.</para>
    /// </summary>
    public static bool TrySnapToGround(double3 nearEcl, out double3 onGroundEcl) =>
        TrySnapToGround(nearEcl, out onGroundEcl, out _);

    /// <summary>
    /// As above, and hands back the centre of the body it draped onto -- which it had to find
    /// anyway. A caller that needs the local up gets it for free instead of walking the system
    /// again for a body this has already identified.
    /// </summary>
    public static bool TrySnapToGround(double3 nearEcl, out double3 onGroundEcl, out double3 centreEcl)
    {
        centreEcl = Vec.Zero;
        onGroundEcl = nearEcl;

        return NearestCelestial(nearEcl) is { } nearest
               && TrySnapToGround(nearest, nearEcl, out onGroundEcl, out centreEcl);
    }

    // The snap onto a body already found.
    private static bool TrySnapToGround(Celestial nearest, double3 nearEcl, out double3 onGroundEcl,
                                        out double3 centreEcl)
    {
        centreEcl = Vec.Zero;
        onGroundEcl = nearEcl;

        try
        {
            double3 centre = nearest.GetPositionEcl();
            centreEcl = centre;
            double3 dirCce = Vec.Unit(nearEcl - centre);
            if (Vec.Len2(dirCce) < 0.5) return false;

            double height = nearest.GetTerrainHeightFromDirCce(dirCce, accurate: true);
            if (!double.IsFinite(height)) return false;

            onGroundEcl = centre + dirCce * (nearest.MeanRadius + height);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// A torus: a ring of solid spheres, draped onto the terrain.
    ///
    /// <para>Spheres because they are the only solid thing the gizmo renderer draws — <c>Render</c>
    /// is <c>RenderSpheres</c> then <c>RenderLines</c>, with no filled polygon anywhere. Enough of
    /// them around the ring, each wider than the gap to the next, and the result reads as a tube
    /// rather than as beads.</para>
    /// </summary>
    /// <param name="tubeRadius">Thickness of the ring itself.</param>
    public static void DrawTorusEcl(double3 centreEcl, double3 normalEcl, double ringRadius,
                                    double tubeRadius, float4 colour, bool drape = true)
    {
        if (Program.GizmosRenderer is null) return;
        if (!Vec.IsFinite(centreEcl) || !(ringRadius > 0.0) || !(tubeRadius > 0.0)) return;

        double3 up = Vec.Unit(normalEcl);
        if (Vec.Len2(up) < 0.5) return;

        double3 seed = Math.Abs(up.X) < 0.9 ? new double3(1, 0, 0) : new double3(0, 1, 0);
        double3 a = Vec.Unit(Vec.Cross(up, seed));
        double3 b = Vec.Unit(Vec.Cross(up, a));

        // Spaced closer together than they are wide, or it beads. Bounded so a large ring cannot
        // ask for thousands of spheres.
        int steps = (int)Math.Clamp(Math.Ceiling(Math.Tau * ringRadius / tubeRadius), 16, 160);
        Celestial? body = drape ? NearestCelestial(centreEcl) : null;

        for (int i = 0; i < steps; i++)
        {
            double angle = Math.Tau * i / steps;
            double3 at = centreEcl + ((a * Math.Cos(angle)) + (b * Math.Sin(angle))) * ringRadius;

            // Each bead sits on the ground under it, so the ring follows a slope instead of
            // burying one side and floating the other.
            if (body is not null && TrySnapToGround(body, at, out double3 ground, out _)) at = ground;

            if (TryEclToEgo(at, out double3 ego))
            {
                Program.GizmosRenderer.DrawSphere(ego, (float)tubeRadius, colour);
            }
        }
    }

    public static void DrawLineEcl(double3 startEcl, double3 endEcl, float4 colour)
    {
        if (Program.GizmosRenderer is null) return;
        if (!TryEclToEgo(startEcl, out double3 a)) return;
        if (!TryEclToEgo(endEcl, out double3 b)) return;
        Program.GizmosRenderer.DrawLine(a, b, colour);
    }
}
