using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Xunit;

namespace DeskPokemon.Tests;

public partial class UiTests
{
    [AvaloniaTheory]
    [InlineData(2)]
    [InlineData(8)]
    public async Task MaximumOwnedCountFitsEveryGlyphWithoutShrinkingTheNormalCount(int requestedScale)
    {
        var settings = Settings.NewPreview(4);
        settings.UiScale = requestedScale;
        var window = new MainWindow(settings, false);
        try
        {
            ShowAndLayout(window);
            await OpenPanelAsync(window, "dex");
            var count = window.FindControl<TextBlock>("OwnedCount")!;
            var fit = window.FindControl<Viewbox>("OwnedCountFit")!;
            var shiny = window.FindControl<CheckBox>("ShinyDex")!;

            count.Text = "보유 1/2072";
            window.UpdateLayout();
            var normalScale = count.TransformToVisual(fit)!.Value.M11;
            Assert.Equal(1, normalScale, 6);

            count.Text = "보유 2072/2072";
            window.UpdateLayout();
            Assert.Equal(13, count.FontSize);
            Assert.True(count.TextLayout.WidthIncludingTrailingWhitespace <= count.Bounds.Width + .01);
            Assert.False(Assert.Single(count.TextLayout.TextLines).HasCollapsed);
            Assert.True(count.TransformToVisual(fit)!.Value.M11 < normalScale);
            var rendered = BoundsIn(window, count);
            var available = BoundsIn(window, fit);
            Assert.True(rendered.Left >= available.Left - .01);
            Assert.True(rendered.Right <= available.Right + .01);
            Assert.True(BoundsIn(window, shiny).Right <= rendered.Left);
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(2)]
    [InlineData(8)]
    public async Task LongRegionalNameUsesEllipsisBesideShinyLevelAndRetainsTheFullTooltip(int requestedScale)
    {
        var settings = Settings.NewPreview(4);
        settings.UiScale = requestedScale;
        settings.AddOwned(4263, true);
        settings.For(4263, true).Level = 100;
        settings.SelectedDex = 4263;
        settings.SelectedShiny = true;
        var window = new MainWindow(settings, false);
        try
        {
            ShowAndLayout(window);
            await WaitForPresentationAsync(window);
            var name = window.FindControl<TextBlock>("PetName")!;
            var level = window.FindControl<TextBlock>("LevelText")!;
            Assert.Equal("가라르 지그제구리", name.Text);
            Assert.Equal("★ Lv. 100", level.Text);
            Assert.True(Assert.Single(name.TextLayout.TextLines).HasCollapsed);
            Assert.True(name.TextLayout.Width <= name.Bounds.Width + .01);
            Assert.True(BoundsIn(window, name).Right <= BoundsIn(window, level).Left);
            Assert.Equal(name.Text, Assert.IsType<string>(ToolTip.GetTip(name)));

            settings.SelectedDex = 4;
            settings.SelectedShiny = false;
            Invoke(window, "UpdateLevelUi");
            window.UpdateLayout();
            Assert.Equal("파이리", name.Text);
            Assert.False(Assert.Single(name.TextLayout.TextLines).HasCollapsed);
            Assert.Equal("파이리", Assert.IsType<string>(ToolTip.GetTip(name)));
        }
        finally { window.Close(); }
    }
}
