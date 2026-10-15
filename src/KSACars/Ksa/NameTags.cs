using Brutal.ImGuiApi;
using Brutal.Numerics;
using KSA;

namespace KSACars;

/// <summary>
/// The name of each other racer in an online race, over their car: a point above the car put on
/// the screen by KSA's camera, so it stays on the car as it moves, smaller and fainter the further
/// off it is. Drawn behind the ImGui frame's windows, from the GUI pass while the interface is
/// up and from <see cref="RaceHudHook"/> while it is hidden.
/// </summary>
internal static class NameTags
{
    // How far over a car's centre of mass its name is, m.
    private const double OverM = 1.4;

    // No tag past this, m; from near to far it shrinks from the large size to the small and fades.
    private const double FarM = 1500.0, NearM = 30.0;
    private const float LargePx = 20f, SmallPx = 13f;

    private static readonly ImColor8 Shade = new(0, 0, 0, 160);

    /// <summary>Inside KSA's ImGui frame. Never throws.</summary>
    public static void Draw()
    {
        try
        {
            if (!Multiplayer.Together || KsaWorld.ControlledVehicle is not { IsDisposed: false } flown) return;
            if (Buggies.Of(flown) is not { PlaceValid: true } mine) return;

            // Behind every window, so the panel and KSA's own are never written over.
            ImDrawListPtr draw = ImGui.GetBackgroundDrawList(ImGui.GetMainViewport());
            ImFontPtr font = ImGui.GetFont();
            foreach ((string name, Vehicle craft) in Multiplayer.Others)
            {
                if (craft.IsDisposed || craft.Parent is not Celestial body || Buggies.Of(craft) is not { PlaceValid: true } car) continue;
                double away = Vec.Len(car.PlaceCcf - mine.PlaceCcf);
                if (away > FarM || !ReferenceEquals(body, flown.Parent)) continue;

                double3 over = car.PlaceCcf + (Vec.Unit(car.PlaceCcf) * OverM);
                if (!KsaWorld.TryProjectAhead(body.GetPositionEcl() + over.Transform(body.GetCcf2Cce()), out float2 at)) continue;

                float far = (float)Math.Clamp((away - NearM) / (FarM - NearM), 0.0, 1.0);
                float px = LargePx + ((SmallPx - LargePx) * far);
                byte alpha = (byte)(255f * (1f - (0.6f * far)));
                float2 box = ImGui.CalcTextSize(name) * (px / ImGui.GetFontSize());
                float2 text = new(at.X - (box.X / 2f), at.Y - box.Y - 6f);

                draw.AddRectFilled(new float2(text.X - 5f, text.Y - 2f), new float2(text.X + box.X + 5f, text.Y + box.Y + 2f), Shade, 4f);
                draw.AddTriangleFilled(new float2(at.X - 5f, at.Y - 6f), new float2(at.X + 5f, at.Y - 6f), at, Shade);
                draw.AddText(font, px, text, new ImColor8(255, 255, 255, alpha), name);
            }
        }
        catch (Exception e)
        {
            Log.Warn($"a name tag could not be drawn: {e.GetBaseException().Message}");
        }
    }
}
