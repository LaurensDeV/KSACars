using Brutal.ImGuiApi;
using Brutal.Numerics;
using KSA;

namespace KSACars;

/// <summary>
/// Draws a circuit on the ground with the mouse, as a vector shape is drawn: a click on the ground
/// puts a point down joined to the one selected, a click on another point joins the two, a click on a
/// road puts a point into it, and a point or one of its handles is dragged.
///
/// <para>While it is on the view is the editor's own, a <see cref="GodView"/> held through KSA's fixed
/// camera mode: the middle button drags the ground, the right one turns and tilts, the wheel comes
/// nearer. The camera goes on following the craft and is written as an offset from it every frame, so
/// it stays over its place on the ground. The roads on the ground are
/// laid again on every change, coarsely while something is dragged and in full, with the clutter
/// taken off them, when it is let go.</para>
/// </summary>
internal sealed class RoadEditor
{
    private const ImGuiWindowFlags Flags = ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoCollapse;

    private const float NodeReach = 13f, KnobReach = 11f, RoadReach = 10f, DragStartsPx = 4f;
    private const double LiftM = 0.07, ClutterMarginM = 1.5, FineSpacingM = 2.0, CoarseSpacingM = 6.0;

    // A handle shorter than this is drawn on its point and could not be picked apart from it.
    private const double ShownHandleM = 2.0;

    private static readonly ImColor8 Point = new(255, 255, 255, 235);
    private static readonly ImColor8 Chosen = new(255, 200, 60, 255);
    private static readonly ImColor8 Hover = new(120, 220, 255, 255);
    private static readonly ImColor8 Knob = new(255, 120, 200, 255);
    private static readonly ImColor8 Lift = new(120, 255, 140, 255);
    private static readonly ImColor8 Shadow = new(0, 0, 0, 170);

    // How far from its point the height knob is drawn, in pixels.
    private const float LiftStalkPx = 46f;

    private enum Grab { None, Node, Handle, Height }

    private readonly record struct KnobAt(int Other, float2 Screen, bool Manual);

    private CircuitHistory _history = new(new Circuit { Name = "Circuit" });
    private readonly ImInputString _name = new(64, "Circuit"u8);
    private Celestial? _body;
    private int _selected;
    private Grab _grab;
    private int _grabNode, _grabOther;
    private float2 _pressAt;
    private bool _moved;
    private bool _loadAtCar;
    private float _loadTurnDeg;
    private float? _widthHeld;
    private string _message = "";

    private Circuit? _laidFor;
    private bool _laidFine;
    private double _lengthM;
    private List<RoadLayout.Stretch> _stretches = [];
    private readonly Dictionary<int, float2> _nodeScreen = [];
    private readonly List<KnobAt> _knobs = [];

    // The selected point's height knob: where it is drawn, which way on screen is up for it, and how
    // many pixels along that a metre of height is.
    private float2? _liftKnob;
    private float2 _liftAxis;
    private float _liftPxPerM;

    // Radians of turn a pixel of right-drag, and metres of ground a pixel of middle-drag for each metre the eye is away.
    private const double TurnPerPx = 0.005, PanPerPxPerM = 0.0012;

    private bool _free = true;
    private bool _viewSet;
    private GodView _view;
    private CameraMode? _modeBefore;

    public bool Enabled { get; set; }

    /// <summary>What the bridge asks of the editor, taken up on its next frame: on or off, a circuit to edit, and a view in degrees and metres.</summary>
    public static bool? AskEnabled;
    public static Circuit? AskCircuit;
    public static (double YawDeg, double PitchDeg, double DistanceM)? AskView;

    private Circuit Now => _history.Now;

    public void Update()
    {
        if (AskEnabled is { } asked) (Enabled, AskEnabled) = (asked, null);
        if (!Enabled || KsaWorld.ControlledVehicle is not { } craft || KsaWorld.ParentBody(craft) is not { } body)
        {
            ReleaseView();
            return;
        }
        if (!ReferenceEquals(body, _body))
        {
            _body = body;
            _viewSet = false;

            // Opened on roads that are already laid, it is those it edits: a new circuit would take them up.
            Circuit? laid = Roads.CircuitOn(body);
            Start(laid ?? new Circuit { Name = "Circuit", Body = body.Id });
            if (laid is not null) (_laidFor, _laidFine) = (Now, true);
        }

        if (AskCircuit is { } given)
        {
            Start(given with { Body = body.Id });
            AskCircuit = null;
        }

        Window();
        if (!Enabled)
        {
            ReleaseView();
            return;
        }

        View(craft, body);

        Project();
        Mouse();
        Keys();
        Lay();
        Overlay();
    }

    // ---- the view ---------------------------------------------------------------------------

    private void View(Vehicle craft, Celestial body)
    {
        if (!_free)
        {
            ReleaseView();
            return;
        }

        doubleQuat ccf2Cce = body.GetCcf2Cce();
        double3 pole = Vec.Unit(body.GetDirCcfFromLatLon(90.0, 0.0));
        if (!_viewSet)
        {
            double3 craftCcf = (craft.GetPositionEcl() - body.GetPositionEcl()).Transform(ccf2Cce.Inverse());
            _view = new GodView(Vec.Unit(craftCcf), 0.0, 60.0 * Math.PI / 180.0, 250.0);
            _viewSet = true;
        }

        if (AskView is { } want)
        {
            _view = _view with { YawRad = want.YawDeg * Math.PI / 180.0, DistanceM = want.DistanceM, PitchRad = 0.0 };
            _view = _view.Turn(0.0, want.PitchDeg * Math.PI / 180.0).Zoom(0.0);
            AskView = null;
        }

        ImGuiIOPtr io = ImGui.GetIO();
        if (!io.WantCaptureMouse)
        {
            float2 moved = io.MouseDelta;
            if (io.MouseWheel != 0f) _view = _view.Zoom(io.MouseWheel);
            if (ImGui.IsMouseDown(ImGuiMouseButton.Right)) _view = _view.Turn(moved.X * TurnPerPx, moved.Y * TurnPerPx);
            if (ImGui.IsMouseDown(ImGuiMouseButton.Middle))
            {
                double perPx = _view.DistanceM * PanPerPxPerM;
                _view = _view.Pan(pole, body.MeanRadius, -moved.X * perPx, moved.Y * perPx);
            }
        }

        double ground = body.GetTerrainHeightFromDirCcf(_view.Target, accurate: false);
        (double3 eye, double3 forward) = _view.Pose(pole, body.MeanRadius + ground);

        // Tilted low over rising ground the eye would be inside the hill.
        double floor = body.MeanRadius + body.GetTerrainHeightFromDirCcf(Vec.Unit(eye), accurate: false) + 3.0;
        if (Vec.Len(eye) < floor) eye = Vec.Unit(eye) * floor;

        _modeBefore ??= KsaWorld.MainViewMode;
        double3 offset = body.GetPositionEcl() + eye.Transform(ccf2Cce) - craft.GetPositionEcl();
        KsaWorld.TryFixedView(offset, forward.Transform(ccf2Cce));
    }

    private void ReleaseView()
    {
        if (_modeBefore is not { } mode) return;
        KsaWorld.RestoreViewMode(mode);
        _modeBefore = null;
    }

    private void Start(Circuit circuit)
    {
        _history = new CircuitHistory(circuit);
        _selected = 0;
        _grab = Grab.None;
        _laidFor = null;
        _name.SetValue(circuit.Name);
    }

    // ---- the window -------------------------------------------------------------------------

    private void Window()
    {
        ImGuiViewportPtr screen = ImGui.GetMainViewport();
        float2 at = new(screen.WorkPos.X + screen.WorkSize.X - 340f, screen.WorkPos.Y + 80f);
        ImGui.SetNextWindowPos(in at, ImGuiCond.FirstUseEver, null);

        bool open = true;
        if (ImGui.Begin("Roads###Roads", ref open, Flags))
        {
            ImGui.SetNextItemWidth(180f);
            ImGui.InputText("Name", _name);
            if (ImGui.IsItemDeactivatedAfterEdit() && _name.ToString().Trim() is { Length: > 0 } name)
            {
                _history.Do(Now with { Name = name });
            }

            if (ImGui.Button("Save", null)) Save();
            ImGui.SameLine(0f, -1f);
            if (ImGui.Button("New", null)) Start(new Circuit { Name = "Circuit", Body = _body!.Id });
            ImGui.SameLine(0f, -1f);
            ImGui.SetNextItemWidth(110f);
            if (ImGui.BeginCombo("##load", "Load..."))
            {
                foreach (string saved in CircuitLibrary.Names())
                {
                    if (ImGui.Selectable(saved, selected: false, ImGuiSelectableFlags.None, (float2?)null)) Load(saved);
                }
                ImGui.EndCombo();
            }

            // A circuit's file is metres from one place, so it can be laid with its first point where the craft is.
            ImGui.Checkbox("Load at the craft", ref _loadAtCar);
            if (_loadAtCar)
            {
                ImGui.SameLine(0f, -1f);
                ImGui.SetNextItemWidth(110f);
                ImGui.SliderFloat("turned", ref _loadTurnDeg, 0f, 360f, "%.0f deg", ImGuiSliderFlags.None);
            }

            if (ImGui.Button("Undo", null)) Undo();
            ImGui.SameLine(0f, -1f);
            if (ImGui.Button("Redo", null)) Redo();
            ImGui.SameLine(0f, -1f);
            if (ImGui.Button("Look straight down", null))
            {
                if (_free) _view = _view with { PitchRad = GodView.MaxPitchRad };
                else KsaWorld.TryLookStraightDown();
            }

            ImGui.Checkbox("Free camera", ref _free);
            if (_free) ImGui.TextDisabled("middle button drags the ground,\nright button turns and tilts, wheel zooms");

            _widthHeld ??= (float)Now.WidthM;
            float width = _widthHeld.Value;
            ImGui.SetNextItemWidth(180f);
            ImGui.SliderFloat("Width", ref width, 4f, 30f, "%.0f m", ImGuiSliderFlags.None);
            _widthHeld = width;
            if (ImGui.IsItemDeactivatedAfterEdit()) _history.Do(Now with { WidthM = Math.Round(width) });
            if (!ImGui.IsItemActive()) _widthHeld = (float)Now.WidthM;

            ImGui.Separator();
            if (Now.Find(_selected) is { } node)
            {
                int roads = Now.Roads.Count(r => r.Touches(node.Id));
                ImGui.Text(roads >= 3 ? $"Junction of {roads} roads" : roads == 2 ? "Point on a road" : "End of a road");

                float corner = (float)node.Corner;
                ImGui.SetNextItemWidth(180f);
                if (ImGui.SliderFloat("Corner", ref corner, 0f, (float)Circuit.MaxCorner, "%.2f", ImGuiSliderFlags.None))
                {
                    _history.BeginDrag();
                    _history.Do(Now.SetCorner(node.Id, corner));
                }
                if (ImGui.IsItemDeactivated()) _history.EndDrag();

                double height = node.HeightM;
                ImGui.SetNextItemWidth(180f);
                if (ImGui.InputDouble("Height (m)", ref height, 0.5, 5.0, "%.1f", ImGuiInputTextFlags.None))
                {
                    _history.Do(Now.SetHeight(node.Id, height));
                }
                ImGui.TextDisabled("or drag the green knob over the point");

                PointSliders(node, roads);

                if (Now.Roads.Any(r => (r.From == node.Id && r.FromHandle is not null) || (r.To == node.Id && r.ToHandle is not null)))
                {
                    if (ImGui.Button("Automatic handles", null)) ClearHandles(node.Id);
                    ImGui.SameLine(0f, -1f);
                }
                if (ImGui.Button("Delete point", null)) DeleteSelected();
                ImGui.TextDisabled("click the ground to carry the road on,\nanother point to join them,\nthis point again to let go of it");
            }
            else
            {
                ImGui.TextDisabled(Now.Nodes.Count == 0 ? "click the ground to start a road"
                    : "click a point to pick it up,\na road to put a point in it,\nor the ground to start another road");
            }

            if (Roads.Warnings.Count > 0)
            {
                ImGui.Separator();
                ImGui.PushTextWrapPos(300f);
                foreach (RoadWarning warning in Roads.Warnings.Take(MostWarningsShown))
                {
                    ImGui.TextColored(WarnText, "! " + warning.Text);
                    if (warning.Node is { } point && ImGui.IsItemClicked(ImGuiMouseButton.Left)) _selected = point;
                }
                if (Roads.Warnings.Count > MostWarningsShown) ImGui.TextDisabled($"and {Roads.Warnings.Count - MostWarningsShown} more, each marked in red");
                ImGui.PopTextWrapPos();
            }

            ImGui.Separator();
            ImGui.TextDisabled($"{Now.Nodes.Count} points, {Now.Roads.Count} roads, {_lengthM:F0} m{(_history.Unsaved ? ", not saved" : "")}");
            if (_message.Length > 0) ImGui.TextDisabled(_message);
        }
        ImGui.End();
        if (!open) Enabled = false;
    }

    private const int MostWarningsShown = 6;
    private static readonly float4 WarnText = new(1.0f, 0.55f, 0.35f, 1.0f);
    private static readonly ImColor8 Warn = new(255, 80, 60, 255);

    // What a point sets of the roads at it: how wide they are there and how they lean, and at a
    // junction how far its corners are rounded. A slider is one step to undo however far it is dragged.
    private void PointSliders(Circuit.Node node, int roads)
    {
        List<Circuit.Road> here = [.. Now.Roads.Where(r => r.Touches(node.Id))];
        if (here.Count == 0) return;

        float width = (float)(Circuit.EndWidth(here[0], node.Id) ?? Now.WidthOf(here[0]));
        ImGui.SetNextItemWidth(180f);
        if (ImGui.SliderFloat("Width here", ref width, 4f, 30f, "%.0f m", ImGuiSliderFlags.None))
        {
            _history.BeginDrag();
            Circuit next = Now;
            foreach (Circuit.Road r in here) next = next.SetEndWidth(node.Id, r.From == node.Id ? r.To : r.From, Math.Round(width));
            _history.Do(next);
        }
        if (ImGui.IsItemDeactivated()) _history.EndDrag();
        if (here.Any(r => Circuit.EndWidth(r, node.Id) is not null))
        {
            ImGui.SameLine(0f, -1f);
            if (ImGui.SmallButton("the road's"))
            {
                Circuit next = Now;
                foreach (Circuit.Road r in here) next = next.SetEndWidth(node.Id, r.From == node.Id ? r.To : r.From, null);
                _history.Do(next);
            }
        }

        if (roads >= 3)
        {
            // A junction is one plane, which its roads take their lean from: there is none to set.
            float radius = (float)(node.JunctionRadiusM ?? RoadJunction.DefaultRadiusM);
            ImGui.SetNextItemWidth(180f);
            if (ImGui.SliderFloat("Corners", ref radius, 0f, 40f, "%.0f m", ImGuiSliderFlags.None))
            {
                _history.BeginDrag();
                _history.Do(Now.SetJunctionRadius(node.Id, Math.Round(radius)));
            }
            if (ImGui.IsItemDeactivated()) _history.EndDrag();
            return;
        }

        // One lean for the road through the point: each road keeps its own as its left edge up from its
        // first point to its second, so the one that runs the other way through here keeps the opposite.
        float lean = 0f;
        for (int i = 0; i < here.Count; i++)
        {
            if (Circuit.EndBankDeg(here[i], node.Id) is { } kept) lean = (float)(kept * Sense(here[i], node.Id, i));
        }
        ImGui.SetNextItemWidth(180f);
        if (ImGui.SliderFloat("Lean", ref lean, (float)-Circuit.MaxBankDeg, (float)Circuit.MaxBankDeg, "%.0f deg", ImGuiSliderFlags.None))
        {
            _history.BeginDrag();
            Circuit next = Now;
            for (int i = 0; i < here.Count; i++)
            {
                double? bank = Math.Abs(lean) < 0.5f ? null : Math.Round(lean) * Sense(here[i], node.Id, i);
                next = next.SetBank(node.Id, here[i].From == node.Id ? here[i].To : here[i].From, bank);
            }
            _history.Do(next);
        }
        if (ImGui.IsItemDeactivated()) _history.EndDrag();
    }

    // Whether a road at a point runs the way the road through that point is taken to: the first road into it, the rest out.
    private static double Sense(Circuit.Road road, int node, int index) => (index == 0 ? road.To == node : road.From == node) ? 1.0 : -1.0;

    private void Save()
    {
        Circuit saving = Now with { Body = _body!.Id, RadiusM = _body.MeanRadius };
        if (CircuitLibrary.Save(saving, out string why))
        {
            _history.MarkSaved();
            _message = $"saved as {Circuit.FileName(saving.Name)}";
        }
        else
        {
            _message = $"not saved: {why}";
        }
    }

    private void Load(string name)
    {
        if (CircuitLibrary.Load(name, out string why) is not { } read)
        {
            _message = $"not loaded: {why}";
            return;
        }
        if (_loadAtCar)
        {
            if (KsaWorld.ControlledVehicle is not { } craft || !KsaWorld.TryCraftSurfacePoint(craft, out _, out double lat, out double lon, out string on)
                || on != _body!.Id)
            {
                _message = "not loaded: no craft here to lay it at";
                return;
            }
            read = read.MovedTo(lat, lon, _loadTurnDeg, _body.MeanRadius, _body.Id);
        }
        else if (read.Body != _body!.Id)
        {
            _message = $"'{read.Name}' is on {read.Body}: tick 'at the craft' to lay it here";
            return;
        }
        Start(read);
        _message = $"loaded {read.Name}";
    }

    private void Undo()
    {
        if (_history.Undo() && Now.Find(_selected) is null) _selected = 0;
    }

    private void Redo()
    {
        if (_history.Redo() && Now.Find(_selected) is null) _selected = 0;
    }

    private void DeleteSelected()
    {
        if (_selected == 0) return;
        _history.Do(Now.RemoveNode(_selected));
        _selected = 0;
    }

    private void ClearHandles(int node)
    {
        Circuit next = Now;
        foreach (Circuit.Road r in Now.Roads.Where(r => r.Touches(node)))
        {
            next = next.SetHandle(node, r.From == node ? r.To : r.From, null);
        }
        _history.Do(next);
    }

    // ---- the mouse and the keys -------------------------------------------------------------

    private void Mouse()
    {
        ImGuiIOPtr io = ImGui.GetIO();
        float2 mouse = ImGui.GetMousePos();

        if (_grab == Grab.None)
        {
            if (io.WantCaptureMouse || !ImGui.IsMouseClicked(ImGuiMouseButton.Left, repeat: false)) return;

            _pressAt = mouse;
            _moved = false;
            if (_liftKnob is { } lift && Vec2(lift - mouse) < KnobReach)
            {
                (_grab, _grabNode) = (Grab.Height, _selected);
            }
            else if (KnobUnder(mouse) is { } knob)
            {
                (_grab, _grabNode, _grabOther) = (Grab.Handle, _selected, knob.Other);
            }
            else if (NodeUnder(mouse) is { } node)
            {
                (_grab, _grabNode) = (Grab.Node, node);
            }
            else if (TryCursor(out double lat, out double lon))
            {
                if (RoadUnder(mouse) is { } road)
                {
                    _history.Do(Now.Split(road.From, road.To, lat, lon, out int mid));
                    _selected = mid;
                }
                else
                {
                    int id;
                    _history.Do(_selected != 0 ? Now.Extend(_selected, lat, lon, out id) : Now.AddNode(lat, lon, out id));
                    _selected = id;
                }
            }
            return;
        }

        if (ImGui.IsMouseDown(ImGuiMouseButton.Left))
        {
            if (!_moved && Vec2(mouse - _pressAt) > DragStartsPx)
            {
                _moved = true;
                _history.BeginDrag();
                if (_grab == Grab.Node) _selected = _grabNode;
            }
            if (_moved && _grab == Grab.Height)
            {
                // Along the knob's own stalk, so it follows the pointer whichever way the view is turned.
                float2 by = io.MouseDelta;
                double metres = ((by.X * _liftAxis.X) + (by.Y * _liftAxis.Y)) / Math.Max(_liftPxPerM, 1e-3f);
                if (Now.Find(_grabNode) is { } raised) _history.Do(Now.SetHeight(_grabNode, raised.HeightM + metres));
            }
            else if (_moved && TryCursor(out double lat, out double lon))
            {
                _history.Do(_grab == Grab.Node ? Now.MoveNode(_grabNode, lat, lon)
                    : Now.SetHandle(_grabNode, _grabOther, new Circuit.Place(lat, lon)));
            }
            return;
        }

        if (_moved) _history.EndDrag();
        else if (_grab == Grab.Node) ClickedNode(_grabNode);
        _grab = Grab.None;
    }

    private void ClickedNode(int node)
    {
        if (_selected == node)
        {
            _selected = 0;
            return;
        }
        if (_selected != 0) _history.Do(Now.Connect(_selected, node));
        _selected = node;
    }

    private void Keys()
    {
        ImGuiIOPtr io = ImGui.GetIO();
        if (io.WantTextInput) return;

        if (ImGui.IsKeyPressed(ImGuiKey.Delete, repeat: false)) DeleteSelected();
        if (io.KeyCtrl && ImGui.IsKeyPressed(ImGuiKey.Z, repeat: false)) Undo();
        if (io.KeyCtrl && ImGui.IsKeyPressed(ImGuiKey.Y, repeat: false)) Redo();
        if (Now.Find(_selected) is { } node)
        {
            if (ImGui.IsKeyPressed(ImGuiKey.PageUp, repeat: true)) _history.Do(Now.SetHeight(node.Id, node.HeightM + 0.5));
            if (ImGui.IsKeyPressed(ImGuiKey.PageDown, repeat: true)) _history.Do(Now.SetHeight(node.Id, node.HeightM - 0.5));
        }
    }

    private bool TryCursor(out double lat, out double lon)
    {
        lat = lon = 0.0;
        return KsaWorld.TryCursorGroundPoint(out _, out lat, out lon, out string body) && body == _body!.Id;
    }

    private static float Vec2(float2 v) => MathF.Sqrt((v.X * v.X) + (v.Y * v.Y));

    private int? NodeUnder(float2 mouse)
    {
        int? best = null;
        float least = NodeReach;
        foreach ((int id, float2 at) in _nodeScreen)
        {
            float d = Vec2(at - mouse);
            if (d < least) (best, least) = (id, d);
        }
        return best;
    }

    private KnobAt? KnobUnder(float2 mouse)
    {
        KnobAt? best = null;
        float least = KnobReach;
        foreach (KnobAt knob in _knobs)
        {
            float d = Vec2(knob.Screen - mouse);
            if (d < least) (best, least) = (knob, d);
        }
        return best;
    }

    private RoadLayout.Stretch? RoadUnder(float2 mouse)
    {
        RoadLayout.Stretch? best = null;
        float least = RoadReach;
        foreach (RoadLayout.Stretch stretch in _stretches)
        {
            // A few hundred points a frame at most, however long the circuit.
            int stride = Math.Max(1, stretch.Line.Length / 60);
            for (int i = 0; i < stretch.Line.Length; i += stride)
            {
                if (!KsaWorld.TryProjectAhead(EclOf(stretch.Line[i], stretch.HeightM[i]), out float2 at)) continue;
                float d = Vec2(at - mouse);
                if (d < least) (best, least) = (stretch, d);
            }
        }
        return best;
    }

    // ---- where things are -------------------------------------------------------------------

    private double3 EclOf(double3 dirCcf, double heightM = 0.0)
    {
        Celestial body = _body!;
        double3 dir = Vec.Unit(dirCcf);
        double ground = body.GetTerrainHeightFromDirCcf(dir, accurate: false);
        return body.GetPositionEcl() + (dir * (body.MeanRadius + ground + heightM + 0.4)).Transform(body.GetCcf2Cce());
    }

    private void Project()
    {
        Celestial body = _body!;
        _nodeScreen.Clear();
        _knobs.Clear();

        Dictionary<int, double3> at = [];
        foreach (Circuit.Node n in Now.Nodes)
        {
            double3 dir = Vec.Unit(body.GetDirCcfFromLatLon(n.LatDeg, n.LonDeg));
            at[n.Id] = dir * body.MeanRadius;
            if (KsaWorld.TryProjectAhead(EclOf(dir, n.HeightM), out float2 screen)) _nodeScreen[n.Id] = screen;
        }

        _liftKnob = null;
        if (Now.Find(_selected) is not { } chosen) return;

        double3 chosenEcl = EclOf(at[chosen.Id], chosen.HeightM);
        double3 upEcl = Vec.Unit(at[chosen.Id]).Transform(body.GetCcf2Cce());
        if (_nodeScreen.TryGetValue(chosen.Id, out float2 foot)
            && KsaWorld.TryApparentRadiusPixels(chosenEcl, 1.0, out float acrossPx) && acrossPx > 1e-3f)
        {
            // Seen from nearly overhead a metre of height is next to nothing on screen, and the knob
            // is then worked straight up the screen at the scale the ground is drawn at.
            float2 axis = new(0f, -1f);
            float perM = acrossPx;
            if (KsaWorld.TryProjectAhead(chosenEcl + upEcl, out float2 above) && Vec2(above - foot) > 0.25f * acrossPx)
            {
                perM = Vec2(above - foot);
                axis = (above - foot) / perM;
            }
            (_liftAxis, _liftPxPerM) = (axis, perM);
            _liftKnob = foot + (axis * LiftStalkPx);
        }

        foreach (Circuit.Road r in Now.Roads)
        {
            if (!r.Touches(_selected)) continue;
            int other = r.From == _selected ? r.To : r.From;
            Circuit.Place? set = r.From == _selected ? r.FromHandle : r.ToHandle;
            double3 handle = RoadLayout.Handle(Now, at, _selected, other, set, body.GetDirCcfFromLatLon, body.MeanRadius);
            if (set is null && Vec.Len(handle) < ShownHandleM) continue;
            if (KsaWorld.TryProjectAhead(EclOf(at[_selected] + handle, Now.Find(_selected)!.HeightM), out float2 screen))
            {
                _knobs.Add(new KnobAt(other, screen, set is not null));
            }
        }
    }

    private void Lay()
    {
        bool fine = _grab == Grab.None || !_moved;
        if (ReferenceEquals(_laidFor, Now) && (_laidFine || !fine)) return;

        Celestial body = _body!;
        Roads.Lay(body, Now, LiftM, fine ? FineSpacingM : CoarseSpacingM, whole: fine, touched: fine ? null : Touched());
        if (fine) Roads.ClearClutter(ClutterMarginM);
        _laidFor = Now;
        _laidFine = fine;

        _stretches = RoadLayout.Of(Now, body.GetDirCcfFromLatLon, body.MeanRadius, CoarseSpacingM);
        _lengthM = 0.0;
        foreach (RoadLayout.Stretch s in _stretches)
        {
            for (int i = 1; i < s.Line.Length; i++) _lengthM += Vec.Len(s.Line[i] - s.Line[i - 1]) * body.MeanRadius;
        }
    }

    // The point being dragged and every point a road joins it to: a point's handle is worked out from
    // its neighbours, so the roads at those turn with it.
    private HashSet<int> Touched()
    {
        HashSet<int> touched = [_grabNode];
        foreach (Circuit.Road road in Now.Roads.Where(r => r.Touches(_grabNode)))
        {
            touched.Add(road.From);
            touched.Add(road.To);
        }
        return touched;
    }

    // ---- what is drawn over the world -------------------------------------------------------

    private void Overlay()
    {
        ImDrawListPtr draw = ImGui.GetBackgroundDrawList();
        float2 mouse = ImGui.GetMousePos();
        bool free = _grab == Grab.None && !ImGui.GetIO().WantCaptureMouse;
        int? over = free ? NodeUnder(mouse) : null;
        KnobAt? overKnob = free ? KnobUnder(mouse) : null;

        if (_nodeScreen.TryGetValue(_selected, out float2 chosen))
        {
            foreach (KnobAt knob in _knobs)
            {
                draw.AddLine(chosen, knob.Screen, Knob, 1.5f);
                draw.AddCircleFilled(knob.Screen, 5.5f, Shadow);
                draw.AddCircleFilled(knob.Screen, 4f, overKnob is { } k && k.Other == knob.Other ? Hover : Knob);
                if (!knob.Manual) draw.AddCircleFilled(knob.Screen, 2f, Shadow);
            }

            if (_liftKnob is { } lift)
            {
                bool held = _grab == Grab.Height || (free && Vec2(lift - mouse) < KnobReach);
                draw.AddLine(chosen, lift, Lift, 1.5f);
                draw.AddCircleFilled(lift, 6.5f, Shadow);
                draw.AddCircleFilled(lift, 5f, held ? Hover : Lift);
                if (Now.Find(_selected) is { } raised)
                {
                    draw.AddText(new float2(lift.X + 10f, lift.Y - 7f), Lift, $"{raised.HeightM:F1} m");
                }
            }

            // Where the next click would carry the road to.
            if (free && over is null && overKnob is null && !(_liftKnob is { } k2 && Vec2(k2 - mouse) < KnobReach)) draw.AddLine(chosen, mouse, Chosen, 1.2f);
        }

        foreach (RoadWarning warning in Roads.Warnings)
        {
            if (!KsaWorld.TryProjectAhead(EclOf(warning.At), out float2 where)) continue;
            draw.AddCircle(where, 13f, Shadow, 0, 4f);
            draw.AddCircle(where, 13f, Warn, 0, 2f);
            draw.AddText(new float2(where.X - 2.5f, where.Y - 7f), Warn, "!");
        }

        foreach ((int id, float2 at) in _nodeScreen)
        {
            bool junction = Now.Roads.Count(r => r.Touches(id)) >= 3;
            float radius = junction ? 7f : 5.5f;
            draw.AddCircleFilled(at, radius + 2f, Shadow);
            draw.AddCircleFilled(at, radius, id == _selected ? Chosen : id == over ? Hover : Point);
        }
    }
}
