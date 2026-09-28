using System.IO;
using System.Text.Json;

namespace DeskPokemon;

public sealed record PokemonDetail(int Dex, string Description, string[] Types);

/// <summary>Bundled Korean species information; usable without a network or UI lifetime.</summary>
public static class PokemonDetails
{
    private static readonly IReadOnlyDictionary<int, PokemonDetail> Details =
        ReadData<PokemonDetail[]>("pokemon-details.json").ToDictionary(p => p.Dex);

    public static PokemonDetail For(int dex) => Details.TryGetValue(dex, out var detail)
        ? detail : throw new ArgumentOutOfRangeException(nameof(dex));

    internal static T ReadData<T>(string name)
    {
        using var stream = typeof(PokemonDetails).Assembly.GetManifestResourceStream("DeskPokemon.Assets.Data." + name)
            ?? throw new InvalidDataException($"Missing bundled Pokemon data: {name}");
        return JsonSerializer.Deserialize<T>(stream) ?? throw new InvalidDataException($"Invalid bundled Pokemon data: {name}");
    }
}
