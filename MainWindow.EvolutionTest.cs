#if DEBUG
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;

namespace DeskPokemon;

public partial class MainWindow
{
    private readonly bool _testMode = App.EvolutionTestMode;
    private Window? _testPanel;
    private bool _switchingTestMode;

    private sealed record TestEggOption(int Dex)
    {
        public override string ToString() => $"#{EvolutionData.NationalDex(Dex)} {PokemonNames.Of(Dex)}" +
            (EvolutionData.Form(Dex) is null ? "" : " · 지역 모습");
    }

    private void SetupEvolutionTestTools()
    {
        var mode = new MenuItem { Header = _testMode ? "일반 모드로 돌아가기" : "진화 테스트 모드" };
        mode.Click += async (_, _) => await RestartForTestModeAsync(!_testMode);
        ContextMenu!.Items.Insert(ContextMenu.Items.Count - 1, mode);
        Closed += (_, _) => _testPanel?.Close();
        if (!_testMode) return;

        Title = "DeskPokemon · 진화 테스트";
        Root.Children.Insert(0, new TextBlock
        {
            Name = "EvolutionTestBanner", Text = "진화 테스트 모드 · 별도 세이브",
            HorizontalAlignment = HorizontalAlignment.Center, FontSize = 11,
            Foreground = Brushes.LightGreen, Background = Brushes.Black,
            Padding = new Thickness(6, 2),
        });
        var open = new MenuItem { Header = "진화 테스트 패널 열기" };
        open.Click += (_, _) => ShowEvolutionTestPanel();
        ContextMenu.Items.Insert(ContextMenu.Items.Count - 1, open);
    }

    private async Task RestartForTestModeAsync(bool testMode, bool reset = false)
    {
        if (_closed || _switchingTestMode) return;
        _switchingTestMode = true;
        try
        {
            if (!TrySaveSettings()) return;
            await App.SwitchEvolutionTestModeAsync(this, testMode, reset);
        }
        finally { _switchingTestMode = false; }
    }

    internal async Task ResetEvolutionTestSaveAsync()
    {
        if (!_testMode || _closed) return;
        if (await AppDialog.ConfirmAsync(this, "진화 테스트 초기화", "진화 테스트용 세이브만 초기화할까요? 일반 세이브는 유지됩니다."))
            await RestartForTestModeAsync(true, reset: true);
    }

    private static bool HasBranchDescendant(int dex)
    {
        var seen = new HashSet<int>();
        var pending = new Stack<int>();
        pending.Push(dex);
        while (pending.TryPop(out var current))
        {
            if (!seen.Add(current)) continue;
            if (EvolutionData.IsBranch(current)) return true;
            foreach (var rule in EvolutionData.From(current)) pending.Push(rule.ToId);
        }
        return false;
    }

    private void ShowEvolutionTestPanel()
    {
        if (!_testMode || _closed) return;
        if (_testPanel is { } existing) { existing.Activate(); return; }

        var root = new StackPanel { Margin = new Thickness(14) };
        var category = new ComboBox
        {
            Name = "TestCategory", Margin = new Thickness(0, 4, 0, 6), HorizontalAlignment = HorizontalAlignment.Stretch,
            ItemsSource = new[] { "분기 계열", "지역 모습", "전체 알 후보" }, SelectedIndex = 0,
        };
        var search = new TextBox { Name = "TestSearch", Margin = new Thickness(0, 0, 0, 6), Watermark = "이름 또는 도감 번호 검색" };
        UiToolTips.Set(search, "이름 또는 도감 번호 검색");
        var species = new ComboBox
        {
            Name = "TestSpecies", Margin = new Thickness(0, 0, 0, 6), MaxDropDownHeight = 300,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        var shiny = new CheckBox { Name = "TestShiny", Content = "이로치", Margin = new Thickness(0, 0, 0, 8) };
        static Button MakeButton(string name, string label, double bottom = 4) => new()
        {
            Name = name, Content = label, Margin = new Thickness(0, 0, 0, bottom), Padding = new Thickness(6),
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        var prepare = MakeButton("TestPrepareEgg", "지정 알 준비");
        var repeat = MakeButton("TestRepeatEgg", "선택 중인 계열의 기본형 다시 준비", 12);
        var status = new TextBlock { Name = "TestStatus", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) };
        var jump = MakeButton("TestJumpLevel", "다음 진화 레벨로 이동");
        var previous = MakeButton("TestPreviousForm", "이전 모습 선택", 0);

        root.Children.Add(new TextBlock { Text = "알로 획득", FontWeight = FontWeight.Bold });
        root.Children.Add(category);
        root.Children.Add(search);
        root.Children.Add(species);
        root.Children.Add(shiny);
        root.Children.Add(prepare);
        root.Children.Add(repeat);
        root.Children.Add(new TextBlock
        {
            Text = "알을 준비한 뒤 게임 화면의 알을 누르세요.", TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 14),
        });
        root.Children.Add(new TextBlock { Text = "선택 중인 육성", FontWeight = FontWeight.Bold });
        root.Children.Add(status);
        root.Children.Add(jump);
        root.Children.Add(previous);

        var options = EvolutionData.EggPool.Select(dex => new TestEggOption(dex)).ToArray();
        void Refresh()
        {
            if (_closed) return;
            var dex = _settings.SelectedDex;
            var color = _settings.SelectedShiny;
            var p = _settings.For(dex, color);
            var next = _settings.NextTestEvolutionLevel(dex, color);
            status.Text = $"{PokemonNames.Of(dex)}{(color ? " ★" : "")} · Lv.{p.Level} ({p.Exp}/{Settings.ExpToNext(p.Level)})\n" +
                $"현재 단계: {PokemonNames.Of(p.CurrentDex)} · 다음: {(next is null ? "없음" : $"Lv.{next}")}";
            var busy = _evolving || _eggState is EggState.Hatching or EggState.Result;
            prepare.IsEnabled = !busy && species.SelectedItem is TestEggOption;
            repeat.IsEnabled = !busy && p.History.Count > 0 && EvolutionData.EggPool.Contains(p.History[0]);
            jump.IsEnabled = !busy && next is not null;
            previous.IsEnabled = !busy && p.History.Count > 1;
        }
        void Filter()
        {
            var selected = (species.SelectedItem as TestEggOption)?.Dex;
            var term = search.Text?.Trim() ?? "";
            var filtered = options.Where(o =>
                (category.SelectedIndex switch
                {
                    0 => HasBranchDescendant(o.Dex),
                    1 => EvolutionData.Form(o.Dex) is not null,
                    _ => true,
                }) && (term.Length == 0 || o.ToString().Contains(term, StringComparison.CurrentCultureIgnoreCase)))
                .ToArray();
            species.ItemsSource = filtered;
            species.SelectedItem = filtered.FirstOrDefault(o => o.Dex == selected) ?? filtered.FirstOrDefault();
            Refresh();
        }
        category.SelectionChanged += (_, _) => Filter();
        search.TextChanged += (_, _) => Filter();
        species.SelectionChanged += (_, _) => Refresh();
        Filter();

        async Task PrepareAsync(int dex, bool isShiny)
        {
            if (_closed || _evolving || _eggState is EggState.Hatching or EggState.Result) return;
            try
            {
                _settings.GrantTestEgg(dex, isShiny);
                _lastEggTick = DateTime.UtcNow;
                SetEggState(EggState.Ready);
                Refresh();
            }
            catch (Exception ex) { if (!_closed) await AppDialog.ShowAsync(this, $"알 준비 실패: {ex.Message}", "진화 테스트"); }
        }
        prepare.Click += async (_, _) =>
        {
            if (species.SelectedItem is TestEggOption choice) await PrepareAsync(choice.Dex, shiny.IsChecked == true);
        };
        repeat.Click += async (_, _) =>
        {
            var color = _settings.SelectedShiny;
            var history = _settings.For(_settings.SelectedDex, color).History;
            if (history.Count > 0) await PrepareAsync(history[0], color);
        };
        jump.Click += async (_, _) =>
        {
            if (_closed || _evolving || _eggState is EggState.Hatching or EggState.Result) return;
            try
            {
                var dex = _settings.SelectedDex;
                var color = _settings.SelectedShiny;
                if (_settings.JumpToNextTestEvolutionLevel(dex, color) is null) return;
                UpdateLevelUi();
                RefreshIconCell(dex, color);
                RefreshEvolutionUi();
                Refresh();
                await CheckEvolutionAsync();
                if (!_closed) Refresh();
            }
            catch (Exception ex) { if (!_closed) await AppDialog.ShowAsync(this, $"레벨 이동 실패: {ex.Message}", "진화 테스트"); }
        };
        previous.Click += async (_, _) =>
        {
            if (_closed || _evolving || _eggState is EggState.Hatching or EggState.Result) return;
            var color = _settings.SelectedShiny;
            var history = _settings.For(_settings.SelectedDex, color).History;
            if (history.Count < 2) return;
            var before = history[^2];
            ShinyDex.IsChecked = color;
            SelectGenTab(PokemonIcons.GenOf(before));
            await SelectPokemonAsync(new PokemonChoice(before, color));
            if (!_closed) Refresh();
        };

        var panel = new Window
        {
            Title = "진화 테스트 도구 · 별도 세이브",
            SystemDecorations = SystemDecorations.None, Background = Brush.Parse("#F3F0E1"),
            Content = new Border
            {
                BorderThickness = new Thickness(2), BorderBrush = Brush.Parse("#0C1D36"),
                Child = new Grid { RowDefinitions = new RowDefinitions("Auto,*") },
            },
            Width = 365, Height = 500, MinWidth = 320, MinHeight = 340,
            ShowInTaskbar = true, Topmost = true, CanResize = true,
            WindowStartupLocation = WindowStartupLocation.Manual,
        };
        RenderOptions.SetTextRenderingMode(panel, TextRenderingMode.Antialias);
        RenderOptions.SetBitmapInterpolationMode(panel, BitmapInterpolationMode.None);
        var layout = (Grid)((Border)panel.Content).Child!;
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Background = Brush.Parse("#ECD385") };
        var heading = new TextBlock { Text = panel.Title, Margin = new Thickness(10, 8), FontSize = 11, VerticalAlignment = VerticalAlignment.Center };
        header.Children.Add(heading);
        var close = MakeButton("TestClosePanel", "닫기", 0);
        Grid.SetColumn(close, 1);
        close.Click += (_, _) => panel.Close();
        header.Children.Add(close);
        heading.PointerPressed += (_, e) =>
        {
            if (e.GetCurrentPoint(heading).Properties.IsLeftButtonPressed)
                panel.BeginMoveDrag(e);
        };
        layout.Children.Add(header);
        var scroll = new ScrollViewer { Content = root };
        Grid.SetRow(scroll, 1);
        layout.Children.Add(scroll);
        var area = WorkingArea;
        panel.Position = new PixelPoint(Math.Max(area.X, Position.X - (int)Math.Ceiling(380 * DesktopScaling)),
            Math.Max(area.Y, Position.Y));
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        timer.Tick += (_, _) => Refresh();
        panel.Closed += (_, _) => { timer.Stop(); _testPanel = null; };
        _testPanel = panel;
        Refresh();
        if (IsVisible) panel.Show(this); else panel.Show();
        timer.Start();
    }
}
#endif
