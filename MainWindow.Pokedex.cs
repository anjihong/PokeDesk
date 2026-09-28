using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;

namespace DeskPokemon;

public partial class MainWindow
{
    private Dictionary<int, Bitmap> _visibleIcons = new();
    private int _visibleGeneration = -1;
    private bool _visibleShiny;
    private bool _dexLoading;
    private readonly List<int> _failedGenerations = new();

    private void BuildGenTabs()
    {
        if (GenTabs.Children.Count != 0) return;
        var style = (ControlTheme)Resources["GenTab"]!;
        foreach (var gen in Enumerable.Range(0, 10))
        {
            var rb = new RadioButton
            {
                Content = gen == 0 ? "전체" : gen.ToString(), Tag = gen,
                GroupName = "Gen", Theme = style,
            };
            Avalonia.Automation.AutomationProperties.SetName(rb, gen == 0 ? "전체 세대" : $"{gen}세대");
            rb.IsCheckedChanged += OnGenChecked;
            GenTabs.Children.Add(rb);
        }
    }

    private void SelectGenTab(int gen)
    {
        foreach (RadioButton rb in GenTabs.Children)
            if ((int)rb.Tag! == gen) rb.IsChecked = true;
    }

    private async void OnGenChecked(object? sender, RoutedEventArgs e)
    {
        if (sender is RadioButton { IsChecked: true }) await RefreshDexAsync();
    }

    private async void OnShinyDexChanged(object? sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        UpdateOwnedCount();
        UpdateDexDetails();
        await RefreshDexAsync();
    }

    private async void OnRetryDex(object? sender, RoutedEventArgs e) => await RefreshDexAsync();

    private async Task RefreshDexAsync()
    {
        if (CheckedGen() is not { } gen) return;
        var shiny = ViewingShiny;
        var request = ++_iconRequest;
        var generations = gen == 0 ? Enumerable.Range(1, 9).ToArray() : new[] { gen };
        var icons = CachedDexIcons(gen, shiny);
        _dexLoading = true;
        _failedGenerations.Clear();
        RebuildIconGrid(gen, icons);
        IconScroll.Offset = default;
        bool IsCurrent() => !_closed && request == _iconRequest && CheckedGen() == gen && ViewingShiny == shiny;
        await Task.WhenAll(generations.Select(async generation =>
        {
            try
            {
                var loaded = await PokemonIcons.LoadGenAsync(generation, shiny);
                if (!IsCurrent()) return;
                foreach (var item in loaded) icons[item.Key] = item.Value;
            }
            catch
            {
                if (IsCurrent()) _failedGenerations.Add(generation);
            }
        }));
        if (!IsCurrent()) return;
        _dexLoading = false;
        RebuildIconGrid(gen, icons);
        UpdateDexDetails();
    }

    private static Dictionary<int, Bitmap> CachedDexIcons(int gen, bool shiny)
    {
        var icons = new Dictionary<int, Bitmap>();
        foreach (var generation in gen == 0 ? Enumerable.Range(1, 9) : new[] { gen })
            if (PokemonIcons.TryGetCachedGen(generation, out var cached, shiny))
                foreach (var entry in cached) icons[entry.Key] = entry.Value;
        return icons;
    }

    private void RebuildIconGrid(int gen, Dictionary<int, Bitmap> icons)
    {
        _visibleIcons = icons;
        _visibleGeneration = gen;
        _visibleShiny = ViewingShiny;
        var ownedOnly = OwnedOnly.IsChecked == true;
        var shinyOnly = ViewingShiny;
        // Each generation sheet populates both color caches in one load. Keep the
        // two maps separate: a shiny icon can never overwrite its normal species key.
        var shinyIcons = shinyOnly ? icons : CachedDexIcons(gen, true);
        var first = gen == 0 ? 1 : PokemonIcons.Generations[gen - 1].First;
        var last = gen == 0 ? 1025 : PokemonIcons.Generations[gen - 1].Last;
        IconGrid.Children.Clear();
        for (var dex = first; dex <= last; dex++)
        {
            if (shinyOnly)
            {
                if (ownedOnly && !_settings.IsOwned(dex, true)) continue;
                shinyIcons.TryGetValue(dex, out var shinyBitmap);
                IconGrid.Children.Add(MakeIconCell(dex, true, shinyBitmap));
                continue;
            }
            if (!ownedOnly || _settings.IsOwned(dex))
            {
                icons.TryGetValue(dex, out var normalBitmap);
                IconGrid.Children.Add(MakeIconCell(dex, false, normalBitmap));
            }
            // Mixed view adds actual shiny possessions immediately after their normal
            // slot, even when the normal form is unowned or ownership filtering is off.
            if (_settings.ShinyOwned.Contains(dex))
            {
                shinyIcons.TryGetValue(dex, out var shinyBitmap);
                IconGrid.Children.Add(MakeIconCell(dex, true, shinyBitmap));
            }
        }
        UpdateDexStatus();
    }

    private void UpdateDexStatus()
    {
        var empty = IconGrid.Children.Count == 0;
        var failed = _failedGenerations.Count > 0;
        DexStatusText.Text = _dexLoading ? "도감 그림을 불러오는 중…"
            : failed ? "일부 그림을 불러오지 못했습니다. 다시 시도할 수 있습니다."
            : empty ? (ViewingShiny ? "이 세대에 보유한 이로치 포켓몬이 없습니다." : "이 세대에 보유한 포켓몬이 없습니다.")
            : "";
        DexStatusText.IsVisible = _dexLoading || failed || empty;
        RetryDexButton.IsVisible = failed && !_dexLoading;
    }

    private int? CheckedGen()
    {
        foreach (RadioButton rb in GenTabs.Children)
            if (rb.IsChecked == true) return (int)rb.Tag!;
        return null;
    }

    private void OnOwnedOnlyChanged(object? sender, RoutedEventArgs e)
    {
        if (CheckedGen() is not { } gen) return;
        var icons = _visibleGeneration == gen && _visibleShiny == ViewingShiny
            ? _visibleIcons : CachedDexIcons(gen, ViewingShiny);
        RebuildIconGrid(gen, icons);
        IconScroll.Offset = default;
    }

    private void UpdateOwnedCount()
    {
        var total = ViewingShiny ? 1025 : 2050;
        var count = ViewingShiny ? _settings.ShinyOwned.Count : _settings.Owned.Count + _settings.ShinyOwned.Count;
        OwnedCount.Text = $"보유 {count}/{total}";
    }

    private RadioButton MakeIconCell(int dex, bool shiny, Bitmap? bmp)
    {
        var owned = _settings.IsOwned(dex, shiny);
        var records = shiny ? _settings.ShinyProgress : _settings.Progress;
        var level = records.TryGetValue(dex, out var progress) ? progress.Level : 1;
        var rb = new RadioButton
        {
            Content = bmp == null
                ? new TextBlock { Text = "?", FontSize = 18, Foreground = Brush.Parse("#80776D"),
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                    VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center }
                : new Image { Source = owned ? bmp : PokemonIcons.SilhouetteOf(dex, bmp, shiny),
                    Width = 40, Height = 40, Stretch = Stretch.Uniform },
            Tag = new PokemonChoice(dex, shiny), GroupName = "Icon",
            Theme = (ControlTheme)Resources["IconButton"]!, IsEnabled = owned,
            Cursor = owned ? new Cursor(StandardCursorType.Hand) : new Cursor(StandardCursorType.Arrow),
            IsChecked = dex == _settings.SelectedDex && shiny == _settings.SelectedShiny,
        };
        rb.Classes.Set("shiny", shiny);
        SetEvolutionCellState(rb, dex, shiny, owned);
        UiToolTips.Set(rb, owned ? $"#{dex} {PokemonNames.Of(dex)}{(shiny ? " ★ 이로치" : "")} · Lv.{level}{EvolutionCellTip(dex, shiny)}"
            : $"#{dex} ???{(shiny ? " ★ 이로치" : "")} (미보유)");
        rb.IsCheckedChanged += OnIconChecked;
        return rb;
    }

    private void RefreshIconCell(int dex, bool shiny)
    {
        if ((ViewingShiny && !shiny) || CheckedGen() is not { } gen) return;
        if (gen != 0 && PokemonIcons.GenOf(dex) != gen) return;
        RebuildIconGrid(gen, CachedDexIcons(gen, ViewingShiny));
        UpdateDexDetails();
    }

    private async void OnIconChecked(object? sender, RoutedEventArgs e)
    {
        var rb = (RadioButton)sender!;
        if (rb.IsChecked != true) return;
        var choice = (PokemonChoice)rb.Tag!;
        await SelectPokemonAsync(choice);
    }

    private async Task SelectPokemonAsync(PokemonChoice choice)
    {
        if (_evolving || !_settings.IsOwned(choice.Dex, choice.IsShiny))
        {
            SyncSelectedIcon();
            return;
        }
        if (choice == new PokemonChoice(_settings.SelectedDex, _settings.SelectedShiny))
        {
            ++_loadRequest;
            UpdateDexDetails();
            return;
        }
        var task = LoadPokemonAsync(choice.Dex, choice.IsShiny);
        var request = _loadRequest;
        if (await task)
        {
            _settings.SelectedDex = choice.Dex;
            _settings.SelectedShiny = choice.IsShiny;
            UpdateLevelUi();
            _dirty = true;
            TrySaveSettings();
            SyncSelectedIcon();
            UpdateDexDetails();
            await CheckEvolutionAsync();
        }
        else if (!_closed && request == _loadRequest) SyncSelectedIcon();
    }

    private void SyncSelectedIcon()
    {
        foreach (RadioButton cell in IconGrid.Children)
        {
            cell.IsCheckedChanged -= OnIconChecked;
            cell.IsChecked = (PokemonChoice)cell.Tag! == new PokemonChoice(_settings.SelectedDex, _settings.SelectedShiny);
            cell.IsCheckedChanged += OnIconChecked;
        }
    }

    private void UpdateDexDetails()
    {
        var dex = _settings.SelectedDex;
        var shiny = _settings.SelectedShiny;
        if (ViewingShiny && !shiny)
        {
            DexDetailName.Text = "이로치 포켓몬을 선택하세요";
            DexDetailNumber.Text = "";
            DexDetailTypes.Text = "";
            DexDetailDescription.Text = "보유한 포켓몬을 선택하면 설명과 성장 상태를 볼 수 있습니다.";
            DexDetailImage.Source = null;
            return;
        }
        var details = PokemonDetails.For(dex);
        DexDetailName.Text = PokemonNames.Of(dex) + (shiny ? " ★" : "");
        DexDetailNumber.Text = $"No.{dex:0000}";
        DexDetailTypes.Text = string.Join(" · ", details.Types);
        var progress = _settings.For(dex, shiny);
        DexDetailDescription.Text = $"Lv.{progress.Level} · 경험치 {progress.Exp}/{Settings.ExpToNext(progress.Level)}\n{details.Description}";
        DexDetailImage.Source = PokemonIcons.TryGetCached(PokemonIcons.GenOf(dex), dex, out var icon, shiny) ? icon : null;
    }
}
