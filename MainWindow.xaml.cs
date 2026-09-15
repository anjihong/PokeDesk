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

    // ---- 선택 패널 ----

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

        var (_, first, last) = PokemonIcons.Generations[gen - 1];
        var style = (Style)Resources["IconButton"];
        IconGrid.Children.Clear();
        for (var dex = first; dex <= last; dex++)
        {
            if (!icons.TryGetValue(dex, out var bmp)) continue;
            var rb = new RadioButton
            {
                // 40x30 캔버스에 원본 크기로 중앙 배치
                Content = new Image { Source = bmp, Width = 40, Height = 30, Stretch = Stretch.None },
                Tag = dex,
                GroupName = "Icon",
                Style = style,
                ToolTip = $"#{dex}",
                IsChecked = dex == _settings.SelectedDex, // 핸들러 연결 전에 설정해 재선택 방지
            };
            rb.Checked += OnIconChecked;
            IconGrid.Children.Add(rb);
        }
        IconScroll.ScrollToTop();
    }

    private async void OnIconChecked(object sender, RoutedEventArgs e)
    {
        var dex = (int)((RadioButton)sender).Tag;
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
            foreach (RadioButton rb in IconGrid.Children)
                if ((int)rb.Tag == prev) rb.IsChecked = true;
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
