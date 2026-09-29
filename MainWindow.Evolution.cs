using Avalonia.Controls;
using Avalonia.Interactivity;
using System.Diagnostics;

namespace DeskPokemon;

public partial class MainWindow
{
    private bool _evolving;
    private int _pendingSelections;
    private bool _checkEvolutionAgain;

    private void SetEvolutionCellState(RadioButton cell, int dex, bool shiny, bool owned) =>
        cell.Classes.Set("evolvable", owned && _settings.CanEvolve(dex, shiny));

    private string EvolutionCellTip(int dex, bool shiny) => _settings.CanEvolve(dex, shiny) ? " · 진화 가능" : "";

    private void RefreshEvolutionUi()
    {
        if (_evolving || _closed) return;
        var shiny = _settings.SelectedShiny;
        var options = _settings.EvolutionOptions(_settings.SelectedDex, shiny);
        var choices = _settings.ShinyOwned.Select(dex => new PokemonChoice(dex, true));
        if (!ViewingShiny) choices = _settings.Owned.Select(dex => new PokemonChoice(dex, false)).Concat(choices);
        var ready = choices.SelectMany(choice => _settings.EvolutionOptions(choice.Dex, choice.IsShiny))
            .Select(rule => rule.FromId).ToHashSet();
        EvolutionNotice.IsVisible = options.Length > 0;
        EvolutionNotice.IsEnabled = true;
        EvolutionNoticeText.Text = options.Length == 0 ? "" : options[0].FromId == _settings.SelectedDex
            ? $"{PokemonNames.Of(_settings.SelectedDex)} · 진화 가능!"
            : $"{PokemonNames.Of(options[0].FromId)} 선택 시 진화";
        EvolutionChoices.IsVisible = false;
        EvolutionChoices.Children.Clear();
        foreach (RadioButton tab in GenTabs.Children)
        {
            var generation = (int)tab.Tag!;
            var active = ready.Any(dex => generation == 0 || PokemonIcons.GenOf(dex) == generation);
            tab.Classes.Set("evolvable", active);
            if (active) UiToolTips.Set(tab, "진화 가능한 포켓몬이 있습니다.");
            else ToolTip.SetTip(tab, null);
        }
        foreach (RadioButton cell in IconGrid.Children)
        {
            var choice = (PokemonChoice)cell.Tag!;
            SetEvolutionCellState(cell, choice.Dex, choice.IsShiny, _settings.IsOwned(choice.Dex, choice.IsShiny));
            if (_settings.IsOwned(choice.Dex, choice.IsShiny))
            {
                var level = _settings.HasOwned(choice.Dex, choice.IsShiny)
                    ? _settings.For(choice.Dex, choice.IsShiny).Level : 1;
                UiToolTips.Set(cell, $"#{EvolutionData.NationalDex(choice.Dex)} {PokemonNames.Of(choice.Dex)}{(choice.IsShiny ? " ★ 이로치" : "")} · Lv.{level}{EvolutionCellTip(choice.Dex, choice.IsShiny)}");
            }
        }
    }

    private async void OnEvolutionClick(object? sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (_evolving || _pendingSelections > 0) return;
        var shiny = _settings.SelectedShiny;
        var options = _settings.EvolutionOptions(_settings.SelectedDex, shiny);
        if (options.Length == 0) return;
        var source = options[0].FromId;
        if (source != _settings.SelectedDex)
        {
            ShinyDex.IsChecked = shiny;
            SelectGenTab(PokemonIcons.GenOf(source));
            MenuTabs.Children.OfType<Avalonia.Controls.Primitives.ToggleButton>()
                .Single(tab => Equals(tab.Tag, "dex")).IsChecked = true;
            RefreshEvolutionUi();
            return;
        }
        await CheckEvolutionAsync();
    }

    private async Task CheckEvolutionAsync()
    {
        if (_closed || !_startupCanEvolve || _pendingSelections > 0 || _eggState == EggState.Hatching) return;
        if (_evolving) { _checkEvolutionAgain = true; return; }
        do
        {
            _checkEvolutionAgain = false;
            RefreshEvolutionUi();
            var dex = _settings.SelectedDex;
            var shiny = _settings.SelectedShiny;
            if (_settings.AvailableEvolutions(dex, shiny).Count == 0) break;
            int target;
            try { target = _settings.PrepareEvolution(dex, shiny); }
            catch (Exception ex)
            {
                ShowSpriteError($"진화 대상을 저장하지 못했습니다. 다시 시도해 주세요.\n{ex.Message}");
                break;
            }
            await EvolveAsync(target);
            if (_closed || _settings.SelectedDex == dex) break;
            // A high-level run advances one step at a time, preserving every appearance.
            _checkEvolutionAgain = true;
        } while (_checkEvolutionAgain && !_closed && _pendingSelections == 0 && _eggState != EggState.Hatching);
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
            targetDex = _settings.PrepareEvolution(dex, shiny, targetDex);
            atlas = await LoadEvolutionAtlasAsync(targetDex, shiny, _lifetime.Token);
            var source = _atlas ?? (temporarySource = await LoadEvolutionAtlasAsync(dex, shiny, _lifetime.Token));
            _lifetime.Token.ThrowIfCancellationRequested();
            _animations["Bounce"].Stop();
            var sourceScale = _atlas == null ? PetScaleFor(source, dex) : Zoom.ScaleX;
            var targetScale = PetScaleFor(atlas, targetDex);
            EvolutionVisual.SetFrames(source, _atlas == null ? 0 : _frame, sourceScale, atlas, targetScale);
            EvolutionVisual.FlipHorizontal = _settings.FlipHorizontal;
            StageZoom.Opacity = 0;
            EvolutionNoticeText.Text = "진화 중…";
            ApplyPresentation();
            await PlayEvolutionPhaseAsync(0, EvolutionEffect.RevealProgress, _lifetime.Token);
            _lifetime.Token.ThrowIfCancellationRequested();
            // The target stays a white silhouette until the atomic save succeeds.
            _settings.CompleteEvolution(dex, shiny, targetDex);
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
            if (!_closed) ShowSpriteError($"진화를 완료하지 못했습니다. 기존 포켓몬과 확정된 진화 대상은 유지됩니다.\n{ex.Message}");
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
