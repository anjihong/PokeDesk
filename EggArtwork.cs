using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DeskPokemon;

/// <summary>게임 규칙과 독립된 알 표시 정의. 자체 PNG는 ResourceUri만 지정하여 교체한다.</summary>
internal static class EggArtwork
{
    internal sealed record Definition(string Atlas, string Frame, string Version = "1", string? ResourceUri = null);

    // PokéRogue EggTier: COMMON=0, RARE=1, EPIC=2, LEGENDARY=3; getKey(): manaphy.
    // Shiny 자체 아트 예: new("", "", "2", "pack://application:,,,/Assets/shiny-egg.png")
    // 해당 PNG의 Build Action을 Resource로 지정한다. 게임 등급·세이브는 변경하지 않는다.
    internal static readonly IReadOnlyDictionary<EggKind, Definition> Definitions =
        new Dictionary<EggKind, Definition>
        {
            [EggKind.Common] = new("egg/egg", "egg_0"),
            [EggKind.Rare] = new("egg/egg", "egg_1"),
            [EggKind.Epic] = new("egg/egg", "egg_2"),
            [EggKind.Legendary] = new("egg/egg", "egg_3"),
            [EggKind.Shiny] = new("egg/egg", "egg_manaphy"),
        };

    public const int Width = 28;
    public const int Height = 30;
    private static readonly Dictionary<Definition, SpriteFrame> Cache = new();
    private static readonly Dictionary<(string Atlas, string Version), Dictionary<string, SpriteFrame>> Atlases = new();

    public static async Task<SpriteFrame> LoadAsync(EggKind kind)
    {
        var definition = Definitions[kind];
        if (Cache.TryGetValue(definition, out var cached)) return cached;
        SpriteFrame source;
        if (definition.ResourceUri is { } uri)
        {
            var bitmap = SpriteAtlas.LoadSheet(uri);
            var crop = new CroppedBitmap(bitmap, new Int32Rect(0, 0, bitmap.PixelWidth, bitmap.PixelHeight));
            crop.Freeze();
            source = new(crop, 0, 0, crop.PixelWidth, crop.PixelHeight);
        }
        else
        {
            var key = (definition.Atlas, definition.Version);
            if (!Atlases.TryGetValue(key, out var frames))
            {
                // v1은 기존 앱이 받은 파일을 재사용한다. 아트 변경 시 버전을 올려 캐시를 분리한다.
                frames = await SpriteAtlas.LoadFramesAsync(definition.Atlas,
                    definition.Version == "1" ? null : definition.Version);
                Atlases[key] = frames;
            }
            source = frames[definition.Frame];
        }
        return Cache[definition] = Normalize(source);
    }

    /// <summary>원본 크기와 무관하게 균열 연출의 공통 28x30 영역에 비율 유지·하단 정렬한다.</summary>
    private static SpriteFrame Normalize(SpriteFrame source)
    {
        if (source.Width == Width && source.Height == Height && source.OffsetX == 0 && source.OffsetY == 0)
            return source;
        var input = new FormatConvertedBitmap(source.Bitmap, PixelFormats.Bgra32, null, 0);
        var pixels = new byte[source.Width * source.Height * 4];
        input.CopyPixels(pixels, source.Width * 4, 0);
        var scale = Math.Min((double)Width / source.Width, (double)Height / source.Height);
        int width = Math.Max(1, (int)Math.Round(source.Width * scale));
        int height = Math.Max(1, (int)Math.Round(source.Height * scale));
        var output = new byte[Width * Height * 4];
        for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
            {
                int from = ((y * source.Height / height) * source.Width + x * source.Width / width) * 4;
                int to = ((y + Height - height) * Width + x + (Width - width) / 2) * 4;
                Buffer.BlockCopy(pixels, from, output, to, 4);
            }
        var bitmap = BitmapSource.Create(Width, Height, 96, 96, PixelFormats.Bgra32, null, output, Width * 4);
        var result = new CroppedBitmap(bitmap, new Int32Rect(0, 0, Width, Height));
        result.Freeze();
        return new(result, 0, 0, Width, Height);
    }
}
