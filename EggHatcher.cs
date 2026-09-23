namespace DeskPokemon;

public readonly record struct HatchResult(int Dex, bool IsShiny, bool IsNew, int Level);

/// <summary>알 생성 시 등급, 그룹 내 균등 종 추첨, 색상 판정을 순서대로 수행한다.</summary>
public static class EggHatcher
{
    private static readonly int[] Ordinary = BaseSpecies.Dex.Where(d => !PokemonRarity.IsSpecial(d)).ToArray();
    private static readonly int[] Special = BaseSpecies.Dex.Where(PokemonRarity.IsSpecial).ToArray();

    public static EggKind RollKind(Random? rng = null)
    {
        var roll = (rng ?? Random.Shared).NextDouble();
        return roll < .475 ? EggKind.Common : roll < .775 ? EggKind.Rare :
            roll < .925 ? EggKind.Epic : roll < .975 ? EggKind.Legendary : EggKind.Shiny;
    }

    public static PendingEgg Create(EggKind? kind = null, bool forceShiny = false, Random? rng = null)
    {
        rng ??= Random.Shared;
        var selected = kind ?? RollKind(rng);
        var pool = selected switch
        {
            EggKind.Common => Ordinary,
            EggKind.Rare => rng.NextDouble() < .15 ? Special : Ordinary,
            EggKind.Epic => rng.NextDouble() < .5 ? Special : Ordinary,
            EggKind.Legendary => Special,
            EggKind.Shiny => BaseSpecies.Dex,
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
        var dex = pool[rng.Next(pool.Length)];
        return new PendingEgg(selected, dex, selected == EggKind.Shiny || forceShiny || rng.NextDouble() < .07);
    }
}
