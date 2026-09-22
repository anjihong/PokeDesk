using System.Text.Json;
using Avalonia;
using Avalonia.Headless.XUnit;
using SkiaSharp;
using Xunit;

namespace DeskPokemon.Tests;

public class SpriteImagingTests
{
    private static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", name);

    [AvaloniaTheory]
    [InlineData("atlas-array.json")]
    [InlineData("atlas-hash.json")]
    public void AtlasPreservesNumericOrderTrimOffsetsAndBodyBounds(string json)
    {
        using var atlas = SpriteAtlas.Parse(Fixture(json), Fixture("atlas.png"));

        Assert.Equal(8, atlas.Width);
        Assert.Equal(8, atlas.Height);
        Assert.Equal(new PixelRect(1, 2, 4, 4), atlas.Body);
        Assert.Collection(atlas.Frames,
            first =>
            {
                Assert.Equal((1, 2, 1, 1), (first.OffsetX, first.OffsetY, first.Width, first.Height));
                Assert.Equal(new byte[] { 0, 0, 255, 255 }, SpritePixels.CopyFrom(first.Bitmap).Pixels);
            },
            second =>
            {
                Assert.Equal((4, 5, 1, 1), (second.OffsetX, second.OffsetY, second.Width, second.Height));
                Assert.Equal(new byte[] { 0, 255, 0, 255 }, SpritePixels.CopyFrom(second.Bitmap).Pixels);
            });
    }

    [Fact]
    public void NamedFramesKeepEggAndCrackNames()
    {
        using var doc = JsonDocument.Parse("""
            {"textures":[{"frames":[{"filename":"egg.png"},{"filename":"egg_crack.png"}]}]}
            """);
        Assert.Equal(new[] { "egg", "egg_crack" }, SpriteAtlas.EnumerateFrames(doc.RootElement)
            .Select(f => Path.GetFileNameWithoutExtension(f.Name)));
    }

    [Fact]
    public void GifComposesDeltaFramesAndRestoresPreviousOrBackground()
    {
        using var codec = SKCodec.Create(Fixture("disposal.gif"));
        Assert.Equal(4, codec.FrameCount);

        // Red remains throughout. Green is RestorePrevious; blue is RestoreBackground.
        var green = SpritePixels.Decode(codec, 1).Pixels;
        AssertPixel(green, 0, 255, 0, 0, 255);
        AssertPixel(green, 1, 0, 255, 0, 255);

        var blue = SpritePixels.Decode(codec, 2).Pixels;
        AssertPixel(blue, 0, 255, 0, 0, 255);
        Assert.Equal(0, blue[1 * 4 + 3]);
        AssertPixel(blue, 2, 0, 0, 255, 255);

        var yellow = SpritePixels.Decode(codec, 3).Pixels;
        AssertPixel(yellow, 0, 255, 0, 0, 255);
        Assert.Equal(0, yellow[1 * 4 + 3]);
        Assert.Equal(0, yellow[2 * 4 + 3]);
        AssertPixel(yellow, 3, 255, 255, 0, 255);
    }

    [AvaloniaFact]
    public void GifResamplesToTenFpsAndSharesLongFrames()
    {
        using var atlas = SpriteAtlas.ParseGif(Fixture("disposal.gif"));

        Assert.Equal(new PixelRect(0, 0, 4, 1), atlas.Body);
        Assert.Equal(5, atlas.Frames.Length); // delays 100, 50, 200, 100 ms
        Assert.Equal(new[] { 1, 2, 3, 3, 4 }, atlas.Frames.Select(f => f.Width));
        Assert.Same(atlas.Frames[2].Bitmap, atlas.Frames[3].Bitmap);
    }

    [AvaloniaFact]
    public void GifClampsZeroAndOneCentisecondDelaysAndSkipsShortFrames()
    {
        using var atlas = SpriteAtlas.ParseGif(Fixture("timing.gif"));

        // Raw delays: 0, 10, 30, 30, 140 ms. Effective timeline ends: 100, 200, 230, 260, 400.
        Assert.Equal(4, atlas.Frames.Length);
        Assert.Equal(new[] { 0, 1, 2, 4 }, atlas.Frames.Select(f => f.OffsetX));
    }

    [AvaloniaFact]
    public void SilhouetteKeepsAlphaAndCachesTheResult()
    {
        var pixels = new SpritePixels(3, 1);
        new byte[] { 90, 140, 180, 0, 100, 160, 200, 128, 110, 180, 220, 255 }
            .CopyTo(pixels.Pixels, 0);
        using var icon = pixels.ToBitmap();
        var original = SpritePixels.CopyFrom(icon).Pixels;
        var silhouette = PokemonIcons.SilhouetteOf(100_001, icon);
        var actual = SpritePixels.CopyFrom(silhouette).Pixels;

        // Skia can normalize RGB under zero alpha; only visible RGB has meaning.
        Assert.Equal(0, actual[3]);
        AssertPixel(actual, 1, 0x28, 0x28, 0x28, 128);
        AssertPixel(actual, 2, 0x28, 0x28, 0x28, 255);
        Assert.Same(silhouette, PokemonIcons.SilhouetteOf(100_001, icon));
        Assert.Equal(original, SpritePixels.CopyFrom(icon).Pixels);
    }

    private static void AssertPixel(byte[] pixels, int x, byte red, byte green, byte blue, byte alpha) =>
        Assert.Equal(new[] { blue, green, red, alpha }, pixels.Skip(x * 4).Take(4));
}
