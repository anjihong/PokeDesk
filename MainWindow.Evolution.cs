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
    private bool _checkChoiceAgain;
    private readonly HashSet<PokemonProgress> _deferredEvolutionChoices = new();

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
        await CheckEvolutionAsync(true);
    }

    private int? ChooseEvolution(int[] targets)
    {
        int? selected = null;
        var panel = new StackPanel { Margin = new Thickness(16) };
        panel.Children.Add(new TextBlock { Text = "아직 얻지 않은 진화체를 선택하세요.", Margin = new Thickness(0, 0, 0, 10) });
        var dialog = new Window
        {
            Title = "진화 선택", Content = panel, SizeToContent = SizeToContent.WidthAndHeight,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false, Topmost = true
        };
        if (IsVisible) dialog.Owner = this;
        foreach (var target in targets)
        {
            var button = new Button { Content = PokemonNames.Of(target), Padding = new Thickness(12, 8, 12, 8), Margin = new Thickness(0, 2, 0, 2) };
            button.Click += (_, _) => { selected = target; dialog.DialogResult = true; };
            panel.Children.Add(button);
        }
        var cancel = new Button { Content = "나중에", IsCancel = true, Margin = new Thickness(0, 10, 0, 0) };
        panel.Children.Add(cancel);
        dialog.ShowDialog();
        return selected;
    }

    private async Task CheckEvolutionAsync(bool allowChoice = false)
    {
        if (_closed || _selectingPokemon) return;
        if (_evolving) { _checkEvolutionAgain = true; _checkChoiceAgain |= allowChoice; return; }
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
                int? chosen = p.PendingEvolution;
                if (chosen is null && _settings.NeedsEvolutionChoice(dex, shiny))
                {
                    // 명시적 클릭으로 다시 열 수 있다. 입력마다 닫은 선택창을 반복 표시하지 않는다.
                    if (!allowChoice && _deferredEvolutionChoices.Contains(p)) break;
                    chosen = ChooseEvolution(options.Select(r => r.ToId).ToArray());
                    if (chosen is null) { _deferredEvolutionChoices.Add(p); break; }
                    _deferredEvolutionChoices.Remove(p);
                }
                if (!IsCurrent()) break;
                var target = _settings.PrepareEvolution(dex, shiny, chosen);
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
        var choiceAgain = _checkChoiceAgain;
        _checkEvolutionAgain = _checkChoiceAgain = false;
        if (recheck && !_closed) await CheckEvolutionAsync(choiceAgain);
    }
}
