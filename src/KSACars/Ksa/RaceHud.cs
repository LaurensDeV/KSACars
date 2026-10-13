using Brutal.ImGuiApi;
using Brutal.Numerics;
using KSA;

namespace KSACars;

/// <summary>
/// The racing display, shown while KSA's own interface is hidden: the flown car's speed, its lap, its
/// place among the cars on the course, and a map of the course about the car, the way it faces at the
/// top, with every car on it.
///
/// <para>Where each car is along the course is kept from the frame hook, on a route down the middle
/// of the roads that are laid. It is drawn from <see cref="RaceHudHook"/>, the one place KSA still
/// draws from with its interface hidden.</para>
/// </summary>
internal static class RaceHud
{
    // A corner any car that ships turns inside, for the route the cars are placed along.
    private const double TurnRadiusM = 8.0;

    // Frames a laying has to have stood before its route is made: a dragged road is laid again every frame.
    private const int SettledFrames = 30;

    // How far apart the places of the course's line on the map are, and how far from the car the map reaches, m.
    private const double EveryM = 8.0, ReachM = 320.0;

    // Further from the route than this a car is looked for anew along the whole of it.
    private const double LostM = 40.0;

    private const float MapPx = 230f, MarginPx = 24f;

    // The rev counter: its radius, where its arc starts and ends in degrees clockwise from three
    // o'clock, how many pieces it is drawn in, and the share of the revs over idle that is red.
    private const float RevsRadiusPx = (MapPx / 2f) - 10f, RevsFromDeg = 125f, RevsToDeg = 415f;
    private const double RedShare = 0.12;
    private const int RevsSteps = 90;

    private static readonly ImColor8 Dim = new(255, 255, 255, 150);

    // The box of times under the laps: how wide it is and how tall a row of it.
    private const float TimesWidePx = 210f, TimesRowPx = 24f;

    private static readonly ImColor8 Ink = new(255, 255, 255, 245), Shade = new(0, 0, 0, 150), Road = new(235, 235, 235, 230),
                                     Edge = new(0, 0, 0, 200), Mine = new(255, 70, 50, 255), Theirs = new(90, 190, 255, 255), Line = new(255, 255, 255, 255), Gate = new(255, 210, 40, 255),
                                     Quicker = new(70, 230, 110, 255), Slower = new(255, 90, 70, 255);

    // How tall the post at a checkpoint is, and from how near to how far off it is drawn, m.
    private const double PostTallM = 14.0, PostNearM = 12.0, PostFarM = 1200.0;

    // How long a split stays on the screen after its checkpoint, s.
    private const double SplitShownSeconds = 4.0;

    private sealed class Seen(double lengthM, double lineM)
    {
        public RaceBoard.Tally Tally { get; } = new(lengthM, lineM);
        public int Index = -1;

        // How far along the route it is, for a road that is not a circuit and has no laps.
        public double S;
    }

    // What a car may be off the asphalt by and still be going through a checkpoint, m.
    private const double GateWiderM = 3.0;

    private static readonly Dictionary<Vehicle, Seen> Cars = [];
    private static Route? _route;
    private static Celestial? _body;
    private static double3[] _line = [];
    private static int _seen = -1, _made = -1, _frames;

    /// <summary>Whether the display is wanted. It is drawn only while KSA's interface is hidden and a car is flown on a laid course.</summary>
    public static bool Enabled { get; set; } = true;

    /// <summary>How many laps the race is: what the lap is shown out of.</summary>
    public static int Laps { get; set; } = 3;

    /// <summary>Once a frame, from the frame hook. Never throws.</summary>
    public static void Update()
    {
        try
        {
            int generation = Roads.Generation;
            if (generation != _seen)
            {
                (_seen, _frames, _route) = (generation, 0, null);
                Cars.Clear();
                return;
            }
            if (generation != _made && ++_frames >= SettledFrames)
            {
                _made = generation;
                _route = Roads.Any ? Roads.RouteOver(null, 0.0, TurnRadiusM, out _body, out _, out _) : null;
                _line = _route is { } made ? [.. Enumerable.Range(0, (int)(made.LengthM / EveryM) + 1).Select(k => made.Standing(Math.Min(k * EveryM, made.LengthM - 1e-3)).At)] : [];
            }
            if (_route is not { } route) return;

            foreach (Buggies.Entry car in Buggies.Cars)
            {
                if (!car.PlaceValid) continue;
                if (!Cars.TryGetValue(car.Craft, out Seen? seen)) Cars[car.Craft] = seen = new Seen(route.LengthM, LineM(route));
                if (seen.Index < 0) seen.Index = route.Nearest(car.PlaceCcf);
                route.Locate(car.PlaceCcf, ref seen.Index, 10.0 + car.SpeedMs, out double s, out double cross);
                if (Math.Abs(cross) > LostM)
                {
                    seen.Index = route.Nearest(car.PlaceCcf);
                    route.Locate(car.PlaceCcf, ref seen.Index, 0.0, out s, out _);
                }
                Route.Sample here = route.Samples[seen.Index];
                seen.S = s;
                if (route.Closed) seen.Tally.See(s, Math.Abs(cross - here.CentreM) <= here.HalfWidth + GateWiderM, Universe.GetElapsedSeconds());
            }
            foreach (Vehicle gone in Cars.Keys.Where(c => !KsaWorld.IsAlive(c)).ToArray()) Cars.Remove(gone);
        }
        catch (Exception e)
        {
            _route = null;
            Log.Error("the racing display lost its place", e);
        }
    }

    // Where the painted line is along a route, which is where a lap is counted.
    private static double LineM(Route route) => route.Closed ? StartGrid.PoleM + StartGrid.LineAheadM : 0.0;

    /// <summary>Inside KSA's frame, between its interface and its render: called through a hook that catches whatever this throws.</summary>
    public static void Draw()
    {
        if (!Enabled || _route is not { } route || KsaWorld.ControlledVehicle is not { IsDisposed: false } flown) return;
        if (Buggies.Of(flown) is not { PlaceValid: true } mine || !Cars.TryGetValue(flown, out Seen? me)) return;

        ImGuiViewportPtr screen = ImGui.GetMainViewport();
        float2 corner = screen.WorkPos, size = screen.WorkSize;
        ImDrawListPtr draw = ImGui.GetForegroundDrawList(screen);
        ImFontPtr font = ImGui.GetFont();

        void Text(float x, float y, float px, string text, bool fromRight = false)
        {
            float wide = ImGui.CalcTextSize(text).X * px / ImGui.GetFontSize();
            float2 at = new(corner.X + (fromRight ? x - wide : x), corner.Y + y);
            draw.AddText(font, px, new float2(at.X + 2f, at.Y + 2f), Shade, text);
            draw.AddText(font, px, at, Ink, text);
        }

        // Speed, bottom right, in a rev counter: nearly a whole circle, clockwise from seven o'clock as a
        // needle goes, numbered in thousands of revs, its last part red; the gear and the speed inside it.
        BuggyProfile profile = mine.Drive.Profile;
        float redline = (float)Math.Max(profile.RedlineRpm, 1000.0);
        float revs = Math.Clamp((float)mine.Drive.Rpm / redline, 0f, 1f);
        // A disc the size of the map's and as far from its corner, so the two sit alike either side of the car.
        float2 hub = new(corner.X + size.X - MarginPx - (MapPx / 2f), corner.Y + size.Y - MarginPx - (MapPx / 2f));
        draw.AddCircleFilled(hub, MapPx / 2f, Shade, 48);
        float2 Rim(float share, float radius)
        {
            // Clockwise on a screen whose y runs down.
            float angle = (RevsFromDeg + ((RevsToDeg - RevsFromDeg) * share)) * MathF.PI / 180f;
            return new float2(hub.X + (radius * MathF.Cos(angle)), hub.Y + (radius * MathF.Sin(angle)));
        }
        void At(float2 middle, float px, string text, ImColor8 colour)
        {
            float2 box = ImGui.CalcTextSize(text) * (px / ImGui.GetFontSize());
            float2 at = new(middle.X - (box.X / 2f), middle.Y - (box.Y / 2f));
            draw.AddText(font, px, new float2(at.X + 1.5f, at.Y + 1.5f), Shade, text);
            draw.AddText(font, px, at, colour, text);
        }

        float redFrom = (float)Math.Clamp(1.0 - ((profile.RedlineRpm - profile.IdleRpm) * RedShare / profile.RedlineRpm), 0.5, 0.97);
        for (int step = 0; step < RevsSteps; step++)
        {
            float from = step / (float)RevsSteps, to = (step + 1) / (float)RevsSteps;
            bool red = from >= redFrom;
            draw.AddLine(Rim(from, RevsRadiusPx), Rim(to, RevsRadiusPx), red ? Slower : Dim, red ? 4f : 2f);
        }
        // A number every thousand revs, or every two thousand of an engine that turns past ten.
        int every = redline > 10500f ? 2000 : 1000;
        for (int rpm = 0; rpm <= redline + 1f; rpm += every)
        {
            float share = rpm / redline;
            draw.AddLine(Rim(share, RevsRadiusPx - 7f), Rim(share, RevsRadiusPx), share >= redFrom ? Slower : Ink, 2f);
            At(Rim(share, RevsRadiusPx - 20f), 14f, $"{rpm / 1000}", share >= redFrom ? Slower : Dim);
        }
        draw.AddLine(Rim(revs, RevsRadiusPx - 24f), Rim(revs, RevsRadiusPx + 6f), Shade, 6f);
        draw.AddLine(Rim(revs, RevsRadiusPx - 24f), Rim(revs, RevsRadiusPx + 6f), revs >= redFrom ? Slower : Ink, 3.5f);

        At(new float2(hub.X, hub.Y - 40f), 34f, $"{mine.Drive.Gear + 1}", Ink);
        At(new float2(hub.X, hub.Y + 8f), 64f, $"{mine.SpeedMs * 3.6:F0}", Ink);
        At(new float2(hub.X, hub.Y + 50f), 14f, "KM/H", Dim);

        Span<double> totals = stackalloc double[Cars.Count];
        int k = 0, my = 0;
        foreach ((Vehicle craft, Seen seen) in Cars)
        {
            if (ReferenceEquals(craft, flown)) my = k;
            totals[k++] = route.Closed ? seen.Tally.ProgressM : seen.S;
        }
        double line = LineM(route);
        int lap = me.Tally.Lap;
        double now = Universe.GetElapsedSeconds();

        // Laps, top left, and under them the race's time, the lap's and the best lap's.
        string laps = route.Closed ? (lap > Laps ? "FINISH" : $"{lap}/{Laps}") : "-";
        Text(MarginPx, MarginPx, 56f, laps);
        Text(MarginPx + (ImGui.CalcTextSize(laps).X * 56f / ImGui.GetFontSize()) + 10f, MarginPx + 6f, 20f, "LAPS");
        if (route.Closed)
        {
            float top = corner.Y + MarginPx + 66f, left = corner.X + MarginPx;
            draw.AddRectFilled(new float2(left, top), new float2(left + TimesWidePx, top + (3f * TimesRowPx) + 6f), Shade, 4f);
            void Row(int row, string name, double? seconds)
            {
                float y = MarginPx + 66f + 4f + (row * TimesRowPx);
                Text(MarginPx + 8f, y, 18f, name);
                Text(MarginPx + TimesWidePx - 8f, y, 18f, seconds is { } t ? RaceBoard.Clock(t) : "-:--.---", fromRight: true);
            }
            Row(0, "TIME", me.Tally.RaceSoFar(now));
            Row(1, "LAP", me.Tally.LapSoFar(now));
            Row(2, "BEST", me.Tally.BestLapS);
        }

        // Place, top right.
        if (Cars.Count > 1)
        {
            string place = $"{RaceBoard.Position(totals, my)}/{Cars.Count}";
            Text(size.X - MarginPx, MarginPx, 56f, place, fromRight: true);
            Text(size.X - MarginPx - (ImGui.CalcTextSize(place).X * 56f / ImGui.GetFontSize()) - 12f, MarginPx + 6f, 20f, "POSITION", fromRight: true);
        }

        // For a few seconds after a checkpoint, top middle: how much earlier or later than on the best lap the car was there.
        void Centred(float y, float px, string text, ImColor8 colour)
        {
            float wide = ImGui.CalcTextSize(text).X * px / ImGui.GetFontSize();
            float2 at = new(corner.X + ((size.X - wide) / 2f), corner.Y + y);
            draw.AddText(font, px, new float2(at.X + 2f, at.Y + 2f), Shade, text);
            draw.AddText(font, px, at, colour, text);
        }
        if (route.Closed && me.Tally.Split is { } split && now - split.At < SplitShownSeconds)
        {
            Centred(MarginPx, 44f, RaceBoard.Gap(split.DeltaS), split.DeltaS <= 0.0 ? Quicker : Slower);
        }

        // The checkpoint it is due at, as a post standing on the road there with how far off it is.
        if (route.Closed && _body is { } body && ReferenceEquals(flown.Parent, body))
        {
            double3 foot = route.Standing(me.Tally.NextGateM).At;
            double away = Vec.Len(foot - mine.PlaceCcf);
            doubleQuat ccf2Cce = body.GetCcf2Cce();
            double3 centreEcl = body.GetPositionEcl();
            if (away > PostNearM && away < PostFarM
                && KsaWorld.TryProjectAhead(centreEcl + foot.Transform(ccf2Cce), out float2 low)
                && KsaWorld.TryProjectAhead(centreEcl + (foot + (Vec.Unit(foot) * PostTallM)).Transform(ccf2Cce), out float2 high))
            {
                draw.AddLine(new float2(low.X + 1.5f, low.Y), new float2(high.X + 1.5f, high.Y), Shade, 3f);
                draw.AddLine(low, high, Ink, 2f);
                draw.AddTriangleFilled(new float2(high.X - 5f, high.Y - 8f), new float2(high.X + 5f, high.Y - 8f), new float2(high.X, high.Y - 1f), Ink);

                void Over(float y, float px, string text)
                {
                    float wide = ImGui.CalcTextSize(text).X * px / ImGui.GetFontSize();
                    float2 at = new(high.X - (wide / 2f), high.Y + y);
                    draw.AddText(font, px, new float2(at.X + 1.5f, at.Y + 1.5f), Shade, text);
                    draw.AddText(font, px, at, Ink, text);
                }
                bool atLine = me.Tally.Passed % RaceBoard.Gates(route.LengthM) == 0;
                Over(-30f, 16f, !atLine ? "CHECKPOINT" : me.Tally.Passed == 0 ? "START" : me.Tally.Lap >= Laps ? "FINISH" : "LAP");
                Over(-58f, 26f, $"{away:F0} M");
                draw.AddLine(new float2(high.X - 46f, high.Y - 32f), new float2(high.X + 46f, high.Y - 32f), Gate, 2f);
            }
        }

        // Past a checkpoint without having been through it: nothing counts until it goes back.
        if (route.Closed && me.Tally.Missed)
        {
            Centred(MarginPx + 52f, 34f, "MISSED CHECKPOINT", Slower);
        }

        // The course about the car, bottom left, the way the car faces at the top.
        float half = MapPx / 2f;
        float2 middle = new(corner.X + MarginPx + half, corner.Y + size.Y - MarginPx - half);
        draw.AddCircleFilled(middle, half, Shade, 48);
        float scale = half / (float)ReachM;
        bool Spot(double3 place, out float2 at)
        {
            (double right, double down) = RaceBoard.OnMap(place, mine.PlaceCcf, mine.AheadCcf);
            at = new float2(middle.X + ((float)right * scale), middle.Y + ((float)down * scale));
            return (right * right) + (down * down) < (ReachM - 6.0) * (ReachM - 6.0);
        }
        for (int i = 0; i + 1 < _line.Length || (route.Closed && i < _line.Length); i++)
        {
            bool a = Spot(_line[i], out float2 from), b = Spot(_line[(i + 1) % _line.Length], out float2 to);
            if (a && b) draw.AddLine(from, to, Road, 3f);
        }
        if (route.Closed && Spot(route.Standing(line).At, out float2 start)) draw.AddCircleFilled(start, 3.5f, Line, 8);
        if (route.Closed && Spot(route.Standing(me.Tally.NextGateM).At, out float2 gate))
        {
            draw.AddCircleFilled(gate, 6f, Edge, 12);
            draw.AddCircleFilled(gate, 4.5f, Gate, 12);
        }
        foreach ((Vehicle craft, Seen _) in Cars)
        {
            if (ReferenceEquals(craft, flown) || Buggies.Of(craft) is not { PlaceValid: true } other || !Spot(other.PlaceCcf, out float2 at)) continue;
            draw.AddCircleFilled(at, 5.5f, Edge, 12);
            draw.AddCircleFilled(at, 4f, Theirs, 12);
        }
        draw.AddTriangleFilled(new float2(middle.X, middle.Y - 9f), new float2(middle.X - 6f, middle.Y + 6f), new float2(middle.X + 6f, middle.Y + 6f), Edge);
        draw.AddTriangleFilled(new float2(middle.X, middle.Y - 7f), new float2(middle.X - 4.5f, middle.Y + 4.5f), new float2(middle.X + 4.5f, middle.Y + 4.5f), Mine);
    }
}
