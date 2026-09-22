namespace DeskPokemon;

internal static class PokemonRarity
{
    // National Dex IDs. Legendary/mythical: PokeAPI pokemon_species.csv flags.
    // https://github.com/PokeAPI/pokeapi/blob/master/data/v2/csv/pokemon_species.csv
    // Paradox: Scarlet/Violet ancient/future species (including DLC), explicitly added.
    // Ultra Beasts are not legendary/mythical/Paradox and remain in the ordinary pool.
    private static readonly HashSet<int> Special =
    [
        144,145,146,150,151,243,244,245,249,250,251,
        377,378,379,380,381,382,383,384,385,386,
        480,481,482,483,484,485,486,487,488,489,490,491,492,493,494,
        638,639,640,641,642,643,644,645,646,647,648,649,
        716,717,718,719,720,721,772,773,785,786,787,788,789,790,791,792,
        800,801,802,807,808,809,888,889,890,891,892,893,894,895,896,897,898,905,
        1001,1002,1003,1004,1007,1008,1014,1015,1016,1017,1024,1025,
        984,985,986,987,988,989,990,991,992,993,994,995,1005,1006,1009,1010,1020,1021,1022,1023
    ];

    public static bool IsSpecial(int dex) => Special.Contains(dex);
}
