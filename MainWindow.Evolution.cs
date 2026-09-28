using Avalonia.Controls;
using Avalonia.Interactivity;

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
        try
        {
            atlas = await SpriteAtlas.LoadAsync(targetDex, shiny);
            _lifetime.Token.ThrowIfCancellationRequested();
            EvolutionNoticeText.Text = "진화 중…";
            for (var i = 0; i < 4; i++)
            {
                Stage.Opacity = i % 2 == 0 ? .25 : 1;
                await Task.Delay(120, _lifetime.Token);
            }
            var result = _settings.Evolve(dex, targetDex, shiny);
            if (result == null) return;
            _dirty = false;
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
            atlas?.Dispose();
            Stage.Opacity = 1;
            _evolving = false;
            if (!_closed)
            {
                RefreshEvolutionUi();
                ApplyPresentation();
            }
        }
    }
}
