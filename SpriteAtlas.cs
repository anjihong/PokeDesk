using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DeskPokemon;

/// <summary>한 프레임. Bitmap은 시트에서 잘라낸 조각, Offset은 원본 캔버스(SourceSize) 내 위치.</summary>
public sealed record SpriteFrame(CroppedBitmap Bitmap, int OffsetX, int OffsetY, int Width, int Height);

/// <summary>
/// PokeRogue 에셋 저장소의 TexturePacker 아틀라스(pokemon/{id}.json + .png)를
/// 런타임에 받아 %LOCALAPPDATA%\DeskPokemon\sprites 에 캐시하고 프레임 배열로 푼다.
/// 에셋은 앱에 번들하지 않는다(라이선스: 저장소 README 참고).
/// PokeRogue 쪽이 정지(1프레임)인 종은 PokeAPI 미러의 BW 스타일 GIF를 합성해 같은 프레임 형태로 만든다.
/// 다운로드·시트 로드·프레임 열거 헬퍼는 아이콘 아틀라스(<see cref="PokemonIcons"/>)와 공유.
/// </summary>
public sealed class SpriteAtlas
{
    private const string BaseUrl =
        "https://raw.githubusercontent.com/pagefaultgames/pokerogue-assets/beta/images/";

    /// <summary>dex 650 초과는 Smogon 커뮤니티 제작 BW 스타일 스프라이트.</summary>
    private const string GifUrl =
        "https://raw.githubusercontent.com/PokeAPI/sprites/master/sprites/pokemon/versions/generation-v/black-white/animated/";

    internal static string CacheDirectory { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DeskPokemon", "sprites");

    internal static HttpClient Http { get; set; } = new() { Timeout = TimeSpan.FromSeconds(10) };
    private static readonly Dictionary<string, Task<string>> Downloads = new(StringComparer.OrdinalIgnoreCase);

    public int Width { get; }
    public int Height { get; }
    public SpriteFrame[] Frames { get; }

    /// <summary>
    /// 모든 프레임의 실제 픽셀 영역 합집합(캔버스 좌표). 9세대처럼 96x96 고정 캔버스에 여백이 많은 경우
    /// 캔버스가 아니라 이 영역을 기준으로 배율·발 위치를 잡아야 크기가 균일해짐.
    /// </summary>
    public Int32Rect Body { get; }

    private SpriteAtlas(int width, int height, SpriteFrame[] frames)
    {
        Width = width;
        Height = height;
        Frames = frames;

        int l = int.MaxValue, t = int.MaxValue, r = 0, b = 0;
        foreach (var f in frames)
        {
            l = Math.Min(l, f.OffsetX);
            t = Math.Min(t, f.OffsetY);
            r = Math.Max(r, f.OffsetX + f.Width);
            b = Math.Max(b, f.OffsetY + f.Height);
        }
        Body = new Int32Rect(l, t, Math.Max(1, r - l), Math.Max(1, b - t));
    }

    /// <summary>
    /// 일반: PokeRogue exp/ → 기본 → PokeAPI GIF.
    /// 아틀라스가 정지이고 GIF가 없으면 정지 아틀라스를 유지한다.
    /// </summary>
    public static async Task<SpriteAtlas> LoadAsync(int dexId)
    {
        var key = PokemonForms.SpriteKey(dexId);
        SpriteAtlas? still = null;
        foreach (var dir in new[] { "pokemon/exp/", "pokemon/" })
        {
            try
            {
                still = Parse(await CachedAsync($"{dir}{key}.json"), await CachedAsync($"{dir}{key}.png"));
                break;
            }
            catch (HttpRequestException)
            {
                // 그 경로에 없음 → 다음 후보
            }
        }
        if (still is { Frames.Length: > 1 }) return still;

        try
        {
            return ParseGif(await CachedAsync($"pokeapi/{dexId}.gif", $"{GifUrl}{dexId}.gif"));
        }
        catch (HttpRequestException) when (still != null)
        {
            return still; // GIF도 없음 → 정지 그대로
        }
    }

    /// <summary>캐시 기준 상대 경로를 받아 캐시 경로 반환. 없으면 url(생략 시 pokerogue-assets images/)에서 다운로드.</summary>
    internal static async Task<string> CachedAsync(string relative, string? url = null)
    {
        var path = Path.GetFullPath(Path.Combine(CacheDirectory, relative));
        Task<string> download;
        lock (Downloads)
        {
            if (!Downloads.TryGetValue(path, out download!))
                Downloads[path] = download = DownloadAsync(path, url ?? BaseUrl + relative);
        }
        try { return await download; }
        finally
        {
            lock (Downloads)
            {
                if (Downloads.TryGetValue(path, out var pending) && ReferenceEquals(pending, download))
                    Downloads.Remove(path); // 성공·실패 모두 제거하여 실패한 요청도 재시도할 수 있다.
            }
        }
    }

    private static async Task<string> DownloadAsync(string path, string url)
    {
        if (File.Exists(path)) return path;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            var bytes = await Http.GetByteArrayAsync(url).ConfigureAwait(false);
            await File.WriteAllBytesAsync(temporary, bytes).ConfigureAwait(false);
            File.Move(temporary, path, overwrite: true); // 완성된 파일만 캐시 경로에 공개한다.
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
        return path;
    }

    internal static BitmapImage LoadSheet(string pngPath)
    {
        var sheet = new BitmapImage();
        sheet.BeginInit();
        sheet.UriSource = new Uri(pngPath);
        sheet.CacheOption = BitmapCacheOption.OnLoad;
        sheet.EndInit();
        sheet.Freeze();
        return sheet;
    }

    /// <summary>
    /// TexturePacker JSON의 프레임 열거. 두 가지 형태 지원:
    /// {"textures":[{"frames":[...]}]} (배열, filename 속성) / {"frames":{"name":{...}}} (해시).
    /// </summary>
    internal static IEnumerable<(string Name, JsonElement Elem)> EnumerateFrames(JsonElement root)
    {
        var frames = root.TryGetProperty("textures", out var textures)
            ? textures[0].GetProperty("frames")
            : root.GetProperty("frames");

        if (frames.ValueKind == JsonValueKind.Array)
        {
            foreach (var f in frames.EnumerateArray())
                yield return (f.GetProperty("filename").GetString()!, f);
        }
        else
        {
            foreach (var p in frames.EnumerateObject())
                yield return (p.Name, p.Value);
        }
    }

    internal static Int32Rect ReadRect(JsonElement r) => new(
        r.GetProperty("x").GetInt32(), r.GetProperty("y").GetInt32(),
        r.GetProperty("w").GetInt32(), r.GetProperty("h").GetInt32());

    private static SpriteAtlas Parse(string jsonPath, string pngPath)
    {
        var sheet = LoadSheet(pngPath);

        using var doc = JsonDocument.Parse(File.ReadAllText(jsonPath));
        var all = EnumerateFrames(doc.RootElement).ToArray();
        if (all.Length == 0)
            throw new InvalidDataException("아틀라스에 프레임이 없음");

        // 배열 순서는 재생 순서가 아님. filename("0001.png") 숫자 기준 정렬.
        var frames = all
            .Select(f => (Index: int.Parse(Path.GetFileNameWithoutExtension(f.Name)), f.Elem))
            .OrderBy(x => x.Index)
            .Select(x => MakeFrame(sheet, x.Elem))
            .ToArray();

        var size = all[0].Elem.GetProperty("sourceSize");
        return new SpriteAtlas(size.GetProperty("w").GetInt32(), size.GetProperty("h").GetInt32(), frames);
    }

    /// <summary>
    /// 이름 프레임 아틀라스(egg/egg, egg/egg_crack 등). 파일명(확장자 제외) → 프레임.
    /// 오프셋은 spriteSourceSize 기준(trimmed 프레임을 원래 캔버스에 놓을 위치).
    /// </summary>
    internal static async Task<Dictionary<string, SpriteFrame>> LoadFramesAsync(string relativeBase, string? version = null)
    {
        var cacheBase = version == null ? relativeBase : $"art/{version}/{relativeBase}";
        var jsonPath = await CachedAsync($"{cacheBase}.json", BaseUrl + relativeBase + ".json");
        var pngPath = await CachedAsync($"{cacheBase}.png", BaseUrl + relativeBase + ".png");
        var sheet = LoadSheet(pngPath);

        using var doc = JsonDocument.Parse(File.ReadAllText(jsonPath));
        var result = new Dictionary<string, SpriteFrame>();
        foreach (var (name, elem) in EnumerateFrames(doc.RootElement))
            result[Path.GetFileNameWithoutExtension(name)] = MakeFrame(sheet, elem);
        return result;
    }

    /// <summary>
    /// 차분 인코딩 GIF를 프레임마다 전체 캔버스로 합성(disposal 0~3)하고 불투명 영역만 잘라
    /// 아틀라스 프레임과 같은 형태로 만든다. 앱은 고정 10fps라 GIF 지연(5~30cs 혼재)을 100ms 간격으로 리샘플.
    /// </summary>
    private static SpriteAtlas ParseGif(string gifPath)
    {
        var bytes = File.ReadAllBytes(gifPath);
        int w = BitConverter.ToUInt16(bytes, 6), h = BitConverter.ToUInt16(bytes, 8); // 논리 화면 크기
        var decoder = new GifBitmapDecoder(
            new MemoryStream(bytes), BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);

        var canvas = new byte[w * h * 4];
        byte[]? restore = null; // disposal 3: 직전 프레임 그리기 전 상태
        var prevRect = Int32Rect.Empty;
        var prevDisposal = 0;
        var timeline = new List<(SpriteFrame Frame, int EndMs)>();
        var clock = 0;

        foreach (var src in decoder.Frames)
        {
            if (prevDisposal == 2) ClearRect(canvas, w, h, prevRect);
            else if (prevDisposal == 3 && restore != null) Buffer.BlockCopy(restore, 0, canvas, 0, canvas.Length);

            var meta = (BitmapMetadata)src.Metadata;
            prevRect = new Int32Rect(Query(meta, "/imgdesc/Left"), Query(meta, "/imgdesc/Top"), src.PixelWidth, src.PixelHeight);
            prevDisposal = Query(meta, "/grctlext/Disposal");
            if (prevDisposal == 3) restore = (byte[])canvas.Clone();

            Blit(canvas, w, h, src, prevRect);
            var delay = Query(meta, "/grctlext/Delay");
            clock += (delay <= 1 ? 10 : delay) * 10; // 브라우저 관례: 0·1cs는 100ms
            timeline.Add((Snapshot(canvas, w, h), clock));
        }

        // 100ms 간격 샘플링: 짧은 프레임은 건너뛰고 긴 프레임은 반복
        var frames = new List<SpriteFrame>();
        for (int t = 0, i = 0; t < clock; t += 100)
        {
            while (timeline[i].EndMs <= t) i++;
            frames.Add(timeline[i].Frame);
        }
        return new SpriteAtlas(w, h, frames.ToArray());
    }

    private static int Query(BitmapMetadata meta, string query) =>
        meta.ContainsQuery(query) ? Convert.ToInt32(meta.GetQuery(query)) : 0;

    private static void ClearRect(byte[] canvas, int w, int h, Int32Rect r)
    {
        var right = Math.Min(w, r.X + r.Width);
        if (right <= r.X) return;
        for (var y = r.Y; y < Math.Min(h, r.Y + r.Height); y++)
            Array.Clear(canvas, (y * w + r.X) * 4, (right - r.X) * 4);
    }

    /// <summary>GIF 투명은 0/255 이진이라 불투명 픽셀만 덮어쓰면 됨.</summary>
    private static void Blit(byte[] canvas, int w, int h, BitmapSource src, Int32Rect at)
    {
        var px = new byte[at.Width * at.Height * 4];
        new FormatConvertedBitmap(src, PixelFormats.Bgra32, null, 0).CopyPixels(px, at.Width * 4, 0);
        for (var y = 0; y < at.Height; y++)
        {
            for (var x = 0; x < at.Width; x++)
            {
                int cx = at.X + x, cy = at.Y + y, s = (y * at.Width + x) * 4;
                if (cx >= w || cy >= h || px[s + 3] == 0) continue;
                Buffer.BlockCopy(px, s, canvas, (cy * w + cx) * 4, 4);
            }
        }
    }

    /// <summary>현재 캔버스에서 불투명 픽셀 경계만 잘라 프레임으로. Offset은 캔버스 내 위치.</summary>
    private static SpriteFrame Snapshot(byte[] canvas, int w, int h)
    {
        int l = w, t = h, r = -1, b = -1;
        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++)
            {
                if (canvas[(y * w + x) * 4 + 3] == 0) continue;
                l = Math.Min(l, x); t = Math.Min(t, y); r = Math.Max(r, x); b = Math.Max(b, y);
            }
        }
        // ponytail: 완전 투명 프레임은 하단 중앙 1px로 둠(Body 합집합 왜곡 최소화). 현재 19종엔 없음.
        var box = r < 0 ? new Int32Rect(w / 2, h - 1, 1, 1) : new Int32Rect(l, t, r - l + 1, b - t + 1);
        var bmp = new CroppedBitmap(BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, canvas, w * 4), box);
        bmp.Freeze();
        return new SpriteFrame(bmp, box.X, box.Y, box.Width, box.Height);
    }

    private static SpriteFrame MakeFrame(BitmapImage sheet, JsonElement elem)
    {
        var rect = ReadRect(elem.GetProperty("frame"));
        var s = elem.GetProperty("spriteSourceSize");
        var bmp = new CroppedBitmap(sheet, rect);
        bmp.Freeze();
        return new SpriteFrame(bmp, s.GetProperty("x").GetInt32(), s.GetProperty("y").GetInt32(), rect.Width, rect.Height);
    }
}
