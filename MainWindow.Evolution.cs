using Avalonia.Controls;
using Avalonia.Interactivity;
using System.Diagnostics;

namespace DeskPokemon;

public partial class MainWindow
{
    private bool _evolving;

    private void SetEvolutionCellState(RadioButton cell, int dex, bool shiny, bool owned) =>
        cell.Classes.Set("evolvable", owned && _settings.CanEvolve(dex, shiny));

    private string EvolutionCellTip(int dex, bool shiny) => _settings.CanEvolve(dex, shiny) ? " · 진화 가능" : "";

    private void RefreshEvolutionUi()
    {
        if (_evolving || _closed) return;
        var shiny = _settings.SelectedShiny;
        var direct = _settings.AvailableEvolutions(_settings.SelectedDex, shiny);
        var owned = shiny ? _settings.ShinyOwned : _settings.Owned;
        var ready = owned.Where(dex => _settings.CanEvolve(dex, shiny)).Order().ToArray();
        EvolutionNotice.IsVisible = ready.Length > 0;
        EvolutionNotice.IsEnabled = true;
        EvolutionNoticeText.Text = direct.Count > 0
            ? $"{PokemonNames.Of(_settings.SelectedDex)} · 진화 가능!"
            : ready.Length > 0 ? $"{PokemonNames.Of(ready[0])}{(ready.Length > 1 ? $" 외 {ready.Length - 1}마리" : "")} · 진화 가능!" : "";
        EvolutionChoices.IsVisible = false;
        EvolutionChoices.Children.Clear();
        foreach (RadioButton cell in IconGrid.Children)
        {
            var choice = (PokemonChoice)cell.Tag!;
            SetEvolutionCellState(cell, choice.Dex, choice.IsShiny, _settings.IsOwned(choice.Dex, choice.IsShiny));
            if (_settings.IsOwned(choice.Dex, choice.IsShiny))
            {
                var level = _settings.For(choice.Dex, choice.IsShiny).Level;
                UiToolTips.Set(cell, $"#{choice.Dex} {PokemonNames.Of(choice.Dex)}{(choice.IsShiny ? " ★ 이로치" : "")} · Lv.{level}{EvolutionCellTip(choice.Dex, choice.IsShiny)}");
            }
        }
    }

    private async void OnEvolutionClick(object? sender, RoutedEventArgs e)
    {
        if (_evolving) return;
        var shiny = _settings.SelectedShiny;
        var options = _settings.AvailableEvolutions(_settings.SelectedDex, shiny);
        if (options.Count == 1)
        {
            await EvolveAsync(options[0].TargetDex);
            return;
        }
        EvolutionChoices.Children.Clear();
        if (options.Count > 1)
        {
            foreach (var option in options)
            {
                var button = new Button { Content = PokemonNames.Of(option.TargetDex), Margin = new Avalonia.Thickness(2) };
                button.Click += async (_, _) => await EvolveAsync(option.TargetDex);
                EvolutionChoices.Children.Add(button);
            }
        }
        else
        {
            var owned = shiny ? _settings.ShinyOwned : _settings.Owned;
            foreach (var dex in owned.Where(dex => _settings.CanEvolve(dex, shiny)).Order())
            {
                var button = new Button { Content = $"{PokemonNames.Of(dex)} 선택", Margin = new Avalonia.Thickness(2) };
                button.Click += async (_, _) => await SelectPokemonAsync(new PokemonChoice(dex, shiny));
                EvolutionChoices.Children.Add(button);
            }
        }
        EvolutionChoices.IsVisible = EvolutionChoices.Children.Count > 0;
        ApplyPresentation();
    }

    private async Task CheckEvolutionAsync()
    {
        if (_closed || _evolving || _eggState == EggState.Hatching) return;
        RefreshEvolutionUi();
        var options = _settings.AvailableEvolutions(_settings.SelectedDex, _settings.SelectedShiny);
        if (options.Count == 1 && EvolutionRules.OptionsFor(_settings.SelectedDex).Count == 1)
            await EvolveAsync(options[0].TargetDex);
        // Branching evolutions require an explicit choice; never choose a random destination.
    }

    private async Task EvolveAsync(int targetDex)
    {
        if (_closed || _evolving || _eggState == EggState.Hatching) return;
        var dex = _settings.SelectedDex;
        var shiny = _settings.SelectedShiny;
        if (!_settings.AvailableEvolutions(dex, shiny).Any(option => option.TargetDex == targetDex)) return;
        _evolving = true;
        ++_loadRequest;
        EvolutionChoices.IsVisible = false;
        EvolutionNotice.IsVisible = true;
        EvolutionNotice.IsEnabled = false;
        EvolutionNoticeText.Text = "진화 준비 중…";
        SpriteAtlas? atlas = null;
        SpriteAtlas? temporarySource = null;
        var previousOpacity = StageZoom.Opacity;
        try
        {
            atlas = await LoadEvolutionAtlasAsync(targetDex, shiny, _lifetime.Token);
            var source = _atlas ?? (temporarySource = await LoadEvolutionAtlasAsync(dex, shiny, _lifetime.Token));
            _lifetime.Token.ThrowIfCancellationRequested();
            _animations["Bounce"].Stop();
            var sourceScale = _atlas == null ? PetScaleFor(source) : Zoom.ScaleX;
            var targetScale = PetScaleFor(atlas);
            EvolutionVisual.SetFrames(source, _atlas == null ? 0 : _frame, sourceScale, atlas, targetScale);
            EvolutionVisual.FlipHorizontal = _settings.FlipHorizontal;
            StageZoom.Opacity = 0;
            EvolutionNoticeText.Text = "진화 중…";
            ApplyPresentation();
            await PlayEvolutionPhaseAsync(0, EvolutionEffect.RevealProgress, _lifetime.Token);
            _lifetime.Token.ThrowIfCancellationRequested();
            // The target stays a white silhouette until the atomic save succeeds.
            var result = _settings.Evolve(dex, targetDex, shiny);
            if (result == null) return;
            _dirty = false;
            EvolutionNoticeText.Text = "진화 완료!";
            await PlayEvolutionPhaseAsync(EvolutionEffect.RevealProgress, 1, _lifetime.Token);
            // Apply the target only after reveal so atlas sizing cannot move the effect's foot anchor.
            ApplyPokemonAtlas(atlas);
            atlas = null;
            _animations["LevelUp"].Play();
            UpdateOwnedCount();
            if (CheckedGen() is { } gen) RebuildIconGrid(gen, CachedDexIcons(gen, ViewingShiny));
            SyncSelectedIcon();
            UpdateDexDetails();
        }
        catch (OperationCanceledException) when (_closed) { }
        catch (Exception ex)
        {
            if (!_closed) ShowSpriteError($"진화를 완료하지 못했습니다. 기존 포켓몬은 유지됩니다.\n{ex.Message}");
        }
        finally
        {
            EvolutionVisual.Clear();
            temporarySource?.Dispose();
            atlas?.Dispose();
            StageZoom.Opacity = previousOpacity;
            Stage.Opacity = 1;
            _evolving = false;
            if (!_closed)
            {
                RefreshEvolutionUi();
                ApplyPresentation();
            }
        }
    }

    private async Task PlayEvolutionPhaseAsync(double from, double to, CancellationToken cancellationToken)
    {
        var clock = Stopwatch.StartNew();
        var duration = (to - from) * EvolutionEffect.DurationSeconds;
        while (clock.Elapsed.TotalSeconds < duration)
        {
            cancellationToken.ThrowIfCancellationRequested();
            EvolutionVisual.FlipHorizontal = _settings.FlipHorizontal;
            EvolutionVisual.Progress = from + (to - from) * Math.Min(1, clock.Elapsed.TotalSeconds / duration);
            await Task.Delay(16, cancellationToken);
        }
        EvolutionVisual.Progress = to;
    }

    private static async Task<SpriteAtlas> LoadEvolutionAtlasAsync(int dex, bool shiny, CancellationToken cancellationToken)
    {
        var load = SpriteAtlas.LoadAsync(dex, shiny);
        try { return await load.WaitAsync(cancellationToken); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Shared cache downloads may continue for another caller after this window closes.
            // The abandoned caller still owns its eventual atlas and must release it.
            _ = DisposeAbandonedEvolutionAtlasAsync(load);
            throw;
        }
    }

    private static async Task DisposeAbandonedEvolutionAtlasAsync(Task<SpriteAtlas> load)
    {
        try { (await load.ConfigureAwait(false)).Dispose(); }
        catch { /* The original window was cancelled; consume the abandoned load's error. */ }
    }
}
