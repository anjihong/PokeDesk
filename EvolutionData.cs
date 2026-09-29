using System.Text.Json;

namespace DeskPokemon;

public sealed record PokemonForm(int Id, int Dex, int Generation, string Name, string SpriteKey);
public sealed record EvolutionRule(int FromId, int ToId, int Level, string LevelSource);

/// <summary>검증된 정적 진화 규칙과 지역 모습 카탈로그. 정수 ID와 전국도감 번호는 별개다.</summary>
public static class EvolutionData
{
    private sealed record Data(PokemonForm[] Forms, int[] EggPool, EvolutionRule[] Rules);
    private static readonly Data Catalog = Load();
    public static IReadOnlyList<PokemonForm> Forms => Catalog.Forms;
    public static IReadOnlyList<EvolutionRule> Rules => Catalog.Rules;
    public static IReadOnlyList<int> EggPool => Catalog.EggPool;
    public const int Count = 1036;
    private static readonly Dictionary<int, PokemonForm> Regional = Catalog.Forms.ToDictionary(f => f.Id);
    private static readonly ILookup<int, EvolutionRule> Outgoing = Catalog.Rules.ToLookup(r => r.FromId);

    private static Data Load()
    {
        using var stream = typeof(EvolutionData).Assembly.GetManifestResourceStream("DeskPokemon.Assets.Data.evolutions.json")
            ?? throw new InvalidOperationException("Missing evolution catalog.");
        return JsonSerializer.Deserialize<Data>(stream, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidOperationException("Invalid evolution catalog.");
    }

    public static bool Contains(int id) => id is >= 1 and <= 1025 || Regional.ContainsKey(id);
    public static PokemonForm? Form(int id) => Regional.GetValueOrDefault(id);
    public static int NationalDex(int id) => Form(id)?.Dex ?? id;
    public static EvolutionRule[] From(int id) => Outgoing[id].ToArray();
    public static bool IsBranch(int id) => Outgoing[id].Count() > 1;
}
