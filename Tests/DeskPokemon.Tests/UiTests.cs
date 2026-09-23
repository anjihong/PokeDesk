using System.Collections.Concurrent;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Xunit;

namespace DeskPokemon.Tests;

[Collection("Artwork assets")]
public class UiTests
{
    [AvaloniaTheory]
    [InlineData(1)]
    [InlineData(2)]
    public void StarterUsesBundledKoreanFontAndDeliversSelectedDex(int scale)
    {
        var window = new StarterWindow([1, 4, 7], false);
        using var icon = TestSprite();
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            Assert.Equal(SystemDecorations.None, window.SystemDecorations);
            Assert.Equal(TextRenderingMode.Antialias, RenderOptions.GetTextRenderingMode(window));
            Assert.Contains("NanumGothic", window.FontFamily.Name);
            Assert.Contains(WindowTransparencyLevel.Transparent, window.TransparencyLevelHint);
            var choices = window.FindControl<StackPanel>("Choices")!;
            foreach (var image in choices.GetVisualDescendants().OfType<Image>()) image.Source = icon;
            Assert.Equal(3, choices.Children.Count);
            Assert.InRange(window.Bounds.Width, 330, 410);
            Assert.InRange(window.Bounds.Height, 110, 180);
            Capture(window, "starter", scale);

            int? selected = null;
            window.Selected += dex => selected = dex;
            ((Button)choices.Children[1]).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(4, selected);
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task SharedMainViewOpensDexAndRestoresCollapsedHeight(int scale)
    {
        var settings = Settings.NewPreview(4);
        settings.For(4).Exp = 15;
        settings.PendingEgg = new(EggKind.Common, 4, false);
        var window = new MainWindow(settings, false);
        using var icon = TestSprite();
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            Invoke(window, "BuildGenTabs");
            PopulatePet(window, icon);
            var transform = window.FindControl<Image>("Sprite")!.TransformToVisual(window)!.Value;
            Assert.Equal(3.5, transform.M11, 6);
            Assert.Equal(3.5, transform.M22, 6);
            Assert.Equal(2, window.FindControl<Canvas>("EggStage")!.TransformToVisual(window)!.Value.M11, 6);
            var icons = Enumerable.Range(1, 30).ToDictionary(dex => dex, _ => icon);
            Invoke(window, "RebuildIconGrid", 1, icons);
            var grid = window.FindControl<WrapPanel>("IconGrid")!;
            Assert.Equal(30, grid.Children.Count);
            var unowned = (RadioButton)grid.Children[0];
            Assert.False(unowned.IsEnabled);
            Assert.True(ToolTip.GetShowOnDisabled(unowned));
            Assert.True(((RadioButton)grid.Children[3]).IsChecked);
            Assert.Equal("Lv. 1", window.FindControl<TextBlock>("LevelText")!.Text);
            Assert.Equal(50, window.FindControl<Avalonia.Controls.Shapes.Rectangle>("ExpBar")!.Width);
            Assert.True(window.FindControl<Avalonia.Controls.Shapes.Rectangle>("ExpBar")!.IsVisible);
            Assert.Equal(9, window.FindControl<StackPanel>("GenTabs")!.Children.Count);
            Assert.Equal(300, window.Bounds.Width);
            Assert.True(window.Topmost);
            Assert.Equal(TextRenderingMode.Antialias, RenderOptions.GetTextRenderingMode(window));
            Assert.False(window.ShowInTaskbar);
            Assert.False(window.FindControl<Border>("InputNotice")!.IsVisible);
            var closedHeight = window.Bounds.Height;
            Capture(window, "main-collapsed", scale);

            var menu = window.FindControl<StackPanel>("MenuTabs")!;
            var dex = (ToggleButton)menu.Children[0];
            dex.IsChecked = true;
            await Task.Delay(350);
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            Assert.True(window.FindControl<Border>("Drawer")!.Height >= 180);
            Assert.True(window.Bounds.Height > closedHeight + 150);
            Capture(window, "main-dex", scale);

            window.FindControl<CheckBox>("OwnedOnly")!.IsChecked = true;
            Invoke(window, "RebuildIconGrid", 1, icons);
            Assert.Single(grid.Children);
            Assert.Equal((4, false), Choice((RadioButton)grid.Children[0]));
            Capture(window, "main-owned", scale);

            dex.IsChecked = false;
            await Task.Delay(350);
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            Assert.Equal(0, window.FindControl<Border>("Drawer")!.Height);
            Assert.Equal(closedHeight, window.Bounds.Height);
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task ShinyDexFiltersAndSelectionKeepEachColorsProgressSeparate(int scale)
    {
        using var assets = new UiAssets();
        var settings = Settings.NewPreview(4);
        settings.PendingEgg = new(EggKind.Common, 4, false);
        settings.AddOwned(7);
        settings.AddOwned(4, true);
        settings.For(4).Level = 3;
        settings.For(4).Exp = 17;
        settings.For(4, true).Level = 7;
        settings.For(4, true).Exp = 62;
        var window = new MainWindow(settings, false);
        try
        {
            ShowAndLayout(window);
            Invoke(window, "BuildGenTabs");
            Invoke(window, "SelectGenTab", 1);
            var grid = window.FindControl<WrapPanel>("IconGrid")!;
            await EventuallyAsync(window, () => grid.Children.Count == 151);
            var ownedOnly = window.FindControl<CheckBox>("OwnedOnly")!;
            var shinyFilter = window.FindControl<CheckBox>("ShinyDex")!;
            ownedOnly.IsChecked = true;
            Assert.Equal(2, grid.Children.Count);
            Assert.Equal("보유 2/1025", window.FindControl<TextBlock>("OwnedCount")!.Text);

            shinyFilter.IsChecked = true;
            await EventuallyAsync(window, () => grid.Children.Count == 1 && Choice((RadioButton)grid.Children[0]).Shiny);
            Assert.Equal(4, settings.SelectedDex);
            Assert.False(settings.SelectedShiny); // Viewing a collection must not change the pet.
            Assert.Equal("보유 1/1025", window.FindControl<TextBlock>("OwnedCount")!.Text);
            var shinyCell = (RadioButton)grid.Children[0];
            Assert.Equal((4, true), Choice(shinyCell));
            Assert.Contains("★ 이로치", TipText(shinyCell));
            Assert.Contains("Lv.7", TipText(shinyCell));
            Assert.False(shinyCell.IsChecked == true);
            shinyCell.IsChecked = true; // Use the real async selection handler.
            await EventuallyAsync(window, () => settings.SelectedShiny && window.FindControl<Image>("Sprite")!.Source != null);
            Assert.Equal("★ Lv. 7", window.FindControl<TextBlock>("LevelText")!.Text);
            Assert.Equal("현재 경험치: 62\n필요 경험치: 210", TipText(window.FindControl<Border>("ExpTrack")!));
            Invoke(window, "AddExp");
            Assert.Equal(63, settings.For(4, true).Exp);
            Assert.Equal(17, settings.For(4).Exp);
            Assert.Equal(30, window.FindControl<Avalonia.Controls.Shapes.Rectangle>("ExpBar")!.Width);

            var tab = (ToggleButton)window.FindControl<StackPanel>("MenuTabs")!.Children[0];
            tab.IsChecked = true;
            var content = window.FindControl<Border>("DrawerContent")!;
            content.Measure(new Size(300, double.PositiveInfinity));
            await EventuallyAsync(window, () => Math.Abs(window.FindControl<Border>("Drawer")!.Height - content.DesiredSize.Height) < .001);
            var left = BoundsIn(window, ownedOnly);
            var right = BoundsIn(window, shinyFilter);
            var count = BoundsIn(window, window.FindControl<TextBlock>("OwnedCount")!);
            Assert.InRange(Math.Abs(left.Y - right.Y), 0, 1);
            Assert.True(left.Right <= right.Left, "The two collection filters must not overlap.");
            Assert.True(right.Right <= count.Left, "Filters must not overlap the ownership count.");
            Capture(window, "main-shiny", scale);

            ownedOnly.IsChecked = false;
            Assert.Equal(151, grid.Children.Count);
            var unownedShiny = grid.Children.OfType<RadioButton>().Single(cell => Choice(cell).Dex == 7);
            Assert.False(unownedShiny.IsEnabled); // Owning normal #7 does not unlock shiny #7.
            Assert.True(ToolTip.GetShowOnDisabled(unownedShiny));
            Assert.Contains("???", TipText(unownedShiny));
            Assert.Contains("★ 이로치", TipText(unownedShiny));
            Assert.Contains("미보유", TipText(unownedShiny));
            Assert.True(PokemonIcons.TryGetCached(1, 7, out var shinyIcon, true));
            Assert.NotSame(shinyIcon, ((Image)unownedShiny.Content!).Source);

            var selectedSprite = window.FindControl<Image>("Sprite")!.Source;
            shinyFilter.IsChecked = false;
            await EventuallyAsync(window, () => grid.Children.Count == 151 && !Choice((RadioButton)grid.Children[0]).Shiny);
            Assert.True(settings.SelectedShiny);
            var normalCell = grid.Children.OfType<RadioButton>().Single(cell => Choice(cell).Dex == 4);
            Assert.Contains("Lv.3", TipText(normalCell));
            Assert.DoesNotContain("이로치", TipText(normalCell));
            normalCell.IsChecked = true;
            await EventuallyAsync(window, () => !settings.SelectedShiny &&
                !ReferenceEquals(selectedSprite, window.FindControl<Image>("Sprite")!.Source));
            Assert.Equal("Lv. 3", window.FindControl<TextBlock>("LevelText")!.Text);
            Assert.Equal("현재 경험치: 17\n필요 경험치: 90", TipText(window.FindControl<Border>("ExpTrack")!));
            Assert.Equal(63, settings.For(4, true).Exp);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task FailedShinySelectionRestoresThePreviousPetAndItsProgress()
    {
        using var assets = new UiAssets { MissingSpriteDex = 7 };
        var settings = Settings.NewPreview(4);
        settings.AddOwned(7, true);
        settings.For(4).Level = 3;
        settings.For(4).Exp = 17;
        var window = new MainWindow(settings, false);
        try
        {
            ShowAndLayout(window);
            Assert.True(await InvokeAsync<bool>(window, "LoadPokemonAsync", 4, false));
            var previousSprite = window.FindControl<Image>("Sprite")!.Source;
            Invoke(window, "BuildGenTabs");
            Invoke(window, "SelectGenTab", 1);
            var grid = window.FindControl<WrapPanel>("IconGrid")!;
            await EventuallyAsync(window, () => grid.Children.Count == 151);
            window.FindControl<CheckBox>("ShinyDex")!.IsChecked = true;
            await EventuallyAsync(window, () => grid.Children.Count == 151 && Choice((RadioButton)grid.Children[0]).Shiny);
            grid.Children.OfType<RadioButton>().Single(cell => Choice(cell).Dex == 7).IsChecked = true;
            await EventuallyAsync(window, () => window.FindControl<TextBlock>("SpriteStatus")!.IsVisible && !settings.SelectedShiny);
            Assert.Equal(4, settings.SelectedDex);
            Assert.Same(previousSprite, window.FindControl<Image>("Sprite")!.Source);
            Assert.Equal("Lv. 3", window.FindControl<TextBlock>("LevelText")!.Text);
            Assert.Equal("현재 경험치: 17\n필요 경험치: 90", TipText(window.FindControl<Border>("ExpTrack")!));
            Assert.DoesNotContain(grid.Children.OfType<RadioButton>(), cell => cell.IsChecked == true);
            Assert.Equal(1, settings.For(7, true).Level);
            Assert.Equal(0, settings.For(7, true).Exp);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task ShinyHatchCommitsOnlyItsColorAndFitsAnimatedResultInsideTheEggArea()
    {
        using var assets = new UiAssets();
        var settings = Settings.NewPreview(4);
        settings.AddOwned(4, true);
        settings.SelectedShiny = true;
        settings.PendingEgg = new(EggKind.Rare, 4, true);
        settings.Eggs = 1;
        var window = new MainWindow(settings, false);
        using var icon = TestSprite();
        try
        {
            ShowAndLayout(window);
            PopulatePet(window, icon);
            SetEggState(window, "Ready");
            await InvokeAsync(window, "HatchAsync");
            await EventuallyAsync(window, () => Field<SpriteAtlas?>(window, "_resultAtlas") != null);
            Field<DispatcherTimer>(window, "_resultTimer").Stop();
            StopAnimation(window, "ResultPop");
            StopAnimation(window, "FlashOut");
            Assert.Equal(0, settings.Eggs);
            Assert.Equal(1, settings.For(4).Level);
            Assert.Equal(2, settings.For(4, true).Level);
            Assert.Equal("★ Lv. 2", window.FindControl<TextBlock>("LevelText")!.Text);
            Assert.Equal("Lv.2 ↑", window.FindControl<TextBlock>("NewText")!.Text);
            Assert.Equal("★ 이로치\n파이리", window.FindControl<TextBlock>("BubbleText")!.Text);
            Assert.False(window.FindControl<Canvas>("EggStage")!.IsVisible);
            Assert.False(window.FindControl<LayoutTransformControl>("EggZoom")!.IsVisible);
            var stage = window.FindControl<Canvas>("ResultStage")!;
            var zoom = window.FindControl<LayoutTransformControl>("ResultZoom")!;
            Assert.True(stage.IsVisible);
            Assert.True(zoom.IsVisible);
            var scale = Assert.IsType<ScaleTransform>(zoom.LayoutTransform);
            Assert.InRange(scale.ScaleX, .01, .99);
            Assert.Equal(scale.ScaleX, scale.ScaleY);
            Assert.True(stage.Width * scale.ScaleX <= 80.001);
            Assert.True(stage.Height * scale.ScaleY <= 60.001);

            var atlas = Field<SpriteAtlas>(window, "_resultAtlas");
            Assert.Equal(2, atlas.Frames.Length);
            var image = window.FindControl<Image>("ResultImage")!;
            var first = image.Source;
            Invoke(window, "ShowResultFrame", 1);
            Assert.NotSame(first, image.Source);
            Assert.Same(atlas.Frames[1].Bitmap, image.Source);
            Assert.Equal(atlas.Frames[1].OffsetX - atlas.Body.X, Canvas.GetLeft(image));
            Assert.Equal(atlas.Frames[1].OffsetY - atlas.Body.Y, Canvas.GetTop(image));
            Assert.True(Canvas.GetLeft(image) + image.Width <= stage.Width);
            Assert.True(Canvas.GetTop(image) + image.Height <= stage.Height);
            window.UpdateLayout();
            Capture(window, "egg-result", 2);
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(EggKind.Common, "커먼 알")]
    [InlineData(EggKind.Rare, "레어 알")]
    [InlineData(EggKind.Epic, "에픽 알")]
    [InlineData(EggKind.Legendary, "레전더리 알")]
    [InlineData(EggKind.Shiny, "이로치알")]
    public async Task EveryEggGradeShowsItsArtworkAndNameWithoutRevealingTheHatch(EggKind kind, string name)
    {
        using var assets = new UiAssets();
        var settings = Settings.NewPreview(4);
        settings.PendingEgg = new(kind, 7, true);
        settings.Eggs = 1;
        var pending = settings.PendingEgg;
        var window = new MainWindow(settings, false);
        using var icon = TestSprite();
        try
        {
            ShowAndLayout(window);
            PopulatePet(window, icon);
            SetEggState(window, "Ready");
            await InvokeAsync(window, "LoadEggAssetsAsync");
            StopAnimation(window, "EggIdle");
            window.UpdateLayout();
            Assert.Equal(name + "\n클릭하여 부화", window.FindControl<TextBlock>("BubbleText")!.Text);
            Assert.Equal(name, TipText(window.FindControl<Canvas>("EggStage")!));
            Assert.False(window.FindControl<Avalonia.Controls.Shapes.Ellipse>("EggFallback")!.IsVisible);
            var image = window.FindControl<Image>("EggImage")!;
            var artwork = Assert.IsAssignableFrom<Bitmap>(image.Source);
            Assert.Equal(new PixelSize(28, 30), artwork.PixelSize);
            var pixels = SpritePixels.CopyFrom(artwork);
            var offset = 10 * pixels.Stride + 8 * 4;
            Assert.Equal(new byte[] { (byte)(40 + (int)kind * 42), (byte)(170 - (int)kind * 27),
                (byte)(210 - (int)kind * 31), 255 }, pixels.Pixels[offset..(offset + 4)]);
            Assert.True(window.FindControl<LayoutTransformControl>("EggZoom")!.IsVisible);
            Assert.False(window.FindControl<LayoutTransformControl>("ResultZoom")!.IsVisible);
            Assert.Null(window.FindControl<Image>("ResultImage")!.Source);
            Assert.Same(pending, settings.PendingEgg);
            Assert.Equal(1, settings.Eggs);
            Assert.DoesNotContain(7, settings.ShinyOwned);
            Assert.DoesNotContain(assets.Requests, path => path.Contains("/pokemon/"));
            Capture(window, "egg-ready-" + kind, 2);

            settings.Eggs = 0;
            settings.EggSeconds = 60;
            SetEggState(window, "Waiting");
            StopAnimation(window, "EggWait");
            Assert.Equal(name + "\n29:00", window.FindControl<TextBlock>("BubbleText")!.Text);
            Assert.Same(pending, settings.PendingEgg);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task ExperienceTooltipOpensOnHoverAndUpdatesInPlaceWhenLevelChanges()
    {
        var settings = Settings.NewPreview(4);
        settings.For(4).Exp = 29;
        var window = new MainWindow(settings, false);
        try
        {
            ShowAndLayout(window);
            var track = window.FindControl<Border>("ExpTrack")!;
            var tooltip = Assert.IsType<ToolTip>(ToolTip.GetTip(track));
            Assert.Equal(PlacementMode.Top, ToolTip.GetPlacement(track));
            Assert.Equal(300, ToolTip.GetShowDelay(track));
            var label = Assert.IsType<TooltipText>(tooltip.Content);
            Assert.Equal("현재 경험치: 29\n필요 경험치: 30", label.Text);
            Assert.False(ToolTip.GetIsOpen(track));

            Hover(window, track);
            Assert.True(track.IsPointerOver);
            await EventuallyAsync(window, () => ToolTip.GetIsOpen(track));
            Assert.NotNull(tooltip.GetVisualRoot());
            await EventuallyAsync(window, () => tooltip.Bounds.Width > 0 && tooltip.Bounds.Height > 0 && tooltip.Opacity == 1);
            Capture(tooltip, "exp-tooltip", 1);
            Capture(tooltip, "exp-tooltip", 2);

            Invoke(window, "AddExp");
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            Assert.Equal(2, settings.For(4).Level);
            Assert.Equal(0, settings.For(4).Exp);
            Assert.Equal("Lv. 2", window.FindControl<TextBlock>("LevelText")!.Text);
            Assert.Same(tooltip, ToolTip.GetTip(track));
            Assert.True(ToolTip.GetIsOpen(track));
            Assert.Same(label, tooltip.Content);
            Assert.Equal("현재 경험치: 0\n필요 경험치: 60", label.Text);
            Assert.False(window.FindControl<Avalonia.Controls.Shapes.Rectangle>("ExpBar")!.IsVisible);

            // The same already-visible tooltip must continue updating, without a second hover.
            Invoke(window, "AddExp");
            Assert.Same(tooltip, ToolTip.GetTip(track));
            Assert.True(ToolTip.GetIsOpen(track));
            Assert.Equal("현재 경험치: 1\n필요 경험치: 60", label.Text);

            window.MouseMove(new Point(-1, -1), RawInputModifiers.None);
            await EventuallyAsync(window, () => !ToolTip.GetIsOpen(track));
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task EmptyExperienceBarStillHasAFullWidthHoverTarget()
    {
        var window = new MainWindow(Settings.NewPreview(4), false);
        try
        {
            ShowAndLayout(window);
            var track = window.FindControl<Border>("ExpTrack")!;
            var bar = window.FindControl<Avalonia.Controls.Shapes.Rectangle>("ExpBar")!;
            Assert.Equal(0, bar.Width);
            Assert.False(bar.IsVisible);
            Assert.Equal(100, track.Bounds.Width);

            // Hover near the right edge of the background track, where no fill exists.
            Hover(window, track, new Point(track.Bounds.Width - 2, track.Bounds.Height / 2));
            Assert.True(track.IsPointerOver);
            await EventuallyAsync(window, () => ToolTip.GetIsOpen(track));
            var tooltip = Assert.IsType<ToolTip>(ToolTip.GetTip(track));
            Assert.Equal("현재 경험치: 0\n필요 경험치: 30", Assert.IsType<TooltipText>(tooltip.Content).Text);
            Assert.NotNull(tooltip.GetVisualRoot());
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task DrawerMovesUpByItsExpansionAndReturnsToEachChosenPositionWithoutDrift()
    {
        var window = new MainWindow(Settings.NewPreview(4), false);
        try
        {
            var (tab, drawer, area, expansion) = PreparePositionedDrawer(window);
            var baselineHeight = window.Bounds.Height;
            for (var cycle = 0; cycle < 3; cycle++)
            {
                var origin = new PixelPoint(area.X + 137 + cycle * 17,
                    area.Y + (int)Math.Ceiling(expansion * window.DesktopScaling) + 73 + cycle * 11);
                window.Position = origin;
                Assert.Equal(origin, window.Position);

                tab.IsChecked = true;
                await EventuallyAsync(window, () => Math.Abs(drawer.Height - expansion) < .001);
                Assert.True(window.Bounds.Height > baselineHeight + 150);
                Assert.Equal(origin.X, window.Position.X);
                Assert.Equal(origin.Y - (int)Math.Round((window.Bounds.Height - baselineHeight) * window.DesktopScaling),
                    window.Position.Y);

                tab.IsChecked = false;
                await EventuallyAsync(window, () => drawer.Height == 0);
                Assert.Equal(baselineHeight, window.Bounds.Height);
                Assert.Equal(origin, window.Position);
            }
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task MovingAnOpenDrawerThenResizingThePetKeepsTheNewBottomAnchor()
    {
        var window = new MainWindow(Settings.NewPreview(4), false);
        try
        {
            var (tab, drawer, area, expansion) = PreparePositionedDrawer(window);
            var origin = new PixelPoint(area.X + 123,
                area.Y + (int)Math.Ceiling(expansion * window.DesktopScaling) + 160);
            window.Position = origin;
            tab.IsChecked = true;
            await EventuallyAsync(window, () => Math.Abs(drawer.Height - expansion) < .001);

            var draggedPosition = new PixelPoint(origin.X + 61, window.Position.Y + 97);
            window.Position = draggedPosition;
            Assert.Equal(draggedPosition, window.Position);
            var heightBeforePetResize = window.Bounds.Height;
            var widthBeforePetResize = window.Bounds.Width;

            // A sprite change can resize the pet while its drawer remains open.
            // Change the real measured content, not the positioning callback or anchor fields.
            window.FindControl<Canvas>("Stage")!.Height += 18.25;
            await EventuallyAsync(window, () => window.Bounds.Height > heightBeforePetResize);
            var resizedOpenHeight = window.Bounds.Height;
            var resizedOpenPosition = window.Position;
            Assert.Equal(widthBeforePetResize, window.Bounds.Width);
            Assert.Equal(draggedPosition.X, resizedOpenPosition.X);
            Assert.Equal(draggedPosition.Y - (int)Math.Round(
                (resizedOpenHeight - heightBeforePetResize) * window.DesktopScaling), resizedOpenPosition.Y);

            tab.IsChecked = false;
            await EventuallyAsync(window, () => drawer.Height == 0);
            Assert.True(resizedOpenHeight > window.Bounds.Height);
            Assert.Equal(draggedPosition.X, window.Position.X);
            Assert.Equal(resizedOpenPosition.Y + (int)Math.Round(
                (resizedOpenHeight - window.Bounds.Height) * window.DesktopScaling), window.Position.Y);
            Assert.NotEqual(origin, window.Position);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task ReversingTheDrawerMidAnimationKeepsTheOriginalPositionAnchor()
    {
        var window = new MainWindow(Settings.NewPreview(4), false);
        try
        {
            var (tab, drawer, area, expansion) = PreparePositionedDrawer(window);
            var baselineHeight = window.Bounds.Height;
            var origin = new PixelPoint(area.X + 151,
                area.Y + (int)Math.Ceiling(expansion * window.DesktopScaling) + 81);
            window.Position = origin;
            tab.IsChecked = true;

            // Freeze the real timeline at an intermediate frame, then let headless
            // resize events settle without racing its wall-clock completion.
            SeekDrawerFrameWithoutClock(window, .07);
            await EventuallyAsync(window, () => window.Bounds.Height > baselineHeight);
            Assert.True(drawer.Height < expansion);
            Assert.InRange(window.Position.Y, area.Y + 1, origin.Y - 1);
            var partialHeight = window.Bounds.Height;
            tab.IsChecked = false;

            SeekDrawerFrameWithoutClock(window, .10);
            await EventuallyAsync(window, () => window.Bounds.Height < partialHeight);
            Assert.True(window.Bounds.Height > baselineHeight);
            tab.IsChecked = true;
            await EventuallyAsync(window, () => Math.Abs(drawer.Height - expansion) < .001);
            Assert.Equal(origin.X, window.Position.X);
            Assert.Equal(origin.Y - (int)Math.Round((window.Bounds.Height - baselineHeight) * window.DesktopScaling),
                window.Position.Y);

            tab.IsChecked = false;
            await EventuallyAsync(window, () => drawer.Height == 0);
            Assert.Equal(baselineHeight, window.Bounds.Height);
            Assert.Equal(origin, window.Position);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task DrawerClampsAtTheScreenTopAndStillReturnsToItsOriginalPosition()
    {
        var window = new MainWindow(Settings.NewPreview(4), false);
        try
        {
            var (tab, drawer, area, expansion) = PreparePositionedDrawer(window);
            var baselineHeight = window.Bounds.Height;
            var origin = new PixelPoint(area.X + 127, area.Y + 5);
            window.Position = origin;
            for (var cycle = 0; cycle < 2; cycle++)
            {
                tab.IsChecked = true;
                await EventuallyAsync(window, () => Math.Abs(drawer.Height - expansion) < .001);
                Assert.Equal(origin.X, window.Position.X);
                Assert.Equal(area.Y, window.Position.Y);

                tab.IsChecked = false;
                await EventuallyAsync(window, () => drawer.Height == 0);
                Assert.Equal(baselineHeight, window.Bounds.Height);
                Assert.Equal(origin, window.Position);
            }
        }
        finally { window.Close(); }
    }

    private static (int Dex, bool Shiny) Choice(RadioButton cell)
    {
        var tag = cell.Tag!;
        return ((int)tag.GetType().GetProperty("Dex")!.GetValue(tag)!,
            (bool)tag.GetType().GetProperty("IsShiny")!.GetValue(tag)!);
    }

    private static string TipText(Control control) =>
        Assert.IsType<TooltipText>(Assert.IsType<ToolTip>(ToolTip.GetTip(control)).Content).Text!;

    private static Rect BoundsIn(MainWindow window, Control control) =>
        new Rect(control.Bounds.Size).TransformToAABB(control.TransformToVisual(window)!.Value);

    private static T Field<T>(MainWindow window, string name) =>
        (T)typeof(MainWindow).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(window)!;

    private static void SetEggState(MainWindow window, string state) => Invoke(window, "SetEggState",
        Enum.Parse(typeof(MainWindow).GetNestedType("EggState", BindingFlags.NonPublic)!, state));

    private static void StopAnimation(MainWindow window, string name) =>
        Field<Dictionary<string, Timeline>>(window, "_animations")[name].Stop();

    private static Task InvokeAsync(MainWindow window, string method, params object[] args) =>
        (Task)typeof(MainWindow).GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(window, args)!;

    private static Task<T> InvokeAsync<T>(MainWindow window, string method, params object[] args) =>
        (Task<T>)typeof(MainWindow).GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(window, args)!;

    // Every HTTP request is served by deterministic generated pixels, and all files
    // stay under a temporary cache. The shared collection serializes static seams.
    private sealed class UiAssets : HttpMessageHandler
    {
        private readonly string _directory = Path.Combine(Path.GetTempPath(), "PokeDesk-ui-" + Guid.NewGuid().ToString("N"));
        private readonly string _previousDirectory = SpriteAtlas.CacheDirectory;
        private readonly HttpClient _previousHttp = SpriteAtlas.Http;
        private readonly HttpClient _client;
        private readonly byte[] _normal = Sheet(false);
        private readonly byte[] _shiny = Sheet(true);
        private readonly byte[] _eggs = EggSheet();
        public int MissingSpriteDex { get; init; }
        public ConcurrentQueue<string> Requests { get; } = new();

        public UiAssets()
        {
            PokemonIcons.ClearCacheForTests();
            EggArtwork.ClearCacheForTests();
            SpriteAtlas.CacheDirectory = _directory;
            _client = new HttpClient(this, disposeHandler: false);
            SpriteAtlas.Http = _client;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var path = request.RequestUri!.AbsolutePath;
            Requests.Enqueue(path);
            if (MissingSpriteDex != 0 && path.Contains($"/{MissingSpriteDex}."))
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            if (path.Contains("egg_crack")) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            var shiny = path.Contains("/shiny/");
            var egg = path.Contains("/egg/egg.");
            byte[] data;
            if (path.EndsWith(".json"))
            {
                var frames = new List<object>();
                object Frame(string name, int x, int y, int width, int height, int offsetX = 0, int offsetY = 0) => new
                {
                    filename = name, frame = new { x, y, w = width, h = height },
                    sourceSize = new { w = width + offsetX + 8, h = height + offsetY + 8 },
                    spriteSourceSize = new { x = offsetX, y = offsetY, w = width, h = height },
                };
                if (path.Contains("pokemon_icons_1"))
                    for (var dex = 1; dex <= 151; dex++)
                    {
                        var key = PokemonForms.SpriteKey(dex);
                        frames.Add(Frame(key + ".png", 8, 8, 24, 30));
                        frames.Add(Frame(key.Insert(dex.ToString().Length, "s") + ".png", 120, 8, 24, 30));
                    }
                else if (egg)
                    foreach (var kind in Enum.GetValues<EggKind>())
                        frames.Add(Frame(kind == EggKind.Shiny ? "egg_manaphy" : "egg_" + (int)kind,
                            (int)kind * 32, 0, kind == EggKind.Shiny ? 26 : 28, kind == EggKind.Shiny ? 31 : 30));
                else
                {
                    frames.Add(Frame("0001.png", 0, 0, 100, 80, 10, 12));
                    frames.Add(Frame("0002.png", 112, 0, 100, 80, 12, 14));
                }
                data = JsonSerializer.SerializeToUtf8Bytes(new { frames, meta = new { size = new { w = 256, h = 96 } } });
            }
            else if (path.EndsWith(".png")) data = egg ? _eggs : shiny ? _shiny : _normal;
            else return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(data) });
        }

        private static byte[] Sheet(bool shiny)
        {
            var pixels = new SpritePixels(256, 96);
            for (var y = 2; y < 80; y++)
            for (var x = 2; x < 212; x++)
            {
                if (x is >= 100 and < 114) continue;
                var offset = y * pixels.Stride + x * 4;
                var alternate = x >= 112;
                var eye = y is >= 18 and <= 21 && (x % 112 is >= 24 and <= 27 or >= 72 and <= 75);
                pixels.Pixels[offset] = eye ? (byte)25 : shiny || alternate ? (byte)190 : (byte)50;
                pixels.Pixels[offset + 1] = eye ? (byte)25 : (byte)(y > 55 ? 210 : 130);
                pixels.Pixels[offset + 2] = eye ? (byte)25 : shiny || alternate ? (byte)70 : (byte)240;
                pixels.Pixels[offset + 3] = 255;
            }
            return Png(pixels);
        }

        private static byte[] EggSheet()
        {
            var pixels = new SpritePixels(160, 32);
            foreach (var kind in Enum.GetValues<EggKind>())
            {
                var width = kind == EggKind.Shiny ? 26 : 28;
                var height = kind == EggKind.Shiny ? 31 : 30;
                for (var y = 1; y < height; y++)
                for (var x = 2; x < width - 2; x++)
                {
                    if (y < 6 && (x < 7 || x >= width - 7)) continue;
                    var offset = y * pixels.Stride + ((int)kind * 32 + x) * 4;
                    var spot = (x + y * 2) % 13 < 4;
                    pixels.Pixels[offset] = spot ? (byte)(40 + (int)kind * 42) : (byte)190;
                    pixels.Pixels[offset + 1] = spot ? (byte)(170 - (int)kind * 27) : (byte)225;
                    pixels.Pixels[offset + 2] = spot ? (byte)(210 - (int)kind * 31) : (byte)245;
                    pixels.Pixels[offset + 3] = 255;
                }
            }
            return Png(pixels);
        }

        private static byte[] Png(SpritePixels pixels)
        {
            using var bitmap = pixels.ToBitmap();
            using var stream = new MemoryStream();
            bitmap.Save(stream);
            return stream.ToArray();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                PokemonIcons.ClearCacheForTests();
                EggArtwork.ClearCacheForTests();
                SpriteAtlas.Http = _previousHttp;
                SpriteAtlas.CacheDirectory = _previousDirectory;
                _client.Dispose();
                if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
            }
            base.Dispose(disposing);
        }
    }

    private static void ShowAndLayout(MainWindow window)
    {
        window.Show();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
    }

    private static void Hover(MainWindow window, Control control, Point? point = null)
    {
        var local = point ?? new Point(control.Bounds.Width / 2, control.Bounds.Height / 2);
        var position = control.TranslatePoint(local, window);
        Assert.NotNull(position);
        window.MouseMove(position.Value, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
    }

    private static async Task EventuallyAsync(MainWindow window, Func<bool> condition)
    {
        var timeout = System.Diagnostics.Stopwatch.StartNew();
        do
        {
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            if (condition()) return;
            await Task.Delay(20);
        } while (timeout.Elapsed < TimeSpan.FromSeconds(3));
        Assert.True(condition(), "The expected UI state was not reached within three seconds.");
    }

    private static void SeekDrawerFrameWithoutClock(MainWindow window, double seconds)
    {
        var animation = (Timeline)typeof(MainWindow).GetField("_drawerAnimation", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(window)!;
        animation.Dispose(); // Stop automatic ticks; the next toggle replaces this timeline normally.
        typeof(Timeline).GetMethod("Apply", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(animation, [seconds]);
    }

    private static (ToggleButton Tab, Border Drawer, PixelRect Area, double Expansion) PreparePositionedDrawer(MainWindow window)
    {
        ShowAndLayout(window);
        var drawer = window.FindControl<Border>("Drawer")!;
        var content = window.FindControl<Border>("DrawerContent")!;
        content.Measure(new Size(300, double.PositiveInfinity));
        var expansion = content.DesiredSize.Height;
        var area = (PixelRect)typeof(MainWindow).GetProperty("WorkingArea", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(window)!;
        // startServices=false deliberately skips native startup; enable its positioning
        // path after initial layout. This tests the headless backend's real DesktopScaling,
        // not the 2x bitmap export used by the screenshot tests above.
        typeof(MainWindow).GetField("_placed", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(window, true);
        var tab = (ToggleButton)window.FindControl<StackPanel>("MenuTabs")!.Children[0];
        return (tab, drawer, area, expansion);
    }

    private static void Invoke(MainWindow window, string method, params object[] args) =>
        typeof(MainWindow).GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(window, args);

    // Original, deterministic test pixels: no network, cache, random choice, or external artwork.
    private static Bitmap TestSprite()
    {
        var pixels = new SpritePixels(24, 30);
        for (var y = 2; y < 29; y++)
        for (var x = 2; x < 22; x++)
        {
            if (y < 8 && (x < 5 || x > 18)) continue;
            var offset = y * pixels.Stride + x * 4;
            var eye = y is 10 or 11 && x is 7 or 16;
            pixels.Pixels[offset] = eye ? (byte)30 : (byte)60;
            pixels.Pixels[offset + 1] = eye ? (byte)30 : (byte)(y > 18 ? 210 : 135);
            pixels.Pixels[offset + 2] = eye ? (byte)30 : (byte)240;
            pixels.Pixels[offset + 3] = 255;
        }
        return pixels.ToBitmap();
    }

    private static void PopulatePet(MainWindow window, Bitmap icon)
    {
        var sprite = window.FindControl<Image>("Sprite")!;
        sprite.Source = icon;
        sprite.Width = 24;
        sprite.Height = 30;
        var stage = window.FindControl<Canvas>("Stage")!;
        stage.Width = 24;
        stage.Height = 30;
        var zoom = (ScaleTransform)window.FindControl<LayoutTransformControl>("StageZoom")!.LayoutTransform!;
        zoom.ScaleX = zoom.ScaleY = 3.5;
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
    }

    private static void Capture(Control window, string name, int scale)
    {
        window.UpdateLayout();
        var size = window.Bounds.Size;
        using var bitmap = new RenderTargetBitmap(
            new PixelSize((int)Math.Ceiling(size.Width * scale), (int)Math.Ceiling(size.Height * scale)),
            new Vector(96 * scale, 96 * scale));
        bitmap.Render(window);
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root != null && !File.Exists(Path.Combine(root.FullName, "DeskPokemon.csproj"))) root = root.Parent;
        var output = Path.Combine(root?.FullName ?? AppContext.BaseDirectory, "artifacts", "test-results", "screenshots");
        Directory.CreateDirectory(output);
        bitmap.Save(Path.Combine(output, $"{name}-{scale}x.png"));
        var layout = new List<object>();
        CaptureLayout(window, "root", layout);
        File.WriteAllText(Path.Combine(output, $"{name}-{scale}x.layout.json"),
            JsonSerializer.Serialize(layout, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static void CaptureLayout(Visual visual, string path, List<object> layout)
    {
        if (!visual.IsVisible) return;
        var bounds = visual.Bounds;
        var matrix = visual.RenderTransform?.Value ?? Matrix.Identity;
        layout.Add(new
        {
            path,
            type = visual.GetType().Name,
            name = (visual as Control)?.Name,
            bounds = new[] { bounds.X, bounds.Y, bounds.Width, bounds.Height }.Select(v => Math.Round(v, 3)).ToArray(),
            text = (visual as TextBlock)?.Text,
            font = (visual as TextBlock)?.FontFamily.Name,
            fontSize = (visual as TextBlock)?.FontSize,
            foreground = (visual as TextBlock)?.Foreground?.ToString(),
            background = (visual as Border)?.Background?.ToString(),
            opacity = Math.Round(visual.Opacity, 3),
            clip = visual.ClipToBounds,
            transform = new[] { matrix.M11, matrix.M12, matrix.M21, matrix.M22, matrix.M31, matrix.M32 }
                .Select(v => Math.Round(v, 3)).ToArray(),
        });
        var children = visual.GetVisualChildren().ToArray();
        for (var i = 0; i < children.Length; i++) CaptureLayout(children[i], $"{path}/{i}", layout);
    }
}
