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

        // No taller than the screen, and scrolled within when it would be: unfolded, Race online is most of a screen.
        float2 least = new(0f, 0f), most = new(float.MaxValue, screen.WorkSize.Y - (2f * Margin));
        ImGui.SetNextWindowSizeConstraints(in least, in most);
        if (ImGui.Begin("Fast & Purrious###Car", ref Visible, Flags))
        {
            KeepOnScreen(screen);
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

            // A guest races in the host's world, which only the host changes.
            bool guest = Multiplayer.InSomeonesWorld;

            // A course is laid where the car is, with the car on its start.
            ImGui.SeparatorText("Course");
            if (guest) ImGui.TextDisabled("You are a guest: the host's course is used.");
            ImGui.BeginDisabled(guest);
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
            }
            ImGui.EndDisabled();
            if (Roads.Any)
            {
                bool line = RacingLine.Enabled;
                if (ImGui.Checkbox("Racing line", ref line)) RacingLine.Set(line);
            }
            if (Courses.Message.Length > 0) ImGui.TextDisabled(Courses.Message);

            // A car to race, on the grid behind this one, which sets off when this one does.
            if (Roads.Any)
            {
                ImGui.BeginDisabled(guest);
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
                ImGui.EndDisabled();
                if (Opponents.Message.Length > 0) ImGui.TextDisabled(Opponents.Message);
                if (Practising.Message.Length > 0) ImGui.TextDisabled(Practising.Message);

                // Speed, lap, place and a map, for when everything else is hidden.
                bool hud = RaceHud.Enabled;
                if (ImGui.Checkbox("Racing display with the interface hidden", ref hud)) RaceHud.Enabled = hud;
                if (hud && !guest)
                {
                    int laps = RaceHud.Laps;
                    ImGui.SetNextItemWidth(170f);
                    if (ImGui.SliderInt("Laps", ref laps, 1, 20)) RaceHud.Laps = laps;
                }
            }

            RaceOnline();

            // Drawing roads is unfinished, so only a developer's install offers it, and never to a guest in another's world.
            if (Build.Developer && !guest)
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

    // Hosting a race, or joining one with a code, step by step: see Multiplayer. Folded until it is opened.
    private static void RaceOnline()
    {
        if (!_named)
        {
            NetName.SetValue(Multiplayer.PlayerName);
            _named = true;
        }
        if (OpenOnline)
        {
            ImGui.SetNextItemOpen(true, ImGuiCond.Always);
            OpenOnline = false;
        }
        if (!ImGui.CollapsingHeader($"Race online{Folded()}###online", ImGuiTreeNodeFlags.None)) return;

        ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + WrapWidth);
        if (!Multiplayer.Active) Choose();
        else if (Multiplayer.Hosting) Hosting();
        else Guest();

        if (Multiplayer.Countdown is { } wait) ImGui.Text($"LIGHTS OUT IN {wait:F0}");
        if (Multiplayer.Together) Note("Time warp and pause are off.");
        if (Multiplayer.Behind is { } behind && Math.Abs(behind) > 0.5)
        {
            Note($"Your game is {Math.Abs(behind):F1} s {(behind > 0 ? "behind" : "ahead of")} the host's.");
        }
        if (Multiplayer.Active)
        {
            Recent(3);
            string leave = Multiplayer.Hosting ? "Stop hosting" : Multiplayer.InSomeonesWorld ? "Leave the race" : "Close";
            if (ImGui.Button(leave, null)) Multiplayer.Stop();
            if (Multiplayer.InSomeonesWorld) Note("You keep the host's world and your car.");
        }
        ImGui.PopTextWrapPos();
    }

    private const float WrapWidth = 330f;

    /// <summary>Opens the Race online section on the next frame, as a click on it would: for the bridge.</summary>
    public static bool OpenOnline { get; set; }

    // What the header says while it is folded, so the race can be followed without opening it.
    private static string Folded()
    {
        if (!Multiplayer.Active) return "";
        int others = Math.Max(0, Multiplayer.Racers.Count() - 1);
        string who = others == 1 ? "1 other racer" : $"{others} other racers";
        if (Multiplayer.Hosting) return $": hosting, {who}";
        return Multiplayer.InSomeonesWorld ? $": guest, {who}" : ": joining";
    }

    private static void Choose()
    {
        ImGui.TextWrapped("Race friends on their own computers, in the host's world.");
        ImGui.SetNextItemWidth(170f);
        ImGui.InputText("Your name", NetName);
        Multiplayer.PlayerName = NetName.ToString().Trim();
        if (ImGui.Button("Host a race", null)) Multiplayer.Host();
        ImGui.SameLine(0f, -1f);
        if (ImGui.Button("Join a race", null)) Multiplayer.Ready();
        Note("Joining replaces your open world with the host's: save first.");
        Recent(2);
    }

    private static void Hosting()
    {
        bool laid = Roads.Any;
        bool someone = Multiplayer.Racers.Count() > 1;

        Step(1, laid, laid ? "Course laid." : "Lay a course, under Course above.");
        Step(2, someone, "Send your code to your friends:");
        if (Multiplayer.HostCode is { } code) CodeLine("##host", code.Code);
        else Note("    finding your code...");
        Streaming();
        Step(3, someone, "Wait for them to join.");
        Note("Someone can't get in? Paste their code:");
        ImGui.SetNextItemWidth(150f);
        ImGui.InputTextWithHint("##letin", "their code", NetCode);
        ImGui.SameLine(0f, -1f);
        if (ImGui.Button("Let in", null)) Multiplayer.LetIn(NetCode.ToString());
        Step(4, false, "Start the race:");
        ImGui.BeginDisabled(!laid || !someone);
        int laps = RaceHud.Laps;
        ImGui.SetNextItemWidth(120f);
        if (ImGui.SliderInt("Laps##online", ref laps, 1, 20)) RaceHud.Laps = laps;
        ImGui.SameLine(0f, -1f);
        if (ImGui.Button("Start race", null)) Multiplayer.StartRace();
        ImGui.EndDisabled();
    }

    private static void Guest()
    {
        if (Multiplayer.InSomeonesWorld)
        {
            if (Multiplayer.WorldSays.Length > 0) ImGui.Text(Multiplayer.WorldSays);
            else if (Multiplayer.Countdown is null) ImGui.TextWrapped("You're in. Wait for the host to start the race.");
            return;
        }

        if (Multiplayer.Trouble.Length > 0) ImGui.TextWrapped($"Out of the race: {Multiplayer.Trouble}.");
        Step(1, false, "Paste the host's code:");
        ImGui.SetNextItemWidth(150f);
        ImGui.InputTextWithHint("##join", "the host's code", NetCode);
        ImGui.SameLine(0f, -1f);
        if (ImGui.Button("Join", null)) Multiplayer.Join(NetCode.ToString());
        Step(2, false, "Wait: you're put in the host's world, on their grid.");
        Note("Can't get in? Send the host your code:");
        if (Multiplayer.GuestCode is { } mine) CodeLine("##guest", mine);
        else Note("    finding your code...");
        Streaming();
    }

    private static readonly float4 Warning = new(1.0f, 0.35f, 0.35f, 1.0f);

    // In red, under every code: a code is the player's internet address, and a stream shows it to everyone.
    private static void Streaming()
    {
        ImGui.PushStyleColor(ImGuiCol.Text, in Warning);
        ImGui.TextWrapped("Streaming? Keep codes off screen: they contain your internet address.");
        ImGui.PopStyleColor();
    }

    // Greyed and wrapped: ImGui's own greyed text does not wrap, and this window grows to fit its widest line.
    private static void Note(string text)
    {
        ImGui.BeginDisabled(true);
        ImGui.TextWrapped(text);
        ImGui.EndDisabled();
    }

    // One step of the instructions: the next one to do stands out, and one done is greyed.
    private static void Step(int number, bool done, string text)
    {
        if (done) Note($"{number}. {text}");
        else ImGui.TextWrapped($"{number}. {text}");
    }

    private static void CodeLine(string id, string code)
    {
        Shown.SetValue(code);
        ImGui.SetNextItemWidth(150f);
        ImGui.InputText(id, Shown, ImGuiInputTextFlags.ReadOnly);
        ImGui.SameLine(0f, -1f);
        if (ImGui.Button($"Copy{id}", null)) ImGui.SetClipboardText(code);
    }

    private static void Recent(int lines)
    {
        IReadOnlyList<string> said = Multiplayer.Recent;
        for (int i = Math.Max(0, said.Count - lines); i < said.Count; i++) Note(said[i]);
    }

    private const float Margin = 16f;

    // Moved up when its bottom would be off the screen: it opens halfway down, and grows when a section is unfolded.
    private static void KeepOnScreen(ImGuiViewportPtr screen)
    {
        float2 at = ImGui.GetWindowPos(), size = ImGui.GetWindowSize();
        float bottom = screen.WorkPos.Y + screen.WorkSize.Y - Margin;
        if (at.Y + size.Y <= bottom) return;
        float2 to = new(at.X, Math.Max(screen.WorkPos.Y + Margin, bottom - size.Y));
        ImGui.SetWindowPos(in to, ImGuiCond.Always);
    }

    private static void BeamButton(Buggies.Entry car, string label, BeamSetting setting)
    {
        if (ImGui.RadioButton(label, car.Beam == setting)) car.Beam = setting;
    }
}
