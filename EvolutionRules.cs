namespace DeskPokemon;

public sealed record EvolutionOption(int TargetDex, int RequiredLevel);
public readonly record struct EvolutionResult(int FromDex, int TargetDex, bool IsShiny, int Level, int Exp);

/// <summary>Compatibility facade over the pinned catalog, including regional and Eevee branches.</summary>
public static class EvolutionRules
{
    private static readonly IReadOnlyDictionary<int, IReadOnlyList<EvolutionOption>> Options = EvolutionData.Rules
        .GroupBy(rule => rule.FromId).ToDictionary(group => group.Key,
            group => (IReadOnlyList<EvolutionOption>)Array.AsReadOnly(group
                .Select(rule => new EvolutionOption(rule.ToId, rule.Level)).ToArray()));

    public static IReadOnlyList<EvolutionOption> OptionsFor(int dex) =>
        Options.TryGetValue(dex, out var options) ? options : Array.Empty<EvolutionOption>();
}
