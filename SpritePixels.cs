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
