using Brutal.ImGuiApi;
using Brutal.Numerics;
using KSA;

namespace KSACars;

/// <summary>
/// KSA's orbit camera kept behind the car being flown, and turned by a gamepad's right stick. The stored
/// view of the followed craft is written, in KSA's surface frame so the horizon stays level, and the
/// frame the player had is put back when the chase ends.
/// </summary>
internal static class ChaseCamera
{
    private static OrbitView? _view;
    private static CameraReferenceFrame _frameBefore;
    private static long _atMs;
    private static bool _complained;

    // What the player set with the stick or the mouse, apart from the slope's share of what is written.
    private static double _tilt;
    private static double _slope;
    private static double _heading;
    private static double _turnRate;

    // The view's own heading, apart from the shake written with it.
    private static double _azimuth;

    // What speed does to the view, eased; and the field of view and the distance as the player has them,
    // with what was last written of each, so a change of the player's is told from one of this.
    private static double _speed, _clock;
    private static Camera? _camera;
    private static float _fovDeg, _fovWritten;
    private static double _distance, _distanceWritten;

    /// <summary>The mode chosen, from the panel or the gamepad's Back; with none the view chases at medium while a gamepad is being read.</summary>
    public static ChaseMode? Chosen { get; set; }

    public static ChaseMode Mode => Chosen ?? (GamepadInput.Present ? ChaseMode.Medium : ChaseMode.Free);

    public static bool On => Mode != ChaseMode.Free;

    // The mode whose distance was last put on the view, so a new one sets it once and the mouse wheel moves it after.
    private static ChaseMode? _applied;

    /// <summary>The next mode, as the gamepad's Back steps through them, said on the screen for a moment.</summary>
    public static void Cycle() => Choose(ChaseView.Next(Mode));

    /// <summary>A mode chosen, and said on the screen for a moment.</summary>
    public static void Choose(ChaseMode mode)
    {
        Chosen = mode;
        (_note, _noteUntil) = ($"Camera: {mode.ToString().ToLowerInvariant()}", Environment.TickCount64 + NoteMs);
    }

    /// <summary>A chase, for a race starting: the mode the player last chased in, or medium if they were free.</summary>
    public static void Chase()
    {
        if (Mode == ChaseMode.Free) Chosen = ChaseMode.Medium;
    }

    private const long NoteMs = 2000;
    private static string _note = "";
    private static long _noteUntil;

    /// <summary>The mode just chosen, low in the middle of the screen. Inside KSA's ImGui frame; never throws.</summary>
    public static void DrawNote()
    {
        try
        {
            if (Environment.TickCount64 > _noteUntil) return;
            ImGuiViewportPtr screen = ImGui.GetMainViewport();
            ImDrawListPtr draw = ImGui.GetForegroundDrawList(screen);
            const float px = 26f;
            float2 box = ImGui.CalcTextSize(_note) * (px / ImGui.GetFontSize());
            float2 at = new(screen.WorkPos.X + ((screen.WorkSize.X - box.X) / 2f), screen.WorkPos.Y + (screen.WorkSize.Y * 0.8f));
            draw.AddRectFilled(new float2(at.X - 10f, at.Y - 5f), new float2(at.X + box.X + 10f, at.Y + box.Y + 5f), new ImColor8(0, 0, 0, 160), 6f);
            draw.AddText(ImGui.GetFont(), px, at, new ImColor8(255, 255, 255, 245), _note);
        }
        catch (Exception e)
        {
            Log.Warn($"the camera's note could not be drawn: {e.GetBaseException().Message}");
        }
    }

    public static void Update()
    {
        try
        {
            long now = Environment.TickCount64;
            double dt = _atMs == 0 ? 0.0 : Math.Min((now - _atMs) / 1000.0, 0.1);
            _atMs = now;

            if (KsaWorld.ControlledVehicle is not { } craft || Buggies.Of(craft) is not { } car
                || KsaWorld.MainViewMode != CameraMode.Orbit
                || Program.GetMainCamera() is not { } camera || !ReferenceEquals(camera.Following, craft)
                || craft.OrbitView is not { } view)
            {
                Release();
                return;
            }

            (double lookX, double lookY) = GamepadInput.Look;
            lookY = Math.Clamp(lookY + KsaWorld.TiltKeys, -1.0, 1.0);
            if (!On)
            {
                Release();
                // Only what is asked for, and to KSA's own limits: written every frame, the chase's lower limit held
                // the player's own camera at it and the spring KSA turns the camera by threw it about on every drag.
                if (lookX != 0.0) view.Azimuth -= lookX * ChaseView.StickRadPerSec * dt;
                if (lookY != 0.0)
                {
                    view.Elevation = Math.Clamp(view.Elevation - (lookY * ChaseView.StickRadPerSec * dt), -Math.PI / 2.0, Math.PI / 2.0);
                }
                return;
            }

            if (!ReferenceEquals(view, _view))
            {
                Release();
                _view = view;
                _frameBefore = view.ReferenceFrame;
                _tilt = Math.Min(view.Elevation, ChaseView.StartElevationRad);
                (_slope, _turnRate, _heading, _speed, _azimuth) = (0.0, 0.0, double.NaN, 0.0, view.Azimuth);
                _camera = camera;
                _fovDeg = _fovWritten = FovDeg(camera);
                _distance = _distanceWritten = view.DistancePower;
                _applied = null;
            }
            if (_applied != Mode)
            {
                _applied = Mode;
                _distance = ChaseView.DistancePower(Mode);
            }
            view.ReferenceFrame = CameraReferenceFrame.Surface;
            // While a mouse button holds the view, KSA's own drag has it and the chase writes neither angle: what is
            // written is a target KSA springs its camera towards, and the spring is fed the target's own rate of
            // change, so an angle written over a drag threw the camera round at the speed of the mouse. Anything the
            // chase would hold the drag to -- its own tilt limits, its look-round -- is another way to write over it,
            // so it holds it to nothing and takes where the player leaves the view when the button comes up.
            bool dragging = KsaWorld.MouseHeld;

            if (craft.GetEnu2Cce() is not { } enu2Cce) return;
            // The frame's axes turned as KSA's controller turns them, so the angle's sense is KSA's own.
            double3 east = double3.UnitX.Transform(enu2Cce);
            double3 up = double3.UnitZ.Transform(enu2Cce);
            double3 quarter = east.Transform(QuaternionEx.AngleAxis(Math.PI / 2.0, up));
            double3 ahead = (car.Part.Asmb2VehicleAsmb * new double3(0, 1, 0)).Transform(craft.Body2Cce);
            double along = Vec.Dot(ahead, east), across = Vec.Dot(ahead, quarter);
            if (!dragging) _tilt = ChaseView.Tilt(_tilt, lookY, dt);
            // A car standing on its nose or its tail has no heading to be behind.
            if ((along * along) + (across * across) > 0.04)
            {
                double heading = Math.Atan2(across, along);
                if (dt > 0.0 && double.IsFinite(_heading))
                {
                    double rate = (ChaseView.Nearest(heading, _heading) - _heading) / dt;
                    _turnRate = ChaseView.Ease(_turnRate, rate, 0.25, dt);
                }
                _heading = heading;
                // Not while it is dragged: the view is the player's then, and this is what brings it back behind
                // the car once it is let go.
                if (!dragging) _azimuth = ChaseView.Follow(_azimuth, heading + ChaseView.Lead(_turnRate), -lookX, dt);
                // Nose down is a view turned down with it, so a road falling away stays in the picture.
                _slope = ChaseView.Ease(_slope, Math.Asin(Math.Clamp(Vec.Dot(ahead, up), -1.0, 1.0)), ChaseView.FollowSeconds, dt);
            }

            // Speed widens the view, lets the car run away from it and brings it down towards the road.
            _speed = ChaseView.Ease(_speed, ChaseView.SpeedShare(car.SpeedMs), 0.4, dt);
            if (!KsaWorld.IsPaused) _clock += dt;
            (double shakeAzimuth, double shakeElevation) = ChaseView.Shake(_speed, _clock);
            double drop = ChaseView.MostDropRad * _speed;
            if (dragging)
            {
                _azimuth = ChaseView.Nearest(view.Azimuth - shakeAzimuth, _azimuth);
                _tilt = Math.Clamp(view.Elevation - _slope - drop - shakeElevation, ChaseView.HighestDraggedRad, ChaseView.LowestDraggedRad);
                Dragged(view);
            }
            else
            {
                view.Azimuth = _azimuth + shakeAzimuth;
                view.Elevation = Math.Clamp(_tilt + _slope + drop + shakeElevation, -Math.PI / 2.0, Math.PI / 2.0);
            }

            if (Math.Abs(FovDeg(camera) - _fovWritten) > 0.05f) _fovDeg = FovDeg(camera);
            _fovWritten = Math.Min(_fovDeg + (float)(ChaseView.MostFovDeg * _speed), Camera.MAXIMUM_FOV);
            camera.SetFieldOfView(_fovWritten);

            double pull = 1.0 + (ChaseView.MostPull * _speed);
            if (Math.Abs(view.DistancePower - _distanceWritten) > 1e-6) _distance = view.DistancePower / pull;
            view.DistancePower = _distanceWritten = _distance * pull;
        }
        catch (Exception e)
        {
            if (_complained) return;
            _complained = true;
            Log.Error("the chase camera failed", e);
        }
    }

    private static long _saidAtMs;

    // Twice a second while the view is dragged: where the drag has put it and what the chase is holding, so a
    // camera that still fights a drag can be read out of the log instead of guessed at.
    private static void Dragged(OrbitView view)
    {
        long now = Environment.TickCount64;
        if (now - _saidAtMs < 500) return;
        _saidAtMs = now;
        const double deg = 180.0 / Math.PI;
        Log.Info($"cam: dragged to azimuth {view.Azimuth * deg:F1} deg, elevation {view.Elevation * deg:F1}; "
                 + $"the chase holds heading {_azimuth * deg:F1}, tilt {_tilt * deg:F1}, slope {_slope * deg:F1}");
    }

    private static float FovDeg(Camera camera) => (float)(camera.GetFieldOfView() * 180.0 / Math.PI);

    private static void Release()
    {
        if (_view is { } view)
        {
            view.ReferenceFrame = _frameBefore;
            view.DistancePower = _distance;
            _camera?.SetFieldOfView(_fovDeg);
        }
        (_view, _camera) = (null, null);
    }
}
