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

    public MainWindow()
    {
        InitializeComponent();
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

        // 실행 시간 누적 → 30분마다 알 1개. 절전 등으로 틱이 밀려도 한 번에 최대 5초만 인정.
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
            }
            else _dirty = true;
            UpdateEggUi();
        };
        egg.Start();
        UpdateEggUi();
        UpdateOwnedCount();
        _ = LoadEggIconAsync();

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
        Stage.Width = atlas.Width;
        Stage.Height = atlas.Height;
        // 캔버스 크기가 포켓몬마다 다르므로(42~96) 표시 높이 ~126px로 맞춤. 정수 배율 유지(픽셀아트).
        var zoom = Math.Max(1, Math.Round(126.0 / atlas.Height));
        Zoom.ScaleX = Zoom.ScaleY = zoom;
        ShowFrame(0);
        UpdateLevelUi();
        return true;
    }

    private void ShowFrame(int i)
    {
        _frame = i;
        var f = _atlas!.Frames[i];
        Sprite.Source = f.Bitmap;
        Sprite.Width = f.Width;
        Sprite.Height = f.Height;
        Canvas.SetLeft(Sprite, f.OffsetX);
        Canvas.SetTop(Sprite, f.OffsetY);
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

    private void UpdateEggUi()
    {
        EggCountText.Text = $"알 x{_settings.Eggs}";
        HatchButton.IsEnabled = _settings.Eggs > 0;
        EggTimerText.Text = $"다음 알 {TimeSpan.FromSeconds(_settings.RemainingEggSeconds):mm\\:ss}";
    }

    /// <summary>알 아이콘(pokerogue-assets egg/egg_icons 첫 프레임). 실패해도 조용히 빈 칸.</summary>
    private async Task LoadEggIconAsync()
    {
        try
        {
            var jsonPath = await SpriteAtlas.CachedAsync("egg/egg_icons.json");
            var pngPath = await SpriteAtlas.CachedAsync("egg/egg_icons.png");
            var sheet = SpriteAtlas.LoadSheet(pngPath);
            using var doc = System.Text.Json.JsonDocument.Parse(System.IO.File.ReadAllText(jsonPath));
            foreach (var (_, elem) in SpriteAtlas.EnumerateFrames(doc.RootElement))
            {
                var bmp = new CroppedBitmap(sheet, SpriteAtlas.ReadRect(elem.GetProperty("frame")));
                bmp.Freeze();
                EggIcon.Source = bmp;
                break;
            }
        }
        catch
        {
            // 네트워크 실패 등: 아이콘 없이 진행
        }
    }

    private async Task HatchOneAsync()
    {
        if (_settings.Hatch() is not { } res) return;
        _settings.Save();
        _dirty = false;
        UpdateEggUi();

        var name = $"#{res.Dex} {PokemonNames.Of(res.Dex)}";
        HatchResultText.Text = res.IsNew ? $"{name} 새 포켓몬!" : $"{name} 중복 → Lv.{res.Level}";
        HatchResultIcon.Source = null;
        HatchResultRow.Visibility = Visibility.Visible;
        UpdateOwnedCount();
        if (res.Dex == _settings.SelectedDex) UpdateLevelUi();

        try
        {
            var icons = await PokemonIcons.LoadGenAsync(PokemonIcons.GenOf(res.Dex));
            if (icons.TryGetValue(res.Dex, out var bmp)) HatchResultIcon.Source = bmp;
        }
        catch
        {
            // 아이콘 없어도 텍스트는 표시됨
        }
        RefreshIconCell(res.Dex);
    }

    private async void OnHatch(object sender, RoutedEventArgs e) => await HatchOneAsync();

    /// <summary>테스트용: 알 1개 지급 후 바로 부화.</summary>
    private async void OnDebugEgg(object sender, RoutedEventArgs e)
    {
        _settings.Eggs++;
        await HatchOneAsync();
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

    private void UpdateOwnedCount() => OwnedCount.Text = $"보유 {_settings.Owned.Count}/{PokemonIcons.Generations[^1].Last}";

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

    // ---- 창 ----

    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (!_placed) return;
        // 스프라이트 교체로 크기가 바뀌어도 발 위치(하단 중앙) 고정
        if (e.HeightChanged) Top += e.PreviousSize.Height - e.NewSize.Height;
        if (e.WidthChanged) Left += (e.PreviousSize.Width - e.NewSize.Width) / 2;
    }

    private void OnDrag(object sender, MouseButtonEventArgs e) => DragMove();

    private void OnExit(object sender, RoutedEventArgs e) => Application.Current.Shutdown();
}
