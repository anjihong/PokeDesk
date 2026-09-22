#if DEBUG
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
#endif
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace DeskPokemon;

public partial class MainWindow : Window
{
    private readonly InputHook _hook = new();
    private readonly Settings _settings;
    private SpriteAtlas? _atlas;
    private int _frame;
    private int _loadRequest; // 최신 스프라이트 로드 요청 번호. 빠른 연속 선택 시 옛 결과 무시.
    private int _iconRequest;
    private int _eggArtRequest;
    private int _resultRequest;
    private bool _closed;
    private bool _saveErrorReported;
    private readonly List<DispatcherTimer> _timers = new();
    private SpriteAtlas? _resultAtlas;
    private int _resultFrame;
    private readonly record struct PokemonChoice(int Dex, bool IsShiny);
    private bool ViewingShiny => ShinyDex.IsChecked == true;
    private bool _dirty;      // 설정 변경됨, 주기 저장 대기
    private bool _placed;     // 초기 위치 잡은 뒤부터 크기 변화에 맞춰 하단 고정
    private DateTime _lastEggTick; // 알 타이머 직전 틱 시각(UTC)
    private Point? _positionBeforeDrawer; // 서랍을 펼치기 전 위치. 빠르게 접었다 펼쳐도 유지.
    private double _heightBeforeDrawer;
    private int _drawerAnimationVersion;

    private enum EggState { Waiting, Ready, Hatching, Result }
    private EggState _eggState;
    private Dictionary<string, SpriteFrame>? _crackFrames;
    private readonly DispatcherTimer _resultTimer = new() { Interval = TimeSpan.FromSeconds(5) };

    public MainWindow(Settings settings)
    {
        _settings = settings;
        InitializeComponent();
        UpdateLevelUi();
        ApplyLayout(LayoutDefaults.BubbleX, LayoutDefaults.BubbleY, LayoutDefaults.EggX, LayoutDefaults.EggY);
#if DEBUG
        SetupLayoutEditor();
        SetupUnlockAll();
        SetupReset();
        SetupTestEggs();
#endif
        Loaded += OnLoaded;
        SizeChanged += OnSizeChanged;
        Closing += (_, e) => e.Cancel = !TrySaveSettings();
        Closed += (_, _) =>
        {
            _closed = true;
            foreach (var timer in _timers) timer.Stop();
            _resultTimer.Stop();
            _hook.Dispose();
        };
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        BuildGenTabs();

        if (!await LoadPokemonAsync(_settings.SelectedDex, _settings.SelectedShiny))
            ShowSpritePlaceholder();
        if (_closed) return;

        // PokeRogue와 동일: frameRate 10, 무한 반복
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        timer.Tick += (_, _) =>
        {
            if (_atlas != null) ShowFrame((_frame + 1) % _atlas.Frames.Length);
            if (_eggState == EggState.Result && _resultAtlas != null)
                ShowResultFrame((_resultFrame + 1) % _resultAtlas.Frames.Length);
        };
        _timers.Add(timer);
        timer.Start();

        // 입력마다 디스크 쓰지 않고 5초 주기로 저장
        var save = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        save.Tick += (_, _) =>
        {
            if (!_dirty) return;
            TrySaveSettings();
        };
        _timers.Add(save);
        save.Start();

        // 실행 시간 누적 → 30분마다 알 1개(알이 있으면 일시정지). 절전 등으로 틱이 밀려도 한 번에 최대 5초만 인정.
        _lastEggTick = DateTime.UtcNow;
        var egg = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        egg.Tick += (_, _) =>
        {
            var now = DateTime.UtcNow;
            var dt = Math.Clamp((now - _lastEggTick).TotalSeconds, 0, 5);
            _lastEggTick = now;
            if (_settings.TickEgg(dt))
            {
                _dirty = true;
                TrySaveSettings();
                if (_eggState == EggState.Waiting) SetEggState(EggState.Ready);
            }
            else if (_settings.Eggs == 0)
            {
                _dirty = true;
                if (_eggState == EggState.Waiting) UpdateBubbleCountdown();
            }
        };
        _timers.Add(egg);
        egg.Start();
        _resultTimer.Tick += (_, _) =>
        {
            _resultTimer.Stop();
            SetEggState(_settings.Eggs > 0 ? EggState.Ready : EggState.Waiting);
        };
        SetEggState(_settings.Eggs > 0 ? EggState.Ready : EggState.Waiting);
        UpdateOwnedCount();
        _ = LoadCrackAssetsAsync();

        var bounce = (Storyboard)Resources["Bounce"];
        // 훅 콜백은 빨리 반환해야 하므로 애니메이션 시작은 큐에 넘김
        _hook.Triggered += () => Dispatcher.BeginInvoke(() =>
        {
            if (_closed) return;
            bounce.Begin(this, true);
            AddExp();
        });

        UpdateLayout();
        var wa = SystemParameters.WorkArea;
        Left = wa.Right - ActualWidth - 20;
        Top = wa.Bottom - ActualHeight - 20;
        _placed = true;

        SelectGenTab(PokemonIcons.GenOf(_settings.SelectedDex));
    }

    /// <summary>스프라이트 교체. 실패 시 메시지 띄우고 false(이전 포켓몬 유지). 더 최신 요청이 있으면 조용히 false.</summary>
    private async Task<bool> LoadPokemonAsync(int dex, bool shiny = false)
    {
        var req = ++_loadRequest;
        SpriteAtlas atlas;
        try
        {
            atlas = await SpriteAtlas.LoadAsync(dex, shiny);
        }
        catch (Exception ex)
        {
            if (!_closed && req == _loadRequest)
                MessageBox.Show($"스프라이트 로드 실패 (#{dex}{(shiny ? " 이로치" : "")}): {ex.Message}", "DeskPokemon");
            return false;
        }
        if (_closed || req != _loadRequest) return false;

        _atlas = atlas;
        SpriteMissing.Visibility = Visibility.Collapsed;
        // 캔버스(37~98px, 9세대는 96 고정+여백)가 아니라 실제 몸체 영역을 스테이지로 삼고,
        // 몸체 높이가 항상 BodyTargetHeight가 되도록 소수 배율. 넓은 포켓몬은 폭 상한으로 제한.
        var body = atlas.Body;
        Stage.Width = body.Width;
        Stage.Height = body.Height;
        var zoom = Math.Min(BodyTargetHeight / body.Height, BodyMaxWidth / body.Width);
        zoom = Math.Max(1, Math.Round(zoom * 4) / 4); // 0.25 단위: 픽셀 굵기 불균일 완화
        Zoom.ScaleX = Zoom.ScaleY = zoom;
        // 바운스 스트레치(ScaleY 1.12)가 창 위로 잘리지 않게 여백을 표시 높이에 비례
        TopArea.Margin = new Thickness(0, Math.Ceiling(body.Height * zoom * 0.14) + 4, 0, 0);
        ShowFrame(0);
        UpdateLevelUi();
        return true;
    }

    private void ShowSpritePlaceholder()
    {
        _atlas = null;
        Sprite.Source = null;
        Stage.Width = 60;
        Stage.Height = 42;
        Zoom.ScaleX = Zoom.ScaleY = 2;
        SpriteMissing.Text = $"{PokemonNames.Of(_settings.SelectedDex)}\n{(_settings.SelectedShiny ? "이로치\n" : "")}이미지 없음";
        SpriteMissing.Visibility = Visibility.Visible;
        UpdateLevelUi();
    }

    private bool TrySaveSettings()
    {
        try
        {
            _settings.Save();
            _dirty = false;
            _saveErrorReported = false;
            return true;
        }
        catch (Exception ex)
        {
            _dirty = true;
            if (!_saveErrorReported)
                MessageBox.Show($"저장하지 못했습니다. 저장 위치를 확인해 주세요.\n{ex.Message}", "저장 실패");
            _saveErrorReported = true;
            return false;
        }
    }

    private const double BodyTargetHeight = 110;
    private const double BodyMaxWidth = 170;

    private void ShowFrame(int i)
    {
        _frame = i;
        var f = _atlas!.Frames[i];
        Sprite.Source = f.Bitmap;
        Sprite.Width = f.Width;
        Sprite.Height = f.Height;
        // 스테이지 원점 = 몸체 영역 좌상단
        Canvas.SetLeft(Sprite, f.OffsetX - _atlas.Body.X);
        Canvas.SetTop(Sprite, f.OffsetY - _atlas.Body.Y);
    }

    // ---- 레벨 ----

    private void AddExp()
    {
        var leveled = _settings.AddExp(_settings.SelectedDex, _settings.SelectedShiny);
        _dirty = true;
        UpdateLevelUi();
        if (leveled) ((Storyboard)Resources["LevelUp"]).Begin(this, true);
    }

    private void UpdateLevelUi()
    {
        var p = _settings.For(_settings.SelectedDex, _settings.SelectedShiny);
        var required = Settings.ExpToNext(p.Level);
        LevelText.Text = $"{(_settings.SelectedShiny ? "★ " : "")}Lv. {p.Level}";
        ExpBar.Width = ExpTrack.Width * p.Exp / required;
        // ToolTip 인스턴스는 유지하고 내용만 바꿔 이미 열린 툴팁에도 즉시 반영한다.
        ExpToolTip.Content = $"현재 경험치: {p.Exp:N0}\n필요 경험치: {required:N0}";
    }

    // ---- 알 ----

    /// <summary>
    /// 알 상태 머신. Waiting(카운트다운) → Ready(클릭하여 부화, 통통) → Hatching(흔들림+균열+플래시) → Result(아이콘+이름+NEW!, 5초) → Waiting.
    /// </summary>
    private void SetEggState(EggState state)
    {
        _eggState = state;
        if (state != EggState.Result)
        {
            _resultAtlas = null;
            ResultImage.Source = null;
        }
        if (state is EggState.Waiting or EggState.Ready)
        {
            ++_resultRequest;
            _ = LoadEggAssetsAsync();
        }
        var idle = (Storyboard)Resources["EggIdle"];
        var wait = (Storyboard)Resources["EggWait"];
        switch (state)
        {
            case EggState.Waiting:
                idle.Stop(this);
                ShowEgg(true);
                UpdateBubbleCountdown();
                wait.Begin(this, true);
                break;
            case EggState.Ready:
                wait.Stop(this);
                ShowEgg(true);
                BubbleText.Text = $"{EggName(_settings.PendingEgg!.Kind)}\n클릭하여 부화";
                idle.Begin(this, true);
                break;
            case EggState.Hatching:
                idle.Stop(this);
                wait.Stop(this); // EggShake가 EggRotate를 쓰므로 루프 정지
                BubbleText.Text = "...";
                break;
            case EggState.Result:
                // BubbleText/NewText는 HatchAsync가 채움
                EggStage.Visibility = Visibility.Collapsed;
                ResultStage.Visibility = Visibility.Visible;
                NewText.Visibility = Visibility.Visible;
                break;
        }
    }

    private void ShowEgg(bool show)
    {
        EggStage.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        CrackImage.Visibility = Visibility.Collapsed;
        ResultStage.Visibility = Visibility.Collapsed;
        NewText.Visibility = Visibility.Collapsed;
    }

    private static string EggName(EggKind kind) => kind switch
    {
        EggKind.Common => "커먼 알", EggKind.Rare => "레어 알", EggKind.Epic => "에픽 알",
        EggKind.Legendary => "레전더리 알", EggKind.Shiny => "이로치알", _ => "알"
    };

    private void UpdateBubbleCountdown() =>
        BubbleText.Text = $"{EggName(_settings.PendingEgg!.Kind)}\n{TimeSpan.FromSeconds(_settings.RemainingEggSeconds):mm\\:ss}";

    /// <summary>표시 정의만 사용해 알 외형을 로딩한다. 부화 결과는 로딩하지 않는다.</summary>
    private async Task LoadEggAssetsAsync()
    {
        var request = ++_eggArtRequest;
        var kind = _settings.PendingEgg!.Kind;
        EggImage.Source = null;
        EggFallback.Visibility = Visibility.Visible;
        EggStage.ToolTip = EggName(kind);
        try
        {
            var f = await EggArtwork.LoadAsync(kind);
            if (_closed || request != _eggArtRequest) return;
            EggImage.Source = f.Bitmap;
            EggFallback.Visibility = Visibility.Collapsed;
        }
        catch { /* 등급 이름과 대체 알을 유지한다. */ }
    }

    private async Task LoadCrackAssetsAsync()
    {
        try
        {
            _crackFrames = await SpriteAtlas.LoadFramesAsync("egg/egg_crack");
        }
        catch
        {
            // 네트워크 실패 등: 대체 타원으로 진행
        }
    }

    /// <summary>균열 단계(1~4) 오버레이. 균열 캔버스는 80x80이고 알(28x30)은 그 중앙 → (26,25) 기준으로 배치.</summary>
    private void ShowCrack(int stage)
    {
        if (_crackFrames == null || !_crackFrames.TryGetValue(stage.ToString(), out var f)) return;
        CrackImage.Source = f.Bitmap;
        CrackImage.Width = f.Width;
        CrackImage.Height = f.Height;
        Canvas.SetLeft(CrackImage, f.OffsetX - 26);
        Canvas.SetTop(CrackImage, f.OffsetY - 25);
        CrackImage.Visibility = Visibility.Visible;
    }

    private async void OnEggClick(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true; // 드래그로 넘어가지 않게
        if (_eggState != EggState.Ready) return;
        await HatchAsync();
    }

    private async Task HatchAsync()
    {
        SetEggState(EggState.Hatching);
        var shake = (Storyboard)Resources["EggShake"];
        for (var stage = 1; stage <= 3; stage++)
        {
            ShowCrack(stage);
            shake.Begin(this, true);
            await Task.Delay(550);
        }
        ShowCrack(4);
        await Task.Delay(250);

        if (_closed) return;
        HatchResult? result;
        try { result = _settings.Hatch(); }
        catch (Exception ex)
        {
            MessageBox.Show($"부화 결과를 저장하지 못했습니다. 알은 유지됩니다.\n{ex.Message}", "부화 실패");
            SetEggState(EggState.Ready);
            return;
        }
        if (result is not { } res)
        {
            SetEggState(EggState.Waiting);
            return;
        }
        _dirty = false;
        _lastEggTick = DateTime.UtcNow;
        UpdateOwnedCount();
        if (res.Dex == _settings.SelectedDex && res.IsShiny == _settings.SelectedShiny) UpdateLevelUi();
        RefreshIconCell(res.Dex, res.IsShiny);

        ((Storyboard)Resources["FlashOut"]).Begin(this, true); // From=1이라 Opacity 직접 설정 불필요(이전 애니메이션이 값을 잡고 있어 무시됨)
        await Task.Delay(150);

        if (_closed) return;
        ResultImage.Source = null;
        ResultStage.Width = 40;
        ResultStage.Height = 30;
        ResultZoom.ScaleX = ResultZoom.ScaleY = 2;
        NewText.Text = res.IsNew ? "NEW!" : $"Lv.{res.Level} ↑";
        BubbleText.Text = $"{(res.IsShiny ? "★ 이로치\n" : "")}{PokemonNames.Of(res.Dex)}";
        SetEggState(EggState.Result);
        ((Storyboard)Resources["ResultPop"]).Begin(this, true);
        _resultTimer.Stop();
        _ = LoadHatchResultAsync(res, ++_resultRequest);
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
                ResultImage.Width = icon.PixelWidth;
                ResultImage.Height = icon.PixelHeight;
                Canvas.SetLeft(ResultImage, (40 - icon.PixelWidth) / 2.0);
                Canvas.SetTop(ResultImage, 30 - icon.PixelHeight);
            }
        }
        catch { /* 이름은 계속 표시한다. */ }
        if (!IsCurrent()) return;
        try
        {
            var atlas = await SpriteAtlas.LoadAsync(result.Dex, result.IsShiny);
            if (!IsCurrent()) return;
            _resultAtlas = atlas;
            ResultStage.Width = atlas.Body.Width;
            ResultStage.Height = atlas.Body.Height;
            ResultZoom.ScaleX = ResultZoom.ScaleY = Math.Min(80.0 / atlas.Body.Width, 60.0 / atlas.Body.Height);
            ShowResultFrame(0);
        }
        catch { /* 애니메이션 실패 시 이미 표시한 아이콘·이름을 유지한다. */ }
        finally
        {
            // 첫 다운로드가 느려도 이미지 로딩 완료 후 5초 동안 결과를 보여 준다.
            if (IsCurrent()) _resultTimer.Start();
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

    // ---- 메뉴 탭 / 서랍 ----

    // ToggleButton이라 같은 탭을 다시 누르면 Unchecked → 접힘. 다른 탭을 누르면 나머지를 코드에서 해제.
    private void OnMenuChecked(object sender, RoutedEventArgs e)
    {
        var btn = (ToggleButton)sender;
        foreach (ToggleButton other in MenuTabs.Children)
            if (other != btn) other.IsChecked = false;
        var isDex = (string)btn.Tag == "dex";
        DexPanel.Visibility = isDex ? Visibility.Visible : Visibility.Collapsed;
        PlaceholderPanel.Visibility = isDex ? Visibility.Collapsed : Visibility.Visible;
        AnimateDrawer(open: true);
    }

    private void OnMenuUnchecked(object sender, RoutedEventArgs e)
    {
        foreach (ToggleButton other in MenuTabs.Children)
            if (other.IsChecked == true) return; // 다른 탭으로 전환 중이면 그 탭이 열어 줌
        AnimateDrawer(open: false);
    }

    /// <summary>서랍 높이만큼 위로 이동해 하단을 유지하고, 접으면 펼치기 전 위치로 돌아온다.</summary>
    private void AnimateDrawer(bool open)
    {
        var version = ++_drawerAnimationVersion;
        UpdateLayout();
        DrawerContent.Measure(new Size(300, double.PositiveInfinity));
        var target = open ? DrawerContent.DesiredSize.Height : 0;
        if (_positionBeforeDrawer == null)
        {
            // 펼친 채 드래그했다면 현재 위치에서 서랍이 접힌 위치를 새 원점으로 삼는다.
            _positionBeforeDrawer = new Point(Left, Top + Drawer.ActualHeight);
            _heightBeforeDrawer = ActualHeight - Drawer.ActualHeight;
        }
        var anim = new DoubleAnimation(Drawer.ActualHeight, target, TimeSpan.FromMilliseconds(250))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        };
        anim.Completed += (_, _) =>
        {
            if (version != _drawerAnimationVersion) return;
            // 완료된 애니메이션의 HoldEnd 값을 제거해 실제 Height를 최종값으로 확정한다.
            Drawer.Height = target;
            Drawer.BeginAnimation(HeightProperty, null);
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
            {
                if (version != _drawerAnimationVersion) return;
                UpdateLayout();
                PositionForDrawer(ActualHeight);
                // 최종 SizeChanged까지 원점을 유지해야 접는 마지막 프레임에서 위치가 밀리지 않는다.
                if (!open) _positionBeforeDrawer = null;
            }));
        };
        Drawer.BeginAnimation(HeightProperty, anim);
    }

    // ---- 도감/선택 패널 ----

    private void BuildGenTabs()
    {
        var style = (Style)Resources["GenTab"];
        foreach (var g in PokemonIcons.Generations)
        {
            var rb = new RadioButton { Content = g.Gen, Tag = g.Gen, GroupName = "Gen", Style = style };
            rb.Checked += OnGenChecked;
            GenTabs.Children.Add(rb);
        }
    }

    private void SelectGenTab(int gen)
    {
        foreach (RadioButton rb in GenTabs.Children)
            if ((int)rb.Tag == gen) rb.IsChecked = true;
    }

    private async void OnGenChecked(object sender, RoutedEventArgs e) => await RefreshDexAsync();

    private async void OnShinyDexChanged(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        UpdateOwnedCount();
        await RefreshDexAsync();
    }

    private async Task RefreshDexAsync()
    {
        if (CheckedGen() is not { } gen) return;
        var shiny = ViewingShiny;
        var request = ++_iconRequest;
        IconGrid.Children.Clear();
        try
        {
            var icons = await PokemonIcons.LoadGenAsync(gen, shiny);
            if (_closed || request != _iconRequest || CheckedGen() != gen || ViewingShiny != shiny) return;
            RebuildIconGrid(gen, icons);
            IconScroll.ScrollToTop();
        }
        catch (Exception ex)
        {
            if (!_closed && request == _iconRequest)
                MessageBox.Show($"아이콘 로드 실패 ({gen}세대): {ex.Message}", "DeskPokemon");
        }
    }

    private void RebuildIconGrid(int gen, Dictionary<int, BitmapSource> icons)
    {
        var ownedOnly = OwnedOnly.IsChecked == true;
        var shiny = ViewingShiny;
        var (_, first, last) = PokemonIcons.Generations[gen - 1];
        IconGrid.Children.Clear();
        for (var dex = first; dex <= last; dex++)
        {
            if (!icons.TryGetValue(dex, out var bmp)) continue;
            if (ownedOnly && !_settings.IsOwned(dex, shiny)) continue;
            IconGrid.Children.Add(MakeIconCell(dex, shiny, bmp));
        }
    }

    private int? CheckedGen()
    {
        foreach (RadioButton rb in GenTabs.Children)
            if (rb.IsChecked == true) return (int)rb.Tag;
        return null;
    }

    private void OnOwnedOnlyChanged(object sender, RoutedEventArgs e)
    {
        if (CheckedGen() is not { } gen) return;
        if (!PokemonIcons.TryGetCachedGen(gen, out var icons, ViewingShiny)) return;
        RebuildIconGrid(gen, icons);
        IconScroll.ScrollToTop();
    }

    private void UpdateOwnedCount()
    {
        var total = PokemonIcons.Generations[^1].Last;
        var owned = ViewingShiny ? _settings.ShinyOwned : _settings.Owned;
        OwnedCount.Text = $"보유 {(_settings.UnlockAll ? total : owned.Count)}/{total}";
    }

    private RadioButton MakeIconCell(int dex, bool shiny, BitmapSource bmp)
    {
        var owned = _settings.IsOwned(dex, shiny);
        var records = shiny ? _settings.ShinyProgress : _settings.Progress;
        var level = records.TryGetValue(dex, out var progress) ? progress.Level : 1;
        var rb = new RadioButton
        {
            Content = new Image
            {
                Source = owned ? bmp : PokemonIcons.SilhouetteOf(dex, bmp, shiny),
                Width = 40, Height = 30, Stretch = Stretch.None,
            },
            Tag = new PokemonChoice(dex, shiny),
            GroupName = "Icon",
            Style = (Style)Resources["IconButton"],
            ToolTip = owned ? $"#{dex} {PokemonNames.Of(dex)}{(shiny ? " ★ 이로치" : "")} · Lv.{level}"
                            : $"#{dex} ???{(shiny ? " ★ 이로치" : "")} (미보유)",
            IsEnabled = owned,
            Cursor = owned ? Cursors.Hand : Cursors.Arrow,
            IsChecked = dex == _settings.SelectedDex && shiny == _settings.SelectedShiny,
        };
        rb.Checked += OnIconChecked;
        return rb;
    }

    private void RefreshIconCell(int dex, bool shiny)
    {
        if (shiny != ViewingShiny) return;
        var gen = PokemonIcons.GenOf(dex);
        if (CheckedGen() != gen) return;
        if (PokemonIcons.TryGetCachedGen(gen, out var icons, shiny)) RebuildIconGrid(gen, icons);
    }

    private async void OnIconChecked(object sender, RoutedEventArgs e)
    {
        var rb = (RadioButton)sender;
        var choice = (PokemonChoice)rb.Tag;
        if (!_settings.IsOwned(choice.Dex, choice.IsShiny))
        {
            rb.IsChecked = false;
            return;
        }
        if (choice == new PokemonChoice(_settings.SelectedDex, _settings.SelectedShiny))
        {
            ++_loadRequest; // 이전 비동기 선택을 취소하고 현재 표시를 유지한다.
            return;
        }
        var task = LoadPokemonAsync(choice.Dex, choice.IsShiny);
        var request = _loadRequest;
        if (await task)
        {
            // 이미지가 준비되기 전에는 선택/경험치/세이브를 바꾸지 않는다.
            _settings.SelectedDex = choice.Dex;
            _settings.SelectedShiny = choice.IsShiny;
            UpdateLevelUi();
            _dirty = true;
            TrySaveSettings();
            SyncSelectedIcon();
        }
        else if (!_closed && request == _loadRequest) SyncSelectedIcon();
    }

    private void SyncSelectedIcon()
    {
        foreach (RadioButton cell in IconGrid.Children)
        {
            cell.Checked -= OnIconChecked;
            cell.IsChecked = (PokemonChoice)cell.Tag == new PokemonChoice(_settings.SelectedDex, _settings.SelectedShiny);
            cell.Checked += OnIconChecked;
        }
    }

    // ---- 배치 ----

    private void ApplyLayout(double bubbleX, double bubbleY, double eggX, double eggY)
    {
        BubbleOffset.X = bubbleX;
        BubbleOffset.Y = bubbleY;
        EggOffset.X = eggX;
        EggOffset.Y = eggY;
    }

#if DEBUG
    private void SetupTestEggs()
    {
        var menu = new MenuItem { Header = "테스트 알 즉시 지급 (기존 알 교체)" };
        var forceShiny = new MenuItem { Header = "이번 테스트 알 확정 이로치", IsCheckable = true, StaysOpenOnClick = true };
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

    // ---- 초기화(개발자, Debug 빌드 전용) ----

    /// <summary>우클릭 메뉴에 초기화 항목 추가. 세이브 삭제 후 앱을 다시 띄워 스타팅 선택부터.</summary>
    private void SetupReset()
    {
        var item = new MenuItem { Header = "초기화(테스트)" };
        item.Click += (_, _) =>
        {
            if (MessageBox.Show("세이브를 삭제하고 스타팅 선택부터 다시 시작할까요?", "초기화",
                    MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            Settings.Delete();
            Process.Start(Environment.ProcessPath!);
            Environment.Exit(0); // Shutdown()은 Closed에서 세이브를 다시 쓰므로 바로 종료
        };
        ContextMenu!.Items.Insert(ContextMenu.Items.Count - 1, item); // "종료" 앞
    }

    // ---- 전체 해금 치트(개발자, Debug 빌드 전용) ----

    /// <summary>우클릭 메뉴에 전체 해금 토글 추가. Owned를 건드리지 않아 끄면 원래대로 돌아감.</summary>
    private void SetupUnlockAll()
    {
        var item = new MenuItem { Header = "전체 해금(치트)", IsCheckable = true };
        item.Click += async (_, _) => await SetUnlockAll(item.IsChecked);
        ContextMenu!.Items.Insert(ContextMenu.Items.Count - 1, item); // "종료" 앞
    }

    private async Task SetUnlockAll(bool on)
    {
        _settings.UnlockAll = on;

        // 끌 때 미보유 종을 보고 있었으면 기본 포켓몬으로 복귀
        if (!on)
        {
            ++_loadRequest; // 해금 상태로 시작한 요청이 뒤늦게 적용되는 것을 막는다.
            if (!_settings.IsOwned(_settings.SelectedDex, _settings.SelectedShiny))
            {
                _settings.SelectedDex = _settings.StarterDex;
                _settings.SelectedShiny = false;
                if (!await LoadPokemonAsync(_settings.StarterDex)) ShowSpritePlaceholder();
                UpdateLevelUi();
                TrySaveSettings();
            }
        }

        UpdateOwnedCount();
        if (CheckedGen() is { } gen && PokemonIcons.TryGetCachedGen(gen, out var icons, ViewingShiny))
        {
            RebuildIconGrid(gen, icons);
            IconScroll.ScrollToTop();
        }
    }

    // ---- 배치 편집(개발자, Debug 빌드 전용) ----

    private bool _layoutEdit;
    private Point? _dragStart;   // 드래그 시작 시 루트 기준 마우스 위치
    private Point _dragOrigin;   // 드래그 시작 시 오프셋

    /// <summary>우클릭 메뉴에 배치 편집 항목 추가 + 말풍선/알 드래그 핸들러 연결.</summary>
    private void SetupLayoutEditor()
    {
        var edit = new MenuItem { Header = "배치 편집", IsCheckable = true };
        edit.Click += (_, _) => SetLayoutEdit(edit.IsChecked);
        var save = new MenuItem { Header = "배치 저장(소스 기본값)" };
        save.Click += (_, _) => SaveLayoutDefaults();
        var reset = new MenuItem { Header = "배치 되돌리기" };
        reset.Click += (_, _) =>
            ApplyLayout(LayoutDefaults.BubbleX, LayoutDefaults.BubbleY, LayoutDefaults.EggX, LayoutDefaults.EggY);

        var menu = ContextMenu!;
        var at = menu.Items.Count - 1; // "종료" 앞
        menu.Items.Insert(at, new Separator());
        menu.Items.Insert(at, reset);
        menu.Items.Insert(at, save);
        menu.Items.Insert(at, edit);

        foreach (var el in new FrameworkElement[] { Bubble, EggGroup })
        {
            el.PreviewMouseLeftButtonDown += OnLayoutDragStart;
            el.PreviewMouseMove += OnLayoutDragMove;
            el.PreviewMouseLeftButtonUp += OnLayoutDragEnd;
        }
    }

    private void SetLayoutEdit(bool on)
    {
        _layoutEdit = on;
        var vis = on ? Visibility.Visible : Visibility.Collapsed;
        BubbleEditFrame.Visibility = vis;
        EggEditFrame.Visibility = vis;
        Bubble.Cursor = on ? Cursors.SizeAll : null;
        EggGroup.Cursor = on ? Cursors.SizeAll : null;
    }

    private void OnLayoutDragStart(object sender, MouseButtonEventArgs e)
    {
        if (!_layoutEdit) return;
        e.Handled = true; // 창 DragMove·알 부화 클릭 차단
        var el = (FrameworkElement)sender;
        var t = (TranslateTransform)el.RenderTransform;
        _dragStart = e.GetPosition(Root);
        _dragOrigin = new Point(t.X, t.Y);
        el.CaptureMouse();
    }

    private void OnLayoutDragMove(object sender, MouseEventArgs e)
    {
        if (_dragStart is not { } start) return;
        var el = (FrameworkElement)sender;
        var t = (TranslateTransform)el.RenderTransform;
        var d = e.GetPosition(Root) - start;
        var (prevX, prevY) = (t.X, t.Y);
        t.X = Math.Round(_dragOrigin.X + d.X);
        t.Y = Math.Round(_dragOrigin.Y + d.Y);
        // 창(루트) 밖으로 나가면 잘리므로 축별로 되돌림
        var bounds = el.TransformToAncestor(Root).TransformBounds(new Rect(el.RenderSize));
        if (bounds.Left < 0 || bounds.Right > Root.ActualWidth) t.X = prevX;
        if (bounds.Top < 0 || bounds.Bottom > Root.ActualHeight) t.Y = prevY;
    }

    private void OnLayoutDragEnd(object sender, MouseButtonEventArgs e)
    {
        if (_dragStart == null) return;
        e.Handled = true;
        _dragStart = null;
        ((FrameworkElement)sender).ReleaseMouseCapture();
    }

    /// <summary>현재 오프셋으로 LayoutDefaults.cs를 다시 씀. 다음 빌드부터 기본 위치.</summary>
    private void SaveLayoutDefaults()
    {
        var path = LayoutDefaults.SourcePath;
        if (!File.Exists(path))
        {
            MessageBox.Show($"소스 파일을 찾을 수 없음:\n{path}", "배치 저장");
            return;
        }
        static string N(double v) => v.ToString(CultureInfo.InvariantCulture);
        var src = File.ReadAllText(path);
        src = Regex.Replace(src, @"BubbleX = [^,]+, BubbleY = [^;]+;",
            $"BubbleX = {N(BubbleOffset.X)}, BubbleY = {N(BubbleOffset.Y)};");
        src = Regex.Replace(src, @"EggX = [^,]+, EggY = [^;]+;",
            $"EggX = {N(EggOffset.X)}, EggY = {N(EggOffset.Y)};");
        File.WriteAllText(path, src);
        MessageBox.Show(
            $"저장됨 — 다시 빌드하면 기본값으로 반영\n\n말풍선 ({N(BubbleOffset.X)}, {N(BubbleOffset.Y)})\n알 ({N(EggOffset.X)}, {N(EggOffset.Y)})\n\n{path}",
            "배치 저장");
    }
#endif

    // ---- 창 ----

    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (!_placed) return;
        if (_positionBeforeDrawer != null)
        {
            PositionForDrawer(e.NewSize.Height);
            return;
        }
        // 스프라이트 교체로 크기가 바뀌어도 발 위치(하단 중앙) 고정
        if (e.HeightChanged) Top += e.PreviousSize.Height - e.NewSize.Height;
        if (e.WidthChanged) Left += (e.PreviousSize.Width - e.NewSize.Width) / 2;
    }

    private void PositionForDrawer(double height)
    {
        if (_positionBeforeDrawer is not { } origin) return;
        // 누적 이동 대신 원점과 전체 높이 차이를 사용하므로 clamp/애니메이션 반전에도 복귀 위치가 보존된다.
        Left = origin.X;
        var wa = SystemParameters.WorkArea;
        Top = Math.Clamp(origin.Y - (height - _heightBeforeDrawer), wa.Top, Math.Max(wa.Top, wa.Bottom - height));
    }

    private void OnDrag(object sender, MouseButtonEventArgs e)
    {
        var before = new Point(Left, Top);
        DragMove();
        // DragMove는 드래그가 끝나면 반환한다. 이동 없는 클릭은 복귀 원점을 보존한다.
        if (new Point(Left, Top) != before) _positionBeforeDrawer = null;
    }

    private void OnExit(object sender, RoutedEventArgs e) => Application.Current.Shutdown();
}
