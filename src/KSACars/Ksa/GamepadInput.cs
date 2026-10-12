using System.Runtime.InteropServices;
using Brutal.GlfwApi;
using KSA;

namespace KSACars;

/// <summary>
/// The first gamepad SDL knows, or GLFW where there is no SDL beside the mod, read once a frame for the car being flown. KSA reads a gamepad for its
/// menus alone, so the sticks and triggers reach a car here and its buttons do what the keys do.
/// </summary>
internal static class GamepadInput
{
    private const int Joysticks = 16;

    // Where GLFW's state has its six axes: after fifteen buttons and a byte of padding.
    private const int AxesAt = 16;

    // One reference swapped whole, because the physics window reads it and the frame hook writes it.
    private sealed record Reading(PadDrive Drive);

    private static volatile Reading? _reading;
    private static PadButtons _buttons;
    private static int _joystick = -1;
    private static bool _sdlPad;
    private static bool _complained;

    /// <summary>What the gamepad asks of the car being flown, or null with no gamepad.</summary>
    public static PadDrive? For(Vehicle craft) =>
        _reading is { } reading && ReferenceEquals(craft, KsaWorld.ControlledVehicle) ? reading.Drive : null;

    /// <summary>Whether a gamepad is being read.</summary>
    public static bool Present => _reading is not null;

    /// <summary>How far the stick that turns the view is held over, right and back positive.</summary>
    public static (double X, double Y) Look => _reading is { } reading ? (reading.Drive.LookX, reading.Drive.LookY) : (0.0, 0.0);

    /// <summary>Reads the gamepad. From the frame hook: GLFW answers for a joystick on the main thread only.</summary>
    public static void Poll()
    {
        try
        {
            if (!TryRead(out PadState pad))
            {
                _reading = null;
                _buttons = PadButtons.None;
                return;
            }

            _reading = new Reading(Gamepad.Drive(pad));
            PadButtons pressed = Gamepad.Pressed(_buttons, pad.Buttons);
            _buttons = pad.Buttons;
            if (pressed != PadButtons.None) Press(pressed);
        }
        catch (Exception e)
        {
            _reading = null;
            if (_complained) return;
            _complained = true;
            Log.Error("reading the gamepad failed", e);
        }
    }

    private static bool TryRead(out PadState pad)
    {
        if (SdlPad.Ready)
        {
            bool read = SdlPad.TryRead(out pad);
            if (read != _sdlPad) Log.Info(read ? $"gamepad: {SdlPad.Name}" : "the gamepad is gone");
            _sdlPad = read;
            return read;
        }

        pad = default;
        GlfwGamepadState state = default;
        if (_joystick < 0 || !Glfw.GetGamepadState(_joystick, out state))
        {
            int found = -1;
            for (int i = 0; i < Joysticks && found < 0; i++)
            {
                if (Glfw.JoystickIsGamepad(i) && Glfw.GetGamepadState(i, out state)) found = i;
            }

            if (found != _joystick)
            {
                Log.Info(found < 0 ? "the gamepad is gone" : $"gamepad {found}: {Glfw.GetGamepadName(found)}");
            }
            _joystick = found;
            if (found < 0) return false;
        }

        ReadOnlySpan<byte> raw = MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(ref state, 1));
        ReadOnlySpan<float> axes = MemoryMarshal.Cast<byte, float>(raw[AxesAt..]);
        PadButtons buttons = PadButtons.None;
        for (int i = 0; i <= (int)GlfwGamepadButton.Last; i++)
        {
            if (raw[i] != 0) buttons |= (PadButtons)(1 << i);
        }

        pad = new PadState(axes[(int)GlfwGamepadAxis.LeftX], axes[(int)GlfwGamepadAxis.LeftY],
                           axes[(int)GlfwGamepadAxis.RightX], axes[(int)GlfwGamepadAxis.RightY],
                           axes[(int)GlfwGamepadAxis.LeftTrigger], axes[(int)GlfwGamepadAxis.RightTrigger], buttons);
        return true;
    }

    private static void Press(PadButtons pressed)
    {
        if (KsaWorld.ControlledVehicle is not { } craft || Buggies.Of(craft) is not { } car) return;

        if (pressed.HasFlag(PadButtons.X) && car.Drive.Profile.HasDownforce) car.Downforce = !car.Downforce;
        if (pressed.HasFlag(PadButtons.Y)) Buggies.Right(craft);
        if (pressed.HasFlag(PadButtons.DpadUp)) car.Beam = (BeamSetting)(((int)car.Beam + 1) % 3);
        if (pressed.HasFlag(PadButtons.Start) && car.Drive.Profile.HasRockets)
        {
            KsaWorld.TrySetEngineOn(craft, !(KsaWorld.EngineOn(craft) ?? false));
        }
    }
}
