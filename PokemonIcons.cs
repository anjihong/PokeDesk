using System.IO;
using System.Text.Json;
using System.Windows.Media.Imaging;

namespace DeskPokemon;

/// <summary>
/// 세대별 아이콘 아틀라스(pokemon_icons_{gen}.json + .png)를 받아 도감 번호 → 아이콘 비트맵으로 푼다.
/// 기본형만 사용: 파일명이 순수 숫자인 프레임만 채택("4s" 색違い, "6-mega-x" 등은 무시).
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
        var result = new Dictionary<int, BitmapSource>();
        foreach (var (name, elem) in SpriteAtlas.EnumerateFrames(doc.RootElement))
        {
            if (!int.TryParse(Path.GetFileNameWithoutExtension(name), out var dex)) continue;
            var bmp = new CroppedBitmap(sheet, SpriteAtlas.ReadRect(elem.GetProperty("frame")));
            bmp.Freeze();
            result[dex] = bmp;
        }

        Cache[gen] = result;
        return result;
    }
}
