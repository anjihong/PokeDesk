using System.IO;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using SkiaSharp;

namespace DeskPokemon;

/// <summary>Windows와 macOS에서 같은 BGRA/비프리멀티 픽셀로 디코딩하고 Avalonia에 전달한다.</summary>
internal sealed class SpritePixels(int width, int height)
{
    public int Width { get; } = width;
    public int Height { get; } = height;
    public int Stride { get; } = checked(width * 4);
    public byte[] Pixels { get; } = new byte[checked(width * height * 4)];

    public static SpritePixels Load(string path)
    {
        using var codec = SKCodec.Create(path) ?? throw new InvalidDataException($"이미지를 읽을 수 없음: {path}");
        return Decode(codec, 0);
    }

    public static SpritePixels Decode(SKCodec codec, int frameIndex)
    {
        var image = new SpritePixels(codec.Info.Width, codec.Info.Height);
        var info = new SKImageInfo(image.Width, image.Height, SKColorType.Bgra8888, SKAlphaType.Unpremul);
        var handle = GCHandle.Alloc(image.Pixels, GCHandleType.Pinned);
        try
        {
            // priorFrame=-1: 필요한 이전 프레임과 GIF disposal을 Skia가 합성한다.
            var result = codec.GetPixels(info, handle.AddrOfPinnedObject(), image.Stride,
                new SKCodecOptions(frameIndex) { PriorFrame = -1 });
            if (result != SKCodecResult.Success)
                throw new InvalidDataException($"이미지 프레임을 읽을 수 없음: {result}");
        }
        finally
        {
            handle.Free();
        }
        return image;
    }

    public static SpritePixels CopyFrom(Bitmap bitmap)
    {
        var image = new SpritePixels(bitmap.PixelSize.Width, bitmap.PixelSize.Height);
        using var converted = new WriteableBitmap(bitmap.PixelSize, new Vector(96, 96),
            PixelFormat.Bgra8888, AlphaFormat.Unpremul);
        using var buffer = converted.Lock();
        bitmap.CopyPixels(buffer, AlphaFormat.Unpremul);
        for (var y = 0; y < image.Height; y++)
            Marshal.Copy(buffer.Address + y * buffer.RowBytes, image.Pixels, y * image.Stride, image.Stride);
        return image;
    }

    public Bitmap ToBitmap() => Crop(new PixelRect(0, 0, Width, Height));

    /// <summary>Alpha-only local bounds and the center of the lowest 10% (at most six rows).</summary>
    internal (PixelRect OpaqueBounds, Point FootAnchor) AnalyzeFoot(PixelRect rect)
    {
        if (rect.X < 0 || rect.Y < 0 || rect.Width <= 0 || rect.Height <= 0 ||
            rect.Right > Width || rect.Bottom > Height)
            throw new InvalidDataException("아틀라스 프레임이 이미지 범위를 벗어남");
        var left = rect.Width;
        var top = rect.Height;
        var right = -1;
        var bottom = -1;
        for (var y = 0; y < rect.Height; y++)
        for (var x = 0; x < rect.Width; x++)
        {
            if (Pixels[(rect.Y + y) * Stride + (rect.X + x) * 4 + 3] == 0) continue;
            left = Math.Min(left, x);
            top = Math.Min(top, y);
            right = Math.Max(right, x);
            bottom = Math.Max(bottom, y);
        }
        if (right < 0)
            return (new PixelRect(0, 0, 0, 0), new Point(rect.Width / 2.0, rect.Height));

        var opaque = new PixelRect(left, top, right - left + 1, bottom - top + 1);
        var bandHeight = Math.Clamp((int)Math.Ceiling(opaque.Height * .1), 1, 6);
        var footLeft = rect.Width;
        var footRight = -1;
        for (var y = bottom - bandHeight + 1; y <= bottom; y++)
        for (var x = left; x <= right; x++)
        {
            if (Pixels[(rect.Y + y) * Stride + (rect.X + x) * 4 + 3] == 0) continue;
            footLeft = Math.Min(footLeft, x);
            footRight = Math.Max(footRight, x);
        }
        // Pixel-edge coordinates: Y is beneath the last visible row, not its center.
        return (opaque, new Point((footLeft + footRight + 1) / 2.0, bottom + 1));
    }

    public Bitmap Crop(PixelRect rect)
    {
        if (rect.X < 0 || rect.Y < 0 || rect.Width <= 0 || rect.Height <= 0 ||
            rect.Right > Width || rect.Bottom > Height)
            throw new InvalidDataException("아틀라스 프레임이 이미지 범위를 벗어남");

        var handle = GCHandle.Alloc(Pixels, GCHandleType.Pinned);
        try
        {
            // Bitmap 생성자는 픽셀을 복사하므로 시트와 핀을 보관할 필요가 없다.
            var address = handle.AddrOfPinnedObject() + rect.Y * Stride + rect.X * 4;
            return new Bitmap(PixelFormat.Bgra8888, AlphaFormat.Unpremul, address,
                rect.Size, new Vector(96, 96), Stride);
        }
        finally
        {
            handle.Free();
        }
    }
}
