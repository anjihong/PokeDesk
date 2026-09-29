using System.Net.Http;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Xunit;

namespace DeskPokemon.Tests;

public partial class UiTests
{
    [AvaloniaFact]
    public async Task StartupWaitsForBothSpriteAndEggBeforeRevealingTheFinalPosition()
    {
        using var assets = new UiAssets();
        using var gate = new StartupArtworkGate(assets);
        using var client = new HttpClient(gate, disposeHandler: false);
        SpriteAtlas.Http = client;
        var settings = Settings.NewPreview(4);
        settings.PendingEgg = new(EggKind.Common, 4, false);
        var window = NewStartupWindowWithoutNativeServices(settings);
        try
        {
            window.Show();
            Invoke(window, "OnLoaded", window, EventArgs.Empty);
            await gate.SpriteRequested.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(0, window.Opacity);
            Assert.False(window.StartupPresentation.IsCompleted);

            gate.ReleaseSprite.TrySetResult();
            await gate.EggRequested.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.NotNull(window.FindControl<Image>("Sprite")!.Source);
            Assert.True(window.FindControl<Control>("EggFallback")!.IsVisible);
            Assert.Equal(0, window.Opacity);
            Assert.False(window.StartupPresentation.IsCompleted);

            gate.ReleaseEgg.TrySetResult();
            await window.StartupPresentation.WaitAsync(TimeSpan.FromSeconds(5));
            await EventuallyAsync(window, () => !Field<bool>(window, "_dexLoading"));
            Assert.Equal(1, window.Opacity);
            Assert.False(window.FindControl<Control>("EggFallback")!.IsVisible);
            Assert.NotNull(window.FindControl<Image>("EggImage")!.Source);
            var area = (PixelRect)typeof(MainWindow).GetProperty("WorkingArea", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(window)!;
            Assert.Equal(new PixelPoint(area.Right - (int)Math.Ceiling(window.Bounds.Width * window.DesktopScaling) - 20,
                area.Bottom - (int)Math.Ceiling(window.Bounds.Height * window.DesktopScaling) - 20), window.Position);
            Assert.Null(Field<InputHook?>(window, "_hook"));
        }
        finally
        {
            gate.ReleaseSprite.TrySetResult();
            gate.ReleaseEgg.TrySetResult();
            try { await window.StartupPresentation.WaitAsync(TimeSpan.FromSeconds(5)); }
            finally { window.Close(); }
        }
    }

    [AvaloniaFact]
    public async Task LevelUpWhileStartupEggIsLoadingDefersEvolutionAndRevealsOnlyItsCompletedForm()
    {
        using var assets = new UiAssets();
        using var gate = new StartupArtworkGate(assets);
        using var client = new HttpClient(gate, disposeHandler: false);
        SpriteAtlas.Http = client;
        var settings = Settings.NewPreview(4);
        settings.PendingEgg = new(EggKind.Common, 4, false);
        settings.For(4).Level = 15;
        settings.For(4).Exp = Settings.ExpToNext(15) - 1;
        var window = NewStartupWindowWithoutNativeServices(settings);
        var effect = window.FindControl<EvolutionEffect>("EvolutionVisual")!;
        var observedEvolution = false;
        var evolutionStayedHidden = true;
        bool? evolvingWhenRevealed = null;
        int? dexWhenRevealed = null;
        effect.PropertyChanged += (_, e) =>
        {
            if (e.Property != EvolutionEffect.ProgressProperty || effect.Progress <= 0) return;
            observedEvolution = true;
            evolutionStayedHidden &= window.Opacity == 0;
        };
        window.PropertyChanged += (_, e) =>
        {
            if (e.Property != Visual.OpacityProperty || window.Opacity != 1) return;
            evolvingWhenRevealed = Field<bool>(window, "_evolving");
            dexWhenRevealed = settings.SelectedDex;
        };
        try
        {
            window.Show();
            Invoke(window, "OnLoaded", window, EventArgs.Empty);
            await gate.SpriteRequested.Task.WaitAsync(TimeSpan.FromSeconds(5));
            gate.ReleaseSprite.TrySetResult();
            await gate.EggRequested.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(0, window.Opacity);

            Invoke(window, "OnGlobalInput");
            Dispatcher.UIThread.RunJobs();
            Assert.Equal((16, 0), (settings.For(4).Level, settings.For(4).Exp));
            Assert.Equal(4, settings.SelectedDex);
            Assert.Null(settings.For(4).PendingEvolution);
            Assert.False(Field<bool>(window, "_evolving"));
            Assert.False(effect.HasFrames);
            Assert.DoesNotContain(assets.Requests, request => request.Contains("/5."));
            Assert.False(window.StartupPresentation.IsCompleted);
            Assert.Equal(0, window.Opacity);

            gate.ReleaseEgg.TrySetResult();
            await window.StartupPresentation.WaitAsync(TimeSpan.FromSeconds(5));
            await EventuallyAsync(window, () => !Field<bool>(window, "_dexLoading"));
            Assert.True(observedEvolution);
            Assert.True(evolutionStayedHidden);
            Assert.Equal(false, evolvingWhenRevealed);
            Assert.Equal(5, dexWhenRevealed);
            Assert.Equal(1, window.Opacity);
            Assert.False(effect.HasFrames);
            Assert.False(window.FindControl<Control>("EggFallback")!.IsVisible);
            Assert.Same(settings.For(4), settings.For(5));
            Assert.Equal(new[] { 4, 5 }, settings.For(4).History);
            Assert.Equal((16, 0), (settings.For(5).Level, settings.For(5).Exp));
        }
        finally
        {
            gate.ReleaseSprite.TrySetResult();
            gate.ReleaseEgg.TrySetResult();
            try { await window.StartupPresentation.WaitAsync(TimeSpan.FromSeconds(5)); }
            finally { window.Close(); }
        }
    }

    [AvaloniaFact]
    public async Task FailedInitialSpriteStillRevealsAUsablePlaceholder()
    {
        using var assets = new UiAssets { MissingSpriteDex = 4 };
        var settings = Settings.NewPreview(4);
        settings.PendingEgg = new(EggKind.Common, 4, false);
        var window = NewStartupWindowWithoutNativeServices(settings);
        try
        {
            window.Show();
            Invoke(window, "OnLoaded", window, EventArgs.Empty);
            await window.StartupPresentation.WaitAsync(TimeSpan.FromSeconds(5));
            await EventuallyAsync(window, () => !Field<bool>(window, "_dexLoading"));
            Assert.Equal(1, window.Opacity);
            Assert.True(window.FindControl<TextBlock>("SpriteMissing")!.IsVisible);
            Assert.True(window.FindControl<TextBlock>("SpriteStatus")!.IsVisible);
            Assert.False(window.FindControl<Control>("EggFallback")!.IsVisible);
            Assert.Equal(4, settings.SelectedDex);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task ClosingDuringTheEggWaitCancelsTheRevealAndPreviewStartsVisible()
    {
        using var assets = new UiAssets();
        var window = new MainWindow(Settings.NewPreview(4), false);
        Assert.Equal(1, window.Opacity);
        var pendingEgg = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        typeof(MainWindow).GetField("_eggArtLoad", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(window, pendingEgg.Task);
        try
        {
            window.Show();
            Invoke(window, "BuildGenTabs");
            window.Opacity = 0;
            var complete = InvokeAsync(window, "CompleteStartupPresentationAsync");
            Assert.False(complete.IsCompleted);
            await EventuallyAsync(window, () => !Field<bool>(window, "_dexLoading"));
            window.Close();
            await complete.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(window.StartupPresentation.IsCanceled);
            pendingEgg.TrySetResult();
            Assert.Equal(0, window.Opacity);
            Assert.False(window.IsVisible);
        }
        finally { pendingEgg.TrySetResult(); window.Close(); }
    }

    private static MainWindow NewStartupWindowWithoutNativeServices(Settings settings)
    {
        // Exercise the real OnLoaded lifecycle without constructing global input hooks,
        // OS startup registration, or a save path outside this isolated preview.
        var window = new MainWindow(settings, false);
        typeof(MainWindow).GetField("_startServices", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(window, true);
        Invoke(window, "InitializeStartupPresentation");
        return window;
    }

    private sealed class StartupArtworkGate : HttpMessageHandler
    {
        private readonly HttpMessageInvoker _inner;
        public TaskCompletionSource SpriteRequested { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource EggRequested { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseSprite { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseEgg { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public StartupArtworkGate(HttpMessageHandler inner) => _inner = new HttpMessageInvoker(inner, disposeHandler: false);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/pokemon/exp/4.json"))
            {
                SpriteRequested.TrySetResult();
                await ReleaseSprite.Task.WaitAsync(token);
            }
            if (path.EndsWith("/egg/egg.json"))
            {
                EggRequested.TrySetResult();
                await ReleaseEgg.Task.WaitAsync(token);
            }
            return await _inner.SendAsync(request, token);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _inner.Dispose();
            base.Dispose(disposing);
        }
    }
}
