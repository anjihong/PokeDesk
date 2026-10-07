using Avalonia.Media.Imaging;

namespace DeskPokemon;

internal static class EvolutionArtwork
{
    /// <summary>The caller owns the returned bitmap. An optional effect must not delay evolution indefinitely.</summary>
    internal static async Task<Bitmap?> LoadSparkleAsync(CancellationToken cancellationToken,
        TimeSpan? timeout = null)
    {
        var download = SpriteAtlas.CachedAsync("effects/evo_sparkle.png");
        try
        {
            var path = await download.WaitAsync(timeout ?? TimeSpan.FromSeconds(1), cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            return SpriteAtlas.LoadSheet(path).ToBitmap();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _ = ObserveDownloadAsync(download);
            throw;
        }
        catch
        {
            // The shared cache may still finish for the next evolution; it owns no bitmap.
            _ = ObserveDownloadAsync(download);
            return null;
        }
    }

    private static async Task ObserveDownloadAsync(Task<string> download)
    {
        try { await download.ConfigureAwait(false); }
        catch { /* A timed-out optional effect uses the white-circle fallback. */ }
    }
}
