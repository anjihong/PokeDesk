using Xunit;

namespace DeskPokemon.Tests;

public class PokemonDisplaySizeTests
{
    [Fact]
    public void CharmanderLineGrowsGentlyAndExtremeSpeciesStayWithinAModestRange()
    {
        Assert.Equal(96, PokemonDisplaySize.TargetHeight(4));
        Assert.Equal(106, PokemonDisplaySize.TargetHeight(5));
        Assert.Equal(114, PokemonDisplaySize.TargetHeight(6));
        var heights = Enumerable.Range(1, 1025).Concat(EvolutionData.Forms.Select(form => form.Id))
            .Select(PokemonDisplaySize.TargetHeight).ToArray();
        Assert.All(heights, height => Assert.InRange(height, 92, 120));
        Assert.True(heights.Distinct().Count() > 5);
        Assert.True(heights.Max() / heights.Min() < 1.31);
        Assert.NotEqual(PokemonDisplaySize.TargetHeight(8194), PokemonDisplaySize.TargetHeight(980));
    }
}
