using System.Net.Http;
using System.Reflection;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Xunit;

namespace DeskPokemon.Tests;

public partial class UiTests
{
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SelectingAnotherPokemonCancelsEvolutionAndKeepsTheReservedTargetForItsColor(bool shiny)
    {
        using var assets = new UiAssets();
        var settings = Settings.NewPreview(4);
        settings.AddOwned(7);
        if (shiny) settings.AddOwned(4, true);
        settings.SelectedShiny = shiny;
        settings.For(4, shiny).Level = 16;
        settings.For(4, shiny).Exp = 17;
        var window = new MainWindow(settings, false);
        try
        {
            ShowAndLayout(window);
            Assert.True(await InvokeAsync<bool>(window, "LoadPokemonAsync", 4, shiny));
            var evolution = InvokeAsync(window, "CheckEvolutionAsync");
            var effect = window.FindControl<EvolutionEffect>("EvolutionVisual")!;
            await EventuallyAsync(window, () => effect.HasFrames && effect.Progress > .02);

            await SelectEvolutionChoiceAsync(window, 7).WaitAsync(TimeSpan.FromSeconds(2));
            await evolution.WaitAsync(TimeSpan.FromSeconds(2));

            Assert.Equal(7, settings.SelectedDex);
            Assert.False(settings.SelectedShiny);
            Assert.Equal("꼬부기", window.FindControl<TextBlock>("PetName")!.Text);
            Assert.Equal(5, settings.For(4, shiny).PendingEvolution);
            Assert.Equal((16, 17), (settings.For(4, shiny).Level, settings.For(4, shiny).Exp));
            Assert.Equal(new[] { 4 }, settings.For(4, shiny).History);
            Assert.False(settings.HasOwned(5, shiny));
            Assert.False(settings.HasOwned(5, !shiny));
            Assert.False(effect.HasFrames);
            Assert.False(Field<bool>(window, "_evolving"));
            Assert.Equal(0, Field<int>(window, "_pendingSelections"));
            Assert.Equal(1, window.FindControl<LayoutTransformControl>("StageZoom")!.Opacity);
            Assert.False(window.FindControl<TextBlock>("SpriteStatus")!.IsVisible);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task SelectingTheCommittedTargetDuringRevealKeepsItsBodyAndSavedEvolution()
    {
        using var assets = new UiAssets();
        var settings = Settings.NewPreview(4);
        settings.For(4).Level = 16;
        var window = new MainWindow(settings, false);
        try
        {
            ShowAndLayout(window);
            Assert.True(await InvokeAsync<bool>(window, "LoadPokemonAsync", 4, false));
            var source = Field<SpriteAtlas>(window, "_atlas");
            var evolution = InvokeAsync(window, "CheckEvolutionAsync");
            var effect = window.FindControl<EvolutionEffect>("EvolutionVisual")!;
            await EventuallyAsync(window, () => settings.SelectedDex == 5 && effect.HasFrames);

            await SelectEvolutionChoiceAsync(window, 5).WaitAsync(TimeSpan.FromSeconds(2));
            await evolution.WaitAsync(TimeSpan.FromSeconds(2));

            Assert.Equal(5, settings.SelectedDex);
            Assert.True(settings.HasOwned(5));
            Assert.Equal(new[] { 4, 5 }, settings.For(4).History);
            Assert.Null(settings.For(4).PendingEvolution);
            Assert.NotSame(source, Field<SpriteAtlas>(window, "_atlas"));
            Assert.Equal("리자드", window.FindControl<TextBlock>("PetName")!.Text);
            Assert.False(effect.HasFrames);
            Assert.False(Field<bool>(window, "_evolving"));
            Assert.Equal(1, window.FindControl<LayoutTransformControl>("StageZoom")!.Opacity);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task SelectionCancelsEvolutionPreloadWithoutWaitingForTheDownloadOrApplyingItsLateResult()
    {
        using var assets = new UiAssets();
        var originalHttp = SpriteAtlas.Http;
        var delayed = new DelayedEvolutionTarget(originalHttp);
        using var client = new HttpClient(delayed);
        SpriteAtlas.Http = client;
        var settings = Settings.NewPreview(4);
        settings.For(4).Level = 16;
        settings.AddOwned(7);
        var window = new MainWindow(settings, false);
        Task? evolution = null;
        try
        {
            ShowAndLayout(window);
            Assert.True(await InvokeAsync<bool>(window, "LoadPokemonAsync", 4, false));
            evolution = InvokeAsync(window, "CheckEvolutionAsync");
            await delayed.Started.Task.WaitAsync(TimeSpan.FromSeconds(3));
            Assert.True(Field<bool>(window, "_evolving"));
            Assert.False(window.FindControl<EvolutionEffect>("EvolutionVisual")!.HasFrames);

            await SelectEvolutionChoiceAsync(window, 7).WaitAsync(TimeSpan.FromSeconds(2));
            await evolution.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.False(delayed.Release.Task.IsCompleted);
            Assert.Equal(7, settings.SelectedDex);
            var selectedAtlas = Field<SpriteAtlas>(window, "_atlas");

            delayed.Release.TrySetResult();
            // Drain the shared download while its abandoned evolution caller releases
            // its own atlas; it must never apply that result to the selected body.
            using var completedDownload = await SpriteAtlas.LoadAsync(5).WaitAsync(TimeSpan.FromSeconds(3));
            Dispatcher.UIThread.RunJobs();
            Assert.Same(selectedAtlas, Field<SpriteAtlas>(window, "_atlas"));
            Assert.Equal("꼬부기", window.FindControl<TextBlock>("PetName")!.Text);
            Assert.Equal(7, settings.SelectedDex);
            Assert.Equal(5, settings.For(4).PendingEvolution);
            Assert.False(settings.HasOwned(5));
            Assert.False(window.FindControl<EvolutionEffect>("EvolutionVisual")!.HasFrames);
        }
        finally
        {
            delayed.Release.TrySetResult();
            window.Close();
            try
            {
                if (evolution is not null) await evolution.WaitAsync(TimeSpan.FromSeconds(3));
                if (delayed.Started.Task.IsCompleted)
                {
                    using var drained = await SpriteAtlas.LoadAsync(5).WaitAsync(TimeSpan.FromSeconds(3));
                    Dispatcher.UIThread.RunJobs();
                }
            }
            finally { SpriteAtlas.Http = originalHttp; }
        }
    }

    [AvaloniaFact]
    public async Task TwoSelectionsDuringEvolutionCleanupKeepOnlyTheLatestChoice()
    {
        using var assets = new UiAssets();
        var settings = Settings.NewPreview(4);
        settings.For(4).Level = 16;
        settings.AddOwned(7);
        settings.AddOwned(1, true);
        var window = new MainWindow(settings, false);
        try
        {
            ShowAndLayout(window);
            Assert.True(await InvokeAsync<bool>(window, "LoadPokemonAsync", 4, false));
            var evolution = InvokeAsync(window, "CheckEvolutionAsync");
            var effect = window.FindControl<EvolutionEffect>("EvolutionVisual")!;
            await EventuallyAsync(window, () => effect.HasFrames && effect.Progress > .02);

            var older = SelectEvolutionChoiceAsync(window, 7);
            var newest = SelectEvolutionChoiceAsync(window, 1, true);
            await Task.WhenAll(evolution, older, newest).WaitAsync(TimeSpan.FromSeconds(3));

            Assert.Equal(1, settings.SelectedDex);
            Assert.True(settings.SelectedShiny);
            Assert.Equal("이상해씨", window.FindControl<TextBlock>("PetName")!.Text);
            Assert.Equal("★ Lv. 1", window.FindControl<TextBlock>("LevelText")!.Text);
            Assert.Equal(5, settings.For(4).PendingEvolution);
            Assert.False(settings.HasOwned(5));
            Assert.False(effect.HasFrames);
            Assert.False(Field<bool>(window, "_evolving"));
            Assert.Equal(0, Field<int>(window, "_pendingSelections"));
            Assert.Contains(assets.Requests, request => request.EndsWith("/pokemon/exp/shiny/1.json"));
        }
        finally { window.Close(); }
    }

    private static Task SelectEvolutionChoiceAsync(MainWindow window, int dex, bool shiny = false)
    {
        var cell = (RadioButton)typeof(MainWindow).GetMethod("MakeIconCell", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(window, [dex, shiny, null])!;
        return InvokeAsync(window, "SelectPokemonAsync", cell.Tag!);
    }

    private sealed class DelayedEvolutionTarget(HttpClient inner) : HttpMessageHandler
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/pokemon/exp/5.json"))
            {
                Started.TrySetResult();
                await Release.Task.WaitAsync(cancellationToken);
            }
            return await inner.GetAsync(request.RequestUri, cancellationToken);
        }
    }
}
