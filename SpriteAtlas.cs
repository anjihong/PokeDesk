using System.IO;
using System.Net.Http;
using System.Text.Json;
using Avalonia;
using Avalonia.Media.Imaging;
using SkiaSharp;

namespace DeskPokemon;

/// <summary>한 프레임. Bitmap은 시트에서 잘라낸 조각, Offset은 원본 캔버스(SourceSize) 내 위치.</summary>
public sealed record SpriteFrame(Bitmap Bitmap, int OffsetX, int OffsetY, int Width, int Height);

/// <summary>
/// PokeRogue 에셋 저장소의 TexturePacker 아틀라스(pokemon/{id}.json + .png)를
/// 런타임에 받아 사용자별 앱 데이터 폴더의 sprites에 캐시하고 프레임 배열로 푼다.
/// 에셋은 앱에 번들하지 않는다(라이선스: 저장소 README 참고).
/// PokeRogue 쪽이 정지(1프레임)인 종은 PokeAPI 미러의 BW 스타일 GIF를 합성해 같은 프레임 형태로 만든다.
/// 다운로드·시트 로드·프레임 열거 헬퍼는 아이콘 아틀라스(<see cref="PokemonIcons"/>)와 공유.
/// </summary>
public sealed class SpriteAtlas : IDisposable
{
    private const string BaseUrl =
        "https://raw.githubusercontent.com/pagefaultgames/pokerogue-assets/beta/images/";

    /// <summary>dex 650 초과는 Smogon 커뮤니티 제작 BW 스타일 스프라이트.</summary>
    private const string GifUrl =
        "https://raw.githubusercontent.com/PokeAPI/sprites/master/sprites/pokemon/versions/generation-v/black-white/animated/";

    private static readonly string CacheDir = AppPaths.SpriteCache;

    private static readonly HttpClient Http = new();
    private bool _disposed;

    public int Width { get; }
    public int Height { get; }
    public SpriteFrame[] Frames { get; }

    /// <summary>
    /// 모든 프레임의 실제 픽셀 영역 합집합(캔버스 좌표). 9세대처럼 96x96 고정 캔버스에 여백이 많은 경우
    /// 캔버스가 아니라 이 영역을 기준으로 배율·발 위치를 잡아야 크기가 균일해짐.
    /// </summary>
    public PixelRect Body { get; }

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
        Body = new PixelRect(l, t, Math.Max(1, r - l), Math.Max(1, b - t));
    }

    /// <summary>
    /// 우선순위: PokeRogue exp/(애니메이션 보강) → PokeRogue 기본 → 기본이 정지면 PokeAPI GIF.
    /// 6~9세대 정지 334종 중 exp가 278종, GIF가 19종을 메우고 37종은 정지로 남음.
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
            var animated = ParseGif(await CachedAsync($"pokeapi/{dexId}.gif", $"{GifUrl}{dexId}.gif"));
            still?.Dispose();
            return animated;
        }
        catch (HttpRequestException) when (still != null)
        {
            return still; // GIF도 없음 → 정지 그대로
        }
        catch
        {
            still?.Dispose();
            throw;
        }
    }

    /// <summary>캐시 기준 상대 경로를 받아 캐시 경로 반환. 없으면 url(생략 시 pokerogue-assets images/)에서 다운로드.</summary>
    internal static async Task<string> CachedAsync(string relative, string? url = null)
    {
        var path = Path.Combine(CacheDir, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        if (!File.Exists(path))
        {
            var bytes = await Http.GetByteArrayAsync(url ?? BaseUrl + relative);
            // 다른 창/프로세스는 완전히 기록된 파일만 보도록 같은 폴더에서 원자적으로 이동한다.
            var temporary = $"{path}.{Guid.NewGuid():N}.tmp";
            try
            {
                await File.WriteAllBytesAsync(temporary, bytes);
                try
                {
                    File.Move(temporary, path);
                }
                catch (IOException) when (File.Exists(path))
                {
                    // 같은 에셋의 동시 요청이 먼저 캐시를 완성했다.
                }
            }
            finally
            {
                if (File.Exists(temporary)) File.Delete(temporary);
            }
        }
        return path;
    }

    internal static SpritePixels LoadSheet(string pngPath) => SpritePixels.Load(pngPath);

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

    internal static PixelRect ReadRect(JsonElement r) => new(
        r.GetProperty("x").GetInt32(), r.GetProperty("y").GetInt32(),
        r.GetProperty("w").GetInt32(), r.GetProperty("h").GetInt32());

    internal static SpriteAtlas Parse(string jsonPath, string pngPath)
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
    internal static async Task<Dictionary<string, SpriteFrame>> LoadFramesAsync(string relativeBase)
    {
        var jsonPath = await CachedAsync($"{relativeBase}.json");
        var pngPath = await CachedAsync($"{relativeBase}.png");
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
    internal static SpriteAtlas ParseGif(string gifPath)
    {
        using var codec = SKCodec.Create(gifPath) ?? throw new InvalidDataException("GIF를 읽을 수 없음");
        var metadata = codec.FrameInfo;
        if (metadata.Length == 0)
            throw new InvalidDataException("GIF에 프레임이 없음");

        var timeline = new List<(SpriteFrame Frame, int EndMs)>();
        var clock = 0;
        try
        {
            for (var i = 0; i < metadata.Length; i++)
            {
                var canvas = SpritePixels.Decode(codec, i);
                var delay = metadata[i].Duration; // Skia의 지연 단위는 ms
                clock = checked(clock + (delay <= 10 ? 100 : delay)); // 브라우저 관례: 0·1cs는 100ms
                timeline.Add((Snapshot(canvas), clock));
            }

            // 100ms 간격 샘플링: 짧은 프레임은 건너뛰고 긴 프레임은 반복
            var frames = new List<SpriteFrame>();
            for (int t = 0, i = 0; t < clock; t += 100)
            {
                while (timeline[i].EndMs <= t) i++;
                frames.Add(timeline[i].Frame);
            }

            var used = frames.Select(f => f.Bitmap).ToHashSet();
            foreach (var entry in timeline)
                if (!used.Contains(entry.Frame.Bitmap)) entry.Frame.Bitmap.Dispose();
            return new SpriteAtlas(codec.Info.Width, codec.Info.Height, frames.ToArray());
        }
        catch
        {
            foreach (var entry in timeline) entry.Frame.Bitmap.Dispose();
            throw;
        }
    }

    /// <summary>현재 캔버스에서 불투명 픽셀 경계만 잘라 프레임으로. Offset은 캔버스 내 위치.</summary>
    private static SpriteFrame Snapshot(SpritePixels canvas)
    {
        int w = canvas.Width, h = canvas.Height;
        int l = w, t = h, r = -1, b = -1;
        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++)
            {
                if (canvas.Pixels[(y * w + x) * 4 + 3] == 0) continue;
                l = Math.Min(l, x); t = Math.Min(t, y); r = Math.Max(r, x); b = Math.Max(b, y);
            }
        }
        // ponytail: 완전 투명 프레임은 하단 중앙 1px로 둠(Body 합집합 왜곡 최소화). 현재 19종엔 없음.
        var box = r < 0 ? new PixelRect(w / 2, h - 1, 1, 1) : new PixelRect(l, t, r - l + 1, b - t + 1);
        return new SpriteFrame(canvas.Crop(box), box.X, box.Y, box.Width, box.Height);
    }

    private static SpriteFrame MakeFrame(SpritePixels sheet, JsonElement elem)
    {
        var rect = ReadRect(elem.GetProperty("frame"));
        var s = elem.GetProperty("spriteSourceSize");
        var bmp = sheet.Crop(rect);
        return new SpriteFrame(bmp, s.GetProperty("x").GetInt32(), s.GetProperty("y").GetInt32(), rect.Width, rect.Height);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        // 리샘플링한 긴 GIF 프레임은 같은 Bitmap을 여러 번 참조한다.
        foreach (var bitmap in Frames.Select(f => f.Bitmap).Distinct()) bitmap.Dispose();
    }
}
