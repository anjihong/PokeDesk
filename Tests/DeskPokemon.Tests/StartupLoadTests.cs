using System.Net;
using System.Net.Http;
using System.Reflection;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Xunit;

namespace DeskPokemon.Tests;

[Collection("Artwork assets")]
public class StartupLoadTests
{
    [AvaloniaFact]
    public async Task DelayedStartupSpriteCannotReplaceANewerSelectedSpriteWithThePlaceholder()
    {
        using var assets = new DelayedStartupAssets();
        var settings = Settings.NewPreview(4);
        settings.AddOwned(7);
        var window = new MainWindow(settings, startServices: false);
        using var icon = new Bitmap(Path.Combine(AppContext.BaseDirectory, "Fixtures", "atlas.png"));
        var startupFinished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            Invoke(window, "OnLoaded", window, EventArgs.Empty);
            await assets.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));

            // OnLoaded selects a generation last, after placing the window. Stop its clocks
            // synchronously in that call, before the dispatcher can tick them. The constructor
            // above creates no input hook, and NewPreview never writes the player's save.
            foreach (var tab in window.FindControl<StackPanel>("GenTabs")!.Children.OfType<RadioButton>())
            {
                tab.IsCheckedChanged += (_, _) =>
                {
                    if (!Field<bool>(window, "_placed")) return;
                    foreach (var timer in Field<List<DispatcherTimer>>(window, "_timers")) timer.Stop();
                    foreach (var animation in Field<Dictionary<string, Timeline>>(window, "_animations").Values)
                        animation.Stop();
                    startupFinished.TrySetResult();
                };
            }

            // Exercise the actual selection handler while the original #4 request is held.
            var choice = (RadioButton)Invoke(window, "MakeIconCell", 7, false, icon)!;
            window.FindControl<WrapPanel>("IconGrid")!.Children.Add(choice);
            choice.IsChecked = true;
            await UntilAsync(() => settings.SelectedDex == 7);
            var selectedAtlas = Field<SpriteAtlas?>(window, "_atlas");
            Assert.NotNull(selectedAtlas);
            Assert.False(assets.Release.Task.IsCompleted);

            assets.Release.TrySetResult();
            await startupFinished.Task.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Equal(7, settings.SelectedDex);
            Assert.Same(selectedAtlas, Field<SpriteAtlas?>(window, "_atlas"));
            Assert.False(window.FindControl<TextBlock>("SpriteMissing")!.IsVisible);
            var image = window.FindControl<Image>("Sprite")!;
            Assert.Contains(selectedAtlas.Frames, frame => ReferenceEquals(frame.Bitmap, image.Source));
            Assert.Null(Field<InputHook?>(window, "_hook"));
            Assert.All(Field<List<DispatcherTimer>>(window, "_timers"), timer => Assert.False(timer.IsEnabled));
        }
        finally
        {
            assets.Release.TrySetResult();
            // Complete outstanding sprite I/O before restoring the static client/cache scope.
            try
            {
                if (!startupFinished.Task.IsCompleted && assets.Started.Task.IsCompleted)
                    await startupFinished.Task.WaitAsync(TimeSpan.FromSeconds(5));
            }
            finally { window.Close(); }
        }
    }

    private static object? Invoke(MainWindow window, string method, params object[] args) =>
        typeof(MainWindow).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)!.Invoke(window, args);

    private static T Field<T>(MainWindow window, string name) =>
        (T)typeof(MainWindow).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;

    private static async Task UntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "The newer sprite selection did not finish.");
            await Task.Delay(10);
        }
    }

    private sealed class DelayedStartupAssets : HttpMessageHandler
    {
        private readonly HttpClient _previousHttp = SpriteAtlas.Http;
        private readonly string _previousDirectory = SpriteAtlas.CacheDirectory;
        private readonly string _directory = Path.Combine(Path.GetTempPath(), "PokeDesk-startup-tests-" + Guid.NewGuid().ToString("N"));
        private readonly HttpClient _client;
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public DelayedStartupAssets()
        {
            PokemonIcons.ClearCacheForTests();
            EggArtwork.ClearCacheForTests();
            Directory.CreateDirectory(_directory);
            _client = new HttpClient(this, disposeHandler: false);
            SpriteAtlas.Http = _client;
            SpriteAtlas.CacheDirectory = _directory;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/pokemon/exp/4.json"))
            {
                Started.TrySetResult();
                await Release.Task.WaitAsync(cancellationToken);
            }
            if (!path.Contains("/pokemon/exp/")) return new HttpResponseMessage(HttpStatusCode.NotFound);
            var fixture = path.EndsWith(".json") ? "atlas-array.json" : "atlas.png";
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", fixture))),
            };
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                PokemonIcons.ClearCacheForTests();
                EggArtwork.ClearCacheForTests();
                SpriteAtlas.Http = _previousHttp;
                SpriteAtlas.CacheDirectory = _previousDirectory;
                _client.Dispose();
                Directory.Delete(_directory, recursive: true);
            }
            base.Dispose(disposing);
        }
    }
}
