using System.Text.Json;
using Avalonia;
using Avalonia.Headless.XUnit;
using Xunit;

namespace DeskPokemon.Tests;

public class FootAnchorTests
{
    [Fact]
    public void FeetIgnoreTransparentPaddingAndAnAsymmetricTailAboveTheBottomBand()
    {
        var pixels = new SpritePixels(40, 30);
        Paint(pixels, new PixelRect(9, 6, 10, 17));
        Paint(pixels, new PixelRect(8, 24, 4, 2));
        Paint(pixels, new PixelRect(17, 24, 4, 2));
        Paint(pixels, new PixelRect(20, 16, 18, 3)); // A long tail must not set the foot center.

        var geometry = pixels.AnalyzeFoot(new PixelRect(0, 0, 40, 30));

        Assert.Equal(new PixelRect(8, 6, 30, 20), geometry.OpaqueBounds);
        Assert.Equal(new Point(14.5, 26), geometry.FootAnchor);
        Assert.NotEqual(20, geometry.FootAnchor.X); // Canvas center.
        Assert.NotEqual(23, geometry.FootAnchor.X); // Whole visible-body center.
    }

    [Fact]
    public void AShallowBottomBandIncludesBothFeetAtSlightlyDifferentHeights()
    {
        var pixels = new SpritePixels(70, 60);
        Paint(pixels, new PixelRect(10, 2, 20, 50));
        Paint(pixels, new PixelRect(7, 52, 5, 2));
        Paint(pixels, new PixelRect(24, 52, 7, 6));
        Paint(pixels, new PixelRect(30, 33, 35, 5));

        var geometry = pixels.AnalyzeFoot(new PixelRect(0, 0, 70, 60));

        // Bottom 10% = six rows. Restricting this to three rows would pick only the right foot.
        Assert.Equal(new Point(19, 58), geometry.FootAnchor);
    }

    [Fact]
    public void FloatingAndTransparentImagesHaveDefinedBottomAnchors()
    {
        var pixels = new SpritePixels(12, 12);
        Paint(pixels, new PixelRect(4, 2, 4, 4), alpha: 128);
        var floating = pixels.AnalyzeFoot(new PixelRect(0, 0, 12, 12));
        Assert.Equal(new Point(6, 6), floating.FootAnchor);
        Assert.Equal(new PixelRect(4, 2, 4, 4), floating.OpaqueBounds);
        var transparent = new SpritePixels(12, 12).AnalyzeFoot(new PixelRect(0, 0, 12, 12));
        Assert.Equal(new Point(6, 12), transparent.FootAnchor);
        Assert.Equal(new PixelRect(0, 0, 0, 0), transparent.OpaqueBounds);
    }

    [Fact]
    public void AnAtlasCropReportsBitmapLocalCoordinatesRatherThanSheetCoordinates()
    {
        var sheet = new SpritePixels(32, 32);
        Paint(sheet, new PixelRect(20, 22, 4, 3));
        var geometry = sheet.AnalyzeFoot(new PixelRect(16, 16, 12, 12));
        Assert.Equal(new PixelRect(4, 6, 4, 3), geometry.OpaqueBounds);
        Assert.Equal(new Point(6, 9), geometry.FootAnchor);
    }

    [AvaloniaFact]
    public void AtlasUsesAlphaBodyUnionAndAStableMedianHorizontalAnchorWithoutEmptyFrameDistortion()
    {
        var first = new SpritePixels(20, 20);
        var second = new SpritePixels(20, 20);
        var outlier = new SpritePixels(20, 20);
        Paint(first, new PixelRect(5, 6, 6, 9));
        Paint(second, new PixelRect(3, 7, 6, 7));
        Paint(outlier, new PixelRect(12, 3, 6, 10));
        using var atlas = ParseFrames(40, 40,
            (first, new PixelPoint(3, 5)),
            (second, new PixelPoint(5, 4)),
            (outlier, new PixelPoint(0, 8)),
            (new SpritePixels(20, 20), new PixelPoint(30, 30)));

        Assert.Equal(new PixelRect(8, 11, 10, 10), atlas.Body);
        Assert.Equal(new PixelRect(5, 6, 6, 9), atlas.Frames[0].OpaqueBounds);
        Assert.Equal(new Point(8, 15), atlas.Frames[0].FootAnchor);
        Assert.Equal(new Point(11, 20), atlas.FootAnchorFor(0));
        Assert.Equal(new Point(11, 18), atlas.FootAnchorFor(1));
        Assert.Equal(new Point(11, 21), atlas.FootAnchorFor(2));
        Assert.Equal(11, atlas.FootAnchorFor(3).X);
        Assert.Equal(14, atlas.FootAlignedWidth); // Visible right extent seven pixels from the foot.
        Assert.True(atlas.FootAlignedWidth > atlas.Body.Width);
    }

    [AvaloniaFact]
    public void DrawingRectAlignsTheBitmapFootToAnAbsoluteDisplayPointAtAnyScale()
    {
        var pixels = new SpritePixels(20, 20);
        Paint(pixels, new PixelRect(5, 6, 6, 9));
        using var atlas = ParseFrames(40, 40, (pixels, new PixelPoint(3, 5)));
        var foot = new Point(118, 160);
        foreach (var scale in new[] { 1d, 1.25, 2d, 4d })
        {
            var destination = atlas.FrameBoundsAt(0, foot, scale);
            var frame = atlas.Frames[0];
            Assert.Equal(foot.X, destination.X + frame.FootAnchor.X * scale, 8);
            Assert.Equal(foot.Y, destination.Y + frame.FootAnchor.Y * scale, 8);
            Assert.Equal(frame.Width * scale, destination.Width);
            Assert.Equal(frame.Height * scale, destination.Height);
            var leftEdge = destination.X + frame.OpaqueBounds.X * scale;
            var rightEdge = destination.X + frame.OpaqueBounds.Right * scale;
            Assert.True(leftEdge >= foot.X - atlas.FootAlignedWidth * scale / 2);
            Assert.True(rightEdge <= foot.X + atlas.FootAlignedWidth * scale / 2);
        }
    }

    [AvaloniaFact]
    public void FullyTransparentAtlasHasFiniteFallbackGeometry()
    {
        using var atlas = ParseFrames(20, 20, (new SpritePixels(20, 20), new PixelPoint(0, 0)));
        Assert.Equal(new Point(10, 20), atlas.FootAnchorFor(0));
        Assert.Equal(new PixelRect(10, 19, 1, 1), atlas.Body);
        Assert.True(double.IsFinite(atlas.FootAlignedWidth));
        Assert.True(atlas.FootAlignedWidth >= 1);
        Assert.Equal(new Rect(0, 0, 20, 20), atlas.FrameBoundsAt(0, new Point(10, 20)));
    }

    [AvaloniaFact]
    public void GifSnapshotsCarryLocalFootMetadataAndOneStableHorizontalAnchor()
    {
        using var atlas = SpriteAtlas.ParseGif(Path.Combine(AppContext.BaseDirectory, "Fixtures", "timing.gif"));
        Assert.Equal(new PixelRect(0, 0, 5, 1), atlas.Body);
        Assert.All(atlas.Frames, frame =>
        {
            Assert.Equal(new PixelRect(0, 0, 1, 1), frame.OpaqueBounds);
            Assert.Equal(new Point(.5, 1), frame.FootAnchor);
        });
        for (var frame = 0; frame < atlas.Frames.Length; frame++)
            Assert.Equal(new Point(2, 1), atlas.FootAnchorFor(frame)); // Median of .5,1.5,2.5,4.5.
    }

    private static void Paint(SpritePixels pixels, PixelRect rectangle, byte alpha = 255)
    {
        for (var y = rectangle.Y; y < rectangle.Bottom; y++)
        for (var x = rectangle.X; x < rectangle.Right; x++)
        {
            var index = y * pixels.Stride + x * 4;
            pixels.Pixels[index] = 40;
            pixels.Pixels[index + 1] = 100;
            pixels.Pixels[index + 2] = 200;
            pixels.Pixels[index + 3] = alpha;
        }
    }

    private static SpriteAtlas ParseFrames(int width, int height, params (SpritePixels Pixels, PixelPoint Offset)[] frames)
    {
        var sheet = new SpritePixels(frames.Max(frame => frame.Pixels.Width), frames.Sum(frame => frame.Pixels.Height));
        var entries = new List<object>();
        var y = 0;
        for (var i = 0; i < frames.Length; i++)
        {
            var (pixels, offset) = frames[i];
            for (var row = 0; row < pixels.Height; row++)
                Array.Copy(pixels.Pixels, row * pixels.Stride, sheet.Pixels, (y + row) * sheet.Stride, pixels.Stride);
            entries.Add(new
            {
                filename = $"{i:D4}.png",
                frame = new { x = 0, y, w = pixels.Width, h = pixels.Height },
                spriteSourceSize = new { x = offset.X, y = offset.Y, w = pixels.Width, h = pixels.Height },
                sourceSize = new { w = width, h = height },
            });
            y += pixels.Height;
        }
        var directory = Path.Combine(Path.GetTempPath(), "PokeDesk-foot-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var imagePath = Path.Combine(directory, "sheet.png");
            var jsonPath = Path.Combine(directory, "sheet.json");
            using (var image = sheet.ToBitmap()) image.Save(imagePath);
            File.WriteAllText(jsonPath, JsonSerializer.Serialize(new { frames = entries }));
            return SpriteAtlas.Parse(jsonPath, imagePath);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }
}
