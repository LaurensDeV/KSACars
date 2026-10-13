using Brutal.ImGuiApi;
using Brutal.Numerics;
using KSA;

namespace KSACars;

/// <summary>
/// Pick a craft up with one click and set it down with the next, so a car can be put wherever it is
/// wanted without driving it there.
///
/// <para>Placement goes through <c>Vehicle.TeleportToLocation</c>, which is how the game moves a
/// craft itself: it builds the kinematic state from the craft's bounding box, so the hull arrives
/// resting on the terrain rather than half inside it. Writing a position directly would put the
/// craft's origin at the point and leave the rest wherever that happens to be.</para>
///
/// <para>The craft does <em>not</em> hover along under the cursor while held. Placement is a
/// buffered engine event, so following the pointer would mean re-initialising the vehicle's
/// kinematic state sixty times a second, with its velocity and orbit rebuilt on every one. What is
/// held is the <em>choice</em>: the craft stays where it is, the ground under the cursor is marked,
/// and the second click moves it once.</para>
///
/// <para>A car set down on a laid road is stood on its asphalt: KSA's placement knows the terrain
/// alone, which under a raised road is the ground beneath the deck. It is placed as any craft is and
/// then, once it has arrived, stood on the road by <see cref="Buggies.Stand"/>.</para>
/// </summary>
internal sealed class CraftMover
{
    private static readonly float4 HoverColour = new(0.6f, 0.8f, 1.0f, 0.8f);
    private static readonly float4 HeldColour = new(1.0f, 0.85f, 0.3f, 1f);
    private static readonly float4 TargetColour = new(0.4f, 1.0f, 0.6f, 1f);

    // Smallest and largest a craft's clickable area may be, in pixels. The floor keeps a distant
    // vessel reachable when it is a couple of pixels across; the ceiling stops one filling the
    // screen from swallowing every click meant for the ground beside it.
    private const float MinPickRadius = 14f;
    private const float MaxPickRadius = 140f;

    // Closer than this to where it already is and the click means "leave it". A vehicle is several
    // metres across, so anything inside this is the same spot as far as anyone aiming is concerned.
    private const double StayPut = 12.0;

    // Radius of the ring drawn where the craft would land, in metres. Fixed rather than scaled to
    // the craft: it marks a spot on the ground, and a big vessel would otherwise hide it.
    private const double MarkerRadius = 12.0;

    private readonly List<Vehicle> _craft = [];
    private readonly List<float2> _screen = [];
    private readonly List<float> _reach = [];

    // As near as this across the ground a craft has arrived where it was sent, or needs no sending.
    private const double ArrivedM = 30.0;

    // How long a car sent to a road is waited for, in frames, before it is left where KSA put it.
    private const int StandWithinFrames = 600;

    private Vehicle? _held;
    private Vehicle? _hovered;
    private int _trace;

    // A car on its way to a road, to be stood on the asphalt once KSA has placed it.
    private (Vehicle Craft, Celestial Body, double3 AtCcf, double3 UpCcf, int Frames)? _toStand;

    /// <summary>Whether clicks on the world pick craft up and set them down. Off, the mouse is not read.</summary>
    public bool Enabled { get; set; }

    /// <summary>The craft waiting to be put down, if any.</summary>
    public Vehicle? Held => _held;

    /// <summary>The craft the pointer is over, which the next click would pick up.</summary>
    public Vehicle? Hovered => _hovered;

    public void Release()
    {
        _held = null;
        _hovered = null;
    }

    /// <summary>
    /// One frame of the tool. Does nothing at all while switched off, including reading the mouse.
    /// </summary>
    public void Update()
    {
        StandArrived();

        if (!Enabled)
        {
            Release();
            return;
        }

        if (!KsaWorld.IsAlive(_held)) _held = null;
        if (!KsaWorld.IsAlive(_hovered)) _hovered = null;

        bool wantCapture = ImGui.GetIO().WantCaptureMouse;
        // Shift is the designate gesture, so a shift-click is not a click on the world.
        bool clicked = ImGui.IsMouseClicked(ImGuiMouseButton.Left, repeat: false)
                       && !ImGui.GetIO().KeyShift;
        bool down = ImGui.IsMouseDown(ImGuiMouseButton.Left);

        // Whether a click reaches this hook at all is not obvious -- ImGui, KSA's own input and
        // this mod all read the same mouse -- so it stays traced, quietly.
        if (clicked || down || ++_trace % 120 == 0)
        {
            float2 where = ImGui.GetMousePos();
            Log.Debug(() => $"mover: capture={wantCapture} clicked={clicked} down={down} "
                            + $"cursor={where.X:F0},{where.Y:F0} held={_held is not null}");
        }

        // The panel and the marker labels are windows; a click meant for one of them is not a
        // click on the world behind it.
        if (wantCapture)
        {
            _hovered = null;
            return;
        }

        // Worked out every frame, not only on click: clicking blind and finding out afterwards
        // which craft was nearest is not an interaction, it is a guess.
        _hovered = UnderCursor();

        if (!clicked) return;

        if (_held is null) PickUp();
        else PutDown();
    }

    // The craft nearest the pointer within reach, or null.
    private Vehicle? UnderCursor()
    {
        _craft.Clear();
        _craft.AddRange(KsaWorld.Vehicles);
        if (_craft.Count == 0) return null;

        _screen.Clear();
        _reach.Clear();
        for (int i = 0; i < _craft.Count; i++)
        {
            // Off-screen craft get a position that cannot be picked, so the indices stay aligned
            // with _craft and the nearest answer means what it says.
            double3 atEcl = KsaWorld.PositionEcl(_craft[i]);
            _screen.Add(KsaWorld.TryProjectAhead(atEcl, out float2 at)
                            ? at
                            : new float2(float.MaxValue, float.MaxValue));

            // As big as the craft looks, so pointing anywhere on a vessel picks it rather than
            // only its centre. MeanRadius is a bounding measure, so this is its whole extent.
            float reach = MinPickRadius;
            if (KsaWorld.TryApparentRadiusPixels(atEcl, KsaWorld.MeanRadius(_craft[i]),
                                                 out float pixels))
            {
                reach = Math.Clamp(pixels, MinPickRadius, MaxPickRadius);
            }

            _reach.Add(reach);
        }

        int pick = Picking.NearestWithin(_screen, _reach, ImGui.GetMousePos());
        return pick < 0 ? null : _craft[pick];
    }

    private void PickUp()
    {
        if (_hovered is not { } craft) return;

        _held = craft;
        _hovered = null;
        Log.Info($"holding {KsaWorld.DisplayName(craft)} - click the ground to set it down");
    }


    private void PutDown()
    {
        if (_held is not { } craft) return;

        // Pointing at the held craft means "leave it". Placing re-derives a position from the
        // latitude and longitude and the surface rule, which is not where the craft is: on a
        // launch pad it is metres below, so a click that should change nothing drops it.
        if (ReferenceEquals(_hovered, craft))
        {
            Log.Info($"left {KsaWorld.DisplayName(craft)} where it was");
            _held = null;
            return;
        }

        if (!TryTarget(out double3 target, out double lat, out double lon, out string body, out RoadSpot? road))
        {
            Log.Info("nothing under the cursor to set it down on");
            return;
        }

        // Or near enough to it. The pick radius is generous, so the cursor can be a little off the
        // craft and still mean the same thing.
        if (Vec.Len(target - KsaWorld.PositionEcl(craft)) < StayPut)
        {
            Log.Info($"left {KsaWorld.DisplayName(craft)} where it was");
            _held = null;
            return;
        }

        // A car a few metres from where it is wanted on a road is stood there as it is: placed first,
        // it would be stood before KSA had moved it and then moved off the road again.
        bool car = road is not null && Buggies.Of(craft) is not null;
        if (car && Across(craft, road!.Value) < ArrivedM)
        {
            _toStand = (craft, road.Value.Body, road.Value.AtCcf, road.Value.UpCcf, StandWithinFrames);
        }
        else if (KsaWorld.TryPlaceOnSurface(craft, body, lat, lon))
        {
            Log.Info($"placed {KsaWorld.DisplayName(craft)} on {body} at "
                     + $"{lat:F4}, {lon:F4}{(car ? ", to be stood on the road there" : "")}");
            if (car) _toStand = (craft, road!.Value.Body, road.Value.AtCcf, road.Value.UpCcf, StandWithinFrames);
        }

        _held = null;
    }

    // A place on a laid road's asphalt: the body, the place in its own frame and the way the asphalt faces.
    private readonly record struct RoadSpot(Celestial Body, double3 AtCcf, double3 UpCcf);

    // How far across the ground a craft is from a place on a road, whatever their heights.
    private static double Across(Vehicle craft, RoadSpot spot)
    {
        if (!KsaWorld.TryPlaceCcf(craft, spot.Body, out double3 at)) return double.MaxValue;
        return Vec.Len(Vec.Cross(Vec.Unit(at), Vec.Unit(spot.AtCcf))) * Vec.Len(spot.AtCcf);
    }

    // Stands the car that was sent to a road on it, once KSA has put it on the ground there.
    private void StandArrived()
    {
        if (_toStand is not { } s) return;

        if (!KsaWorld.IsAlive(s.Craft) || Buggies.Of(s.Craft) is not { } car || s.Frames <= 0)
        {
            _toStand = null;
            return;
        }

        RoadSpot spot = new(s.Body, s.AtCcf, s.UpCcf);
        if (Across(s.Craft, spot) >= ArrivedM)
        {
            _toStand = s with { Frames = s.Frames - 1 };
            return;
        }

        // Facing as it was, as near as the road's own lean allows.
        double3 ahead = Vec.RejectFrom(car.AheadCcf, s.UpCcf);
        if (Vec.Len(ahead) < 0.1) ahead = Vec.RejectFrom(Vec.Cross(s.UpCcf, new double3(0, 0, 1)), s.UpCcf);
        if (Vec.Len(ahead) < 0.1) ahead = Vec.RejectFrom(new double3(1, 0, 0), s.UpCcf);
        if (Buggies.Stand(s.Craft, s.AtCcf, s.UpCcf, Vec.Unit(ahead)))
        {
            Log.Info($"standing {KsaWorld.DisplayName(s.Craft)} on the road it was set down on");
        }
        _toStand = null;
    }

    // Where the next click would put the held craft.
    //
    // A craft under the pointer answers with its own footing rather than with the ray: a ray
    // through a vehicle's middle carries on and meets the ground behind it, so aiming at one and
    // taking the ray lands a vehicle-height's worth of parallax past it. Aiming at the held craft
    // itself therefore leaves it exactly where it is, which is what clicking it twice should do.
    private bool TryTarget(out double3 groundEcl, out double latitudeDeg, out double longitudeDeg,
                           out string bodyName, out RoadSpot? road)
    {
        road = null;
        // The hover is already worked out this frame; asking again would project every craft
        // twice more per frame for the same answer.
        if (_hovered is { } over
            && KsaWorld.TryCraftSurfacePoint(over, out groundEcl, out latitudeDeg,
                                             out longitudeDeg, out bodyName))
        {
            return true;
        }

        if (KsaWorld.TryCursorRoadPoint(out groundEcl, out latitudeDeg, out longitudeDeg, out Celestial? on,
                                        out double3 atCcf, out double3 upCcf) && on is not null)
        {
            bodyName = on.Id ?? string.Empty;
            road = new RoadSpot(on, atCcf, upCcf);
            return true;
        }

        return KsaWorld.TryCursorGroundPoint(out groundEcl, out latitudeDeg, out longitudeDeg,
                                             out bodyName);
    }

    /// <summary>Shows what the next click would pick up, or what is held and where it would go.</summary>
    public void Draw()
    {
        if (!Enabled) return;

        // Nothing held: ring whatever the pointer is over, so the click is aimed rather than
        // taken on trust.
        if (_held is null)
        {
            if (_hovered is not { } candidate || !KsaWorld.IsAlive(candidate)) return;

            double3 atEcl = KsaWorld.PositionEcl(candidate);
            if (!KsaWorld.BeginDraw(candidate, atEcl)) return;

            DrawFootprint(candidate, atEcl, HoverColour);
            return;
        }

        if (!KsaWorld.IsAlive(_held)) return;

        double3 heldEcl = KsaWorld.PositionEcl(_held);
        if (!KsaWorld.BeginDraw(_held, heldEcl)) return;

        // A ring around its feet, so it is obvious which one the next click moves without the
        // marker sitting over the craft it is marking.
        DrawFootprint(_held, heldEcl, HeldColour);

        if (!TryTarget(out double3 groundEcl, out _, out _, out _, out _)) return;

        // Where it would land: a ring on the ground, a short stalk so the spot reads against
        // sloping terrain, and the line from the craft to it.
        double3 up = Up(_held, groundEcl);

        KsaWorld.DrawTorusEcl(groundEcl, up, MarkerRadius, MarkerRadius * 0.14, TargetColour);
        KsaWorld.DrawLineEcl(groundEcl, groundEcl + up * (MarkerRadius * 0.8), TargetColour);
        KsaWorld.DrawLineEcl(heldEcl, groundEcl, TargetColour);
    }

    // A ring at the craft's feet rather than a sphere around it. A sphere big enough to read as
    // "this one" is by construction big enough to hide what it is pointing at.
    private static void DrawFootprint(Vehicle craft, double3 atEcl, float4 colour)
    {
        double radius = Math.Max(KsaWorld.MeanRadius(craft) * 1.4, 5.0);
        double3 up = Up(craft, atEcl);

        // Dropped to the craft's base, so it lies on the ground rather than cutting through the
        // middle of the hull.
        KsaWorld.DrawTorusEcl(atEcl - up * (radius / 1.4), up, radius, radius * 0.10, colour);
    }

    private static double3 Up(Vehicle craft, double3 atEcl)
    {
        double3 up = -Vec.Unit(KsaWorld.GravityAt(craft, atEcl));

        return Vec.Len2(up) < 0.5 ? new double3(0, 0, 1) : up;
    }
}
