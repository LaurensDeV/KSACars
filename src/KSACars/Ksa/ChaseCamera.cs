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

    // What the player set with the stick, apart from the slope's share of what is written.
    private static double _tilt;
    private static double _slope;
    private static double _heading;
    private static double _turnRate;

    /// <summary>The panel's choice; with none made the view chases while a gamepad is being read.</summary>
    public static bool? Chosen { get; set; }

    public static bool On => Chosen ?? GamepadInput.Present;

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
            if (!On)
            {
                Release();
                view.Azimuth -= lookX * ChaseView.StickRadPerSec * dt;
                view.Elevation = ChaseView.Tilt(view.Elevation, lookY, dt);
                return;
            }

            if (!ReferenceEquals(view, _view))
            {
                Release();
                _view = view;
                _frameBefore = view.ReferenceFrame;
                _tilt = Math.Min(view.Elevation, ChaseView.StartElevationRad);
                (_slope, _turnRate, _heading) = (0.0, 0.0, double.NaN);
            }
            view.ReferenceFrame = CameraReferenceFrame.Surface;

            if (craft.GetEnu2Cce() is not { } enu2Cce) return;
            // The frame's axes turned as KSA's controller turns them, so the angle's sense is KSA's own.
            double3 east = double3.UnitX.Transform(enu2Cce);
            double3 up = double3.UnitZ.Transform(enu2Cce);
            double3 quarter = east.Transform(QuaternionEx.AngleAxis(Math.PI / 2.0, up));
            double3 ahead = (car.Part.Asmb2VehicleAsmb * new double3(0, 1, 0)).Transform(craft.Body2Cce);
            double along = Vec.Dot(ahead, east), across = Vec.Dot(ahead, quarter);
            _tilt = ChaseView.Tilt(_tilt, lookY, dt);
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
                view.Azimuth = ChaseView.Follow(view.Azimuth, heading + ChaseView.Lead(_turnRate), -lookX, dt);
                // Nose down is a view turned down with it, so a road falling away stays in the picture.
                _slope = ChaseView.Ease(_slope, Math.Asin(Math.Clamp(Vec.Dot(ahead, up), -1.0, 1.0)), ChaseView.FollowSeconds, dt);
            }
            view.Elevation = Math.Clamp(_tilt + _slope, -Math.PI / 2.0, Math.PI / 2.0);
        }
        catch (Exception e)
        {
            if (_complained) return;
            _complained = true;
            Log.Error("the chase camera failed", e);
        }
    }

    private static void Release()
    {
        if (_view is { } view) view.ReferenceFrame = _frameBefore;
        _view = null;
    }
}
