#if DEBUG
using System.Runtime.CompilerServices;
#endif

namespace DeskPokemon;

/// <summary>말풍선·알 위치 오프셋(px). Debug 빌드의 "배치 편집 → 배치 저장"이 이 파일을 다시 씀.</summary>
public static class LayoutDefaults
{
    public const double BubbleX = -5, BubbleY = 18;
    public const double EggX = -28, EggY = 24;

#if DEBUG
    /// <summary>컴파일 시점의 이 파일 경로. 배치 저장 대상.</summary>
    public static string SourcePath => Here();

    // [CallerFilePath]는 호출한 쪽 파일 경로로 채워짐 — 반드시 이 파일 안에서 호출.
    private static string Here([CallerFilePath] string path = "") => path;
#endif
}
