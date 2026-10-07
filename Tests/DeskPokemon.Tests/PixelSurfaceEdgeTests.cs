using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Xunit;

namespace DeskPokemon.Tests;

public class PixelSurfaceEdgeTests
{
    [AvaloniaTheory]
    [InlineData(79)]
    [InlineData(150)]
    public void DetailPanelKeepsAllFourOriginalCornersWhenItsContentGrows(int height)
    {
        const int width = 325;
        const int scale = 2;
        const int cornerPixels = 16;
        var panel = new PixelSurface
        {
            Asset = "detail_panel",
            Slice = new Thickness(8),
            Width = width,
            Height = height,
        };
        // Pixel surfaces must retain sharp edges even outside a styled Window.
        Assert.True(panel.UseLayoutRounding);
        Assert.Equal(BitmapInterpolationMode.None, RenderOptions.GetBitmapInterpolationMode(panel));
        panel.Measure(new Size(width, height));
        panel.Arrange(new Rect(0, 0, width, height));
        Assert.Equal(new Size(width, height), panel.Bounds.Size);

        using var sourceStream = AssetLoader.Open(new Uri("avares://DeskPokemon/Assets/PixelUI/detail_panel.png"));
        using var sourceBitmap = new Bitmap(sourceStream);
        var source = SpritePixels.CopyFrom(sourceBitmap);
        Assert.Equal(650, source.Width);
        Assert.Equal(158, source.Height);
        using var renderedBitmap = new RenderTargetBitmap(
            new PixelSize(width * scale, height * scale), new Vector(96 * scale, 96 * scale));
        renderedBitmap.Render(panel);
        var rendered = SpritePixels.CopyFrom(renderedBitmap);

        foreach (var bottom in new[] { false, true })
        foreach (var right in new[] { false, true })
        {
            var expected = CornerBytes(source, right, bottom, cornerPixels);
            var actual = CornerBytes(rendered, right, bottom, cornerPixels);
            Assert.Equal(expected, actual);
        }
    }

    [AvaloniaTheory]
    [InlineData("tab_selected")]
    [InlineData("tab_box")]
    [InlineData("tab_settings")]
    public void ResizedMenuTabsKeepTheirSteppedShouldersAndFlatBottomCorners(string asset)
    {
        var tab = new PixelSurface
        {
            Asset = asset, Slice = new Thickness(12, 14, 12, 3), Width = 116, Height = 41,
        };
        tab.Measure(new Size(116, 41));
        tab.Arrange(new Rect(0, 0, 116, 41));
        using var stream = AssetLoader.Open(new Uri($"avares://DeskPokemon/Assets/PixelUI/{asset}.png"));
        using var original = new Bitmap(stream);
        using var bitmap = new RenderTargetBitmap(new PixelSize(232, 82), new Vector(192, 192));
        bitmap.Render(tab);
        var source = SpritePixels.CopyFrom(original);
        var rendered = SpritePixels.CopyFrom(bitmap);
        foreach (var bottom in new[] { false, true })
        foreach (var right in new[] { false, true })
            Assert.Equal(CornerBytes(source, right, bottom, 24, bottom ? 6 : 28),
                CornerBytes(rendered, right, bottom, 24, bottom ? 6 : 28));
    }

    private static byte[] CornerBytes(SpritePixels image, bool right, bool bottom, int size, int? cornerHeight = null)
    {
        var height = cornerHeight ?? size;
        var left = right ? image.Width - size : 0;
        var top = bottom ? image.Height - height : 0;
        var bytes = new byte[size * height * 4];
        for (var y = 0; y < height; y++)
        for (var x = 0; x < size; x++)
        {
            var source = (top + y) * image.Stride + (left + x) * 4;
            var target = (y * size + x) * 4;
            // Hidden RGB may change during premultiplied rendering; only fully
            // transparent pixels are normalized. Every visible byte must match.
            if (image.Pixels[source + 3] == 0) continue;
            Array.Copy(image.Pixels, source, bytes, target, 4);
        }
        return bytes;
    }
}
