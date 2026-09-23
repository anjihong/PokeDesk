using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace DeskPokemon;

public partial class MainWindow
{
    private void SetEggState(EggState state)
    {
        _eggState = state;
        if (state != EggState.Result)
        {
            ResultImage.Source = null;
            _resultAtlas?.Dispose();
            _resultAtlas = null;
        }
        if (state is EggState.Waiting or EggState.Ready)
        {
            ++_resultRequest;
            if (_startServices) _ = LoadEggAssetsAsync();
        }
        var idle = _animations["EggIdle"];
        var wait = _animations["EggWait"];
        switch (state)
        {
            case EggState.Waiting:
                idle.Stop();
                ShowEgg(true);
                UpdateBubbleCountdown();
                wait.Play();
                break;
            case EggState.Ready:
                wait.Stop();
                ShowEgg(true);
                BubbleText.Text = $"{EggName(_settings.PendingEgg!.Kind)}\n클릭하여 부화";
                idle.Play();
                break;
            case EggState.Hatching:
                idle.Stop();
                wait.Stop();
                BubbleText.Text = "...";
                break;
            case EggState.Result:
                EggStage.IsVisible = false;
                ResultStage.IsVisible = true;
                NewText.IsVisible = true;
                break;
        }
    }

    private void ShowEgg(bool show)
    {
        EggStage.IsVisible = show;
        CrackImage.IsVisible = false;
        ResultStage.IsVisible = false;
        NewText.IsVisible = false;
    }

    private static string EggName(EggKind kind) => kind switch
    {
        EggKind.Common => "커먼 알", EggKind.Rare => "레어 알", EggKind.Epic => "에픽 알",
        EggKind.Legendary => "레전더리 알", EggKind.Shiny => "이로치알", _ => "알"
    };

    private void UpdateBubbleCountdown() =>
        BubbleText.Text = $"{EggName(_settings.PendingEgg!.Kind)}\n{TimeSpan.FromSeconds(_settings.RemainingEggSeconds):mm\\:ss}";

    // 외형은 알 등급만으로 로딩한다. 저장된 부화 결과의 이미지는 미리 노출하지 않는다.
    private async Task LoadEggAssetsAsync()
    {
        var request = ++_eggArtRequest;
        var kind = _settings.PendingEgg!.Kind;
        EggImage.Source = null;
        EggFallback.IsVisible = true;
        UiToolTips.Set(EggStage, EggName(kind));
        try
        {
            var frame = await EggArtwork.LoadAsync(kind);
            if (_closed || request != _eggArtRequest) return;
            EggImage.Source = frame.Bitmap; // 앱 수명 동안 EggArtwork 캐시가 소유한다.
            EggFallback.IsVisible = false;
        }
        catch { /* 실패 시에도 등급 이름과 대체 알을 표시한다. */ }
    }

    private async Task LoadCrackAssetsAsync()
    {
        try
        {
            var cracks = await SpriteAtlas.LoadFramesAsync("egg/egg_crack");
            if (_closed) { DisposeFrames(cracks); return; }
            CrackImage.Source = null;
            DisposeFrames(_crackFrames);
            _crackFrames = cracks;
        }
        catch { /* 네트워크 실패 시 균열 없이 진행한다. */ }
    }

    private void ShowCrack(int stage)
    {
        if (_crackFrames == null || !_crackFrames.TryGetValue(stage.ToString(), out var frame)) return;
        CrackImage.Source = frame.Bitmap;
        CrackImage.Width = frame.Width;
        CrackImage.Height = frame.Height;
        Canvas.SetLeft(CrackImage, frame.OffsetX - 26);
        Canvas.SetTop(CrackImage, frame.OffsetY - 25);
        CrackImage.IsVisible = true;
    }

    private async void OnEggClick(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        e.Handled = true;
        if (_eggState != EggState.Ready) return;
        try { await HatchAsync(); }
        catch (OperationCanceledException) when (_closed) { }
    }

    private async Task HatchAsync()
    {
        SetEggState(EggState.Hatching);
        for (var stage = 1; stage <= 3; stage++)
        {
            ShowCrack(stage);
            _animations["EggShake"].Play();
            await Task.Delay(550, _lifetime.Token);
        }
        ShowCrack(4);
        await Task.Delay(250, _lifetime.Token);
        HatchResult? result;
        try { result = _settings.Hatch(); }
        catch (Exception ex)
        {
            SetEggState(EggState.Ready);
            await AppDialog.ShowAsync(this, $"부화 결과를 저장하지 못했습니다. 알은 유지됩니다.\n{ex.Message}", "부화 실패");
            return;
        }
        if (result is not { } hatched)
        {
            SetEggState(EggState.Waiting);
            return;
        }
        _dirty = false;
        _lastEggTick = DateTime.UtcNow;
        UpdateOwnedCount();
        if (hatched.Dex == _settings.SelectedDex && hatched.IsShiny == _settings.SelectedShiny) UpdateLevelUi();
        RefreshIconCell(hatched.Dex, hatched.IsShiny);
        _animations["FlashOut"].Play();
        await Task.Delay(150, _lifetime.Token);
        ResultImage.Source = null;
        ResultStage.Width = 40;
        ResultStage.Height = 30;
        ResultZoomScale.ScaleX = ResultZoomScale.ScaleY = 2;
        NewText.Text = hatched.IsNew ? "NEW!" : $"Lv.{hatched.Level} ↑";
        BubbleText.Text = $"{(hatched.IsShiny ? "★ 이로치\n" : "")}{PokemonNames.Of(hatched.Dex)}";
        SetEggState(EggState.Result);
        _animations["ResultPop"].Play();
        _resultTimer.Stop();
        _ = LoadHatchResultAsync(hatched, ++_resultRequest);
    }

    private async Task LoadHatchResultAsync(HatchResult result, int request)
    {
        bool IsCurrent() => !_closed && request == _resultRequest && _eggState == EggState.Result;
        try
        {
            var icons = await PokemonIcons.LoadGenAsync(PokemonIcons.GenOf(result.Dex), result.IsShiny);
            if (!IsCurrent()) return;
            if (icons.TryGetValue(result.Dex, out var icon))
            {
                ResultImage.Source = icon;
                ResultImage.Width = icon.PixelSize.Width;
                ResultImage.Height = icon.PixelSize.Height;
                Canvas.SetLeft(ResultImage, (40 - icon.PixelSize.Width) / 2.0);
                Canvas.SetTop(ResultImage, 30 - icon.PixelSize.Height);
            }
        }
        catch { /* 이름은 계속 표시한다. 색상이 다른 아이콘으로 대체하지 않는다. */ }
        if (!IsCurrent()) return;
        try
        {
            var atlas = await SpriteAtlas.LoadAsync(result.Dex, result.IsShiny);
            if (!IsCurrent()) { atlas.Dispose(); return; }
            var previous = _resultAtlas;
            _resultAtlas = atlas;
            ResultStage.Width = atlas.Body.Width;
            ResultStage.Height = atlas.Body.Height;
            ResultZoomScale.ScaleX = ResultZoomScale.ScaleY = Math.Min(80.0 / atlas.Body.Width, 60.0 / atlas.Body.Height);
            ShowResultFrame(0);
            previous?.Dispose();
        }
        catch { /* 애니메이션 실패 시 아이콘·이름을 유지한다. */ }
        finally
        {
            if (IsCurrent()) _resultTimer.Start(); // 로딩 후 5초 동안 결과를 보여 준다.
        }
    }

    private void ShowResultFrame(int index)
    {
        _resultFrame = index;
        var frame = _resultAtlas!.Frames[index];
        ResultImage.Source = frame.Bitmap;
        ResultImage.Width = frame.Width;
        ResultImage.Height = frame.Height;
        Canvas.SetLeft(ResultImage, frame.OffsetX - _resultAtlas.Body.X);
        Canvas.SetTop(ResultImage, frame.OffsetY - _resultAtlas.Body.Y);
    }

#if DEBUG
    private void SetupTestEggs()
    {
        var menu = new MenuItem { Header = "테스트 알 즉시 지급 (기존 알 교체)" };
        var forceShiny = new MenuItem { Header = "이번 테스트 알 확정 이로치", ToggleType = MenuItemToggleType.CheckBox, StaysOpenOnClick = true };
        menu.Items.Add(forceShiny);
        menu.Items.Add(new Separator());
        foreach (var kind in Enum.GetValues<EggKind>())
        {
            var item = new MenuItem { Header = $"{EggName(kind)} 즉시 지급" };
            item.Click += (_, _) =>
            {
                if (_eggState is EggState.Hatching or EggState.Result) return;
                var previous = (_settings.PendingEgg, _settings.Eggs, _settings.EggSeconds);
                _settings.GrantTestEgg(kind, forceShiny.IsChecked);
                if (!TrySaveSettings())
                {
                    (_settings.PendingEgg, _settings.Eggs, _settings.EggSeconds) = previous;
                    return;
                }
                forceShiny.IsChecked = false;
                _lastEggTick = DateTime.UtcNow;
                SetEggState(EggState.Ready);
            };
            menu.Items.Add(item);
        }
        ContextMenu!.Opened += (_, _) => menu.IsEnabled = _eggState is not (EggState.Hatching or EggState.Result);
        ContextMenu.Items.Insert(0, menu);
    }
#endif
}
