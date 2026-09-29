using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Xunit;

namespace DeskPokemon.Tests;

public partial class UiTests
{
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void EvolutionStartAndEndKeepAsymmetricFeetOnTheShadowWithEitherFacing(bool flip)
    {
        var settings = Settings.NewPreview(4);
        settings.FlipHorizontal = flip;
        var window = new MainWindow(settings, false);
        using var source = CreateAsymmetricFootAtlas(false);
        using var target = CreateAsymmetricFootAtlas(true);
        try
        {
            ShowAndLayout(window);
            Invoke(window, "ApplyPokemonAtlas", source);
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            AssertDisplayedFootOnShadow(window, source);
            var effect = window.FindControl<EvolutionEffect>("EvolutionVisual")!;
            // Rendering this child alone does not traverse the Window that normally pushes
            // nearest-neighbor interpolation. Match that context so transparent padding is
            // not blended into the last visible pixel row at fractional sprite scales.
            RenderOptions.SetBitmapInterpolationMode(effect, BitmapInterpolationMode.None);
            var stageZoom = window.FindControl<LayoutTransformControl>("StageZoom")!;
            var sourceScale = ((ScaleTransform)stageZoom.LayoutTransform!).ScaleX;
            var targetScale = EvolutionPetScaleFor(target);
            effect.SetFrames(source, 0, sourceScale, target, targetScale);
            effect.FlipHorizontal = flip;
            stageZoom.Opacity = 0;
            foreach (var progress in new[] { 0d, 1d })
            {
                effect.Progress = progress;
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();
                var size = new PixelSize((int)Math.Ceiling(effect.Bounds.Width), (int)Math.Ceiling(effect.Bounds.Height));
                using var rendered = new RenderTargetBitmap(size, new Vector(96, 96));
                rendered.Render(effect);
                var pixels = SpritePixels.CopyFrom(rendered);
                var bottomRow = Enumerable.Range(0, pixels.Width).Where(x =>
                    pixels.Pixels[(pixels.Height - 1) * pixels.Stride + x * 4 + 3] > 200).ToArray();
                Assert.NotEmpty(bottomRow); // Transparent padding must not lift the foot above the baseline.
                var footCenter = (bottomRow[0] + bottomRow[^1] + 1) / 2d;
                Assert.InRange(Math.Abs(footCenter - pixels.Width / 2d), 0, 1);
                var foot = effect.TranslatePoint(new Point(effect.Bounds.Width / 2, effect.Bounds.Height), window)!.Value;
                Assert.InRange(PointDistance(foot, ShadowContact(window)), 0, 1);
            }
            Invoke(window, "ApplyPokemonAtlasFor", target, 5);
            effect.Clear();
            stageZoom.Opacity = 1;
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            AssertDisplayedFootOnShadow(window, target);
        }
        finally { window.Close(); }
    }

    private static void AssertDisplayedFootOnShadow(MainWindow window, SpriteAtlas atlas)
    {
        var frame = atlas.Frames[0];
        var anchor = atlas.FootAnchorFor(0);
        var local = new Point(anchor.X - frame.OffsetX, anchor.Y - frame.OffsetY);
        var foot = window.FindControl<Image>("Sprite")!.TranslatePoint(local, window)!.Value;
        Assert.InRange(PointDistance(foot, ShadowContact(window)), 0, 1);
    }

    private static double PointDistance(Point left, Point right) =>
        Math.Sqrt(Math.Pow(left.X - right.X, 2) + Math.Pow(left.Y - right.Y, 2));

    private static double EvolutionPetScaleFor(SpriteAtlas atlas, int dex = 5) =>
        (double)typeof(MainWindow).GetMethod("PetScaleFor",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!.Invoke(null, [atlas, dex])!;

    private static Point ShadowContact(MainWindow window)
    {
        var shadow = window.FindControl<Image>("PetShadow")!;
        // The opaque shadow bounds are x=4..191 in the 192px bitmap: its visible center is 1 DIP right.
        return shadow.TranslatePoint(new Point(shadow.Bounds.Width / 2 + 1, shadow.Bounds.Height / 2), window)!.Value;
    }

    private static SpriteAtlas CreateAsymmetricFootAtlas(bool evolved)
    {
        var pixels = new SpritePixels(evolved ? 60 : 40, evolved ? 64 : 48);
        var footLeft = evolved ? 40 : 24;
        var footBottom = evolved ? 60 : 40;
        for (var y = 8; y < footBottom; y++)
        for (var x = 0; x < pixels.Width; x++)
        {
            var body = x >= footLeft && x < footLeft + 8;
            var tail = x < footLeft && y is >= 17 and <= 22;
            if (!body && !tail) continue;
            var offset = y * pixels.Stride + x * 4;
            pixels.Pixels[offset] = 80;
            pixels.Pixels[offset + 1] = 180;
            pixels.Pixels[offset + 2] = 230;
            pixels.Pixels[offset + 3] = 255;
        }
        return ParseEvolutionFixture(pixels);
    }

    [AvaloniaTheory]
    [InlineData(1)]
    [InlineData(2)]
    public void EvolutionLightMorphAndRevealRenderDeterministicDistinctBodyShapes(int scale)
    {
        var settings = Settings.NewPreview(4);
        settings.PendingEgg = new(EggKind.Common, 4, false);
        var window = new MainWindow(settings, false);
        using var source = CreateEvolutionAtlas(false);
        using var target = CreateEvolutionAtlas(true);
        try
        {
            ShowAndLayout(window);
            Invoke(window, "ApplyPokemonAtlas", source);
            var effect = window.FindControl<EvolutionEffect>("EvolutionVisual")!;
            var zoom = (ScaleTransform)window.FindControl<LayoutTransformControl>("StageZoom")!.LayoutTransform!;
            effect.SetFrames(source, 0, zoom.ScaleX, target, EvolutionPetScaleFor(target));
            window.FindControl<LayoutTransformControl>("StageZoom")!.Opacity = 0;
            window.FindControl<TextBlock>("EvolutionNoticeText")!.Text = "진화 중…";
            window.FindControl<Button>("EvolutionNotice")!.IsVisible = true;
            foreach (var (name, progress) in new[] { ("evolution-glow", .14), ("evolution-morph", .52), ("evolution-reveal", .90) })
            {
                effect.Progress = progress;
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();
                Capture(window, name, scale);
                Assert.True(effect.HasFrames);
                Assert.Equal(1, window.FindControl<Canvas>("Stage")!.Opacity);
            }
            var light = EvolutionEffect.StateAt(.14);
            Assert.True(light.SourceColor > 0 && light.SourceWhite > 0);
            Assert.Equal(0, light.TargetWhite);
            var morph = EvolutionEffect.StateAt(.52);
            Assert.InRange(morph.SourceWhite, .01, .99);
            Assert.InRange(morph.TargetWhite, .01, .99);
            Assert.Equal(0, morph.SourceColor + morph.TargetColor);
            var reveal = EvolutionEffect.StateAt(.90);
            Assert.InRange(reveal.TargetColor, .01, .99);
            Assert.InRange(reveal.TargetWhite, .01, .99);
            Assert.Equal(0, reveal.SourceWhite);
            Assert.Equal(0, EvolutionEffect.StateAt(EvolutionEffect.RevealProgress).TargetColor);
            Assert.Equal(new EvolutionEffectState(0, 0, 0, 1, 0, 1), EvolutionEffect.StateAt(1));

            effect.Clear();
            Assert.False(effect.HasFrames);
            Assert.False(effect.IsVisible);
            // The effect owns copies: clearing it must not dispose the atlases displayed by the window.
            Assert.NotEmpty(SpritePixels.CopyFrom(source.Frames[0].Bitmap).Pixels);
            Assert.NotEmpty(SpritePixels.CopyFrom(target.Frames[0].Bitmap).Pixels);
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EvolutionCommitsBeforeColorRevealAndKeepsInputsWithoutBounceOrRepeatedRequests(bool shiny)
    {
        using var assets = new UiAssets();
        var settings = Settings.NewPreview(4);
        if (shiny) settings.AddOwned(4, true);
        settings.SelectedShiny = shiny;
        settings.For(4, shiny).Level = 16;
        settings.FlipHorizontal = true;
        var window = new MainWindow(settings, false);
        try
        {
            ShowAndLayout(window);
            Assert.True(await InvokeAsync<bool>(window, "LoadPokemonAsync", 4, shiny));
            var original = window.FindControl<Image>("Sprite")!.Source;
            var evolution = InvokeAsync(window, "EvolveAsync", 5);
            var request = Field<int>(window, "_loadRequest");
            await InvokeAsync(window, "EvolveAsync", 5);
            Assert.Equal(request, Field<int>(window, "_loadRequest"));
            var effect = window.FindControl<EvolutionEffect>("EvolutionVisual")!;
            await EventuallyAsync(window, () => effect.HasFrames && effect.Progress >= .3);
            Assert.Equal(4, settings.SelectedDex);
            Assert.False(settings.IsOwned(5, shiny));
            Assert.Equal(5, settings.For(4, shiny).PendingEvolution);
            Assert.Equal(new[] { 4 }, settings.For(4, shiny).History);
            Assert.True(effect.FlipHorizontal);
            Assert.Equal(0, window.FindControl<LayoutTransformControl>("StageZoom")!.Opacity);
            Assert.Same(original, window.FindControl<Image>("Sprite")!.Source);
            var sourceExp = settings.For(4, shiny).Exp;
            Invoke(window, "OnGlobalInput");
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(sourceExp + 1, settings.For(4, shiny).Exp);
            var squash = (ScaleTransform)window.FindControl<Canvas>("Stage")!.RenderTransform!;
            Assert.Equal(1, squash.ScaleX);
            Assert.Equal(1, squash.ScaleY);

            await EventuallyAsync(window, () => settings.SelectedDex == 5 && effect.HasFrames);
            Assert.True(effect.Progress >= EvolutionEffect.RevealProgress);
            Assert.True(Field<bool>(window, "_evolving"));
            Assert.Same(settings.For(4, shiny), settings.For(5, shiny));
            Assert.Equal(new[] { 4, 5 }, settings.For(4, shiny).History);
            Assert.Equal(5, settings.For(4, shiny).CurrentDex);
            Assert.Null(settings.For(4, shiny).PendingEvolution);
            Assert.Same(original, window.FindControl<Image>("Sprite")!.Source);
            var targetExp = settings.For(5, shiny).Exp;
            Invoke(window, "OnGlobalInput");
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(targetExp + 1, settings.For(5, shiny).Exp);
            Assert.Equal(sourceExp + 2, settings.For(4, shiny).Exp);
            await evolution.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(Field<bool>(window, "_dirty")); // Inputs during reveal must still be saved later.
            Assert.Equal(shiny, settings.SelectedShiny);
            Assert.True(settings.IsOwned(4, shiny));
            Assert.True(settings.IsOwned(5, shiny));
            Assert.False(effect.HasFrames);
            Assert.False(effect.IsVisible);
            Assert.False(Field<bool>(window, "_evolving"));
            Assert.Equal(1, window.FindControl<LayoutTransformControl>("StageZoom")!.Opacity);
            Assert.NotSame(original, window.FindControl<Image>("Sprite")!.Source);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task EvolutionReloadsAMissingSourceAtItsBodyScaleInsteadOfThePlaceholderScale()
    {
        using var assets = new UiAssets();
        using var expectedSource = await SpriteAtlas.LoadAsync(4, false);
        using var expectedTarget = await SpriteAtlas.LoadAsync(5, false);
        var settings = Settings.NewPreview(4);
        settings.For(4).Level = 16;
        var window = new MainWindow(settings, false);
        try
        {
            ShowAndLayout(window);
            Invoke(window, "ShowSpritePlaceholder");
            Assert.Null(Field<SpriteAtlas?>(window, "_atlas"));
            Assert.True(window.FindControl<TextBlock>("SpriteMissing")!.IsVisible);
            var placeholderScale = ((ScaleTransform)window.FindControl<LayoutTransformControl>("StageZoom")!.LayoutTransform!).ScaleX;
            Assert.NotEqual(placeholderScale, EvolutionPetScaleFor(expectedSource, 4));

            var evolution = InvokeAsync(window, "EvolveAsync", 5);
            var effect = window.FindControl<EvolutionEffect>("EvolutionVisual")!;
            await EventuallyAsync(window, () => effect.HasFrames);
            var expectedHeight = Math.Ceiling(Math.Max(
                expectedSource.Body.Height * EvolutionPetScaleFor(expectedSource, 4),
                expectedTarget.Body.Height * EvolutionPetScaleFor(expectedTarget))) + 16;
            Assert.Equal(expectedHeight, effect.Height, 6);
            Assert.Equal(4, settings.SelectedDex);
            window.Close();
            await evolution.WaitAsync(TimeSpan.FromSeconds(1));
            Assert.False(effect.HasFrames);
            Assert.False(settings.IsOwned(5));
            Assert.Equal(5, settings.For(4).PendingEvolution);
            Assert.Equal(new[] { 4 }, settings.For(4).History);
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ClosingDuringEvolutionReleasesFramesAndKeepsOnlyAnAlreadyCommittedResult(bool afterCommit)
    {
        using var assets = new UiAssets();
        var directory = Path.Combine(Path.GetTempPath(), "PokeDesk-evolution-cancel-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "settings.json");
        var settings = Settings.NewAt(4, path);
        settings.For(4).Level = 16;
        settings.Save();
        var window = new MainWindow(settings, false);
        try
        {
            ShowAndLayout(window);
            Assert.True(await InvokeAsync<bool>(window, "LoadPokemonAsync", 4, false));
            var evolution = InvokeAsync(window, "EvolveAsync", 5);
            var effect = window.FindControl<EvolutionEffect>("EvolutionVisual")!;
            await EventuallyAsync(window, () => afterCommit
                ? settings.SelectedDex == 5 && effect.HasFrames
                : effect.HasFrames && effect.Progress > .1);
            window.Close();
            await evolution.WaitAsync(TimeSpan.FromSeconds(1));
            Assert.False(effect.HasFrames);
            Assert.False(effect.IsVisible);
            Assert.False(Field<bool>(window, "_evolving"));
            Assert.Equal(afterCommit ? 5 : 4, settings.SelectedDex);
            Assert.Equal(afterCommit, settings.IsOwned(5));
            Assert.True(settings.IsOwned(4));
            Assert.Equal(afterCommit ? null : (int?)5, settings.For(4).PendingEvolution);
            Assert.Equal(afterCommit ? new[] { 4, 5 } : new[] { 4 }, settings.For(4).History);
            var restored = Settings.LoadFrom(path)!;
            Assert.Equal(settings.SelectedDex, restored.SelectedDex);
            Assert.Equal(afterCommit, restored.HasOwned(5));
            Assert.Equal(settings.For(4).CurrentDex, restored.For(4).CurrentDex);
            Assert.Equal(settings.For(4).History, restored.For(4).History);
            Assert.Equal(settings.For(4).PendingEvolution, restored.For(4).PendingEvolution);
            if (afterCommit) Assert.Same(restored.For(4), restored.For(5));
            else Assert.Equal(5, restored.PrepareEvolution(4)); // Resume the saved target, without rerolling.
        }
        finally { window.Close(); Directory.Delete(directory, recursive: true); }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedEvolutionSaveRestoresTheBodyAndNeverRevealsTheTargetColor(bool afterReservation)
    {
        using var assets = new UiAssets();
        var directory = Path.Combine(Path.GetTempPath(), "PokeDesk-evolution-effect-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "settings.json");
        var settings = Settings.NewAt(4, path);
        settings.For(4).Level = 16;
        settings.Save();
        if (afterReservation) Assert.Equal(5, settings.PrepareEvolution(4, target: 5));
        File.Move(path, path + ".original");
        Directory.CreateDirectory(path); // Fail the atomic file promotion without touching real user data.
        var before = JsonSerializer.Serialize(settings);
        var window = new MainWindow(settings, false);
        try
        {
            ShowAndLayout(window);
            Assert.True(await InvokeAsync<bool>(window, "LoadPokemonAsync", 4, false));
            var original = window.FindControl<Image>("Sprite")!.Source;
            var effect = window.FindControl<EvolutionEffect>("EvolutionVisual")!;
            var greatestProgress = 0d;
            effect.PropertyChanged += (_, e) =>
            {
                if (e.Property == EvolutionEffect.ProgressProperty) greatestProgress = Math.Max(greatestProgress, effect.Progress);
            };
            await InvokeAsync(window, "EvolveAsync", 5).WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(before, JsonSerializer.Serialize(settings));
            if (afterReservation) Assert.InRange(greatestProgress, .7, EvolutionEffect.RevealProgress);
            else
            {
                Assert.Equal(0, greatestProgress);
                Assert.DoesNotContain(assets.Requests, request => request.Contains("/5."));
            }
            Assert.Equal(afterReservation ? (int?)5 : null, settings.For(4).PendingEvolution);
            var restored = Settings.LoadFrom(path + ".original")!;
            Assert.Equal(settings.For(4).PendingEvolution, restored.For(4).PendingEvolution);
            Assert.Equal(new[] { 4 }, restored.For(4).History);
            Assert.False(restored.HasOwned(5));
            Assert.Same(original, window.FindControl<Image>("Sprite")!.Source);
            Assert.Equal(1, window.FindControl<LayoutTransformControl>("StageZoom")!.Opacity);
            Assert.Equal(1, window.FindControl<Canvas>("Stage")!.Opacity);
            Assert.False(effect.HasFrames);
            Assert.False(effect.IsVisible);
            Assert.Contains("기존 포켓몬", window.FindControl<TextBlock>("SpriteStatus")!.Text);
            Assert.True(window.FindControl<Button>("EvolutionNotice")!.IsEnabled);
        }
        finally
        {
            window.Close();
            Directory.Delete(directory, recursive: true);
        }
    }

    private static SpriteAtlas CreateEvolutionAtlas(bool evolved)
    {
        var width = evolved ? 40 : 26;
        var height = evolved ? 44 : 34;
        var pixels = new SpritePixels(width, height);
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var head = y < 13 && x >= width / 2 - 7 && x < width / 2 + 7;
            var body = y >= 10 && y < height - 3 && x >= width / 2 - 6 && x < width / 2 + 6;
            var feet = y >= height - 6 && (x is >= 3 and <= 9 || x >= width - 10 && x <= width - 4);
            var wings = evolved && y is >= 14 and <= 28 && (x < 10 || x >= width - 10);
            var tail = !evolved && y > 17 && x >= width - 6;
            if (!(head || body || feet || wings || tail)) continue;
            var index = y * pixels.Stride + x * 4;
            var eye = y is 6 or 7 && x == width / 2 + 3;
            pixels.Pixels[index] = eye ? (byte)25 : evolved ? (byte)210 : (byte)65;
            pixels.Pixels[index + 1] = eye ? (byte)25 : evolved ? (byte)145 : (byte)185;
            pixels.Pixels[index + 2] = eye ? (byte)25 : evolved ? (byte)45 : (byte)240;
            pixels.Pixels[index + 3] = 255;
        }
        return ParseEvolutionFixture(pixels);
    }

    private static SpriteAtlas ParseEvolutionFixture(SpritePixels pixels)
    {
        var width = pixels.Width;
        var height = pixels.Height;
        var directory = Path.Combine(Path.GetTempPath(), "PokeDesk-evolution-fixture-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var png = Path.Combine(directory, "sprite.png");
            var json = Path.Combine(directory, "sprite.json");
            using (var bitmap = pixels.ToBitmap()) bitmap.Save(png);
            File.WriteAllText(json, JsonSerializer.Serialize(new
            {
                frames = new[] { new { filename = "0000.png", frame = new { x = 0, y = 0, w = width, h = height },
                    spriteSourceSize = new { x = 0, y = 0, w = width, h = height }, sourceSize = new { w = width, h = height } } }
            }));
            return SpriteAtlas.Parse(json, png);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }
}
