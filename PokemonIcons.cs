using System.Collections.Concurrent;
using System.IO;
using System.Text.Json;
using Avalonia.Media.Imaging;

namespace DeskPokemon;

/// <summary>
/// 세대별 아이콘 아틀라스(pokemon_icons_{gen}.json + .png)를 받아 도감 번호 → 아이콘 비트맵으로 푼다.
/// 기본형만 사용. 이로치는 번호 뒤 s가 붙은 프레임(4s, 964s-zero)을 사용한다.
/// 반환한 비트맵은 앱 수명 캐시가 소유하므로 호출자가 Dispose하면 안 된다.
/// </summary>
public static class PokemonIcons
{
    public static readonly (int Gen, int First, int Last)[] Generations =
    [
        (1, 1, 151), (2, 152, 251), (3, 252, 386), (4, 387, 493), (5, 494, 649),
        (6, 650, 721), (7, 722, 809), (8, 810, 905), (9, 906, 1025),
    ];

    private static readonly ConcurrentDictionary<(int Gen, bool IsShiny), Dictionary<int, Bitmap>> Cache = new();
    private static readonly ConcurrentDictionary<int, Lazy<Task>> Loads = new();

    public static int GenOf(int dex)
    {
        foreach (var g in Generations)
            if (dex >= g.First && dex <= g.Last) return g.Gen;
        return 1;
    }

    public static async Task<Dictionary<int, Bitmap>> LoadGenAsync(int gen, bool isShiny = false)
    {
        if (Cache.TryGetValue((gen, isShiny), out var cached)) return cached;
        if (gen < 1 || gen > Generations.Length) throw new ArgumentOutOfRangeException(nameof(gen));

        // 스타터 세 마리가 동시에 같은 세대를 요청해도 다운로드/네이티브 비트맵은 한 번만 만든다.
        var load = Loads.GetOrAdd(gen, static generation =>
            new Lazy<Task>(() => LoadGenCoreAsync(generation)));
        try
        {
            await load.Value;
            return Cache[(gen, isShiny)];
        }
        catch
        {
            // 실패한 요청만 제거해서 다음 시도는 다시 다운로드할 수 있게 한다.
            Loads.TryRemove(new KeyValuePair<int, Lazy<Task>>(gen, load));
            throw;
        }
    }

    private static async Task LoadGenCoreAsync(int gen)
    {
        var jsonPath = await SpriteAtlas.CachedAsync($"pokemon_icons_{gen}.json");
        var pngPath = await SpriteAtlas.CachedAsync($"pokemon_icons_{gen}.png");
        var sheet = SpriteAtlas.LoadSheet(pngPath);

        using var doc = JsonDocument.Parse(File.ReadAllText(jsonPath));
        var frames = new Dictionary<string, JsonElement>();
        foreach (var (name, elem) in SpriteAtlas.EnumerateFrames(doc.RootElement))
            frames[Path.GetFileNameWithoutExtension(name)] = elem;

        // 한 시트에서 일반·이로치를 한 번에 디코딩하고 두 색상의 캐시를 함께 완성한다.
        var (_, first, last) = Generations[gen - 1];
        var normal = new Dictionary<int, Bitmap>();
        var shiny = new Dictionary<int, Bitmap>();
        try
        {
            for (var dex = first; dex <= last; dex++)
            {
                var key = PokemonForms.SpriteKey(dex);
                if (frames.TryGetValue(key, out var regularFrame))
                    normal[dex] = sheet.Crop(SpriteAtlas.ReadRect(regularFrame.GetProperty("frame")));
                var shinyKey = key.Insert(dex.ToString().Length, "s");
                if (frames.TryGetValue(shinyKey, out var shinyFrame))
                    shiny[dex] = sheet.Crop(SpriteAtlas.ReadRect(shinyFrame.GetProperty("frame")));
            }
        }
        catch
        {
            foreach (var bitmap in normal.Values.Concat(shiny.Values)) bitmap.Dispose();
            throw;
        }

        Cache[(gen, false)] = normal;
        Cache[(gen, true)] = shiny;
    }

    /// <summary>이미 로드된 세대의 아이콘 전체. 네트워크 없이 즉시.</summary>
    public static bool TryGetCachedGen(int gen, out Dictionary<int, Bitmap> icons, bool isShiny = false) =>
        Cache.TryGetValue((gen, isShiny), out icons!);

    /// <summary>이미 로드된 세대의 원본 아이콘. 네트워크 없이 즉시.</summary>
    public static bool TryGetCached(int gen, int dex, out Bitmap bmp, bool isShiny = false)
    {
        bmp = null!;
        return Cache.TryGetValue((gen, isShiny), out var icons) && icons.TryGetValue(dex, out bmp!);
    }

    private static readonly Dictionary<(int Dex, bool IsShiny), Bitmap> Silhouettes = new();
    private static readonly object SilhouetteLock = new();

    /// <summary>미보유 종 표시용 실루엣. 알파는 유지하고 색만 어둡게. 도감 번호별 캐시.</summary>
    public static Bitmap SilhouetteOf(int dex, Bitmap icon, bool isShiny = false)
    {
        lock (SilhouetteLock)
        {
            if (Silhouettes.TryGetValue((dex, isShiny), out var cached)) return cached;

            // Windows와 macOS 모두 BGRA 비프리멀티로 변환한 뒤 알파를 유지한다.
            var pixels = SpritePixels.CopyFrom(icon);
            var px = pixels.Pixels;
            for (var i = 0; i < px.Length; i += 4)
                px[i] = px[i + 1] = px[i + 2] = 0x28;

            var bmp = pixels.ToBitmap();
            Silhouettes[(dex, isShiny)] = bmp;
            return bmp;
        }
    }

    /// <summary>비트맵 사용자와 진행 중인 로드가 없는 격리 테스트에서만 호출한다.</summary>
    internal static void ClearCacheForTests()
    {
        foreach (var bitmap in Cache.Values.SelectMany(icons => icons.Values).Distinct()) bitmap.Dispose();
        Cache.Clear();
        Loads.Clear();
        lock (SilhouetteLock)
        {
            foreach (var bitmap in Silhouettes.Values) bitmap.Dispose();
            Silhouettes.Clear();
        }
    }
}
