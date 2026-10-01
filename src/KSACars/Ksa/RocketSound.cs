using Brutal.Numerics;
using KSA;

namespace KSACars;

/// <summary>
/// The roar of a car's rockets: KSA's own engine sound, started and stopped with the throttle and fed
/// the parameters KSA feeds an engine's each frame. One channel a car, at its centre.
/// </summary>
internal sealed class RocketSound
{
    private const string SoundId = "DefaultEngineSoundBehavior";

    private static readonly KeyHash Throttle = KeyHash.Make("Throttle".AsSpan());
    private static readonly KeyHash Distance = KeyHash.Make("Distance".AsSpan());
    private static readonly KeyHash Pressure = KeyHash.Make("Pressure".AsSpan());
    private static readonly KeyHash Iva = KeyHash.Make("Iva".AsSpan());

    private readonly Dictionary<Vehicle, IChannel> _burning = [];
    private readonly List<Vehicle> _out = [];
    private bool _complained;

    public void Update(Buggies buggies)
    {
        foreach (Buggies.Entry e in buggies.All)
        {
            try
            {
                Update(e);
            }
            catch (Exception ex)
            {
                if (_complained) continue;
                _complained = true;
                Log.Warn($"rocket sound: {ex.Message}");
            }
        }

        foreach (Vehicle v in _burning.Keys)
        {
            if (!buggies.All.Any(b => ReferenceEquals(b.Craft, v))) _out.Add(v);
        }
        foreach (Vehicle v in _out) Stop(v);
        _out.Clear();
    }

    public void StopAll()
    {
        foreach (IChannel channel in _burning.Values) SoundChannels.Cut(channel);
        _burning.Clear();
    }

    private void Update(Buggies.Entry e)
    {
        if (e.Rockets <= 0.0 || !KsaWorld.IsAlive(e.Craft))
        {
            Stop(e.Craft);
            return;
        }

        SpatialAudio spatial = new(e.Craft, double3.Zero);
        bool fresh = !_burning.TryGetValue(e.Craft, out IChannel? channel) || !channel.IsPlaying();
        if (fresh)
        {
            if (channel is not null) SoundChannels.Cut(channel);
            _burning.Remove(e.Craft);

            // Started paused, as KSA starts an engine's: the parameters are set before a sample is heard.
            GameAudio.PlaySound(new SoundEvent { SoundId = SoundId }, spatial, out channel, e.Craft, 1f, startPaused: true);
            if (channel is null) return;
            _burning[e.Craft] = channel;
        }

        channel!.SetSpatialAudio(spatial);
        channel.SetParameter(Throttle, (float)e.Rockets);
        channel.SetParameter(Distance, (float)spatial.Distance());
        channel.SetParameter(Pressure, (float)spatial.AtmosphericPressure);
        channel.SetParameter(Iva, ViewportRegistry.MainViewport.IvaAudio);
        if (fresh) channel.SetPaused(isPaused: false);
    }

    private void Stop(Vehicle craft)
    {
        if (_burning.Remove(craft, out IChannel? channel)) SoundChannels.Cut(channel);
    }
}
