using Brutal.ImGuiApi;
using Brutal.Numerics;

namespace KSACars;

/// <summary>
/// The start of an online race on every racer's screen: the seconds counted down large in the middle
/// and GO! as the lights go out, and for a while after the interface is hidden, how to have it back.
/// Drawn on the foreground of the ImGui frame, with the interface up and with it hidden.
/// </summary>
internal static class StartLights
{
    // How long GO! stays up, and how long the note about the interface stays up after the start, s.
    private const double GoSeconds = 2.0, HintSeconds = 10.0;

    private const float CountPx = 140f, HintPx = 24f;

    private static readonly ImColor8 Ink = new(255, 255, 255, 245), Go = new(80, 230, 110, 255), Shade = new(0, 0, 0, 170);

    /// <summary>Inside KSA's ImGui frame. Never throws.</summary>
    public static void Draw()
    {
        try
        {
            if (Multiplayer.StartSays() is not { } say) return;
            ImGuiViewportPtr screen = ImGui.GetMainViewport();
            ImDrawListPtr draw = ImGui.GetForegroundDrawList(screen);
            ImFontPtr font = ImGui.GetFont();
            float2 middle = new(screen.WorkPos.X + (screen.WorkSize.X / 2f), screen.WorkPos.Y + (screen.WorkSize.Y * 0.38f));

            void Centred(string text, float px, float y, ImColor8 colour)
            {
                float2 box = ImGui.CalcTextSize(text) * (px / ImGui.GetFontSize());
                float2 at = new(middle.X - (box.X / 2f), y - (box.Y / 2f));
                draw.AddText(font, px, new float2(at.X + (px / 30f), at.Y + (px / 30f)), Shade, text);
                draw.AddText(font, px, at, colour, text);
            }

            if (say.Count is { } count) Centred(count, CountPx, middle.Y, count == "GO!" ? Go : Ink);
            if (say.Hint) Centred("Press F2 to show the menu", HintPx, screen.WorkPos.Y + 40f, Ink);
        }
        catch (Exception e)
        {
            Log.Warn($"the start could not be drawn: {e.GetBaseException().Message}");
        }
    }

    /// <summary>What the start shows a time on: the count, or GO! just after the lights, and whether the note about the interface is up.</summary>
    public static (string? Count, bool Hint) Say(double untilLights, double sinceHidden)
    {
        string? count = untilLights > 0.0 ? Math.Ceiling(untilLights).ToString(System.Globalization.CultureInfo.InvariantCulture)
                      : untilLights > -GoSeconds ? "GO!" : null;
        return (count, sinceHidden >= 0.0 && sinceHidden < HintSeconds);
    }
}
