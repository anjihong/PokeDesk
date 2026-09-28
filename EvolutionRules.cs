namespace DeskPokemon;

public sealed record EvolutionOption(int TargetDex, int RequiredLevel);
public readonly record struct EvolutionResult(int FromDex, int TargetDex, bool IsShiny, int Level, int Exp);

/// <summary>Direct evolution steps from a pinned PokeAPI snapshot, with game-specific level fallbacks.</summary>
public static class EvolutionRules
{
    private sealed record Step(int FromDex, int TargetDex, int RequiredLevel);
    private static readonly Step[] Steps = PokemonDetails.ReadData<Step[]>("evolutions.json");
    private static readonly IReadOnlyDictionary<int, IReadOnlyList<EvolutionOption>> Options = Steps
        .GroupBy(step => step.FromDex).ToDictionary(group => group.Key,
            group => (IReadOnlyList<EvolutionOption>)Array.AsReadOnly(group.Select(step => new EvolutionOption(step.TargetDex, step.RequiredLevel)).ToArray()));
    private static readonly IReadOnlyDictionary<int, int> Parents = Steps.ToDictionary(step => step.TargetDex, step => step.FromDex);

    public static IReadOnlyList<EvolutionOption> OptionsFor(int dex) =>
        Options.TryGetValue(dex, out var options) ? options : Array.Empty<EvolutionOption>();

    internal static IEnumerable<int> SelfAndAncestors(int dex)
    {
        yield return dex;
        while (Parents.TryGetValue(dex, out var parent))
        {
            dex = parent;
            yield return dex;
        }
    }
}
