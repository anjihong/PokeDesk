using Xunit;

namespace DeskPokemon.Tests;

public class ProgressTests
{
    [Fact]
    public void NewSaveOwnsOnlyChosenStarterAndKeepsProgressSeparate()
    {
        var settings = Settings.NewPreview(906);
        Assert.Equal(906, settings.SelectedDex);
        Assert.Equal(new[] { 906 }, settings.Owned);
        for (var i = 0; i < 29; i++) Assert.False(settings.AddExp(906));
        Assert.True(settings.AddExp(906));
        Assert.Equal((2, 0), (settings.For(906).Level, settings.For(906).Exp));
        Assert.Equal((1, 0), (settings.For(4).Level, settings.For(4).Exp));
    }

    [Fact]
    public void TenThousandInputsReachLevel26AndKeepRemainingExperience()
    {
        var settings = Settings.NewPreview(4);
        for (var i = 0; i < 10_000; i++) settings.AddExp(4);
        Assert.Equal((26, 250), (settings.For(4).Level, settings.For(4).Exp));
    }

    [Fact]
    public void EggWaitsForFullIntervalPausesUntilHatchedAndAwardsOnlyBaseSpecies()
    {
        var settings = Settings.NewPreview(4);
        Assert.False(settings.TickEgg(Settings.EggIntervalSeconds - 1));
        Assert.Equal(1, settings.RemainingEggSeconds);
        Assert.True(settings.TickEgg(1));
        Assert.False(settings.TickEgg(5000));
        Assert.Equal(1, settings.Eggs);
        Assert.Equal(0, settings.EggSeconds);
        Assert.Equal(0, settings.RemainingEggSeconds);
        var hatch = settings.Hatch()!.Value;
        Assert.Contains(hatch.Dex, BaseSpecies.Dex);
        Assert.True(settings.IsOwned(hatch.Dex, hatch.IsShiny));
        Assert.Null(settings.Hatch());
        Assert.Equal(Settings.EggIntervalSeconds, settings.RemainingEggSeconds);
    }

    [Fact]
    public void DuplicatePokemonGainsLevelWithoutResettingExperience()
    {
        var settings = Settings.NewPreview(4);
        settings.AddExp(4);
        Assert.False(settings.AddOwned(4));
        Assert.Equal((2, 1), (settings.For(4).Level, settings.For(4).Exp));
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(.06, .8)]
    [InlineData(.14, 1.12)]
    [InlineData(.22, 1)]
    [InlineData(1, 1)]
    public void BounceKeepsOriginalKeyframeValues(double time, double expected) =>
        Assert.Equal(expected, Timeline.Sample([new(0, 1), new(.06, .8), new(.14, 1.12), new(.22, 1)], time), 8);
}
