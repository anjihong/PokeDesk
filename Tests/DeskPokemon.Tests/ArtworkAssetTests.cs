using System.Collections.Concurrent;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using Avalonia;
using Avalonia.Headless.XUnit;
using Xunit;

namespace DeskPokemon.Tests;

[CollectionDefinition("Artwork assets", DisableParallelization = true)]
public class ArtworkAssetCollection { }

[Collection("Artwork assets")]
public class ArtworkAssetTests
{
    private static byte[] Fixture(string name) => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));

    [AvaloniaFact]
    public async Task AnimatedShinyAtlasUsesItsOwnCacheAndPrefersExp()
    {
        using var assets = new AssetScope((request, _) => Task.FromResult(
            Reply(Fixture(request.AbsolutePath.EndsWith(".json") ? "atlas-array.json" : "atlas.png"))));

        using var shiny = await SpriteAtlas.LoadAsync(25, true);
        using var normal = await SpriteAtlas.LoadAsync(25);

        Assert.Equal(2, shiny.Frames.Length);
        Assert.Equal(2, normal.Frames.Length);
        Assert.Equal(4, assets.Requests.Count);
        Assert.EndsWith("pokemon/exp/shiny/25.json", assets.Requests[0].AbsolutePath);
        Assert.EndsWith("pokemon/exp/shiny/25.png", assets.Requests[1].AbsolutePath);
        Assert.EndsWith("pokemon/exp/25.json", assets.Requests[2].AbsolutePath);
        Assert.EndsWith("pokemon/exp/25.png", assets.Requests[3].AbsolutePath);
        Assert.True(File.Exists(Path.Combine(assets.Directory, "pokemon/exp/shiny/25.png")));
        Assert.True(File.Exists(Path.Combine(assets.Directory, "pokemon/exp/25.png")));
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ShinyPngFallbackStaysShinyAndTrimsTransparentSpace(bool timeout)
    {
        var pixels = new SpritePixels(6, 5);
        new byte[] { 0, 0, 255, 255, 0, 255, 0, 255 }.CopyTo(pixels.Pixels, (2 * 6 + 1) * 4);
        using var bitmap = pixels.ToBitmap();
        using var encoded = new MemoryStream();
        bitmap.Save(encoded);
        var png = encoded.ToArray();
        using var assets = new AssetScope((request, _) =>
        {
            if (request.AbsolutePath.EndsWith("/sprites/pokemon/shiny/25.png"))
                return Task.FromResult(Reply(png));
            if (timeout) throw new TaskCanceledException("Synthetic asset timeout");
            return Task.FromResult(Missing());
        });

        using var atlas = await SpriteAtlas.LoadAsync(25, true);

        Assert.Single(atlas.Frames);
        Assert.Equal(new PixelRect(1, 2, 2, 1), atlas.Body);
        Assert.Equal((6, 5), (atlas.Width, atlas.Height));
        Assert.Equal(4, assets.Requests.Count);
        Assert.EndsWith("pokemon/exp/shiny/25.json", assets.Requests[0].AbsolutePath);
        Assert.EndsWith("pokemon/shiny/25.json", assets.Requests[1].AbsolutePath);
        Assert.EndsWith("animated/shiny/25.gif", assets.Requests[2].AbsolutePath);
        Assert.EndsWith("sprites/pokemon/shiny/25.png", assets.Requests[3].AbsolutePath);
        Assert.All(assets.Requests, request => Assert.Contains("/shiny/", request.AbsolutePath));
    }

    [AvaloniaFact]
    public async Task MissingShinyNeverFallsBackToNormalArtwork()
    {
        using var assets = new AssetScope((_, _) => Task.FromResult(Missing()));

        await Assert.ThrowsAsync<HttpRequestException>(() => SpriteAtlas.LoadAsync(25, true));

        Assert.Equal(4, assets.Requests.Count);
        Assert.All(assets.Requests, request => Assert.Contains("/shiny/", request.AbsolutePath));
    }

    [AvaloniaFact]
    public async Task StillShinyAtlasIsRetainedWhenGifTimesOut()
    {
        var json = NamedAtlas(("0001", 0, 0));
        using var assets = new AssetScope((request, _) =>
        {
            var path = request.AbsolutePath;
            if (path.EndsWith("pokemon/shiny/25.json")) return Task.FromResult(Reply(json));
            if (path.EndsWith("pokemon/shiny/25.png")) return Task.FromResult(Reply(Fixture("atlas.png")));
            if (path.EndsWith(".gif")) throw new TaskCanceledException("Synthetic asset timeout");
            return Task.FromResult(Missing());
        });

        using var atlas = await SpriteAtlas.LoadAsync(25, true);

        Assert.Single(atlas.Frames);
        Assert.Equal(4, assets.Requests.Count);
        Assert.DoesNotContain(assets.Requests, request => request.AbsolutePath.EndsWith("/sprites/pokemon/shiny/25.png"));
    }

    [Fact]
    public async Task ConcurrentDownloadsShareFailureAndRetryWithoutPartialCacheFiles()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var attempt = 0;
        using var assets = new AssetScope(async (_, token) =>
        {
            if (Interlocked.Increment(ref attempt) == 1)
            {
                started.SetResult();
                await finish.Task.WaitAsync(token);
                return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
            }
            return Reply([1, 2, 3, 4]);
        });
        var tasks = Enumerable.Range(0, 12).Select(_ => SpriteAtlas.CachedAsync("test/asset.bin")).ToArray();
        await started.Task;
        Assert.False(File.Exists(Path.Combine(assets.Directory, "test/asset.bin")));
        Assert.Single(assets.Requests);
        finish.SetResult();
        await Assert.ThrowsAsync<HttpRequestException>(() => Task.WhenAll(tasks));

        var file = await SpriteAtlas.CachedAsync("test/asset.bin");

        Assert.Equal(new byte[] { 1, 2, 3, 4 }, await File.ReadAllBytesAsync(file));
        Assert.Equal(2, assets.Requests.Count);
        Assert.Empty(System.IO.Directory.GetFiles(assets.Directory, "*.tmp", SearchOption.AllDirectories));
    }

    [AvaloniaFact]
    public async Task AtlasVersionChangesCacheDirectoryWithoutChangingRemotePath()
    {
        using var assets = new AssetScope((request, _) => Task.FromResult(Reply(
            request.AbsolutePath.EndsWith(".json") ? NamedAtlas(("egg_0", 0, 0)) : Fixture("atlas.png"))));
        var original = await SpriteAtlas.LoadFramesAsync("egg/egg");
        var changed = await SpriteAtlas.LoadFramesAsync("egg/egg", "2");
        try
        {
            Assert.True(File.Exists(Path.Combine(assets.Directory, "egg/egg.png")));
            Assert.True(File.Exists(Path.Combine(assets.Directory, "art/2/egg/egg.png")));
            Assert.Equal(4, assets.Requests.Count);
            Assert.All(assets.Requests, request => Assert.DoesNotContain("/art/", request.AbsolutePath));
            Assert.NotSame(original["egg_0"].Bitmap, changed["egg_0"].Bitmap);
        }
        finally
        {
            foreach (var frame in original.Values.Concat(changed.Values)) frame.Bitmap.Dispose();
        }
    }

    [AvaloniaFact]
    public async Task IconColorsShareOneLoadButKeepSeparateFormsAndSilhouettes()
    {
        var json = NamedAtlas(("964-zero", 0, 0), ("964s-zero", 3, 1), ("906", 0, 0));
        using var assets = new AssetScope((request, _) => Task.FromResult(Reply(
            request.AbsolutePath.EndsWith(".json") ? json : Fixture("atlas.png"))));

        var normalLoad = PokemonIcons.LoadGenAsync(9);
        var shinyLoad = PokemonIcons.LoadGenAsync(9, true);
        await Task.WhenAll(normalLoad, shinyLoad);
        var normal = await normalLoad;
        var shiny = await shinyLoad;

        Assert.Equal(2, assets.Requests.Count);
        Assert.Equal(new byte[] { 0, 0, 255, 255 }, SpritePixels.CopyFrom(normal[964]).Pixels);
        Assert.Equal(new byte[] { 0, 255, 0, 255 }, SpritePixels.CopyFrom(shiny[964]).Pixels);
        Assert.True(normal.ContainsKey(906));
        Assert.False(shiny.ContainsKey(906));
        Assert.True(PokemonIcons.TryGetCachedGen(9, out var cachedShiny, true));
        Assert.Same(shiny, cachedShiny);
        Assert.True(PokemonIcons.TryGetCached(9, 964, out var cachedIcon, true));
        Assert.Same(shiny[964], cachedIcon);
        var regularSilhouette = PokemonIcons.SilhouetteOf(964, normal[964]);
        var shinySilhouette = PokemonIcons.SilhouetteOf(964, shiny[964], true);
        Assert.NotSame(regularSilhouette, shinySilhouette);
        Assert.Same(shinySilhouette, PokemonIcons.SilhouetteOf(964, shiny[964], true));
    }

    [AvaloniaFact]
    public async Task EggKindsShareAtlasAndReturnIndependentNormalizedCachedFrames()
    {
        var json = NamedAtlas(("egg_0", 0, 0), ("egg_1", 3, 1), ("egg_2", 0, 0),
            ("egg_3", 3, 1), ("egg_manaphy", 0, 0));
        using var assets = new AssetScope((request, _) => Task.FromResult(Reply(
            request.AbsolutePath.EndsWith(".json") ? json : Fixture("atlas.png"))));

        var kinds = Enum.GetValues<EggKind>();
        var frames = await Task.WhenAll(kinds.Select(EggArtwork.LoadAsync));

        Assert.Equal(5, frames.Length);
        Assert.Equal(2, assets.Requests.Count);
        Assert.Equal("egg_manaphy", EggArtwork.Definitions[EggKind.Shiny].Frame);
        Assert.All(frames, frame =>
        {
            Assert.Equal((28, 30, 0, 0), (frame.Width, frame.Height, frame.OffsetX, frame.OffsetY));
            var pixels = SpritePixels.CopyFrom(frame.Bitmap).Pixels;
            Assert.Equal(0, pixels[3]); // 1×1 square is fitted to 28×28 at the bottom.
            Assert.Equal(255, pixels[(29 * 28 + 27) * 4 + 3]);
        });
        Assert.Equal(5, frames.Select(frame => frame.Bitmap).Distinct().Count());
        Assert.Same(frames[Array.IndexOf(kinds, EggKind.Common)], await EggArtwork.LoadAsync(EggKind.Common));
    }

    [AvaloniaFact]
    public async Task EggArtworkReadsAvaloniaResourceWithoutDownloading()
    {
        using var assets = new AssetScope((_, _) => throw new InvalidOperationException("Network should not be used"));
        var definition = new EggArtwork.Definition("", "", ResourceUri: "avares://DeskPokemon.Tests/Fixtures/atlas.png");

        var frame = await EggArtwork.LoadAsync(definition);

        Assert.Equal((28, 30), (frame.Width, frame.Height));
        Assert.Empty(assets.Requests);
        Assert.Same(frame, await EggArtwork.LoadAsync(definition));
    }

    private static byte[] NamedAtlas(params (string Name, int X, int Y)[] frames) => Encoding.UTF8.GetBytes(
        JsonSerializer.Serialize(new
        {
            frames = frames.ToDictionary(frame => frame.Name + ".png", frame => new
            {
                frame = new { x = frame.X, y = frame.Y, w = 1, h = 1 },
                spriteSourceSize = new { x = 0, y = 0, w = 1, h = 1 },
                sourceSize = new { w = 1, h = 1 },
            }),
        }));

    private static HttpResponseMessage Reply(byte[] bytes) => new(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
    private static HttpResponseMessage Missing() => new(HttpStatusCode.NotFound);

    private sealed class AssetScope : IDisposable
    {
        private readonly HttpClient _previousHttp = SpriteAtlas.Http;
        private readonly string _previousDirectory = SpriteAtlas.CacheDirectory;
        private readonly HttpClient _client;
        private readonly ConcurrentQueue<Uri> _requests = new();
        public string Directory { get; } = Path.Combine(Path.GetTempPath(), "PokeDeskArtworkTests", Guid.NewGuid().ToString("N"));
        public IReadOnlyList<Uri> Requests => _requests.ToArray();

        public AssetScope(Func<Uri, CancellationToken, Task<HttpResponseMessage>> respond)
        {
            PokemonIcons.ClearCacheForTests();
            EggArtwork.ClearCacheForTests();
            System.IO.Directory.CreateDirectory(Directory);
            _client = new HttpClient(new Handler(async (request, token) =>
            {
                _requests.Enqueue(request.RequestUri!);
                return await respond(request.RequestUri!, token);
            }));
            SpriteAtlas.Http = _client;
            SpriteAtlas.CacheDirectory = Directory;
        }

        public void Dispose()
        {
            PokemonIcons.ClearCacheForTests();
            EggArtwork.ClearCacheForTests();
            SpriteAtlas.Http = _previousHttp;
            SpriteAtlas.CacheDirectory = _previousDirectory;
            _client.Dispose();
            System.IO.Directory.Delete(Directory, recursive: true);
        }
    }

    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            respond(request, cancellationToken);
    }
}
