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

    public static void Draw()
    {
        if (KsaWorld.ControlledVehicle is not { } craft || Buggies.Of(craft) is not { } car) return;

        ImGuiViewportPtr screen = ImGui.GetMainViewport();
        float2 at = new(screen.WorkPos.X + 16f, screen.WorkPos.Y + (screen.WorkSize.Y * 0.5f));
        ImGui.SetNextWindowPos(in at, ImGuiCond.FirstUseEver, null);

        if (ImGui.Begin("Car", Flags))
        {
            ImGui.Text(car.Drive.Profile.DisplayName);
            ImGui.Text($"{Math.Abs(car.Drive.ForwardSpeed) * 3.6:F0} km/h, gear {car.Drive.Gear + 1}");
            if (car.Level < 0.5) ImGui.TextDisabled(car.Level < -0.5 ? "on its roof" : "on its side");

            ImGui.Text("Headlights");
            BeamButton(car, "Off", BeamSetting.Off);
            ImGui.SameLine(0f, -1f);
            BeamButton(car, "Low", BeamSetting.Low);
            ImGui.SameLine(0f, -1f);
            BeamButton(car, "High", BeamSetting.High);

            if (ImGui.Button("Unflip", null)) Buggies.Right(craft);
        }
        ImGui.End();
    }

    private static void BeamButton(Buggies.Entry car, string label, BeamSetting setting)
    {
        if (ImGui.RadioButton(label, car.Beam == setting)) car.Beam = setting;
    }
}
