using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using System.Windows.Media.Imaging;

namespace DeskPokemon;

/// <summary>한 프레임. Bitmap은 시트에서 잘라낸 조각, Offset은 원본 캔버스(SourceSize) 내 위치.</summary>
public sealed record SpriteFrame(CroppedBitmap Bitmap, int OffsetX, int OffsetY, int Width, int Height);

/// <summary>
/// PokeRogue 에셋 저장소의 TexturePacker 아틀라스(pokemon/{id}.json + .png)를
/// 런타임에 받아 %LOCALAPPDATA%\DeskPokemon\sprites 에 캐시하고 프레임 배열로 푼다.
/// 에셋은 앱에 번들하지 않는다(라이선스: 저장소 README 참고).
/// 다운로드·시트 로드·프레임 열거 헬퍼는 아이콘 아틀라스(<see cref="PokemonIcons"/>)와 공유.
/// </summary>
public sealed class SpriteAtlas
{
    private const string BaseUrl =
        "https://raw.githubusercontent.com/pagefaultgames/pokerogue-assets/beta/images/";

    private static readonly string CacheDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DeskPokemon", "sprites");

    private static readonly HttpClient Http = new();

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
    /// exp/ 는 애니메이션이 보강된 실험 스프라이트. 6~9세대 정지 종 334개 중 278개가 여기 있음.
    /// 없는 56종은 기존 경로(정지)로 폴백.
    /// </summary>
    public static async Task<SpriteAtlas> LoadAsync(int dexId)
    {
        var key = PokemonForms.SpriteKey(dexId);
        foreach (var dir in new[] { "pokemon/exp/", "pokemon/" })
        {
            try
            {
                return Parse(await CachedAsync($"{dir}{key}.json"), await CachedAsync($"{dir}{key}.png"));
            }
            catch (HttpRequestException)
            {
                // 그 경로에 없음 → 다음 후보
            }
        }
        throw new FileNotFoundException($"스프라이트 없음: {key}");
    }

    /// <summary>images/ 기준 상대 경로를 받아 캐시 경로 반환. 없으면 다운로드.</summary>
    internal static async Task<string> CachedAsync(string relative)
    {
        var path = Path.Combine(CacheDir, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        if (!File.Exists(path))
        {
            var bytes = await Http.GetByteArrayAsync(BaseUrl + relative);
            await File.WriteAllBytesAsync(path, bytes);
        }
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

    private static SpriteFrame MakeFrame(BitmapImage sheet, JsonElement elem)
    {
        var rect = ReadRect(elem.GetProperty("frame"));
        var s = elem.GetProperty("spriteSourceSize");
        var bmp = new CroppedBitmap(sheet, rect);
        bmp.Freeze();
        return new SpriteFrame(bmp, s.GetProperty("x").GetInt32(), s.GetProperty("y").GetInt32(), rect.Width, rect.Height);
    }
}
