using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace DeskPokemon;

/// <summary>The shared PokéRogue evolution timeline, with independently owned animated frame copies.</summary>
public sealed class EvolutionEffect : Control, IDisposable
{
    public const double IntroSeconds = .45;
    public const double HoldSeconds = .35;
    public const double WhiteFadeSeconds = .5;
    public const double RevealSeconds = .75;
    public static readonly double MorphSeconds = Enumerable.Range(0, 29)
        .Sum(index => .2 / (1 + index * .5) * (index == 28 ? 1 : 2));
    public static readonly double RevealStartSeconds = IntroSeconds + HoldSeconds + MorphSeconds;
    public static readonly double DurationSeconds = RevealStartSeconds + RevealSeconds;
    public static readonly double RevealProgress = RevealStartSeconds / DurationSeconds;
    public static readonly StyledProperty<double> ProgressProperty =
        AvaloniaProperty.Register<EvolutionEffect, double>(nameof(Progress));
    public static readonly StyledProperty<bool> FlipHorizontalProperty =
        AvaloniaProperty.Register<EvolutionEffect, bool>(nameof(FlipHorizontal));
    private EffectFrame[]? _source;
    private EffectFrame[]? _target;
    private Bitmap? _sparkle;
    private int _sourceStart;
    private double _particleScale;

    static EvolutionEffect() => AffectsRender<EvolutionEffect>(ProgressProperty, FlipHorizontalProperty);

    public double Progress { get => GetValue(ProgressProperty); set => SetValue(ProgressProperty, value); }
    public bool FlipHorizontal { get => GetValue(FlipHorizontalProperty); set => SetValue(FlipHorizontalProperty, value); }
    public bool HasFrames => _source != null && _target != null;

    internal void SetFrames(SpriteAtlas source, int frame, double sourceScale, SpriteAtlas target, double targetScale,
        Bitmap? sparkle = null)
    {
        Clear();
        try
        {
            _source = CopyFrames(source, sourceScale);
            _target = CopyFrames(target, targetScale);
            _sparkle = sparkle == null ? null : SpritePixels.CopyFrom(sparkle).ToBitmap();
            _sourceStart = Math.Clamp(frame, 0, _source.Length - 1);
            _particleScale = sourceScale;
            Height = Math.Ceiling(Math.Max(source.Body.Height * sourceScale, target.Body.Height * targetScale)) + 16;
            Progress = 0;
            IsVisible = true;
            InvalidateVisual();
        }
        catch { Clear(); throw; }
    }

    private static EffectFrame[] CopyFrames(SpriteAtlas atlas, double scale)
    {
        var copies = new List<EffectFrame>();
        try
        {
            for (var index = 0; index < atlas.Frames.Length; index++)
                copies.Add(new EffectFrame(atlas.Frames[index], atlas.FootAnchorFor(index), scale));
            return copies.ToArray();
        }
        catch
        {
            foreach (var copy in copies) copy.Dispose();
            throw;
        }
    }

    private static double SecondsAt(double progress) =>
        (double.IsFinite(progress) ? Math.Clamp(progress, 0, 1) : 0) * DurationSeconds;

    internal static EvolutionEffectState StateAt(double progress)
    {
        var seconds = SecondsAt(progress);
        if (seconds < IntroSeconds)
            return new(1, seconds / IntroSeconds, 0, 0, 1, .25);
        if (seconds < IntroSeconds + HoldSeconds)
            return new(0, 1, 0, 0, 1, .25);
        if (progress <= RevealProgress)
        {
            if (progress == RevealProgress)
                return new(0, 0, 1, 0, .25, 1);
            var elapsed = seconds - IntroSeconds - HoldSeconds;
            for (var cycle = 1d; cycle <= 15; cycle += .5)
            {
                var leg = .2 / cycle;
                var duration = leg * (cycle < 15 ? 2 : 1);
                if (elapsed < duration)
                {
                    var phase = elapsed / leg;
                    var amount = CubicEaseInOut(phase <= 1 ? phase : 2 - phase);
                    return new(0, 1, 1, 0, 1 - .75 * amount, .25 + .75 * amount);
                }
                elapsed -= duration;
            }
            // This exact endpoint is also the save boundary: no target color yet.
            return new(0, 0, 1, 0, .25, 1);
        }
        var reveal = Math.Clamp((seconds - RevealStartSeconds) / WhiteFadeSeconds, 0, 1);
        return new(0, 0, 1 - reveal, 1, .25, 1);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (_source == null || _target == null) return;
        var seconds = SecondsAt(Progress);
        var state = StateAt(Progress);
        // SpriteAtlas's GIF timeline and the normal pet timer both use 100ms frames.
        var tick = (int)Math.Floor(seconds * 10);
        var source = _source[(_sourceStart + tick) % _source.Length];
        var target = _target[tick % _target.Length];
        using var facing = context.PushTransform(FlipHorizontal
            ? new Matrix(-1, 0, 0, 1, Bounds.Width, 0) : Matrix.Identity);
        var oldBounds = source.BoundsIn(Bounds.Size, state.SourceScale);
        var newBounds = target.BoundsIn(Bounds.Size, state.TargetScale);
        Draw(context, source.Color, oldBounds, state.SourceColor);
        Draw(context, source.White, oldBounds, state.SourceWhite);
        Draw(context, target.Color, newBounds, state.TargetColor);
        Draw(context, target.White, newBounds, state.TargetWhite);
        DrawParticles(context, seconds, false);
        if (Progress > RevealProgress) DrawParticles(context, seconds - RevealStartSeconds, true);
    }

    private static double CubicEaseInOut(double value)
    {
        var t = Math.Clamp(value, 0, 1);
        return t < .5 ? 4 * t * t * t : 1 - Math.Pow(-2 * t + 2, 3) / 2;
    }

    private static void Draw(DrawingContext context, Bitmap bitmap, Rect destination, double opacity)
    {
        if (opacity <= 0) return;
        using var fade = context.PushOpacity(Math.Min(1, opacity));
        context.DrawImage(bitmap, destination);
    }

    internal static EvolutionParticle[] ParticlesAt(double seconds, bool inward, double sourceScale = 1)
    {
        const double width = 170, height = 132;
        var count = inward ? 20 : 12;
        var particles = new List<EvolutionParticle>();
        for (var index = 0; index < count; index++)
        {
            var duration = inward ? .620 : .550 + index * .025;
            if (seconds < 0 || seconds >= duration) continue;
            var p = seconds / duration;
            var size = (index % 3 == 0 ? 3 : 2) * sourceScale;
            var angle = 2 * Math.PI * index / count;
            var centerX = width / 2 - size / 2;
            var centerY = height * .58 - size / 2;
            var fromX = inward ? centerX + Math.Cos(angle) * width * .48 : centerX + Math.Cos(angle) * width * .24;
            var fromY = inward ? centerY + Math.Sin(angle) * height * .55 : height * .95;
            var toX = inward ? centerX : fromX + Math.Sin(angle * 2) * width * .12;
            var toY = inward ? centerY : -size;
            particles.Add(new(new Rect(fromX + (toX - fromX) * p, fromY + (toY - fromY) * p, size, size), 1 - p));
        }
        return particles.ToArray();
    }

    private void DrawParticles(DrawingContext context, double seconds, bool inward)
    {
        using var clip = context.PushClip(new Rect((Bounds.Width - 170) / 2, Bounds.Height - 132, 170, 132));
        foreach (var particle in ParticlesAt(seconds, inward, _particleScale))
        {
            var destination = particle.Bounds.Translate(new Vector((Bounds.Width - 170) / 2, Bounds.Height - 132));
            if (_sparkle != null) Draw(context, _sparkle, destination, particle.Opacity);
            else
            {
                using var fade = context.PushOpacity(particle.Opacity);
                context.DrawEllipse(Brushes.White, null, destination);
            }
        }
    }

    public void Clear()
    {
        IsVisible = false;
        if (_source != null) foreach (var frame in _source) frame.Dispose();
        if (_target != null) foreach (var frame in _target) frame.Dispose();
        _sparkle?.Dispose();
        _source = _target = null;
        _sparkle = null;
        Progress = 0;
        InvalidateVisual();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        Clear();
        base.OnDetachedFromVisualTree(e);
    }

    public void Dispose() => Clear();

    private sealed class EffectFrame : IDisposable
    {
        public Bitmap Color { get; }
        public Bitmap White { get; }
        private readonly SpriteFrame _frame;
        private readonly Point _foot;
        private readonly double _scale;

        public EffectFrame(SpriteFrame frame, Point canvasFoot, double scale)
        {
            _frame = frame;
            _foot = new Point(canvasFoot.X - frame.OffsetX, canvasFoot.Y - frame.OffsetY);
            _scale = scale;
            var pixels = SpritePixels.CopyFrom(frame.Bitmap);
            Color = pixels.ToBitmap();
            try
            {
                for (var index = 0; index < pixels.Pixels.Length; index += 4)
                    pixels.Pixels[index] = pixels.Pixels[index + 1] = pixels.Pixels[index + 2] = 255;
                White = pixels.ToBitmap();
            }
            catch { Color.Dispose(); throw; }
        }

        public Rect BoundsIn(Size viewport, double scale)
        {
            scale *= _scale;
            return new(viewport.Width / 2 - _foot.X * scale, viewport.Height - _foot.Y * scale,
                _frame.Width * scale, _frame.Height * scale);
        }

        public void Dispose() { Color.Dispose(); White.Dispose(); }
    }
}

internal readonly record struct EvolutionEffectState(double SourceColor, double SourceWhite, double TargetWhite,
    double TargetColor, double SourceScale, double TargetScale);
internal readonly record struct EvolutionParticle(Rect Bounds, double Opacity);
