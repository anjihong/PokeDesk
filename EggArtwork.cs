using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using Avalonia.Platform;
using SkiaSharp;

namespace DeskPokemon;

/// <summary>
/// 게임 규칙과 독립된 알 표시 정의. 반환한 프레임은 앱 수명 캐시 소유이며 호출자가 Dispose하지 않는다.
/// </summary>
internal static class EggArtwork
{
    internal sealed record Definition(string Atlas, string Frame, string Version = "1", string? ResourceUri = null, bool IsGeneratedShiny = false);

    // 자체 PNG 예: new("", "", "2", "avares://DeskPokemon/Assets/shiny-egg.png").
    // 해당 파일을 AvaloniaResource로 포함한다. 게임 등급과 세이브는 변경하지 않는다.
    internal static readonly IReadOnlyDictionary<EggKind, Definition> Definitions =
        new Dictionary<EggKind, Definition>
        {
            [EggKind.Common] = new("egg/egg", "egg_0"),
            [EggKind.Rare] = new("egg/egg", "egg_1"),
            [EggKind.Epic] = new("egg/egg", "egg_2"),
            [EggKind.Legendary] = new("egg/egg", "egg_3"),
            [EggKind.Shiny] = new("", "", "gold-star-1", IsGeneratedShiny: true),
        };

    public const int Width = 28;
    public const int Height = 30;
    private static readonly ConcurrentDictionary<Definition, Lazy<Task<SpriteFrame>>> Cache = new();
    private static readonly ConcurrentDictionary<(string Atlas, string Version), Lazy<Task<Dictionary<string, SpriteFrame>>>> Atlases = new();

    public static Task<SpriteFrame> LoadAsync(EggKind kind) => LoadAsync(Definitions[kind]);

    internal static async Task<SpriteFrame> LoadAsync(Definition definition)
    {
        var load = Cache.GetOrAdd(definition, static art => new Lazy<Task<SpriteFrame>>(() => LoadCoreAsync(art)));
        try
        {
            return await load.Value;
        }
        catch
        {
            Cache.TryRemove(new KeyValuePair<Definition, Lazy<Task<SpriteFrame>>>(definition, load));
            throw;
        }
    }

    private static async Task<SpriteFrame> LoadCoreAsync(Definition definition)
    {
        if (definition.IsGeneratedShiny) return CreateShinyEgg();
        if (definition.ResourceUri is { } uri)
        {
            using var stream = AssetLoader.Open(new Uri(uri, UriKind.Absolute));
            using var codec = SKCodec.Create(stream) ?? throw new InvalidDataException("알 리소스를 읽을 수 없음");
            var pixels = SpritePixels.Decode(codec, 0);
            using var bitmap = pixels.ToBitmap();
            return Normalize(new SpriteFrame(bitmap, 0, 0, pixels.Width, pixels.Height));
        }

        var key = (definition.Atlas, definition.Version);
        var load = Atlases.GetOrAdd(key, static asset => new Lazy<Task<Dictionary<string, SpriteFrame>>>(() =>
            // v1은 기존 캐시를 재사용하고 아트 변경 버전은 별도 디스크 경로를 사용한다.
            SpriteAtlas.LoadFramesAsync(asset.Atlas, asset.Version == "1" ? null : asset.Version)));
        Dictionary<string, SpriteFrame> frames;
        try
        {
            frames = await load.Value;
        }
        catch
        {
            Atlases.TryRemove(new KeyValuePair<(string Atlas, string Version), Lazy<Task<Dictionary<string, SpriteFrame>>>>(key, load));
            throw;
        }
        return Normalize(frames[definition.Frame]);
    }

    /// <summary>마나피와 구분되는 자체 금색 별 알. 외부 에셋/이미지 편집 없이 작은 픽셀 도형으로 그린다.</summary>
    private static SpriteFrame CreateShinyEgg()
    {
        using var bitmap = new SKBitmap(new SKImageInfo(Width, Height, SKColorType.Bgra8888, SKAlphaType.Unpremul));
        bitmap.Erase(SKColors.Transparent);
        using var canvas = new SKCanvas(bitmap);
        using var shell = new SKPath();
        shell.MoveTo(14, 1);
        shell.CubicTo(9, 1, 3, 13, 3, 20);
        shell.CubicTo(3, 26, 7, 29, 14, 29);
        shell.CubicTo(21, 29, 25, 26, 25, 20);
        shell.CubicTo(25, 13, 19, 1, 14, 1);
        shell.Close();
        using var fill = new SKPaint { IsAntialias = false, Color = new SKColor(0xF8, 0xE9, 0xA2) };
        using var edge = new SKPaint
        {
            IsAntialias = false, Color = new SKColor(0x70, 0x49, 0x21),
            Style = SKPaintStyle.Stroke, StrokeWidth = 1,
        };
        canvas.DrawPath(shell, fill);
        canvas.DrawPath(shell, edge);
        fill.Color = new SKColor(0xFF, 0xFA, 0xDA);
        canvas.DrawRect(8, 9, 2, 5, fill);

        using var star = new SKPath();
        for (var i = 0; i < 10; i++)
        {
            var angle = -Math.PI / 2 + i * Math.PI / 5;
            var radius = i % 2 == 0 ? 7 : 3;
            var x = (float)Math.Round(14 + radius * Math.Cos(angle));
            var y = (float)Math.Round(17 + radius * Math.Sin(angle));
            if (i == 0) star.MoveTo(x, y); else star.LineTo(x, y);
        }
        star.Close();
        fill.Color = new SKColor(0xF2, 0xB8, 0x28);
        canvas.DrawPath(star, fill);
        edge.Color = new SKColor(0xA4, 0x68, 0x12);
        canvas.DrawPath(star, edge);
        canvas.Flush();
        var pixels = new SpritePixels(Width, Height);
        for (var y = 0; y < Height; y++)
            Marshal.Copy(bitmap.GetPixels() + y * bitmap.RowBytes, pixels.Pixels, y * pixels.Stride, pixels.Stride);
        return new SpriteFrame(pixels.ToBitmap(), 0, 0, Width, Height);
    }

    /// <summary>균열 연출의 28×30 영역에 비율 유지·하단 정렬한다. 결과 비트맵은 원본과 독립적이다.</summary>
    internal static SpriteFrame Normalize(SpriteFrame source)
    {
        var input = SpritePixels.CopyFrom(source.Bitmap);
        var scale = Math.Min((double)Width / source.Width, (double)Height / source.Height);
        var width = Math.Max(1, (int)Math.Round(source.Width * scale));
        var height = Math.Max(1, (int)Math.Round(source.Height * scale));
        var output = new SpritePixels(Width, Height);
        for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
            {
                var from = ((y * source.Height / height) * source.Width + x * source.Width / width) * 4;
                var to = ((y + Height - height) * Width + x + (Width - width) / 2) * 4;
                Buffer.BlockCopy(input.Pixels, from, output.Pixels, to, 4);
            }
        return new SpriteFrame(output.ToBitmap(), 0, 0, Width, Height);
    }

    /// <summary>비트맵 사용자와 진행 중인 로드가 없는 격리 테스트에서만 호출한다.</summary>
    internal static void ClearCacheForTests()
    {
        foreach (var load in Cache.Values)
            if (load.IsValueCreated && load.Value.IsCompletedSuccessfully) load.Value.Result.Bitmap.Dispose();
        Cache.Clear();
        foreach (var load in Atlases.Values)
            if (load.IsValueCreated && load.Value.IsCompletedSuccessfully)
                foreach (var frame in load.Value.Result.Values) frame.Bitmap.Dispose();
        Atlases.Clear();
    }
}
