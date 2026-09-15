namespace DeskPokemon;

/// <summary>부화 결과. IsNew=false면 중복이라 레벨만 오른 것, Level은 반영 후 값.</summary>
public readonly record struct HatchResult(int Dex, bool IsNew, int Level);

/// <summary>공식 포획률(<see cref="CaptureRates"/>) 가중 랜덤 뽑기. 포획률 낮은 전설일수록 드묾.</summary>
public static class EggHatcher
{
    // Cumulative[i] = ByDex[1..i] 합. Cumulative[0] = 0.
    private static readonly int[] Cumulative = Build();

    private static int[] Build()
    {
        var c = new int[CaptureRates.ByDex.Length];
        for (var i = 1; i < c.Length; i++) c[i] = c[i - 1] + CaptureRates.ByDex[i];
        return c;
    }

    /// <summary>도감 번호 1~1025 중 하나. 각 종의 확률 = 포획률 / 전체 포획률 합.</summary>
    public static int Roll(Random? rng = null)
    {
        var r = (rng ?? Random.Shared).Next(Cumulative[^1]); // 0 .. total-1
        // Cumulative[i] > r 인 최소 i. 모든 포획률이 1 이상이라 단조 증가.
        var i = Array.BinarySearch(Cumulative, 1, Cumulative.Length - 1, r + 1);
        return i >= 0 ? i : ~i;
    }
}
