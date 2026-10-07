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
    private bool _boxView;
    private bool _dexOwnedFilter;
    private readonly List<int> _failedGenerations = new();

    private void BuildGenTabs()
    {
        if (GenTabs.Children.Count != 0) return;
        var style = (ControlTheme)Resources["GenTab"]!;
        foreach (var gen in Enumerable.Range(0, 10))
        {
            var rb = new RadioButton
            {
                Content = gen == 0 ? "전체" : $"{gen}세대", Tag = gen,
                GroupName = "Gen", Theme = style,
            };
            Avalonia.Automation.AutomationProperties.SetName(rb, gen == 0 ? "전체 세대" : $"{gen}세대");
            rb.IsCheckedChanged += OnGenChecked;
            rb.Click += (_, _) => GenerationPickerButton.Flyout?.Hide();
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
        if (sender is not RadioButton { IsChecked: true } selected) return;
        // IsCheckedChanged can run before the radio group clears the previous tab.
        // Resolve the selection first so a higher generation never reloads the old one.
        foreach (var tab in GenTabs.Children.OfType<RadioButton>())
            if (!ReferenceEquals(tab, selected)) tab.IsChecked = false;
        var gen = (int)selected.Tag!;
        GenerationText.Text = gen == 0 ? "전체" : $"{gen}세대";
        PreviousGenerationButton.IsEnabled = gen > 0;
        NextGenerationButton.IsEnabled = gen < 9;
        GenerationPickerButton.Flyout?.Hide();
        await RefreshDexAsync();
    }

    private void OnPreviousGeneration(object? sender, RoutedEventArgs e) => SelectGenTab(Math.Max(0, (CheckedGen() ?? 1) - 1));
    private void OnNextGeneration(object? sender, RoutedEventArgs e) => SelectGenTab(Math.Min(9, (CheckedGen() ?? 1) + 1));

    private void SetCollectionView(bool box)
    {
        if (box && !_boxView) _dexOwnedFilter = OwnedOnly.IsChecked == true;
        var changed = box != _boxView;
        _boxView = box;
        OwnedOnly.IsEnabled = !box;
        if (box) OwnedOnly.IsChecked = true;
        else if (changed) OwnedOnly.IsChecked = _dexOwnedFilter;
        // A tab change also resets the scroll when the effective filter is unchanged.
        OnOwnedOnlyChanged(this, new RoutedEventArgs());
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
        var ownedOnly = _boxView || OwnedOnly.IsChecked == true;
        var shinyOnly = ViewingShiny;
        // Each generation sheet populates both color caches in one load. Keep the
        // two maps separate: a shiny icon can never overwrite its normal species key.
        var shinyIcons = shinyOnly ? icons : CachedDexIcons(gen, true);
        var entries = gen == 0
            ? Enumerable.Range(1, PokemonIcons.Generations.Length).SelectMany(PokemonIcons.Entries)
            : PokemonIcons.Entries(gen);
        IconGrid.Children.Clear();
        foreach (var dex in entries)
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
        RefreshEvolutionUi();
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
        var total = EvolutionData.Count * (ViewingShiny ? 1 : 2);
        var count = ViewingShiny ? _settings.ShinyOwned.Count : _settings.Owned.Count + _settings.ShinyOwned.Count;
        OwnedCount.Text = $"보유 {count}/{total}";
    }

    private RadioButton MakeIconCell(int dex, bool shiny, Bitmap? bmp)
    {
        var owned = _settings.IsOwned(dex, shiny);
        // Growth dictionaries are keyed by growth run, not species ID. Merely listing
        // unowned/cheat entries must not create additional growth runs.
        var level = _settings.HasOwned(dex, shiny) ? _settings.For(dex, shiny).Level : 1;
        var nationalDex = EvolutionData.NationalDex(dex);
        var rb = new RadioButton
        {
            Content = bmp == null
                ? new TextBlock { Text = "?", FontSize = 26, FontWeight = FontWeight.Bold, Foreground = Brush.Parse("#428BA3"),
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
        rb.Classes.Set("missing", bmp == null);
        SetEvolutionCellState(rb, dex, shiny, owned);
        UiToolTips.Set(rb, owned ? $"#{nationalDex} {PokemonNames.Of(dex)}{(shiny ? " ★ 이로치" : "")} · Lv.{level}{EvolutionCellTip(dex, shiny)}"
            : $"#{nationalDex} ???{(shiny ? " ★ 이로치" : "")} (미보유)");
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
            await CheckEvolutionAsync();
            return;
        }
        _pendingSelections++;
        try
        {
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
            }
            else if (!_closed && request == _loadRequest) SyncSelectedIcon();
        }
        finally { _pendingSelections--; }
        if (_pendingSelections == 0 && !_closed) await CheckEvolutionAsync();
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
            UiToolTips.Set(DexDetailDescription, DexDetailDescription.Text);
            UiToolTips.Set(DexDetailHeader, "이로치 포켓몬을 선택하세요");
            DexDetailImage.Source = null;
            return;
        }
        var details = PokemonDetails.For(dex);
        DexDetailName.Text = PokemonNames.Of(dex) + (shiny ? " ★" : "");
        DexDetailNumber.Text = $"No.{EvolutionData.NationalDex(dex):0000}";
        DexDetailTypes.Text = string.Join(" · ", details.Types);
        var progress = _settings.For(dex, shiny);
        DexDetailDescription.Text = details.Description;
        UiToolTips.Set(DexDetailDescription, details.Description);
        UiToolTips.Set(DexDetailHeader, $"Lv.{progress.Level} · 경험치 {progress.Exp}/{Settings.ExpToNext(progress.Level)}");
        DexDetailImage.Source = PokemonIcons.TryGetCached(PokemonIcons.GenOf(dex), dex, out var icon, shiny) ? icon : null;
    }
}
