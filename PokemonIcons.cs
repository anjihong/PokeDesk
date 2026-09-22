using System.IO;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DeskPokemon;

/// <summary>
/// 세대별 아이콘 아틀라스(pokemon_icons_{gen}.json + .png)를 받아 도감 번호 → 아이콘 비트맵으로 푼다.
/// 기본형만 사용. 이로치는 같은 시트의 번호 뒤 s가 붙은 프레임(4s, 964s-zero)을 사용한다.
/// </summary>
public static class PokemonIcons
{
    public static readonly (int Gen, int First, int Last)[] Generations =
    [
        (1, 1, 151), (2, 152, 251), (3, 252, 386), (4, 387, 493), (5, 494, 649),
        (6, 650, 721), (7, 722, 809), (8, 810, 905), (9, 906, 1025),
    ];

    private static readonly Dictionary<(int Gen, bool IsShiny), Dictionary<int, BitmapSource>> Cache = new();
    // UI 스레드에서 호출한다. 일반·이로치 요청도 같은 세대의 디코딩 작업을 공유한다.
    private static readonly Dictionary<int, Task> Loading = new();

    public static int GenOf(int dex)
    {
        foreach (var g in Generations)
            if (dex >= g.First && dex <= g.Last) return g.Gen;
        return 1;
    }

    public static async Task<Dictionary<int, BitmapSource>> LoadGenAsync(int gen, bool isShiny = false)
    {
        if (Cache.TryGetValue((gen, isShiny), out var cached)) return cached;
        if (!Loading.TryGetValue(gen, out var pending))
            Loading[gen] = pending = LoadGenerationAsync(gen);
        try { await pending; }
        finally
        {
            if (Loading.TryGetValue(gen, out var current) && ReferenceEquals(current, pending))
                Loading.Remove(gen);
        }
        return Cache[(gen, isShiny)];
    }

    private static async Task LoadGenerationAsync(int gen)
    {
        var jsonPath = await SpriteAtlas.CachedAsync($"pokemon_icons_{gen}.json");
        var pngPath = await SpriteAtlas.CachedAsync($"pokemon_icons_{gen}.png");
        var sheet = SpriteAtlas.LoadSheet(pngPath);

        using var doc = JsonDocument.Parse(File.ReadAllText(jsonPath));
        var frames = new Dictionary<string, JsonElement>();
        foreach (var (name, elem) in SpriteAtlas.EnumerateFrames(doc.RootElement))
            frames[Path.GetFileNameWithoutExtension(name)] = elem;

        // 세대 범위의 각 종에 대해 기본형 키("4", "964-zero")와 이름이 같은 프레임만 채택
        var (_, first, last) = Generations[gen - 1];
        // 하나의 시트에 일반·이로치가 함께 있다. 한 번 디코딩하여 양쪽 캐시가 시트를 공유한다.
        foreach (var shiny in new[] { false, true })
        {
            var result = new Dictionary<int, BitmapSource>();
            for (var dex = first; dex <= last; dex++)
            {
                var key = PokemonForms.SpriteKey(dex);
                if (shiny) key = key.Insert(dex.ToString().Length, "s");
                if (!frames.TryGetValue(key, out var elem)) continue;
                var bmp = new CroppedBitmap(sheet, SpriteAtlas.ReadRect(elem.GetProperty("frame")));
                bmp.Freeze();
                result[dex] = bmp;
            }
            Cache[(gen, shiny)] = result;
        }
    }

    /// <summary>이미 로드된 세대의 아이콘 전체. 네트워크 없이 즉시.</summary>
    public static bool TryGetCachedGen(int gen, out Dictionary<int, BitmapSource> icons, bool isShiny = false) =>
        Cache.TryGetValue((gen, isShiny), out icons!);

    /// <summary>이미 로드된 세대의 원본 아이콘. 네트워크 없이 즉시.</summary>
    public static bool TryGetCached(int gen, int dex, out BitmapSource bmp, bool isShiny = false)
    {
        bmp = null!;
        return Cache.TryGetValue((gen, isShiny), out var icons) && icons.TryGetValue(dex, out bmp!);
    }

    private static readonly Dictionary<(int Dex, bool IsShiny), BitmapSource> Silhouettes = new();

    /// <summary>미보유 종 표시용 실루엣. 알파는 유지하고 색만 어둡게. 도감 번호별 캐시.</summary>
    public static BitmapSource SilhouetteOf(int dex, BitmapSource icon, bool isShiny = false)
    {
        if (Silhouettes.TryGetValue((dex, isShiny), out var cached)) return cached;

        // Bgra32는 비프리멀티라 B,G,R만 덮고 A는 그대로 두면 됨
        var bgra = new FormatConvertedBitmap(icon, PixelFormats.Bgra32, null, 0);
        int w = bgra.PixelWidth, h = bgra.PixelHeight, stride = w * 4;
        var px = new byte[stride * h];
        bgra.CopyPixels(px, stride, 0);
        for (var i = 0; i < px.Length; i += 4)
            px[i] = px[i + 1] = px[i + 2] = 0x28;

        var bmp = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, px, stride);
        bmp.Freeze();
        Silhouettes[(dex, isShiny)] = bmp;
        return bmp;
    }
}
