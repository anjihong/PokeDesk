using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace DeskPokemon;

/// <summary>A deterministic, foot-aligned pixel animation with independently owned frame copies.</summary>
public sealed class EvolutionEffect : Control, IDisposable
{
    public const double DurationSeconds = 2.8;
    public const double RevealProgress = 2.2 / DurationSeconds;
    public static readonly StyledProperty<double> ProgressProperty =
        AvaloniaProperty.Register<EvolutionEffect, double>(nameof(Progress));
    public static readonly StyledProperty<bool> FlipHorizontalProperty =
        AvaloniaProperty.Register<EvolutionEffect, bool>(nameof(FlipHorizontal));
    private EffectFrame? _source;
    private EffectFrame? _target;

    static EvolutionEffect() => AffectsRender<EvolutionEffect>(ProgressProperty, FlipHorizontalProperty);

    public double Progress { get => GetValue(ProgressProperty); set => SetValue(ProgressProperty, value); }
    public bool FlipHorizontal { get => GetValue(FlipHorizontalProperty); set => SetValue(FlipHorizontalProperty, value); }
    public bool HasFrames => _source != null && _target != null;

    internal void SetFrames(SpriteAtlas source, int frame, double sourceScale, SpriteAtlas target, double targetScale)
    {
        Clear();
        EffectFrame? oldFrame = null;
        EffectFrame? newFrame = null;
        try
        {
            var sourceIndex = Math.Clamp(frame, 0, source.Frames.Length - 1);
            oldFrame = new EffectFrame(source.Frames[sourceIndex], source.FootAnchorFor(sourceIndex), sourceScale);
            newFrame = new EffectFrame(target.Frames[0], target.FootAnchorFor(0), targetScale);
            _source = oldFrame;
            _target = newFrame;
            Height = Math.Ceiling(Math.Max(source.Body.Height * sourceScale, target.Body.Height * targetScale)) + 16;
            Progress = 0;
            IsVisible = true;
            InvalidateVisual();
        }
        catch
        {
            oldFrame?.Dispose();
            newFrame?.Dispose();
            _source = _target = null;
            throw;
        }
    }

    internal static EvolutionEffectState StateAt(double progress)
    {
        var p = double.IsFinite(progress) ? Math.Clamp(progress, 0, 1) : 0;
        var seconds = p * DurationSeconds;
        if (seconds < .55)
        {
            var light = Smooth(seconds / .55);
            return new(1, light, 0, 0, light * .7, 1);
        }
        if (p < RevealProgress)
        {
            var morph = Smooth((seconds - .55) / 1.65);
            var pulse = 1 + .045 * Math.Pow(Math.Sin((seconds - .55) / 1.65 * Math.PI * 2), 2);
            return new(0, 1 - morph, morph, 0, .7 + .25 * Math.Sin(morph * Math.PI), pulse);
        }
        var reveal = Smooth((p - RevealProgress) / (1 - RevealProgress));
        return new(0, 0, 1 - reveal, reveal, .7 * (1 - reveal), 1);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (_source == null || _target == null) return;
        var state = StateAt(Progress);
        using var facing = context.PushTransform(FlipHorizontal
            ? new Matrix(-1, 0, 0, 1, Bounds.Width, 0) : Matrix.Identity);
        DrawSparkles(context, state.Glow);
        using var pulse = context.PushTransform(new Matrix(state.Pulse, 0, 0, state.Pulse,
            Bounds.Width / 2 * (1 - state.Pulse), Bounds.Height * (1 - state.Pulse)));
        var oldBounds = _source.BoundsIn(Bounds.Size);
        var newBounds = _target.BoundsIn(Bounds.Size);
        DrawGlow(context, _source.White, oldBounds, state.Glow * (state.SourceWhite + state.SourceColor * .5));
        DrawGlow(context, _target.White, newBounds, state.Glow * state.TargetWhite);
        Draw(context, _source.Color, oldBounds, state.SourceColor);
        Draw(context, _target.Color, newBounds, state.TargetColor);
        Draw(context, _source.White, oldBounds, state.SourceWhite);
        Draw(context, _target.White, newBounds, state.TargetWhite);
    }

    private static double Smooth(double value)
    {
        var t = Math.Clamp(value, 0, 1);
        return t * t * (3 - 2 * t);
    }

    private static void Draw(DrawingContext context, Bitmap bitmap, Rect destination, double opacity)
    {
        if (opacity <= 0) return;
        using var fade = context.PushOpacity(Math.Min(1, opacity));
        context.DrawImage(bitmap, destination);
    }

    private static void DrawGlow(DrawingContext context, Bitmap bitmap, Rect destination, double strength)
    {
        foreach (var distance in new[] { 2d, 5d })
        foreach (var offset in new[] { new Vector(-distance, 0), new Vector(distance, 0),
                     new Vector(0, -distance), new Vector(0, distance) })
            Draw(context, bitmap, destination.Translate(offset), strength * (distance == 2 ? .16 : .07));
    }

    private void DrawSparkles(DrawingContext context, double glow)
    {
        if (glow <= 0) return;
        var p = Math.Clamp(Progress, 0, 1);
        for (var index = 0; index < 8; index++)
        {
            var angle = index * Math.PI / 4 + .18 * p;
            var radius = 37 + 18 * (1 - p);
            var x = Math.Round(Bounds.Width / 2 + Math.Cos(angle) * radius);
            var y = Math.Round(Bounds.Height - 57 + Math.Sin(angle) * radius);
            var brush = new SolidColorBrush(Color.FromArgb((byte)(Math.Clamp(glow, 0, 1) * 200), 255, 244, 180));
            context.DrawRectangle(brush, null, new Rect(x - 3, y - 1, 6, 2));
            context.DrawRectangle(brush, null, new Rect(x - 1, y - 3, 2, 6));
        }
    }

    public void Clear()
    {
        IsVisible = false;
        _source?.Dispose();
        _target?.Dispose();
        _source = _target = null;
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

        public Rect BoundsIn(Size viewport) => new(
            viewport.Width / 2 - _foot.X * _scale,
            viewport.Height - _foot.Y * _scale,
            _frame.Width * _scale, _frame.Height * _scale);

        public void Dispose() { Color.Dispose(); White.Dispose(); }
    }
}

internal readonly record struct EvolutionEffectState(double SourceColor, double SourceWhite, double TargetWhite,
    double TargetColor, double Glow, double Pulse);
