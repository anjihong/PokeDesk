#if DEBUG
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
    private readonly Settings _settings = Settings.Load();
    private SpriteAtlas? _atlas;
    private int _frame;
    private int _loadRequest; // 최신 스프라이트 로드 요청 번호. 빠른 연속 선택 시 옛 결과 무시.
    private bool _dirty;      // 설정 변경됨, 주기 저장 대기
    private bool _placed;     // 초기 위치 잡은 뒤부터 크기 변화에 맞춰 하단 고정
    private DateTime _lastEggTick; // 알 타이머 직전 틱 시각(UTC)
    private bool _anchorTop;       // 서랍 펼침/접힘 중: 창 상단 고정(아래로 펼쳐지게)
    private bool _drawerOpen;
    private double? _topBeforeDrawer;

    private enum EggState { Waiting, Ready, Hatching, Result }
    private EggState _eggState;
    private Dictionary<string, SpriteFrame>? _crackFrames;
    private readonly DispatcherTimer _resultTimer = new() { Interval = TimeSpan.FromSeconds(5) };

    public MainWindow()
    {
        InitializeComponent();
        ApplyLayout(LayoutDefaults.BubbleX, LayoutDefaults.BubbleY, LayoutDefaults.EggX, LayoutDefaults.EggY);
#if DEBUG
        SetupLayoutEditor();
        SetupUnlockAll();
#endif
        Loaded += OnLoaded;
        SizeChanged += OnSizeChanged;
        Closed += (_, _) =>
        {
            _hook.Dispose();
            _settings.Save();
        };
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        BuildGenTabs();

        if (!await LoadPokemonAsync(_settings.SelectedDex))
        {
            Close();
            return;
        }

        // PokeRogue와 동일: frameRate 10, 무한 반복
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        timer.Tick += (_, _) =>
        {
            if (_atlas != null) ShowFrame((_frame + 1) % _atlas.Frames.Length);
        };
        timer.Start();

        // 입력마다 디스크 쓰지 않고 5초 주기로 저장
        var save = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        save.Tick += (_, _) =>
        {
            if (!_dirty) return;
            _settings.Save();
            _dirty = false;
        };
        save.Start();

        // 실행 시간 누적 → 30분마다 알 1개(알이 있으면 일시정지). 절전 등으로 틱이 밀려도 한 번에 최대 5초만 인정.
        _lastEggTick = DateTime.UtcNow;
        var egg = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        egg.Tick += (_, _) =>
        {
            var now = DateTime.UtcNow;
            var dt = Math.Min((now - _lastEggTick).TotalSeconds, 5);
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
        egg.Start();
        _resultTimer.Tick += (_, _) =>
        {
            _resultTimer.Stop();
            SetEggState(_settings.Eggs > 0 ? EggState.Ready : EggState.Waiting);
        };
        SetEggState(_settings.Eggs > 0 ? EggState.Ready : EggState.Waiting);
        UpdateOwnedCount();
        _ = LoadEggAssetsAsync();

        var bounce = (Storyboard)Resources["Bounce"];
        // 훅 콜백은 빨리 반환해야 하므로 애니메이션 시작은 큐에 넘김
        _hook.Triggered += () => Dispatcher.BeginInvoke(() =>
        {
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
                MessageBox.Show($"스프라이트 로드 실패 (#{dex}): {ex.Message}", "DeskPokemon");
            return false;
        }
        if (req != _loadRequest) return false;

        _atlas = atlas;
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
        if (leveled) ((Storyboard)Resources["LevelUp"]).Begin(this, true);
    }

    private void UpdateLevelUi()
    {
        var p = _settings.For(_settings.SelectedDex);
        LevelText.Text = $"Lv. {p.Level}";
        ExpBar.Width = ExpTrack.Width * p.Exp / Settings.ExpToNext(p.Level);
    }

    // ---- 알 ----

    /// <summary>
    /// 알 상태 머신. Waiting(카운트다운) → Ready(클릭하여 부화, 통통) → Hatching(흔들림+균열+플래시) → Result(아이콘+이름+NEW!, 5초) → Waiting.
    /// </summary>
    private void SetEggState(EggState state)
    {
        _eggState = state;
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
                BubbleText.Text = "클릭하여\n부화";
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
                ResultImage.Visibility = Visibility.Visible;
                NewText.Visibility = Visibility.Visible;
                break;
        }
    }

    private void ShowEgg(bool show)
    {
        EggStage.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        CrackImage.Visibility = Visibility.Collapsed;
        ResultImage.Visibility = Visibility.Collapsed;
        NewText.Visibility = Visibility.Collapsed;
    }

    private void UpdateBubbleCountdown() =>
        BubbleText.Text = TimeSpan.FromSeconds(_settings.RemainingEggSeconds).ToString(@"mm\:ss");

    /// <summary>알 본체(egg/egg의 egg_0) + 균열 오버레이(egg/egg_crack). 실패하면 대체 타원 유지, 균열 없이 진행.</summary>
    private async Task LoadEggAssetsAsync()
    {
        try
        {
            var egg = await SpriteAtlas.LoadFramesAsync("egg/egg");
            if (egg.TryGetValue("egg_0", out var f))
            {
                EggImage.Source = f.Bitmap;
                EggFallback.Visibility = Visibility.Collapsed;
            }
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

        Flash.Opacity = 1;
        ((Storyboard)Resources["FlashOut"]).Begin(this, true);
        await Task.Delay(150);

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
        NewText.Text = res.IsNew ? "NEW!" : $"Lv.{res.Level} ↑";
        BubbleText.Text = PokemonNames.Of(res.Dex);
        SetEggState(EggState.Result);
        ((Storyboard)Resources["ResultPop"]).Begin(this, true);
        _resultTimer.Stop();
        _resultTimer.Start();
    }

    /// <summary>테스트용: 알을 즉시 준비 상태로.</summary>
    private void OnDebugEgg(object sender, RoutedEventArgs e)
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

    /// <summary>서랍 Height를 0↔내용 높이로. 펼치는 동안 창 상단을 고정해 아래로 내려오게 하고, 끝나면 작업 영역 안으로 보정.</summary>
    private void AnimateDrawer(bool open)
    {
        DrawerContent.Measure(new Size(300, double.PositiveInfinity));
        var target = open ? DrawerContent.DesiredSize.Height : 0;
        if (open && !_drawerOpen) _topBeforeDrawer = Top; // 화면 아래 걸려 위로 밀렸다가 접히면 원위치
        _drawerOpen = open;
        _anchorTop = true;
        var anim = new DoubleAnimation(target, TimeSpan.FromMilliseconds(250))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        };
        anim.Completed += (_, _) =>
        {
            _anchorTop = false;
            if (!open && _topBeforeDrawer is { } top) Top = top;
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

    private async void OnGenChecked(object sender, RoutedEventArgs e)
    {
        var tab = (RadioButton)sender;
        var gen = (int)tab.Tag;

        Dictionary<int, BitmapSource> icons;
        try
        {
            icons = await PokemonIcons.LoadGenAsync(gen);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"아이콘 로드 실패 ({gen}세대): {ex.Message}", "DeskPokemon");
            return;
        }
        if (tab.IsChecked != true) return; // 로드 중 다른 탭 선택됨

        RebuildIconGrid(gen, icons);
        IconScroll.ScrollToTop();
    }

    /// <summary>현재 탭 세대의 격자를 다시 채움. "보유만 보기"면 보유 종만.</summary>
    private void RebuildIconGrid(int gen, Dictionary<int, BitmapSource> icons)
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
            if (rb.IsChecked == true) return (int)rb.Tag;
        return null;
    }

    private void OnOwnedOnlyChanged(object sender, RoutedEventArgs e)
    {
        if (CheckedGen() is not { } gen) return;
        if (!PokemonIcons.TryGetCachedGen(gen, out var icons)) return; // 아직 로드 중이면 로드 완료 시 반영됨
        RebuildIconGrid(gen, icons);
        IconScroll.ScrollToTop();
    }

    private void UpdateOwnedCount()
    {
        var total = PokemonIcons.Generations[^1].Last;
        OwnedCount.Text = $"보유 {(_settings.UnlockAll ? total : _settings.Owned.Count)}/{total}";
    }

    /// <summary>아이콘 셀. 미보유 종은 실루엣 + 비활성.</summary>
    private RadioButton MakeIconCell(int dex, BitmapSource bmp)
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
            Style = (Style)Resources["IconButton"],
            ToolTip = owned ? $"#{dex} {PokemonNames.Of(dex)}" : $"#{dex} ??? (미보유)",
            IsEnabled = owned,
            Cursor = owned ? Cursors.Hand : Cursors.Arrow,
            IsChecked = dex == _settings.SelectedDex, // 핸들러 연결 전에 설정해 재선택 방지
        };
        rb.Checked += OnIconChecked;
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

    private async void OnIconChecked(object sender, RoutedEventArgs e)
    {
        var rb = (RadioButton)sender;
        var dex = (int)rb.Tag;
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
                if ((int)cell.Tag == prev) cell.IsChecked = true;
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
        if (!on && !_settings.IsOwned(_settings.SelectedDex))
        {
            _settings.SelectedDex = Settings.Starter;
            UpdateLevelUi();
            await LoadPokemonAsync(Settings.Starter);
            _settings.Save();
            _dirty = false;
        }

        UpdateOwnedCount();
        if (CheckedGen() is { } gen && PokemonIcons.TryGetCachedGen(gen, out var icons))
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
        if (_anchorTop)
        {
            // 서랍 펼침/접힘 중: 상단 고정(아래로 펼쳐짐). 단, 작업 영역 아래로 나가면 그만큼 위로.
            var wa = SystemParameters.WorkArea;
            if (Top + e.NewSize.Height > wa.Bottom) Top = wa.Bottom - e.NewSize.Height;
            return;
        }
        // 스프라이트 교체로 크기가 바뀌어도 발 위치(하단 중앙) 고정
        if (e.HeightChanged) Top += e.PreviousSize.Height - e.NewSize.Height;
        if (e.WidthChanged) Left += (e.PreviousSize.Width - e.NewSize.Width) / 2;
    }

    private void OnDrag(object sender, MouseButtonEventArgs e) => DragMove();

    private void OnExit(object sender, RoutedEventArgs e) => Application.Current.Shutdown();
}
