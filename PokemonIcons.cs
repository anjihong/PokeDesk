using System.IO;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DeskPokemon;

/// <summary>
/// 세대별 아이콘 아틀라스(pokemon_icons_{gen}.json + .png)를 받아 도감 번호 → 아이콘 비트맵으로 푼다.
/// 기본형만 사용: 프레임명이 <see cref="PokemonForms.SpriteKey"/>와 같은 것만 채택("4s" 색違い, "6-mega-x" 등은 무시).
/// </summary>
public static class PokemonIcons
{
    public static readonly (int Gen, int First, int Last)[] Generations =
    [
        (1, 1, 151), (2, 152, 251), (3, 252, 386), (4, 387, 493), (5, 494, 649),
        (6, 650, 721), (7, 722, 809), (8, 810, 905), (9, 906, 1025),
    ];

    private static readonly Dictionary<int, Dictionary<int, BitmapSource>> Cache = new();

    public static int GenOf(int dex)
    {
        foreach (var g in Generations)
            if (dex >= g.First && dex <= g.Last) return g.Gen;
        return 1;
    }

    public static async Task<Dictionary<int, BitmapSource>> LoadGenAsync(int gen)
    {
        if (Cache.TryGetValue(gen, out var cached)) return cached;

        var jsonPath = await SpriteAtlas.CachedAsync($"pokemon_icons_{gen}.json");
        var pngPath = await SpriteAtlas.CachedAsync($"pokemon_icons_{gen}.png");
        var sheet = SpriteAtlas.LoadSheet(pngPath);

        using var doc = JsonDocument.Parse(File.ReadAllText(jsonPath));
        var frames = new Dictionary<string, JsonElement>();
        foreach (var (name, elem) in SpriteAtlas.EnumerateFrames(doc.RootElement))
            frames[Path.GetFileNameWithoutExtension(name)] = elem;

        // 세대 범위의 각 종에 대해 기본형 키("4", "964-zero")와 이름이 같은 프레임만 채택
        var (_, first, last) = Generations[gen - 1];
        var result = new Dictionary<int, BitmapSource>();
        for (var dex = first; dex <= last; dex++)
        {
            if (!frames.TryGetValue(PokemonForms.SpriteKey(dex), out var elem)) continue;
            var bmp = new CroppedBitmap(sheet, SpriteAtlas.ReadRect(elem.GetProperty("frame")));
            bmp.Freeze();
            result[dex] = bmp;
        }

        Cache[gen] = result;
        return result;
    }

    /// <summary>이미 로드된 세대의 아이콘 전체. 네트워크 없이 즉시.</summary>
    public static bool TryGetCachedGen(int gen, out Dictionary<int, BitmapSource> icons) =>
        Cache.TryGetValue(gen, out icons!);

    /// <summary>이미 로드된 세대의 원본 아이콘. 네트워크 없이 즉시.</summary>
    public static bool TryGetCached(int gen, int dex, out BitmapSource bmp)
    {
        bmp = null!;
        return Cache.TryGetValue(gen, out var icons) && icons.TryGetValue(dex, out bmp!);
    }

    private static readonly Dictionary<int, BitmapSource> Silhouettes = new();

    /// <summary>미보유 종 표시용 실루엣. 알파는 유지하고 색만 어둡게. 도감 번호별 캐시.</summary>
    public static BitmapSource SilhouetteOf(int dex, BitmapSource icon)
    {
        if (Silhouettes.TryGetValue(dex, out var cached)) return cached;

        // Bgra32는 비프리멀티라 B,G,R만 덮고 A는 그대로 두면 됨
        var bgra = new FormatConvertedBitmap(icon, PixelFormats.Bgra32, null, 0);
        int w = bgra.PixelWidth, h = bgra.PixelHeight, stride = w * 4;
        var px = new byte[stride * h];
        bgra.CopyPixels(px, stride, 0);
        for (var i = 0; i < px.Length; i += 4)
            px[i] = px[i + 1] = px[i + 2] = 0x28;

        var bmp = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, px, stride);
        bmp.Freeze();
        Silhouettes[dex] = bmp;
        return bmp;
    }
}
