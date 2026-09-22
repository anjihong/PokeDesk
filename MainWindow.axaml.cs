#if DEBUG
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
#endif
using Avalonia;
using Avalonia.Interactivity;
using Avalonia.Styling;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;

namespace DeskPokemon;

public partial class MainWindow : Window
{
    private readonly InputHook? _hook;
    private readonly bool _startServices;
    private readonly List<DispatcherTimer> _timers = new();
    private readonly CancellationTokenSource _lifetime = new();
    private bool _closed;
    private bool _discardSave;
    private Dictionary<string, SpriteFrame>? _eggFrames;
    private readonly Settings _settings;
    private SpriteAtlas? _atlas;
    private int _frame;
    private int _loadRequest; // 최신 스프라이트 로드 요청 번호. 빠른 연속 선택 시 옛 결과 무시.
    private bool _dirty;      // 설정 변경됨, 주기 저장 대기
    private bool _placed;     // 초기 위치 잡은 뒤부터 크기 변화에 맞춰 하단 고정
    private DateTime _lastEggTick; // 알 타이머 직전 틱 시각(UTC)
    private PixelPoint? _positionBeforeDrawer;
    private PixelPoint _lastDrawerPosition;
    private double _heightBeforeDrawer;

    private enum EggState { Waiting, Ready, Hatching, Result }
    private EggState _eggState;
    private Dictionary<string, SpriteFrame>? _crackFrames;
    private readonly DispatcherTimer _resultTimer = new() { Interval = TimeSpan.FromSeconds(5) };

    /// <summary>XAML 디자이너용 미리보기. 실제 실행은 저장 데이터를 전달하는 생성자를 사용.</summary>
    public MainWindow() : this(Settings.New(4), false) { }

    public MainWindow(Settings settings) : this(settings, true) { }

    internal MainWindow(Settings settings, bool startServices)
    {
        _settings = settings;
        _startServices = startServices;
        _hook = startServices ? new InputHook() : null;
        InitializeComponent();
        BuildAnimations();
        ApplyLayout(LayoutDefaults.BubbleX, LayoutDefaults.BubbleY, LayoutDefaults.EggX, LayoutDefaults.EggY);
#if DEBUG
        SetupLayoutEditor();
        SetupUnlockAll();
        SetupReset();
#endif
        if (startServices) Opened += OnLoaded;
        UpdateLevelUi();
        UpdateOwnedCount();
        if (_hook != null)
        {
            _hook.Triggered += OnGlobalInput;
            _hook.StatusChanged += OnInputStatusChanged;
            RefreshInputStatus();
        }
        AddHandler(PointerPressedEvent, OnLocalPointer, RoutingStrategies.Tunnel);
        AddHandler(KeyDownEvent, OnLocalKey, RoutingStrategies.Tunnel);
        AddHandler(KeyUpEvent, OnLocalKeyUp, RoutingStrategies.Tunnel);
        Deactivated += (_, _) => _localKeys.Clear();
        SizeChanged += OnSizeChanged;
        Closed += (_, _) =>
        {
            _closed = true;
            _lifetime.Cancel();
            foreach (var timer in _timers) timer.Stop();
            _resultTimer.Stop();
            _drawerAnimation?.Dispose();
            foreach (var animation in _animations.Values) animation.Dispose();
            _hook?.Dispose();
            Sprite.Source = null;
            _atlas?.Dispose();
            EggImage.Source = CrackImage.Source = null;
            DisposeFrames(_eggFrames);
            DisposeFrames(_crackFrames);
            if (_startServices && !_discardSave) _settings.Save();
        };
    }

    private async void OnLoaded(object? sender, EventArgs e)
    {
        BuildGenTabs();

        await LoadPokemonAsync(_settings.SelectedDex);
        if (_closed) return;

        // PokeRogue와 동일: frameRate 10, 무한 반복
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        timer.Tick += (_, _) =>
        {
            if (_atlas != null) ShowFrame((_frame + 1) % _atlas.Frames.Length);
        };
        _timers.Add(timer);
        timer.Start();

        // 입력마다 디스크 쓰지 않고 5초 주기로 저장
        var save = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        save.Tick += (_, _) =>
        {
            if (!_dirty) return;
            _settings.Save();
            _dirty = false;
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
                _settings.Save();
                _dirty = false;
                if (_eggState == EggState.Waiting) SetEggState(EggState.Ready);
            }
            else if (_eggState == EggState.Waiting)
            {
                _dirty = true;
                UpdateBubbleCountdown();
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
        _ = LoadEggAssetsAsync();

        UpdateLayout();
        var wa = WorkingArea;
        Position = new PixelPoint(wa.Right - (int)Math.Ceiling(Bounds.Width * DesktopScaling) - 20,
            wa.Bottom - (int)Math.Ceiling(Bounds.Height * DesktopScaling) - 20);
        _placed = true;

        SelectGenTab(PokemonIcons.GenOf(_settings.SelectedDex));
    }

    /// <summary>스프라이트 교체. 실패 시 메시지 띄우고 false(이전 포켓몬 유지). 더 최신 요청이 있으면 조용히 false.</summary>
    private async Task<bool> LoadPokemonAsync(int dex)
    {
        var req = ++_loadRequest;
        SpriteAtlas atlas;
        try
        {
            atlas = await SpriteAtlas.LoadAsync(dex);
        }
        catch (Exception ex)
        {
            if (req == _loadRequest)
                ShowSpriteError($"스프라이트 로드 실패 (#{dex}): {ex.Message}\n우클릭 메뉴에서 다시 불러올 수 있습니다.");
            return false;
        }
        if (req != _loadRequest || _closed)
        {
            atlas.Dispose();
            return false;
        }

        var previous = _atlas;
        _atlas = atlas;
        SpriteStatus.IsVisible = false;
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
        previous?.Dispose();
        UpdateLevelUi();
        return true;
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
        var leveled = _settings.AddExp(_settings.SelectedDex);
        _dirty = true;
        UpdateLevelUi();
        if (leveled) _animations["LevelUp"].Play();
    }

    private void UpdateLevelUi()
    {
        var p = _settings.For(_settings.SelectedDex);
        var required = Settings.ExpToNext(p.Level);
        LevelText.Text = $"Lv. {p.Level}";
        ExpBar.Width = ExpTrack.Width * p.Exp / required;
        ExpBar.IsVisible = p.Exp > 0;
        UiToolTips.Set(ExpTrack, $"현재 경험치: {p.Exp:N0}\n필요 경험치: {required:N0}");
    }

    // ---- 알 ----

    /// <summary>
    /// 알 상태 머신. Waiting(카운트다운) → Ready(클릭하여 부화, 통통) → Hatching(흔들림+균열+플래시) → Result(아이콘+이름+NEW!, 5초) → Waiting.
    /// </summary>
    private void SetEggState(EggState state)
    {
        _eggState = state;
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
                BubbleText.Text = "클릭하여\n부화";
                idle.Play();
                break;
            case EggState.Hatching:
                idle.Stop();
                wait.Stop(); // EggShake가 EggRotate를 쓰므로 루프 정지
                BubbleText.Text = "...";
                break;
            case EggState.Result:
                // BubbleText/NewText는 HatchAsync가 채움
                EggStage.IsVisible = false;
                ResultImage.IsVisible = true;
                NewText.IsVisible = true;
                break;
        }
    }

    private void ShowEgg(bool show)
    {
        EggStage.IsVisible = show;
        CrackImage.IsVisible = false;
        ResultImage.IsVisible = false;
        NewText.IsVisible = false;
    }

    private void UpdateBubbleCountdown() =>
        BubbleText.Text = TimeSpan.FromSeconds(_settings.RemainingEggSeconds).ToString(@"mm\:ss");

    /// <summary>알 본체(egg/egg의 egg_0) + 균열 오버레이(egg/egg_crack). 실패하면 대체 타원 유지, 균열 없이 진행.</summary>
    private async Task LoadEggAssetsAsync()
    {
        try
        {
            var egg = await SpriteAtlas.LoadFramesAsync("egg/egg");
            if (_closed) { DisposeFrames(egg); return; }
            _eggFrames = egg;
            if (egg.TryGetValue("egg_0", out var f))
            {
                EggImage.Source = f.Bitmap;
                EggFallback.IsVisible = false;
            }
            var cracks = await SpriteAtlas.LoadFramesAsync("egg/egg_crack");
            if (_closed) { DisposeFrames(cracks); return; }
            _crackFrames = cracks;
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
        CrackImage.IsVisible = true;
    }

    private async void OnEggClick(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        e.Handled = true; // 드래그로 넘어가지 않게
        if (_eggState != EggState.Ready) return;
        try { await HatchAsync(); }
        catch (OperationCanceledException) when (_closed) { }
    }

    private async Task HatchAsync()
    {
        SetEggState(EggState.Hatching);
        var shake = _animations["EggShake"];
        for (var stage = 1; stage <= 3; stage++)
        {
            ShowCrack(stage);
            shake.Play();
            await Task.Delay(550, _lifetime.Token);
        }
        ShowCrack(4);
        await Task.Delay(250, _lifetime.Token);

        if (_settings.Hatch() is not { } res)
        {
            SetEggState(EggState.Waiting);
            return;
        }
        _settings.Save();
        _dirty = false;
        UpdateOwnedCount();
        if (res.Dex == _settings.SelectedDex) UpdateLevelUi();
        RefreshIconCell(res.Dex);

        _animations["FlashOut"].Play(); // From=1이라 Opacity 직접 설정 불필요(이전 애니메이션이 값을 잡고 있어 무시됨)
        await Task.Delay(150, _lifetime.Token);

        ResultImage.Source = null;
        try
        {
            var icons = await PokemonIcons.LoadGenAsync(PokemonIcons.GenOf(res.Dex));
            if (icons.TryGetValue(res.Dex, out var bmp)) ResultImage.Source = bmp;
        }
        catch
        {
            // 아이콘 없어도 이름은 표시됨
        }
        if (_closed) return;
        NewText.Text = res.IsNew ? "NEW!" : $"Lv.{res.Level} ↑";
        BubbleText.Text = PokemonNames.Of(res.Dex);
        SetEggState(EggState.Result);
        _animations["ResultPop"].Play();
        _resultTimer.Stop();
        _resultTimer.Start();
    }

    /// <summary>테스트용: 알을 즉시 준비 상태로.</summary>
    private void OnDebugEgg(object? sender, RoutedEventArgs e)
    {
        if (_eggState is EggState.Hatching or EggState.Result) return;
        if (_settings.Eggs == 0)
        {
            _settings.Eggs = 1;
            _settings.EggSeconds = 0;
            _settings.Save();
        }
        SetEggState(EggState.Ready);
    }

    // ---- 메뉴 탭 / 서랍 ----

    // ToggleButton이라 같은 탭을 다시 누르면 Unchecked → 접힘. 다른 탭을 누르면 나머지를 코드에서 해제.
    private void OnMenuChecked(object? sender, RoutedEventArgs e)
    {
        var btn = (ToggleButton)sender!;
        if (btn.IsChecked != true) { OnMenuUnchecked(sender, e); return; }
        foreach (ToggleButton other in MenuTabs.Children)
            if (other != btn) other.IsChecked = false;
        var isDex = (string)btn.Tag! == "dex";
        DexPanel.IsVisible = isDex;
        PlaceholderPanel.IsVisible = !isDex;
        AnimateDrawer(open: true);
    }

    private void OnMenuUnchecked(object? sender, RoutedEventArgs e)
    {
        foreach (ToggleButton other in MenuTabs.Children)
            if (other.IsChecked == true) return; // 다른 탭으로 전환 중이면 그 탭이 열어 줌
        AnimateDrawer(open: false);
    }

    /// <summary>서랍 높이만큼 위로 이동해 창 하단을 유지하고, 접으면 펼치기 전 위치로 돌아온다.</summary>
    private Timeline? _drawerAnimation;

    private void AnimateDrawer(bool open)
    {
        _drawerAnimation?.Dispose();
        DrawerContent.Measure(new Size(300, double.PositiveInfinity));
        var target = open ? DrawerContent.DesiredSize.Height : 0;
        if (_positionBeforeDrawer != null && Position != _lastDrawerPosition)
            _positionBeforeDrawer = null; // 펼친 채 실제로 이동했을 때만 새 위치를 기준으로 삼는다.
        if (_positionBeforeDrawer == null)
        {
            // 펼친 채 드래그한 경우에도 현재 위치를 기준으로 접는다.
            _positionBeforeDrawer = new PixelPoint(Position.X,
                Position.Y + (int)Math.Round(Drawer.Height * DesktopScaling));
            _heightBeforeDrawer = Bounds.Height - Drawer.Height;
        }
        _lastDrawerPosition = Position;
        _drawerAnimation = new Timeline(false,
            [new(v => Drawer.Height = v, Drawer.Height, [new(0, Drawer.Height), new(.25, target, Ease.OutCubic)])],
            () =>
            {
                UpdateLayout();
                ClampToScreen();
                if (!open) _positionBeforeDrawer = null;
            });
        _drawerAnimation.Play();
    }

    // ---- 도감/선택 패널 ----

    private void BuildGenTabs()
    {
        var style = (ControlTheme)Resources["GenTab"]!;
        foreach (var g in PokemonIcons.Generations)
        {
            var rb = new RadioButton { Content = g.Gen, Tag = g.Gen, GroupName = "Gen", Theme = style };
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
        var tab = (RadioButton)sender!;
        if (tab.IsChecked != true) return;
        var gen = (int)tab.Tag!;

        Dictionary<int, Bitmap> icons;
        try
        {
            icons = await PokemonIcons.LoadGenAsync(gen);
        }
        catch (Exception ex)
        {
            ShowSpriteError($"아이콘 로드 실패 ({gen}세대): {ex.Message}");
            return;
        }
        if (_closed || tab.IsChecked != true) return; // 로드 중 다른 탭 선택됨

        RebuildIconGrid(gen, icons);
        IconScroll.Offset = default;
    }

    /// <summary>현재 탭 세대의 격자를 다시 채움. "보유만 보기"면 보유 종만.</summary>
    private void RebuildIconGrid(int gen, Dictionary<int, Bitmap> icons)
    {
        var ownedOnly = OwnedOnly.IsChecked == true;
        var (_, first, last) = PokemonIcons.Generations[gen - 1];
        IconGrid.Children.Clear();
        for (var dex = first; dex <= last; dex++)
        {
            if (!icons.TryGetValue(dex, out var bmp)) continue;
            if (ownedOnly && !_settings.IsOwned(dex)) continue;
            IconGrid.Children.Add(MakeIconCell(dex, bmp));
        }
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
        if (!PokemonIcons.TryGetCachedGen(gen, out var icons)) return; // 아직 로드 중이면 로드 완료 시 반영됨
        RebuildIconGrid(gen, icons);
        IconScroll.Offset = default;
    }

    private void UpdateOwnedCount()
    {
        var total = PokemonIcons.Generations[^1].Last;
        OwnedCount.Text = $"보유 {(_settings.UnlockAll ? total : _settings.Owned.Count)}/{total}";
    }

    /// <summary>아이콘 셀. 미보유 종은 실루엣 + 비활성.</summary>
    private RadioButton MakeIconCell(int dex, Bitmap bmp)
    {
        var owned = _settings.IsOwned(dex);
        var rb = new RadioButton
        {
            // 40x30 캔버스에 원본 크기로 중앙 배치
            Content = new Image
            {
                Source = owned ? bmp : PokemonIcons.SilhouetteOf(dex, bmp),
                Width = 40, Height = 30, Stretch = Stretch.None,
            },
            Tag = dex,
            GroupName = "Icon",
            Theme = (ControlTheme)Resources["IconButton"]!,
            IsEnabled = owned,
            Cursor = owned ? new Cursor(StandardCursorType.Hand) : new Cursor(StandardCursorType.Arrow),
            IsChecked = dex == _settings.SelectedDex, // 핸들러 연결 전에 설정해 재선택 방지
        };
        UiToolTips.Set(rb, owned ? $"#{dex} {PokemonNames.Of(dex)}" : $"#{dex} ??? (미보유)");
        rb.IsCheckedChanged += OnIconChecked;
        return rb;
    }

    /// <summary>현재 격자에 해당 종이 있으면 보유 상태를 반영해 셀 교체(부화 직후 실루엣 해제).</summary>
    private void RefreshIconCell(int dex)
    {
        // 격자가 그 세대를 표시 중이면 원본 아이콘은 이미 세대 캐시에 있음
        var gen = PokemonIcons.GenOf(dex);
        if (CheckedGen() != gen) return;
        if (!PokemonIcons.TryGetCached(gen, dex, out var bmp)) return;
        for (var i = 0; i < IconGrid.Children.Count; i++)
        {
            if (IconGrid.Children[i] is not RadioButton { Tag: int tag } || tag != dex) continue;
            IconGrid.Children[i] = MakeIconCell(dex, bmp);
            return;
        }
        // "보유만 보기"로 숨겨져 있던 신규 종 → 격자 다시 채워 나타나게
        if (PokemonIcons.TryGetCachedGen(gen, out var icons)) RebuildIconGrid(gen, icons);
    }

    private async void OnIconChecked(object? sender, RoutedEventArgs e)
    {
        var rb = (RadioButton)sender!;
        if (rb.IsChecked != true) return;
        var dex = (int)rb.Tag!;
        if (!_settings.IsOwned(dex))
        {
            rb.IsChecked = false; // 방어: 비활성 셀이라 보통 도달 안 함
            return;
        }
        if (dex == _settings.SelectedDex) return;

        var prev = _settings.SelectedDex;
        _settings.SelectedDex = dex;
        UpdateLevelUi();

        if (await LoadPokemonAsync(dex))
        {
            _settings.Save();
            _dirty = false;
        }
        else if (_settings.SelectedDex == dex)
        {
            // 실패했고 그 사이 다른 선택도 없었음 → 이전 포켓몬으로 되돌림
            _settings.SelectedDex = prev;
            UpdateLevelUi();
            foreach (RadioButton cell in IconGrid.Children)
                if ((int)cell.Tag! == prev) cell.IsChecked = true;
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
    // ---- 초기화(개발자, Debug 빌드 전용) ----

    /// <summary>우클릭 메뉴에 초기화 항목 추가. 세이브 삭제 후 앱을 다시 띄워 스타팅 선택부터.</summary>
    private void SetupReset()
    {
        var item = new MenuItem { Header = "초기화(테스트)" };
        item.Click += async (_, _) =>
        {
            if (await AppDialog.ConfirmAsync(this, "초기화", "세이브를 삭제하고 스타팅 선택부터 다시 시작할까요?"))
                App.ResetSave();
        };
        ContextMenu!.Items.Insert(ContextMenu.Items.Count - 1, item); // "종료" 앞
    }

    // ---- 전체 해금 치트(개발자, Debug 빌드 전용) ----

    /// <summary>우클릭 메뉴에 전체 해금 토글 추가. Owned를 건드리지 않아 끄면 원래대로 돌아감.</summary>
    private void SetupUnlockAll()
    {
        var item = new MenuItem { Header = "전체 해금(치트)", ToggleType = MenuItemToggleType.CheckBox };
        item.Click += async (_, _) => await SetUnlockAll(item.IsChecked);
        ContextMenu!.Items.Insert(ContextMenu.Items.Count - 1, item); // "종료" 앞
    }

    private async Task SetUnlockAll(bool on)
    {
        _settings.UnlockAll = on;

        // 끌 때 미보유 종을 보고 있었으면 기본 포켓몬으로 복귀
        if (!on && !_settings.IsOwned(_settings.SelectedDex))
        {
            _settings.SelectedDex = _settings.StarterDex;
            UpdateLevelUi();
            await LoadPokemonAsync(_settings.StarterDex);
            if (_closed) return;
            _settings.Save();
            _dirty = false;
        }

        UpdateOwnedCount();
        if (CheckedGen() is { } gen && PokemonIcons.TryGetCachedGen(gen, out var icons))
        {
            RebuildIconGrid(gen, icons);
            IconScroll.Offset = default;
        }
    }

    // ---- 배치 편집(개발자, Debug 빌드 전용) ----

    private bool _layoutEdit;
    private Point? _dragStart;   // 드래그 시작 시 루트 기준 마우스 위치
    private Point _dragOrigin;   // 드래그 시작 시 오프셋

    /// <summary>우클릭 메뉴에 배치 편집 항목 추가 + 말풍선/알 드래그 핸들러 연결.</summary>
    private void SetupLayoutEditor()
    {
        var edit = new MenuItem { Header = "배치 편집", ToggleType = MenuItemToggleType.CheckBox };
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

        foreach (var el in new Control[] { Bubble, EggGroup })
        {
            el.AddHandler(PointerPressedEvent, OnLayoutDragStart, RoutingStrategies.Tunnel);
            el.AddHandler(PointerMovedEvent, OnLayoutDragMove, RoutingStrategies.Tunnel);
            el.AddHandler(PointerReleasedEvent, OnLayoutDragEnd, RoutingStrategies.Tunnel);
        }
    }

    private void SetLayoutEdit(bool on)
    {
        _layoutEdit = on;
        var vis = on;
        BubbleEditFrame.IsVisible = vis;
        EggEditFrame.IsVisible = vis;
        Bubble.Cursor = on ? new Cursor(StandardCursorType.SizeAll) : null;
        EggGroup.Cursor = on ? new Cursor(StandardCursorType.SizeAll) : null;
    }

    private void OnLayoutDragStart(object? sender, PointerPressedEventArgs e)
    {
        if (!_layoutEdit || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        e.Handled = true; // 창 DragMove·알 부화 클릭 차단
        var el = (Control)sender!;
        var t = (TranslateTransform)el.RenderTransform!;
        _dragStart = e.GetPosition(Root);
        _dragOrigin = new Point(t.X, t.Y);
        e.Pointer.Capture(el);
    }

    private void OnLayoutDragMove(object? sender, PointerEventArgs e)
    {
        if (_dragStart is not { } start) return;
        var el = (Control)sender!;
        var t = (TranslateTransform)el.RenderTransform!;
        var d = e.GetPosition(Root) - start;
        var (prevX, prevY) = (t.X, t.Y);
        t.X = Math.Round(_dragOrigin.X + d.X);
        t.Y = Math.Round(_dragOrigin.Y + d.Y);
        // 창(루트) 밖으로 나가면 잘리므로 축별로 되돌림
        var bounds = new Rect(el.Bounds.Size).TransformToAABB(el.TransformToVisual(Root) ?? Matrix.Identity);
        if (bounds.Left < 0 || bounds.Right > Root.Bounds.Width) t.X = prevX;
        if (bounds.Top < 0 || bounds.Bottom > Root.Bounds.Height) t.Y = prevY;
    }

    private void OnLayoutDragEnd(object? sender, PointerReleasedEventArgs e)
    {
        if (_dragStart == null) return;
        e.Handled = true;
        _dragStart = null;
        e.Pointer.Capture(null);
    }

    /// <summary>현재 오프셋으로 LayoutDefaults.cs를 다시 씀. 다음 빌드부터 기본 위치.</summary>
    private void SaveLayoutDefaults()
    {
        var path = LayoutDefaults.SourcePath;
        if (!File.Exists(path))
        {
            _ = AppDialog.ShowAsync(this, $"소스 파일을 찾을 수 없음:\n{path}", "배치 저장");
            return;
        }
        static string N(double v) => v.ToString(CultureInfo.InvariantCulture);
        var src = File.ReadAllText(path);
        src = Regex.Replace(src, @"BubbleX = [^,]+, BubbleY = [^;]+;",
            $"BubbleX = {N(BubbleOffset.X)}, BubbleY = {N(BubbleOffset.Y)};");
        src = Regex.Replace(src, @"EggX = [^,]+, EggY = [^;]+;",
            $"EggX = {N(EggOffset.X)}, EggY = {N(EggOffset.Y)};");
        File.WriteAllText(path, src);
        _ = AppDialog.ShowAsync(this,
            $"저장됨 — 다시 빌드하면 기본값으로 반영\n\n말풍선 ({N(BubbleOffset.X)}, {N(BubbleOffset.Y)})\n알 ({N(EggOffset.X)}, {N(EggOffset.Y)})\n\n{path}",
            "배치 저장");
    }
#endif

    // ---- 창 ----

    private PixelRect WorkingArea => (Screens.ScreenFromWindow(this) ?? Screens.Primary)?.WorkingArea
        ?? new PixelRect(0, 0, 1920, 1080);

    private void OnSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        if (!_placed) return;
        if (_positionBeforeDrawer is { } origin)
        {
            // 드래그 이후 포켓몬 교체 등으로 높이가 변해도 이동한 자리의 하단을 유지한다.
            if (Position != _lastDrawerPosition)
            {
                origin = new PixelPoint(Position.X,
                    Position.Y + (int)Math.Round((e.PreviousSize.Height - _heightBeforeDrawer) * DesktopScaling));
                _positionBeforeDrawer = origin;
            }
            // 프레임별 반올림을 누적하지 않고 펼치기 전 위치에서 총 높이 차이를 뺀다.
            Position = DrawerPosition(origin, e.NewSize.Height);
        }
        else
        {
            Position = new PixelPoint(
                Position.X + (int)Math.Round((e.PreviousSize.Width - e.NewSize.Width) * DesktopScaling / 2),
                Position.Y + (int)Math.Round((e.PreviousSize.Height - e.NewSize.Height) * DesktopScaling));
        }
        ClampToScreen();
    }

    private PixelPoint DrawerPosition(PixelPoint origin, double height) => new(origin.X,
        origin.Y - (int)Math.Round((height - _heightBeforeDrawer) * DesktopScaling));

    private void ClampToScreen()
    {
        Position = ConstrainToScreen(Position);
        _lastDrawerPosition = Position;
    }

    private PixelPoint ConstrainToScreen(PixelPoint position)
    {
        var wa = WorkingArea;
        var width = (int)Math.Ceiling(Bounds.Width * DesktopScaling);
        var height = (int)Math.Ceiling(Bounds.Height * DesktopScaling);
        return new PixelPoint(Math.Clamp(position.X, wa.X, Math.Max(wa.X, wa.Right - width)),
            Math.Clamp(position.Y, wa.Y, Math.Max(wa.Y, wa.Bottom - height)));
    }

    private void OnDrag(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) BeginMoveDrag(e);
    }

    private void OnExit(object? sender, RoutedEventArgs e) => Close();
}
