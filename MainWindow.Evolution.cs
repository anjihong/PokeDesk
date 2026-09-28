using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace DeskPokemon;

public partial class MainWindow
{
    private bool _evolving;
    private bool _selectingPokemon;
    private bool _checkEvolutionAgain;

    private void RefreshEvolutionUi()
    {
        var selected = _settings.EvolutionOptions(_settings.SelectedDex, _settings.SelectedShiny);
        EvolutionBadge.Visibility = selected.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        EvolutionBadge.ToolTip = selected.Length > 0
            ? $"{PokemonNames.Of(selected[0].FromId)} 선택 시 진화" : null;
        var ready = (ViewingShiny ? _settings.ShinyOwned : _settings.Owned)
            .SelectMany(dex => _settings.EvolutionOptions(dex, ViewingShiny))
            .Select(r => r.FromId).ToHashSet();
        foreach (RadioButton tab in GenTabs.Children)
        {
            var active = ready.Any(dex => PokemonIcons.GenOf(dex) == (int)tab.Tag);
            tab.Background = new SolidColorBrush(active ? Color.FromArgb(130, 60, 190, 100) : Color.FromArgb(51, 255, 255, 255));
            tab.ToolTip = active ? "진화 가능한 포켓몬 있음" : null;
        }
        foreach (RadioButton cell in IconGrid.Children)
        {
            var choice = (PokemonChoice)cell.Tag;
            var active = ready.Contains(choice.Dex);
            cell.BorderBrush = active ? Brushes.LightGreen : Brushes.Transparent;
            cell.Background = active ? new SolidColorBrush(Color.FromArgb(60, 80, 220, 120)) : Brushes.Transparent;
            if (_settings.HasOwned(choice.Dex, choice.IsShiny))
                cell.ToolTip = $"#{EvolutionData.NationalDex(choice.Dex)} {PokemonNames.Of(choice.Dex)}" +
                    $"{(choice.IsShiny ? " ★ 이로치" : "")} · Lv.{_settings.For(choice.Dex, choice.IsShiny).Level}" +
                    (active ? " · 진화 가능" : "");
        }
    }

    private async void OnEvolutionClick(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        var options = _settings.EvolutionOptions(_settings.SelectedDex, _settings.SelectedShiny);
        if (options.Length == 0) return;
        var source = options[0].FromId;
        if (source != _settings.SelectedDex)
        {
            ShinyDex.IsChecked = _settings.SelectedShiny;
            SelectGenTab(PokemonIcons.GenOf(source));
            var dexTab = MenuTabs.Children.OfType<System.Windows.Controls.Primitives.ToggleButton>()
                .First(b => (string)b.Tag == "dex");
            dexTab.IsChecked = true;
            RefreshEvolutionUi();
            return;
        }
        await CheckEvolutionAsync();
    }

    private async Task CheckEvolutionAsync()
    {
        if (_closed || _selectingPokemon) return;
        if (_evolving) { _checkEvolutionAgain = true; return; }
        _evolving = true;
        var request = _loadRequest;
        try
        {
            while (!_closed)
            {
                var dex = _settings.SelectedDex;
                var shiny = _settings.SelectedShiny;
                var options = _settings.EvolutionOptions(dex, shiny);
                if (options.Length == 0 || options[0].FromId != dex) break;
                var p = _settings.For(dex, shiny);
                request = ++_loadRequest;
                if (!IsCurrent()) break;
                var target = _settings.PrepareEvolution(dex, shiny);
                var atlas = await SpriteAtlas.LoadAsync(target, shiny);
                if (!IsCurrent()) break;
                var blink = new DoubleAnimation(1, .15, TimeSpan.FromMilliseconds(150))
                    { AutoReverse = true, RepeatBehavior = new RepeatBehavior(2) };
                Stage.BeginAnimation(OpacityProperty, blink);
                await Task.Delay(600);
                if (!IsCurrent()) break;
                _settings.CompleteEvolution(dex, shiny, target);
                _dirty = false;
                ApplyAtlas(atlas);
                UpdateOwnedCount();
                if (CheckedGen() is { } gen && PokemonIcons.TryGetCachedGen(gen, out var icons, ViewingShiny))
                    RebuildIconGrid(gen, icons);
                SyncSelectedIcon();
                RefreshEvolutionUi();

                bool IsCurrent() => !_closed && request == _loadRequest &&
                    _settings.SelectedDex == dex && _settings.SelectedShiny == shiny &&
                    ReferenceEquals(p, _settings.For(dex, shiny));
            }
        }
        catch (Exception ex)
        {
            if (!_closed && request == _loadRequest)
                MessageBox.Show($"진화를 완료하지 못했습니다. 진화 가능 표시를 눌러 다시 시도하세요.\n{ex.Message}", "진화 실패");
        }
        finally
        {
            Stage.BeginAnimation(OpacityProperty, null);
            _evolving = false;
            if (!_closed) RefreshEvolutionUi();
        }
        var recheck = _checkEvolutionAgain;
        _checkEvolutionAgain = false;
        if (recheck && !_closed) await CheckEvolutionAsync();
    }
}
