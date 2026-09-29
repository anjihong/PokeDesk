using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Xunit;

namespace DeskPokemon.Tests;

public partial class UiTests
{
    [AvaloniaFact]
    public async Task SpeciesHaveGentleSizeDifferencesIndependentOfCanvasPaddingWithoutClippingWideTails()
    {
        var window = new MainWindow(Settings.NewPreview(4), false);
        try
        {
            ShowAndLayout(window);
            // Charmander/Charmeleon/Charizard source geometry, then a very wide body and a padded canvas.
            foreach (var (dex, width, height, foot, padding, expectedHeight) in new[]
            {
                (4, 42, 42, 18d, 0, 96d), (5, 69, 56, 17.5, 0, 106d), (6, 89, 91, 39d, 0, 114d),
                (6, 200, 30, 100d, 0, 31.8), (4, 42, 42, 18d, 27, 96d)
            })
            {
                var pixels = new SpritePixels(width + padding * 2, height + padding * 2);
                for (var y = 0; y < height; y++)
                for (var x = 0; x < width; x++)
                {
                    // Upper silhouette reaches both bounds; a narrower pair of feet sets an asymmetric anchor.
                    if (y >= height - 6 && (x < Math.Floor(foot) - 2 || x >= Math.Ceiling(foot) + 2)) continue;
                    var offset = (y + padding) * pixels.Stride + (x + padding) * 4;
                    pixels.Pixels[offset + 2] = pixels.Pixels[offset + 3] = 255;
                }
                var atlas = ParseEvolutionFixture(pixels);
                Invoke(window, "ApplyPokemonAtlasFor", atlas, dex);
                await WaitForPresentationAsync(window);
                var zoom = (ScaleTransform)window.FindControl<LayoutTransformControl>("StageZoom")!.LayoutTransform!;
                Assert.Equal(expectedHeight, atlas.Body.Height * zoom.ScaleY, 6);
                Assert.InRange(atlas.FootAlignedWidth * zoom.ScaleX, 0, 212.001);
                AssertDisplayedFootOnShadow(window, atlas);
            }
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(2, false)]
    [InlineData(2, true)]
    [InlineData(8, false)]
    [InlineData(8, true)]
    public async Task DifferentBodyShapesKeepTheirFeetOnThePaintedShadowAtEveryScaleAndFacing(int scale, bool flipped)
    {
        var settings = Settings.NewPreview(4);
        settings.UiScale = scale;
        settings.FlipHorizontal = flipped;
        var window = new MainWindow(settings, false);
        try
        {
            ShowAndLayout(window);
            foreach (var evolved in new[] { false, true })
            {
                // Different tails, wings, dimensions, and feet, with ownership transferred to the window.
                var atlas = CreateEvolutionAtlas(evolved);
                Invoke(window, "ApplyPokemonAtlas", atlas);
                await WaitForPresentationAsync(window);
                var image = window.FindControl<Image>("Sprite")!;
                var shadow = window.FindControl<Image>("PetShadow")!;
                var stage = window.FindControl<Canvas>("Stage")!;
                var zoom = (ScaleTransform)window.FindControl<LayoutTransformControl>("StageZoom")!.LayoutTransform!;
                Assert.True(stage.Width * zoom.ScaleX <= 212 + .001);
                for (var frameIndex = 0; frameIndex < atlas.Frames.Length; frameIndex++)
                {
                    Invoke(window, "ShowFrame", frameIndex);
                    window.UpdateLayout();
                    var frame = atlas.Frames[frameIndex];
                    var foot = atlas.FootAnchorFor(frameIndex);
                    var local = new Point(foot.X - frame.OffsetX, foot.Y - frame.OffsetY);
                    var displayedFoot = image.TransformToVisual(window)!.Value.Transform(local);
                    // The supplied PNG's painted oval occupies x=4..192, y=2..32 in its 192x34 bitmap.
                    var shadowCenter = shadow.TransformToVisual(window)!.Value.Transform(new Point(49, 8.5));
                    var uiScale = ((ScaleTransform)window.FindControl<LayoutTransformControl>("UiZoom")!.LayoutTransform!).ScaleX;
                    Assert.InRange(Math.Abs(displayedFoot.X - shadowCenter.X), 0, .51 * uiScale);
                    Assert.InRange(Math.Abs(displayedFoot.Y - shadowCenter.Y), 0, .51 * uiScale);
                }
            }
        }
        finally { window.Close(); }
    }
}
