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

            bool moving = mover.Enabled;
            if (ImGui.Checkbox("Move craft with the mouse", ref moving)) mover.Enabled = moving;
            if (moving)
            {
                if (mover.Held is { } held) ImGui.TextDisabled($"holding {KsaWorld.DisplayName(held)}: click the ground");
                else if (mover.Hovered is { } over) ImGui.TextDisabled($"click to pick up {KsaWorld.DisplayName(over)}");
                else ImGui.TextDisabled("click a craft to pick it up");
            }

            // Roads are unfinished, so only a developer's install offers them.
            if (Build.Developer)
            {
                bool editing = roads.Enabled;
                if (ImGui.Checkbox("Build roads", ref editing)) roads.Enabled = editing;
                // One of the two has the mouse's clicks on the world.
                if (roads.Enabled) mover.Enabled = false;

                bool line = RacingLine.Enabled;
                if (ImGui.Checkbox("Racing line", ref line)) RacingLine.Set(line);
            }

            if (ImGui.Button("Unflip", null)) Buggies.Right(craft);
        }
        ImGui.End();
    }

    private static void BeamButton(Buggies.Entry car, string label, BeamSetting setting)
    {
        if (ImGui.RadioButton(label, car.Beam == setting)) car.Beam = setting;
    }
}
