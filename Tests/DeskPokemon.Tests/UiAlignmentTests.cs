using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Xunit;

namespace DeskPokemon.Tests;

public partial class UiTests
{
    [AvaloniaTheory]
    [InlineData(2)]
    [InlineData(8)]
    public async Task EggCaptionAndExperiencePlateShareTheirBottomWithDifferentPetHeights(int requestedScale)
    {
        var settings = Settings.NewPreview(4);
        settings.UiScale = requestedScale;
        settings.PendingEgg = new(EggKind.Rare, 4, false);
        var window = new MainWindow(settings, false);
        using var pet = TestSprite();
        try
        {
            ShowAndLayout(window);
            PopulatePet(window, pet);
            var stage = window.FindControl<Canvas>("Stage")!;
            var caption = window.FindControl<TextBlock>("BubbleText")!;
            foreach (var height in new[] { 30d, 55d })
            foreach (var text in new[] { "30:00", "레어 알\n클릭하여 부화", "★ 이로치\n리자몽" })
            {
                stage.Height = height;
                caption.Text = text;
                window.UpdateLayout();
                Invoke(window, "ApplyPresentation");
                await WaitForPresentationAsync(window);

                var plate = BoundsIn(window, window.FindControl<StackPanel>("ExpGaugeRow")!);
                var bubble = BoundsIn(window, window.FindControl<Grid>("Bubble")!);
                Assert.InRange(Math.Abs(plate.Bottom - bubble.Bottom), 0, 1);
                Assert.True(bubble.Left > plate.Right);
                AssertPresentationFits(window, requestedScale);
            }
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(2)]
    [InlineData(8)]
    public async Task DetailHeaderWrapsLongNamesAndTwoTypesAboveItsSeparator(int requestedScale)
    {
        var settings = Settings.NewPreview(4);
        settings.UiScale = requestedScale;
        var window = new MainWindow(settings, false);
        using var pet = TestSprite();
        try
        {
            ShowAndLayout(window);
            PopulatePet(window, pet);
            await OpenPanelAsync(window, "dex");
            var name = window.FindControl<TextBlock>("DexDetailName")!;
            var types = window.FindControl<TextBlock>("DexDetailTypes")!;
            var number = window.FindControl<TextBlock>("DexDetailNumber")!;
            var numberBadge = window.FindControl<PixelSurface>("DexNumberBadge")!;
            var typeBadge = window.FindControl<PixelSurface>("DexTypeBadge")!;
            var separator = window.FindControl<Border>("DexDetailSeparator")!;
            var description = window.FindControl<TextBlock>("DexDetailDescription")!;
            var header = window.FindControl<Grid>("DexDetailHeader")!;

            foreach (var (longName, typeText) in new[]
            {
                (false, "불꽃"), (false, "물"), (true, "드래곤 · 에스퍼")
            })
            {
                name.Text = longName ? "아주 긴 포켓몬 이름과 이로치 표시 ★" : "파이리";
                types.Text = typeText;
                number.Text = longName ? "No.1025" : "No.0004";
                window.UpdateLayout();
                Invoke(window, "ApplyPresentation");
                await WaitForPresentationAsync(window);
                await EventuallyAsync(window, () => Math.Abs(window.FindControl<Border>("Drawer")!.Height -
                    Field<double>(window, "_drawerTargetHeight")) < .001);

                var nameBounds = BoundsIn(window, name);
                var typeBounds = BoundsIn(window, typeBadge);
                var typeTextBounds = BoundsIn(window, types);
                Assert.True(types.TextLayout.Width > 0);
                // A stretched TextBlock could appear centered while its short text is left-aligned.
                // Verify both natural text width and the actual transformed text rectangle.
                Assert.Equal(types.TextLayout.Width, types.Bounds.Width, 3);
                Assert.Equal(typeBounds.Center.X, typeTextBounds.Center.X, 3);
                Assert.True(typeTextBounds.Left > typeBounds.Left);
                Assert.True(typeTextBounds.Right < typeBounds.Right);
                Assert.InRange(typeBadge.Bounds.Width, 48, 166);
                if (!longName) Assert.Equal(48, typeBadge.Bounds.Width, 3);
                var numberBounds = BoundsIn(window, numberBadge);
                var separatorBounds = BoundsIn(window, separator);
                var headerBounds = BoundsIn(window, header);
                Assert.True(nameBounds.Right <= numberBounds.Left);
                Assert.True(typeBounds.Right <= numberBounds.Left);
                Assert.True(nameBounds.Bottom < separatorBounds.Top);
                Assert.True(typeBounds.Bottom < separatorBounds.Top);
                Assert.True(numberBounds.Bottom < separatorBounds.Top);
                Assert.True(BoundsIn(window, description).Top >= separatorBounds.Bottom);
                Assert.Equal(headerBounds.Right, numberBounds.Right, 3);
                Assert.True(numberBounds.Width > 0);
                Assert.Equal(68, numberBadge.Bounds.Width, 3);
                if (longName)
                {
                    Assert.True(name.Bounds.Height > name.FontSize * 1.5);
                    Assert.True(typeBounds.Top >= nameBounds.Bottom);
                }
                else
                {
                    Assert.True(nameBounds.Top < typeBounds.Bottom && typeBounds.Top < nameBounds.Bottom);
                }
                AssertPresentationFits(window, requestedScale);
            }
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(2)]
    [InlineData(8)]
    public async Task OwnedFilterKeepsItsFirstCellInTheFirstOfSixColumns(int requestedScale)
    {
        using var assets = new UiAssets();
        var settings = Settings.NewPreview(4);
        settings.UiScale = requestedScale;
        settings.AddOwned(5);
        var window = new MainWindow(settings, false);
        using var pet = TestSprite();
        try
        {
            ShowAndLayout(window);
            PopulatePet(window, pet);
            Invoke(window, "BuildGenTabs");
            Invoke(window, "SelectGenTab", 0);
            await WaitForDexAsync(window, 1036);
            await OpenPanelAsync(window, "dex");
            Invoke(window, "ApplyPresentation");
            await WaitForPresentationAsync(window);
            var grid = window.FindControl<WrapPanel>("IconGrid")!;
            Assert.Equal(318, grid.Bounds.Width, 3);
            var firstLeft = BoundsIn(window, (RadioButton)grid.Children[0]).Left;
            var firstTop = ((RadioButton)grid.Children[0]).Bounds.Top;
            Assert.All(grid.Children.OfType<RadioButton>().Take(6), cell => Assert.Equal(firstTop, cell.Bounds.Top));
            Assert.True(((RadioButton)grid.Children[6]).Bounds.Top > firstTop);

            window.FindControl<CheckBox>("OwnedOnly")!.IsChecked = true;
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            Assert.Equal(2, grid.Children.Count);
            Assert.Equal(318, grid.Bounds.Width, 3);
            Assert.Equal(firstLeft, BoundsIn(window, (RadioButton)grid.Children[0]).Left, 3);
            Assert.Equal(0, ((RadioButton)grid.Children[0]).Bounds.Left, 3);
            Assert.Equal(53, ((RadioButton)grid.Children[1]).Bounds.Left, 3);
            Assert.Equal(((RadioButton)grid.Children[0]).Bounds.Top, ((RadioButton)grid.Children[1]).Bounds.Top);
            var count = window.FindControl<TextBlock>("OwnedCount")!;
            count.Text = "보유 2072/2072"; // Maximum mixed collection count must fit beside both filters.
            window.UpdateLayout();
            var ownedBounds = BoundsIn(window, window.FindControl<CheckBox>("OwnedOnly")!);
            var shinyFilter = window.FindControl<CheckBox>("ShinyDex")!;
            var shinyBounds = BoundsIn(window, shinyFilter);
            Assert.True(ownedBounds.Right <= shinyBounds.Left);
            Assert.True(shinyBounds.Right <= BoundsIn(window, count).Left);
            var presenter = Assert.Single(shinyFilter.GetVisualDescendants().OfType<ContentPresenter>());
            var star = Assert.IsAssignableFrom<TextBlock>(presenter.Child);
            Assert.Equal("★", star.Text);
            Assert.True(star.TextLayout.Width > 0);
            Assert.True(star.TextLayout.Width <= presenter.Bounds.Width + .001,
                $"Star glyph width {star.TextLayout.Width} exceeds content width {presenter.Bounds.Width}.");
            Assert.True(star.TextLayout.Height <= presenter.Bounds.Height + .001);
            var starBounds = BoundsIn(window, star);
            foreach (var container in new[] { shinyBounds, BoundsIn(window, presenter) })
            {
                Assert.True(starBounds.Left >= container.Left - .001);
                Assert.True(starBounds.Top >= container.Top - .001);
                Assert.True(starBounds.Right <= container.Right + .001);
                Assert.True(starBounds.Bottom <= container.Bottom + .001);
            }
            AssertPresentationFits(window, requestedScale);
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(2)]
    [InlineData(8)]
    public async Task MenuTabFacesAlignWithTheDrawerInteriorWithoutABackingImage(int requestedScale)
    {
        using var assets = new UiAssets();
        var settings = Settings.NewPreview(4);
        settings.UiScale = requestedScale;
        var window = new MainWindow(settings, false);
        using var pet = TestSprite();
        try
        {
            ShowAndLayout(window);
            PopulatePet(window, pet);
            foreach (var panel in new[] { "dex", "box", "settings" })
            {
                await OpenPanelAsync(window, panel);
                await WaitForPresentationAsync(window);
                var menu = window.FindControl<StackPanel>("MenuTabs")!;
                var row = Assert.IsType<Grid>(menu.GetVisualParent());
                // Only the three buttons draw this row; no rail is hidden behind them.
                Assert.Same(menu, Assert.Single(row.Children));
                var tabs = menu.Children.OfType<ToggleButton>().ToArray();
                Assert.Equal(new[] { "dex", "box", "settings" }, tabs.Select(tab => (string)tab.Tag!).ToArray());
                Assert.Equal(352, row.Bounds.Width, 3);
                Assert.Equal(344, menu.Bounds.Width, 3);
                var menuBounds = BoundsIn(window, menu);
                var drawerFrame = Assert.Single(window.FindControl<Border>("DrawerContent")!
                    .GetVisualDescendants().OfType<PixelSurface>(), surface => surface.Asset == "drawer_frame");
                var drawerBounds = BoundsIn(window, drawerFrame);
                var uiScale = ((ScaleTransform)window.FindControl<LayoutTransformControl>("UiZoom")!
                    .LayoutTransform!).ScaleX;
                var inset = 4 * uiScale;
                Assert.Equal(drawerBounds.Left + inset, menuBounds.Left, 3);
                Assert.Equal(drawerBounds.Right - inset, menuBounds.Right, 3);
                // Check the artwork's actual transformed rectangles, not only button hit boxes.
                var frames = tabs.Select(tab => Assert.Single(tab.GetVisualDescendants().OfType<PixelSurface>(),
                    surface => surface.Name == "PART_Frame")).ToArray();
                Assert.Equal(drawerBounds.Left + inset, BoundsIn(window, frames[0]).Left, 3);
                Assert.Equal(drawerBounds.Right - inset, BoundsIn(window, frames[^1]).Right, 3);
                foreach (var frame in frames)
                {
                    Assert.Equal(116, frame.Bounds.Width, 3);
                    var bounds = BoundsIn(window, frame);
                    Assert.True(bounds.Left >= menuBounds.Left - .001);
                    Assert.True(bounds.Right <= menuBounds.Right + .001);
                }
                // The artwork has a 3 DIP left / 2 DIP right tab border; the drawer's
                // cream face begins 7 DIP from the left and 6 DIP from the right.
                Assert.Equal(drawerBounds.Left + 7 * uiScale,
                    BoundsIn(window, frames[0]).Left + 3 * uiScale, 3);
                Assert.Equal(drawerBounds.Right - 6 * uiScale,
                    BoundsIn(window, frames[^1]).Right - 2 * uiScale, 3);
                AssertPresentationFits(window, requestedScale);
            }
        }
        finally { window.Close(); }
    }

    private static Task WaitForPresentationAsync(MainWindow window) => EventuallyAsync(window, () =>
        Math.Abs(window.Bounds.Width - 352 * ((ScaleTransform)window.FindControl<LayoutTransformControl>("UiZoom")!
            .LayoutTransform!).ScaleX) < .001);

    [AvaloniaFact]
    public async Task MixedOwnedCellsKeepTheirImagesAndMarkOnlyShinyWithACornerStar()
    {
        using var assets = new UiAssets();
        var settings = Settings.NewPreview(4);
        settings.AddOwned(4, true);
        var window = new MainWindow(settings, false);
        try
        {
            ShowAndLayout(window);
            Invoke(window, "BuildGenTabs");
            Invoke(window, "SelectGenTab", 1);
            await WaitForDexAsync(window, 152);
            window.FindControl<CheckBox>("OwnedOnly")!.IsChecked = true;
            await OpenPanelAsync(window, "dex");
            var grid = window.FindControl<WrapPanel>("IconGrid")!;
            Assert.Equal(2, grid.Children.Count);
            foreach (var cell in grid.Children.OfType<RadioButton>())
            {
                var shiny = Choice(cell).Shiny;
                Assert.IsType<Image>(cell.Content);
                Assert.Equal(shiny, cell.Classes.Contains("shiny"));
                var star = Assert.Single(cell.GetVisualDescendants().OfType<TextBlock>(), text => text.Name == "PART_Shiny");
                Assert.Equal(shiny, star.IsVisible);
                Assert.Equal("★", star.Text);
                Assert.False(star.IsHitTestVisible);
                if (shiny)
                {
                    var cellBounds = BoundsIn(window, cell);
                    var badge = BoundsIn(window, star);
                    Assert.True(badge.Left > cellBounds.Center.X);
                    Assert.True(badge.Bottom < cellBounds.Center.Y);
                    Assert.True(badge.Right < cellBounds.Right);
                    Assert.True(badge.Top > cellBounds.Top);
                }
            }
        }
        finally { window.Close(); }
    }
}
