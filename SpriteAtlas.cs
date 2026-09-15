using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using System.Windows.Media.Imaging;

namespace DeskPokemon;

/// <summary>한 프레임. Bitmap은 시트에서 잘라낸 조각, Offset은 원본 캔버스(SourceSize) 내 위치.</summary>
public sealed record SpriteFrame(CroppedBitmap Bitmap, int OffsetX, int OffsetY, int Width, int Height);

/// <summary>
/// PokeRogue 에셋 저장소의 TexturePacker 아틀라스({id}.json + {id}.png)를
/// 런타임에 받아 %LOCALAPPDATA%\DeskPokemon\sprites 에 캐시하고 프레임 배열로 푼다.
/// 에셋은 앱에 번들하지 않는다(라이선스: 저장소 README 참고).
/// </summary>
public sealed class SpriteAtlas
{
    private const string BaseUrl =
        "https://raw.githubusercontent.com/pagefaultgames/pokerogue-assets/beta/images/pokemon/";

    private static readonly string CacheDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DeskPokemon", "sprites");

    public int Width { get; }
    public int Height { get; }
    public SpriteFrame[] Frames { get; }

    private SpriteAtlas(int width, int height, SpriteFrame[] frames)
    {
        Width = width;
        Height = height;
        Frames = frames;
    }

    public static async Task<SpriteAtlas> LoadAsync(int dexId)
    {
        var jsonPath = await CachedAsync($"{dexId}.json");
        var pngPath = await CachedAsync($"{dexId}.png");
        return Parse(jsonPath, pngPath);
    }

    private static async Task<string> CachedAsync(string name)
    {
        Directory.CreateDirectory(CacheDir);
        var path = Path.Combine(CacheDir, name);
        if (!File.Exists(path))
        {
            using var http = new HttpClient();
            var bytes = await http.GetByteArrayAsync(BaseUrl + name);
            await File.WriteAllBytesAsync(path, bytes);
        }
        return path;
    }

    private static SpriteAtlas Parse(string jsonPath, string pngPath)
    {
        var sheet = new BitmapImage();
        sheet.BeginInit();
        sheet.UriSource = new Uri(pngPath);
        sheet.CacheOption = BitmapCacheOption.OnLoad;
        sheet.EndInit();
        sheet.Freeze();

        using var doc = JsonDocument.Parse(File.ReadAllText(jsonPath));
        var frameArray = doc.RootElement.GetProperty("textures")[0].GetProperty("frames");
        if (frameArray.GetArrayLength() == 0)
            throw new InvalidDataException("아틀라스에 프레임이 없음");

        // 배열 순서는 재생 순서가 아님. filename("0001.png") 숫자 기준 정렬.
        var frames = frameArray.EnumerateArray()
            .Select(f => (Index: int.Parse(Path.GetFileNameWithoutExtension(f.GetProperty("filename").GetString()!)), Elem: f))
            .OrderBy(x => x.Index)
            .Select(x =>
            {
                var r = x.Elem.GetProperty("frame");
                var s = x.Elem.GetProperty("spriteSourceSize");
                var rect = new Int32Rect(
                    r.GetProperty("x").GetInt32(), r.GetProperty("y").GetInt32(),
                    r.GetProperty("w").GetInt32(), r.GetProperty("h").GetInt32());
                var bmp = new CroppedBitmap(sheet, rect);
                bmp.Freeze();
                return new SpriteFrame(bmp, s.GetProperty("x").GetInt32(), s.GetProperty("y").GetInt32(), rect.Width, rect.Height);
            })
            .ToArray();

        var size = frameArray[0].GetProperty("sourceSize");
        return new SpriteAtlas(size.GetProperty("w").GetInt32(), size.GetProperty("h").GetInt32(), frames);
    }
}
