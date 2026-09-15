namespace DeskPokemon;

/// <summary>부화 결과. IsNew=false면 중복이라 레벨만 오른 것, Level은 반영 후 값.</summary>
public readonly record struct HatchResult(int Dex, bool IsNew, int Level);

/// <summary>
/// 진화 전 포켓몬(<see cref="BaseSpecies"/>) 중 공식 포획률(<see cref="CaptureRates"/>) 가중 랜덤 뽑기.
/// 포획률 낮은 전설일수록 드묾.
/// </summary>
public static class EggHatcher
{
    // Pool[k] = 기본형 도감 번호, Cumulative[k] = Pool[0..k] 포획률 합(포함).
    // 모든 포획률이 1 이상이라 순증가 → 이진 탐색 결과가 유일.
    // (진화형 포획률을 0으로 두는 방식은 누적합에 같은 값이 연속돼 진화형이 뽑힐 수 있어 쓰지 않음)
    private static readonly int[] Pool = BaseSpecies.Dex;
    private static readonly int[] Cumulative = Build();

    private static int[] Build()
    {
        var c = new int[Pool.Length];
        var sum = 0;
        for (var k = 0; k < Pool.Length; k++)
        {
            sum += CaptureRates.ByDex[Pool[k]];
            c[k] = sum;
        }
        return c;
    }

    /// <summary>기본형 도감 번호 하나. 각 종의 확률 = 포획률 / 기본형 포획률 합.</summary>
    public static int Roll(Random? rng = null)
    {
        var r = (rng ?? Random.Shared).Next(Cumulative[^1]); // 0 .. total-1
        var k = Array.BinarySearch(Cumulative, r + 1);        // Cumulative[k] > r 인 최소 k
        return Pool[k >= 0 ? k : ~k];
    }
}
