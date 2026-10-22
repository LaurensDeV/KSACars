using System.Runtime.InteropServices;

namespace KSACars;

/// <summary>
/// A gamepad read through SDL, where there is a <c>Native/SDL3</c> beside the mod: it has drivers of its
/// own for pads GLFW cannot see, a Steam Controller among them, which is a keyboard and a mouse to
/// anything else unless Steam is running. Absent or failing, <see cref="GamepadInput"/> falls back on GLFW.
/// </summary>
internal static class SdlPad
{
    private const string Lib = "SDL3";
    private const uint InitGamepad = 0x2000;

    // SDL's numbers for its buttons, which are not GLFW's.
    private static readonly (int Button, PadButtons Flag)[] Buttons =
    [
        (0, PadButtons.A), (1, PadButtons.B), (2, PadButtons.X), (3, PadButtons.Y), (4, PadButtons.Back),
        (6, PadButtons.Start), (9, PadButtons.LeftBumper), (10, PadButtons.RightBumper), (11, PadButtons.DpadUp),
        (13, PadButtons.DpadLeft), (14, PadButtons.DpadRight),
    ];

    private static nint _lib;
    private static nint _pad;
    private static bool _tried;

    /// <summary>Whether SDL is loaded and reading pads, tried once.</summary>
    public static bool Ready
    {
        get
        {
            if (!_tried)
            {
                _tried = true;
                try { _lib = Start(); }
                catch (Exception e) { Log.Warn($"SDL could not be started, so gamepads are GLFW's: {e.Message}"); }
            }
            return _lib != 0;
        }
    }

    /// <summary>The name of the pad being read, or null with none.</summary>
    public static string? Name => _pad == 0 ? null : Marshal.PtrToStringUTF8(SDL_GetGamepadName(_pad));

    private static nint Start()
    {
        string? dir = Path.GetDirectoryName(typeof(SdlPad).Assembly.Location);
        if (dir is null) return 0;
        nint lib = 0;
        foreach (string name in (string[])["SDL3.dll", "libSDL3.so.0"])
        {
            if (NativeLibrary.TryLoad(Path.Combine(dir, "Native", name), out lib)) break;
        }
        if (lib == 0)
        {
            Log.Info("no SDL beside the mod, so gamepads are GLFW's");
            return 0;
        }

        NativeLibrary.SetDllImportResolver(typeof(SdlPad).Assembly, (wanted, _, _) => wanted == Lib ? lib : 0);
        // The game's window is GLFW's, so to SDL this process never has the focus.
        SDL_SetHint("SDL_JOYSTICK_ALLOW_BACKGROUND_EVENTS", "1");
        SDL_SetHint("SDL_JOYSTICK_HIDAPI_STEAM", "1");
        SDL_SetMainReady();
        if (!SDL_Init(InitGamepad)) throw new InvalidOperationException(Marshal.PtrToStringUTF8(SDL_GetError()));
        return lib;
    }

    /// <summary>The first pad SDL has, as it is now. On the thread SDL was started on.</summary>
    public static bool TryRead(out PadState pad)
    {
        pad = default;
        SDL_PumpEvents();
        if (_pad != 0 && !SDL_GamepadConnected(_pad))
        {
            SDL_CloseGamepad(_pad);
            _pad = 0;
        }
        if (_pad == 0)
        {
            nint ids = SDL_GetGamepads(out int count);
            if (ids != 0)
            {
                if (count > 0) _pad = SDL_OpenGamepad((uint)Marshal.ReadInt32(ids));
                SDL_free(ids);
            }
            if (_pad == 0) return false;
        }

        PadButtons buttons = PadButtons.None;
        foreach ((int button, PadButtons flag) in Buttons)
        {
            if (SDL_GetGamepadButton(_pad, button)) buttons |= flag;
        }

        pad = new PadState(Stick(0), Stick(1), Stick(2), Stick(3), Trigger(4), Trigger(5), buttons);
        return true;
    }

    private static double Stick(int axis) => Math.Max(SDL_GetGamepadAxis(_pad, axis) / 32767.0, -1.0);

    // SDL has a trigger from 0 to full, where a PadState has GLFW's -1 to 1.
    private static double Trigger(int axis) => (SDL_GetGamepadAxis(_pad, axis) / 32767.0 * 2.0) - 1.0;

    [DllImport(Lib)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool SDL_Init(uint flags);

    [DllImport(Lib)]
    private static extern void SDL_SetMainReady();

    [DllImport(Lib)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool SDL_SetHint([MarshalAs(UnmanagedType.LPUTF8Str)] string name,
                                           [MarshalAs(UnmanagedType.LPUTF8Str)] string value);

    [DllImport(Lib)]
    private static extern nint SDL_GetError();

    [DllImport(Lib)]
    private static extern void SDL_PumpEvents();

    [DllImport(Lib)]
    private static extern nint SDL_GetGamepads(out int count);

    [DllImport(Lib)]
    private static extern void SDL_free(nint memory);

    [DllImport(Lib)]
    private static extern nint SDL_OpenGamepad(uint id);

    [DllImport(Lib)]
    private static extern void SDL_CloseGamepad(nint pad);

    [DllImport(Lib)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool SDL_GamepadConnected(nint pad);

    [DllImport(Lib)]
    private static extern nint SDL_GetGamepadName(nint pad);

    [DllImport(Lib)]
    private static extern short SDL_GetGamepadAxis(nint pad, int axis);

    [DllImport(Lib)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool SDL_GetGamepadButton(nint pad, int button);
}
