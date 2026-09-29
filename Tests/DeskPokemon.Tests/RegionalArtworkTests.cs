using System.Net;
using System.Net.Http;
using System.Text.Json;
using Avalonia.Headless.XUnit;
using Xunit;

namespace DeskPokemon.Tests;

[Collection("Artwork assets")]
public class RegionalArtworkTests
{
    [Fact]
    public void RegionalNamesSpriteKeysAndGenerationsFollowTheCatalog()
    {
        Assert.Equal(11, EvolutionData.Forms.Count);
        foreach (var form in EvolutionData.Forms)
        {
            Assert.Equal(form.Name, PokemonNames.Of(form.Id));
            Assert.Equal(form.SpriteKey, PokemonForms.SpriteKey(form.Id));
            Assert.Equal(form.Generation, PokemonIcons.GenOf(form.Id));
            Assert.Contains(form.Id, PokemonIcons.Entries(form.Generation));
        }
        var entries = Enumerable.Range(1, 9).SelectMany(PokemonIcons.Entries).ToArray();
        Assert.Equal(1036, entries.Length);
        Assert.Equal(entries.Length, entries.Distinct().Count());
        Assert.Equal(106, PokemonIcons.Entries(8).Count());
        Assert.Equal(121, PokemonIcons.Entries(9).Count());
        Assert.Equal("가라르 나옹", PokemonNames.Of(4052));
        Assert.Equal("팔데아 우파", PokemonNames.Of(8194));
        Assert.Equal("964-zero", PokemonForms.SpriteKey(964));
        Assert.Equal("파이리", PokemonNames.Of(4));
    }

    [AvaloniaFact]
    public async Task RegionalIconsKeepTheFormAndColorCachesSeparate()
    {
        var json = NamedAtlas("4052", "4052s", "4264", "4264s");
        using var assets = new RegionalAssets(request => Reply(request.AbsolutePath.EndsWith(".json") ? json : FixturePng()));
        var normal = await PokemonIcons.LoadGenAsync(8);
        var shiny = await PokemonIcons.LoadGenAsync(8, true);

        Assert.Equal(new[] { 4052, 4264 }, normal.Keys.Order());
        Assert.Equal(new[] { 4052, 4264 }, shiny.Keys.Order());
        Assert.NotSame(normal[4052], shiny[4052]);
        Assert.NotSame(normal[4052], normal[4264]);
        Assert.True(PokemonIcons.TryGetCached(8, 4052, out var cached, true));
        Assert.Same(shiny[4052], cached);
        Assert.Equal(2, assets.Requests.Count);
        Assert.All(assets.Requests, request => Assert.Contains("pokemon_icons_8.", request.AbsolutePath));
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RegionalStillAtlasDoesNotRequestAnUnrelatedPokeApiId(bool shiny)
    {
        using var assets = new RegionalAssets(request => Reply(
            request.AbsolutePath.EndsWith(".json") ? NamedAtlas("0001") : FixturePng()));

        using var atlas = await SpriteAtlas.LoadAsync(4052, shiny);

        Assert.Single(atlas.Frames);
        Assert.Equal(2, assets.Requests.Count);
        var folder = shiny ? "pokemon/exp/shiny/4052" : "pokemon/exp/4052";
        Assert.EndsWith(folder + ".json", assets.Requests[0].AbsolutePath);
        Assert.EndsWith(folder + ".png", assets.Requests[1].AbsolutePath);
        Assert.DoesNotContain(assets.Requests, request => request.AbsolutePath.Contains("/sprites/pokemon/"));
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MissingRegionalAtlasFailsWithoutCrossFormFallback(bool shiny)
    {
        using var assets = new RegionalAssets(_ => new HttpResponseMessage(HttpStatusCode.NotFound));

        await Assert.ThrowsAsync<HttpRequestException>(() => SpriteAtlas.LoadAsync(8194, shiny));

        Assert.Equal(2, assets.Requests.Count);
        Assert.All(assets.Requests, request =>
        {
            Assert.EndsWith("/8194.json", request.AbsolutePath);
            Assert.Equal(shiny, request.AbsolutePath.Contains("/shiny/"));
            Assert.DoesNotContain("/sprites/pokemon/", request.AbsolutePath);
        });
    }

    private static byte[] FixturePng() => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "atlas.png"));

    private static byte[] NamedAtlas(params string[] names) => JsonSerializer.SerializeToUtf8Bytes(new
    {
        frames = names.ToDictionary(name => name + ".png", _ => new
        {
            frame = new { x = 0, y = 0, w = 1, h = 1 },
            spriteSourceSize = new { x = 0, y = 0, w = 1, h = 1 },
            sourceSize = new { w = 1, h = 1 },
        }),
    });

    private static HttpResponseMessage Reply(byte[] bytes) =>
        new(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };

    private sealed class RegionalAssets : HttpMessageHandler
    {
        private readonly HttpClient previousHttp = SpriteAtlas.Http;
        private readonly string previousDirectory = SpriteAtlas.CacheDirectory;
        private readonly string directory = Path.Combine(Path.GetTempPath(), "PokeDeskRegionalArtwork", Guid.NewGuid().ToString("N"));
        private readonly HttpClient client;
        private readonly Func<Uri, HttpResponseMessage> respond;
        public List<Uri> Requests { get; } = new();

        public RegionalAssets(Func<Uri, HttpResponseMessage> respond)
        {
            this.respond = respond;
            PokemonIcons.ClearCacheForTests();
            Directory.CreateDirectory(directory);
            client = new HttpClient(this, disposeHandler: false);
            SpriteAtlas.Http = client;
            SpriteAtlas.CacheDirectory = directory;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Requests.Add(request.RequestUri!);
            return Task.FromResult(respond(request.RequestUri!));
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                PokemonIcons.ClearCacheForTests();
                SpriteAtlas.Http = previousHttp;
                SpriteAtlas.CacheDirectory = previousDirectory;
                client.Dispose();
                Directory.Delete(directory, recursive: true);
            }
            base.Dispose(disposing);
        }
    }
}
