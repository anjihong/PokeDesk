using System.Collections.Concurrent;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace DeskPokemon;

/// <summary>Draws the bundled 2px-grid artwork at one logical pixel per grid cell.
/// Nine-slice borders keep their pixel thickness when a panel's content grows.</summary>
public sealed class PixelSurface : Decorator
{
    public static readonly StyledProperty<string?> AssetProperty =
        AvaloniaProperty.Register<PixelSurface, string?>(nameof(Asset));
    public static readonly StyledProperty<Thickness> SliceProperty =
        AvaloniaProperty.Register<PixelSurface, Thickness>(nameof(Slice), new Thickness(4));
    private static readonly ConcurrentDictionary<string, Bitmap> Images = new();

    static PixelSurface() => AffectsRender<PixelSurface>(AssetProperty, SliceProperty);

    public string? Asset { get => GetValue(AssetProperty); set => SetValue(AssetProperty, value); }
    public Thickness Slice { get => GetValue(SliceProperty); set => SetValue(SliceProperty, value); }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (string.IsNullOrEmpty(Asset) || Bounds.Width <= 0 || Bounds.Height <= 0) return;
        var bitmap = Images.GetOrAdd(Asset, static name =>
        {
            using var stream = AssetLoader.Open(new Uri($"avares://DeskPokemon/Assets/PixelUI/{name}.png"));
            return new Bitmap(stream);
        });
        var width = bitmap.Size.Width;
        var height = bitmap.Size.Height;
        var left = Math.Min(Slice.Left, Math.Min(width / 4, Bounds.Width / 2));
        var right = Math.Min(Slice.Right, Math.Min(width / 4, Bounds.Width / 2));
        var top = Math.Min(Slice.Top, Math.Min(height / 4, Bounds.Height / 2));
        var bottom = Math.Min(Slice.Bottom, Math.Min(height / 4, Bounds.Height / 2));
        double[] sourceX = [0, left * 2, width - right * 2, width];
        double[] sourceY = [0, top * 2, height - bottom * 2, height];
        double[] targetX = [0, left, Bounds.Width - right, Bounds.Width];
        double[] targetY = [0, top, Bounds.Height - bottom, Bounds.Height];
        for (var y = 0; y < 3; y++)
        for (var x = 0; x < 3; x++)
        {
            var source = new Rect(sourceX[x], sourceY[y], sourceX[x + 1] - sourceX[x], sourceY[y + 1] - sourceY[y]);
            var target = new Rect(targetX[x], targetY[y], targetX[x + 1] - targetX[x], targetY[y + 1] - targetY[y]);
            if (source.Width > 0 && source.Height > 0 && target.Width > 0 && target.Height > 0)
                context.DrawImage(bitmap, source, target);
        }
    }
}
