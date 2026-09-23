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
    private bool _saveErrorReported;
    private readonly Settings _settings;
    private SpriteAtlas? _atlas;
    private int _frame;
    private int _loadRequest; // 최신 스프라이트 로드 요청 번호. 빠른 연속 선택 시 옛 결과 무시.
    private int _iconRequest;
    private int _eggArtRequest;
    private int _resultRequest;
    private SpriteAtlas? _resultAtlas;
    private int _resultFrame;
    private readonly record struct PokemonChoice(int Dex, bool IsShiny);
    private bool ViewingShiny => ShinyDex.IsChecked == true;
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
    public MainWindow() : this(Settings.NewPreview(4), false) { }

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
        SetupTestEggs();
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
        Closing += (_, e) =>
        {
            if (_startServices && !_discardSave) e.Cancel = !TrySaveSettings();
        };
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
            ResultImage.Source = null;
            _resultAtlas?.Dispose();
            EggImage.Source = CrackImage.Source = null;
            DisposeFrames(_crackFrames);
        };
    }

    private async void OnLoaded(object? sender, EventArgs e)
    {
        BuildGenTabs();

        var initialLoad = LoadPokemonAsync(_settings.SelectedDex, _settings.SelectedShiny);
        var initialRequest = _loadRequest;
        if (!await initialLoad && !_closed && initialRequest == _loadRequest)
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

        UpdateLayout();
        var wa = WorkingArea;
        Position = new PixelPoint(wa.Right - (int)Math.Ceiling(Bounds.Width * DesktopScaling) - 20,
            wa.Bottom - (int)Math.Ceiling(Bounds.Height * DesktopScaling) - 20);
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
                ShowSpriteError($"스프라이트 로드 실패 (#{dex}{(shiny ? " 이로치" : "")}): {ex.Message}\n우클릭 메뉴에서 다시 불러올 수 있습니다.");
            return false;
        }
        if (req != _loadRequest || _closed)
        {
            atlas.Dispose();
            return false;
        }

        var previous = _atlas;
        _atlas = atlas;
        SpriteMissing.IsVisible = false;
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

    private void ShowSpritePlaceholder()
    {
        Sprite.Source = null;
        _atlas?.Dispose();
        _atlas = null;
        Stage.Width = 60;
        Stage.Height = 42;
        Zoom.ScaleX = Zoom.ScaleY = 2;
        SpriteMissing.Text = $"{PokemonNames.Of(_settings.SelectedDex)}\n{(_settings.SelectedShiny ? "이로치\n" : "")}이미지 없음";
        SpriteMissing.IsVisible = true;
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
            if (!_saveErrorReported && !_closed)
                _ = AppDialog.ShowAsync(this, $"저장하지 못했습니다. 저장 위치를 확인해 주세요.\n{ex.Message}", "저장 실패");
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
        if (leveled) _animations["LevelUp"].Play();
    }

    private void UpdateLevelUi()
    {
        var p = _settings.For(_settings.SelectedDex, _settings.SelectedShiny);
        var required = Settings.ExpToNext(p.Level);
        LevelText.Text = $"{(_settings.SelectedShiny ? "★ " : "")}Lv. {p.Level}";
        ExpBar.Width = ExpTrack.Width * p.Exp / required;
        ExpBar.IsVisible = p.Exp > 0;
        UiToolTips.Set(ExpTrack, $"현재 경험치: {p.Exp:N0}\n필요 경험치: {required:N0}");
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
        if (sender is RadioButton { IsChecked: true }) await RefreshDexAsync();
    }

    private async void OnShinyDexChanged(object? sender, RoutedEventArgs e)
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
            IconScroll.Offset = default;
        }
        catch (Exception ex)
        {
            if (!_closed && request == _iconRequest)
                ShowSpriteError($"아이콘 로드 실패 ({gen}세대): {ex.Message}");
        }
    }

    private void RebuildIconGrid(int gen, Dictionary<int, Bitmap> icons)
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
            if (rb.IsChecked == true) return (int)rb.Tag!;
        return null;
    }

    private void OnOwnedOnlyChanged(object? sender, RoutedEventArgs e)
    {
        if (CheckedGen() is not { } gen) return;
        if (!PokemonIcons.TryGetCachedGen(gen, out var icons, ViewingShiny)) return;
        RebuildIconGrid(gen, icons);
        IconScroll.Offset = default;
    }

    private void UpdateOwnedCount()
    {
        var total = PokemonIcons.Generations[^1].Last;
        var owned = ViewingShiny ? _settings.ShinyOwned : _settings.Owned;
        OwnedCount.Text = $"보유 {(_settings.UnlockAll ? total : owned.Count)}/{total}";
    }

    private RadioButton MakeIconCell(int dex, bool shiny, Bitmap bmp)
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
            Theme = (ControlTheme)Resources["IconButton"]!,
            IsEnabled = owned,
            Cursor = owned ? new Cursor(StandardCursorType.Hand) : new Cursor(StandardCursorType.Arrow),
            IsChecked = dex == _settings.SelectedDex && shiny == _settings.SelectedShiny,
        };
        UiToolTips.Set(rb, owned ? $"#{dex} {PokemonNames.Of(dex)}{(shiny ? " ★ 이로치" : "")} · Lv.{level}"
            : $"#{dex} ???{(shiny ? " ★ 이로치" : "")} (미보유)");
        rb.IsCheckedChanged += OnIconChecked;
        return rb;
    }

    private void RefreshIconCell(int dex, bool shiny)
    {
        if (shiny != ViewingShiny) return;
        var gen = PokemonIcons.GenOf(dex);
        if (CheckedGen() != gen) return;
        if (PokemonIcons.TryGetCachedGen(gen, out var icons, shiny)) RebuildIconGrid(gen, icons);
    }

    private async void OnIconChecked(object? sender, RoutedEventArgs e)
    {
        var rb = (RadioButton)sender!;
        if (rb.IsChecked != true) return;
        var choice = (PokemonChoice)rb.Tag!;
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
            cell.IsCheckedChanged -= OnIconChecked;
            cell.IsChecked = (PokemonChoice)cell.Tag! == new PokemonChoice(_settings.SelectedDex, _settings.SelectedShiny);
            cell.IsCheckedChanged += OnIconChecked;
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
        if (!on)
        {
            ++_loadRequest; // 해금 상태로 시작한 요청이 뒤늦게 적용되는 것을 막는다.
            if (!_settings.IsOwned(_settings.SelectedDex, _settings.SelectedShiny))
            {
                _settings.SelectedDex = _settings.StarterDex;
                _settings.SelectedShiny = false;
                var starterLoad = LoadPokemonAsync(_settings.StarterDex);
                var starterRequest = _loadRequest;
                if (!await starterLoad && !_closed && starterRequest == _loadRequest)
                    ShowSpritePlaceholder();
                if (_closed) return;
                UpdateLevelUi();
                TrySaveSettings();
            }
        }

        UpdateOwnedCount();
        if (CheckedGen() is { } gen && PokemonIcons.TryGetCachedGen(gen, out var icons, ViewingShiny))
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
