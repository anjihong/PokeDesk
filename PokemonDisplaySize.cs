namespace DeskPokemon;

/// <summary>Gentle species size differences, shared by both colors and evolution rendering.</summary>
public static class PokemonDisplaySize
{
    private sealed record Data(Dictionary<int, int> Heights);
    private static readonly Dictionary<int, int> Heights = PokemonDetails.ReadData<Data>("pokemon-sizes.json").Heights;

    public static double TargetHeight(int dex)
    {
        if (!Heights.TryGetValue(dex, out var decimeters)) return 84;
        // Compress source height differences; a giant species never dwarfs a small one.
        // Match the reference's ~77 DIP Charmander while keeping gradual evolution sizes.
        // Charmander / Charmeleon / Charizard: 76 / 86 / 94 DIP.
        var target = 84 + 12 * Math.Log2(decimeters / 10.0);
        return Math.Clamp(Math.Round(target / 2, MidpointRounding.AwayFromZero) * 2, 72, 100);
    }
}
