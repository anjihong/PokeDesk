using System.Diagnostics;
using Avalonia.Threading;

namespace DeskPokemon;

internal enum Ease { Linear, EggWindUp, EggPop, EggSwing, EggSpring, OutCubic }
internal readonly record struct Frame(double Time, double Value, Ease Easing = Ease.Linear);
internal sealed record Track(Action<double> Set, double Baseline, Frame[] Frames);

/// <summary>One UI-thread clock for the original per-property animation tracks on every OS.</summary>
internal sealed class Timeline : IDisposable
{
    private readonly bool _repeat;
    private readonly Track[] _tracks;
    private readonly Action? _completed;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1d / 60) };
    private readonly Stopwatch _clock = new();
    private readonly double _duration;

    public Timeline(bool repeat, Track[] tracks, Action? completed = null)
    {
        _repeat = repeat;
        _tracks = tracks;
        _completed = completed;
        _duration = tracks.Max(t => t.Frames[^1].Time);
        _timer.Tick += Tick;
    }

    public void Play()
    {
        _clock.Restart();
        Apply(0);
        _timer.Start();
    }

    public void Stop()
    {
        _timer.Stop();
        _clock.Stop();
        foreach (var track in _tracks) track.Set(track.Baseline);
    }

    private void Tick(object? sender, EventArgs e)
    {
        var time = _clock.Elapsed.TotalSeconds;
        Apply(_repeat ? time % _duration : Math.Min(time, _duration));
        if (_repeat || time < _duration) return;
        _timer.Stop();
        _clock.Stop();
        _completed?.Invoke();
    }

    private void Apply(double time)
    {
        foreach (var track in _tracks) track.Set(Sample(track.Frames, time));
    }

    internal static double Sample(Frame[] frames, double time)
    {
        var previous = frames[0];
        if (time <= previous.Time) return previous.Value;
        foreach (var next in frames.Skip(1))
        {
            if (time <= next.Time)
            {
                var amount = (time - previous.Time) / (next.Time - previous.Time);
                return previous.Value + (next.Value - previous.Value) * Interpolate(amount, next.Easing);
            }
            previous = next;
        }
        return previous.Value;
    }

    private static double Interpolate(double t, Ease ease) => ease switch
    {
        Ease.EggWindUp => t < .5 ? 4 * t * t * t : 1 - Math.Pow(-2 * t + 2, 3) / 2,
        Ease.EggPop => 1 - (1 - t) * (1 - t),
        Ease.EggSwing => (1 - Math.Cos(Math.PI * t)) / 2,
        // WPF ElasticEase EaseOut, Oscillations=2, Springiness=4.
        Ease.EggSpring => 1 - ((Math.Exp(4 * (1 - t)) - 1) / (Math.Exp(4) - 1))
            * Math.Sin((2 * Math.PI * 2 + Math.PI / 2) * (1 - t)),
        Ease.OutCubic => 1 - Math.Pow(1 - t, 3),
        _ => t,
    };

    public void Dispose()
    {
        _timer.Stop();
        _timer.Tick -= Tick;
        _clock.Stop();
    }
}
