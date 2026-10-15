using Brutal.ImGuiApi;
using Brutal.Numerics;
using KSA;

namespace KSACars;

/// <summary>
/// The panel shown while a car is being flown: what it is doing, and a button that sets it back on
/// its wheels. Drawn from the GUI pass, so it goes with the rest of the UI when that is hidden.
/// </summary>
internal static class CarPanel
{
    // Neither takes the keyboard: KSA drops the flown craft's held keys while an ImGui window has it,
    // and those keys are the throttle and the steering.
    private const ImGuiWindowFlags Flags = ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoCollapse
                                           | ImGuiWindowFlags.NoFocusOnAppearing | ImGuiWindowFlags.NoNav;

    private static float? _rockWeight;

    /// <summary>Whether the panel is open. Closed, a small button that opens it is left in its place.</summary>
    public static bool Visible = true;

    private static bool _warnedModMenu;

    /// <summary>
    /// Called by ModMenu, if the player has it, to fill this mod's entry in its menu. Static, because
    /// that is what ModMenu can always call.
    /// </summary>
    [ModMenuEntry("Fast & Purrious")]
    public static void DrawModMenu()
    {
        // Inside ModMenu's own menu build, where anything thrown would be its failure and not in this log.
        try
        {
            bool visible = Visible;
            if (ImGui.MenuItem("Panel", default, ref visible, true)) Visible = visible;
        }
        catch (Exception e)
        {
            if (_warnedModMenu) return;
            _warnedModMenu = true;
            Log.Warn($"ModMenu entry failed, so its Panel item will not work: {e.Message}");
        }
    }

    private static string? _course;

    public static void Draw(CraftMover mover, RoadEditor roads)
    {
        if (KsaWorld.ControlledVehicle is not { } craft || Buggies.Of(craft) is not { } car) return;

        ImGuiViewportPtr screen = ImGui.GetMainViewport();
        float2 at = new(screen.WorkPos.X + 16f, screen.WorkPos.Y + (screen.WorkSize.Y * 0.5f));
        ImGui.SetNextWindowPos(in at, ImGuiCond.FirstUseEver, null);

        if (!Visible)
        {
            if (ImGui.Begin("Fast & Purrious##reopen", Flags | ImGuiWindowFlags.NoTitleBar))
            {
                if (ImGui.SmallButton("F&P")) Visible = true;
            }
            ImGui.End();
            return;
        }

        if (ImGui.Begin("Fast & Purrious###Car", ref Visible, Flags))
        {
            ImGui.Text(car.Drive.Profile.DisplayName);
            // On the speed's own line, so nothing under it moves when the car goes over.
            string lying = car.Level >= 0.5 ? "" : car.Level < -0.5 ? ", on its roof" : ", on its side";
            ImGui.Text($"{Math.Abs(car.Drive.ForwardSpeed) * 3.6:F0} km/h, gear {car.Drive.Gear + 1}{lying}");

            ImGui.Text("Headlights");
            BeamButton(car, "Off", BeamSetting.Off);
            ImGui.SameLine(0f, -1f);
            BeamButton(car, "Low", BeamSetting.Low);
            ImGui.SameLine(0f, -1f);
            BeamButton(car, "High", BeamSetting.High);

            ScoopProfile[] scoops = car.Drive.Profile.Scoops;
            if (scoops.Length > 0)
            {
                ImGui.Text("Scoop size");
                if (ImGui.RadioButton("None##scoop", car.Scoop < 0)) car.Scoop = -1;
                for (int k = 0; k < scoops.Length; k++)
                {
                    ImGui.SameLine(0f, -1f);
                    if (ImGui.RadioButton($"{scoops[k].Size}##scoop", car.Scoop == k)) car.Scoop = k;
                }

                // Held here while it is dragged and applied on release: every change makes KSA
                // rebuild its rocks.
                _rockWeight ??= (float)(KsaWorld.RockWeight * 100.0);
                float weight = _rockWeight.Value;
                ImGui.SliderFloat("Rock weight", ref weight, 0.01f, 100f, "%.2f%%", ImGuiSliderFlags.Logarithmic);
                _rockWeight = weight;
                if (ImGui.IsItemDeactivatedAfterEdit()) KsaWorld.RockWeight = weight / 100.0;
                if (!ImGui.IsItemActive()) _rockWeight = (float)(KsaWorld.RockWeight * 100.0);
            }

            bool chasing = ChaseCamera.On;
            if (ImGui.Checkbox("Chase camera", ref chasing)) ChaseCamera.Chosen = chasing;

            bool moving = mover.Enabled;
            if (ImGui.Checkbox("Move craft with the mouse", ref moving)) mover.Enabled = moving;
            if (moving)
            {
                if (mover.Held is { } held) ImGui.TextDisabled($"holding {KsaWorld.DisplayName(held)}: click the ground");
                else if (mover.Hovered is { } over) ImGui.TextDisabled($"click to pick up {KsaWorld.DisplayName(over)}");
                else ImGui.TextDisabled("click a craft to pick it up");
            }

            // A course is laid where the car is, with the car on its start.
            ImGui.SeparatorText("Course");
            ImGui.SetNextItemWidth(170f);
            if (ImGui.BeginCombo("##course", _course ?? "Choose..."))
            {
                foreach (string name in CircuitLibrary.Names())
                {
                    if (ImGui.Selectable(name, selected: name == _course, ImGuiSelectableFlags.None, (float2?)null)) _course = name;
                }
                ImGui.EndCombo();
            }
            if (_course is { } chosen)
            {
                ImGui.SameLine(0f, -1f);
                if (ImGui.Button("Lay here", null)) Courses.Lay(chosen);
            }
            if (Roads.Any)
            {
                if (ImGui.Button("Take up roads", null)) Courses.TakeUp();
                ImGui.SameLine(0f, -1f);
                bool line = RacingLine.Enabled;
                if (ImGui.Checkbox("Racing line", ref line)) RacingLine.Set(line);
            }
            if (Courses.Message.Length > 0) ImGui.TextDisabled(Courses.Message);

            // A car to race, on the grid behind this one, which sets off when this one does.
            if (Roads.Any)
            {
                if (ImGui.Button("Add opponent", null)) Opponents.Add();
                if (Opponents.Count > 0)
                {
                    ImGui.SameLine(0f, -1f);
                    if (ImGui.Button("Remove opponents", null)) Opponents.Clear();
                }
                // How much slower than the best lap they know of the course they are, to set them to race a player.
                float gap = (float)(Opponents.Gap * 100.0);
                bool flatOut = Opponents.FlatOut;
                if (ImGui.Checkbox("Perfect opponents (full speed, no mistakes)", ref flatOut)) Opponents.FlatOut = flatOut;
                if (!flatOut) ImGui.SetNextItemWidth(170f);
                if (!flatOut && ImGui.SliderFloat("Opponents slower by", ref gap, 0f, (float)(Rival.MostGap * 100.0), "%.0f%%", ImGuiSliderFlags.None)) Opponents.Gap = gap / 100.0;
                if (Opponents.Message.Length > 0) ImGui.TextDisabled(Opponents.Message);
                if (Practising.Message.Length > 0) ImGui.TextDisabled(Practising.Message);

                // Speed, lap, place and a map, for when everything else is hidden.
                bool hud = RaceHud.Enabled;
                if (ImGui.Checkbox("Racing display with the interface hidden", ref hud)) RaceHud.Enabled = hud;
                if (hud)
                {
                    int laps = RaceHud.Laps;
                    ImGui.SetNextItemWidth(170f);
                    if (ImGui.SliderInt("Laps", ref laps, 1, 20)) RaceHud.Laps = laps;
                }
            }

            RaceOnline();

            // Drawing roads is unfinished, so only a developer's install offers it.
            if (Build.Developer)
            {
                bool editing = roads.Enabled;
                if (ImGui.Checkbox("Build roads", ref editing)) roads.Enabled = editing;
                // One of the two has the mouse's clicks on the world.
                if (roads.Enabled) mover.Enabled = false;
            }

            if (ImGui.Button("Unflip", null)) Buggies.Right(craft);
        }
        ImGui.End();
    }

    private static readonly ImInputString NetName = new(NetWire.NameChars * 4, ""u8);
    private static readonly ImInputString NetCode = new(96, ""u8);
    private static readonly ImInputString Shown = new(96, ""u8);
    private static bool _named;

    // Hosting a race, or joining one with a code: see Multiplayer.
    private static void RaceOnline()
    {
        ImGui.SeparatorText("Race online");
        if (!_named)
        {
            NetName.SetValue(Multiplayer.PlayerName);
            _named = true;
        }

        if (!Multiplayer.Active)
        {
            ImGui.SetNextItemWidth(170f);
            ImGui.InputText("Your name", NetName);
            Multiplayer.PlayerName = NetName.ToString().Trim();
            if (ImGui.Button("Host a race", null)) Multiplayer.Host();
            ImGui.SameLine(0f, -1f);
            if (ImGui.Button("Join a race", null)) Multiplayer.Ready();
            Recent(2);
            return;
        }

        if (Multiplayer.Hosting)
        {
            if (Multiplayer.HostCode is { } code)
            {
                CodeLine("Your code", code.Code);
                ImGui.TextDisabled(code.How);
            }
            else ImGui.TextDisabled("finding your code...");
            if (Multiplayer.RouterSays.Length > 0) ImGui.TextDisabled(Multiplayer.RouterSays);
            if (Multiplayer.LocalAddress is { } local) ImGui.TextDisabled($"on your own network: {local}");

            ImGui.SetNextItemWidth(170f);
            ImGui.InputTextWithHint("##letin", "a guest's code", NetCode);
            ImGui.SameLine(0f, -1f);
            if (ImGui.Button("Let in", null)) Multiplayer.LetIn(NetCode.ToString());

            if (Multiplayer.OnTheCourse)
            {
                int laps = RaceHud.Laps;
                ImGui.SetNextItemWidth(120f);
                if (ImGui.SliderInt("Race laps", ref laps, 1, 20)) RaceHud.Laps = laps;
                if (ImGui.Button("Start race", null)) Multiplayer.StartRace();
            }
        }
        else if (!Multiplayer.Joined)
        {
            ImGui.SetNextItemWidth(170f);
            ImGui.InputTextWithHint("##join", "the host's code", NetCode);
            ImGui.SameLine(0f, -1f);
            if (ImGui.Button("Join", null)) Multiplayer.Join(NetCode.ToString());
            if (Multiplayer.GuestCode is { } mine)
            {
                CodeLine("Your code", mine);
                ImGui.TextDisabled("if joining does not work, give the host this to let you in");
            }
        }

        if (Multiplayer.Trouble.Length > 0) ImGui.TextDisabled(Multiplayer.Trouble);
        if (Multiplayer.Countdown is { } wait) ImGui.Text($"Lights out in {wait:F1} s");
        foreach (NetSession.Racer racer in Multiplayer.Racers)
        {
            string car = BuggyProfile.All.FirstOrDefault(p => p.PartId == racer.Car)?.DisplayName ?? racer.Car;
            ImGui.TextDisabled($"{racer.Box + 1}. {racer.Name}{(racer.Id == Multiplayer.Me ? " (you)" : "")}, {car}");
        }
        Recent(3);
        if (ImGui.Button(Multiplayer.Hosting ? "Stop hosting" : "Leave", null)) Multiplayer.Stop();
    }

    private static void CodeLine(string label, string code)
    {
        Shown.SetValue(code);
        ImGui.SetNextItemWidth(120f);
        ImGui.InputText($"##{label}", Shown, ImGuiInputTextFlags.ReadOnly);
        ImGui.SameLine(0f, -1f);
        if (ImGui.SmallButton($"Copy##{label}")) ImGui.SetClipboardText(code);
        ImGui.SameLine(0f, -1f);
        ImGui.Text(label);
    }

    private static void Recent(int lines)
    {
        IReadOnlyList<string> said = Multiplayer.Recent;
        for (int i = Math.Max(0, said.Count - lines); i < said.Count; i++) ImGui.TextDisabled(said[i]);
    }

    private static void BeamButton(Buggies.Entry car, string label, BeamSetting setting)
    {
        if (ImGui.RadioButton(label, car.Beam == setting)) car.Beam = setting;
    }
}
