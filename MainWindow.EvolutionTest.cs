#if DEBUG
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace DeskPokemon;

public partial class MainWindow
{
    private sealed record TestEggOption(int Dex)
    {
        public override string ToString() => $"#{EvolutionData.NationalDex(Dex)} {PokemonNames.Of(Dex)}" +
            (EvolutionData.Form(Dex) is null ? "" : " · 지역 모습");
    }

    private Window? _testPanel;

    private void SetupEvolutionTestTools()
    {
        var mode = new MenuItem { Header = _testMode ? "일반 모드로 돌아가기" : "진화 테스트 모드" };
        mode.Click += (_, _) => RestartForTestMode(!_testMode);
        ContextMenu!.Items.Insert(ContextMenu.Items.Count - 1, mode);
        if (!_testMode) return;

        Root.Children.Insert(0, new TextBlock
        {
            Text = "진화 테스트 모드 · 별도 세이브",
            HorizontalAlignment = HorizontalAlignment.Center,
            Foreground = Brushes.LightGreen,
            Background = Brushes.Black,
            Padding = new Thickness(6, 2, 6, 2)
        });
        var open = new MenuItem { Header = "진화 테스트 패널 열기" };
        open.Click += (_, _) => ShowEvolutionTestPanel();
        ContextMenu.Items.Insert(ContextMenu.Items.Count - 1, open);
    }

    private void RestartForTestMode(bool testMode, bool reset = false)
    {
        void Relaunch(object? sender, EventArgs args)
        {
            Closed -= Relaunch;
            try
            {
                if (reset) File.Delete(App.EvolutionTestSavePath);
                Process.Start(new ProcessStartInfo(Environment.ProcessPath!)
                {
                    Arguments = testMode ? "--evolution-test" : "",
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"모드를 전환하지 못했습니다.\n{ex.Message}", "DeskPokemon");
            }
        }
        Closed += Relaunch;
        Close();
        if (!_closed) Closed -= Relaunch; // 저장 실패로 종료가 취소됨
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
        if (!_testMode) return;
        if (_testPanel is { } existing) { existing.Activate(); return; }

        var root = new StackPanel { Margin = new Thickness(14) };
        var category = new ComboBox { Margin = new Thickness(0, 4, 0, 6), ItemsSource = new[] { "분기 계열", "지역 모습", "전체 알 후보" }, SelectedIndex = 0 };
        var search = new TextBox { Margin = new Thickness(0, 0, 0, 6), ToolTip = "이름 또는 도감 번호 검색" };
        var species = new ComboBox { Margin = new Thickness(0, 0, 0, 6), MaxDropDownHeight = 300 };
        var shiny = new CheckBox { Content = "이로치", Margin = new Thickness(0, 0, 0, 8) };
        var prepare = new Button { Content = "지정 알 준비", Margin = new Thickness(0, 0, 0, 4), Padding = new Thickness(6) };
        var repeat = new Button { Content = "선택 중인 계열의 기본형 다시 준비", Margin = new Thickness(0, 0, 0, 12), Padding = new Thickness(6) };
        var status = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) };
        var jump = new Button { Content = "다음 진화 레벨로 이동", Margin = new Thickness(0, 0, 0, 4), Padding = new Thickness(6) };
        var previous = new Button { Content = "이전 모습 선택", Padding = new Thickness(6) };

        root.Children.Add(new TextBlock { Text = "알로 획득", FontWeight = FontWeights.Bold });
        root.Children.Add(category);
        root.Children.Add(search);
        root.Children.Add(species);
        root.Children.Add(shiny);
        root.Children.Add(prepare);
        root.Children.Add(repeat);
        root.Children.Add(new TextBlock { Text = "알을 준비한 뒤 게임 화면의 알을 누르세요.", Margin = new Thickness(0, 0, 0, 14) });
        root.Children.Add(new TextBlock { Text = "선택 중인 육성", FontWeight = FontWeights.Bold });
        root.Children.Add(status);
        root.Children.Add(jump);
        root.Children.Add(previous);

        var options = EvolutionData.EggPool.Select(dex => new TestEggOption(dex)).ToArray();
        void Filter()
        {
            var selected = (species.SelectedItem as TestEggOption)?.Dex;
            var term = search.Text.Trim();
            var filtered = options.Where(o =>
                (category.SelectedIndex switch
                {
                    0 => HasBranchDescendant(o.Dex),
                    1 => EvolutionData.Form(o.Dex) is not null,
                    _ => true
                }) && (term.Length == 0 || o.ToString().Contains(term, StringComparison.CurrentCultureIgnoreCase)))
                .ToArray();
            species.ItemsSource = filtered;
            species.SelectedItem = filtered.FirstOrDefault(o => o.Dex == selected) ?? filtered.FirstOrDefault();
        }
        category.SelectionChanged += (_, _) => Filter();
        search.TextChanged += (_, _) => Filter();
        Filter();

        void Refresh()
        {
            if (_closed) return;
            var dex = _settings.SelectedDex;
            var color = _settings.SelectedShiny;
            var p = _settings.For(dex, color);
            var next = _settings.NextTestEvolutionLevel(dex, color);
            status.Text = $"{PokemonNames.Of(dex)}{(color ? " ★" : "")} · Lv.{p.Level} ({p.Exp}/{Settings.ExpToNext(p.Level)})\n" +
                $"현재 단계: {PokemonNames.Of(p.CurrentDex)} · 다음: {(next is null ? "없음" : $"Lv.{next}")}";
            var eggBusy = _eggState is EggState.Hatching or EggState.Result;
            prepare.IsEnabled = !eggBusy && species.SelectedItem is TestEggOption;
            repeat.IsEnabled = !eggBusy && EvolutionData.EggPool.Contains(p.History[0]);
            jump.IsEnabled = !_evolving && next is not null;
            previous.IsEnabled = !_evolving && p.History.Count > 1;
        }

        void Prepare(int dex, bool isShiny)
        {
            if (_eggState is EggState.Hatching or EggState.Result) return;
            try
            {
                _settings.GrantTestEgg(dex, isShiny);
                _lastEggTick = DateTime.UtcNow;
                SetEggState(EggState.Ready);
                Refresh();
            }
            catch (Exception ex) { MessageBox.Show($"알 준비 실패: {ex.Message}", "진화 테스트"); }
        }
        prepare.Click += (_, _) =>
        {
            if (species.SelectedItem is TestEggOption choice) Prepare(choice.Dex, shiny.IsChecked == true);
        };
        repeat.Click += (_, _) =>
        {
            var dex = _settings.SelectedDex;
            var color = _settings.SelectedShiny;
            Prepare(_settings.For(dex, color).History[0], color);
        };
        jump.Click += async (_, _) =>
        {
            if (_evolving) return;
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
            }
            catch (Exception ex) { MessageBox.Show($"레벨 이동 실패: {ex.Message}", "진화 테스트"); }
        };
        previous.Click += async (_, _) =>
        {
            if (_evolving) return;
            var dex = _settings.SelectedDex;
            var color = _settings.SelectedShiny;
            var history = _settings.For(dex, color).History;
            if (history.Count < 2) return;
            var before = history[^2];
            ShinyDex.IsChecked = color;
            SelectGenTab(PokemonIcons.GenOf(before));
            await SelectPokemonAsync(new PokemonChoice(before, color));
            Refresh();
        };

        var panel = new Window
        {
            Title = "진화 테스트 도구 · 별도 세이브", Content = new ScrollViewer { Content = root },
            Width = 365, Height = 465, MinWidth = 320, MinHeight = 340,
            ShowInTaskbar = true, Topmost = true,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = Math.Max(SystemParameters.WorkArea.Left, Left - 380), Top = Math.Max(SystemParameters.WorkArea.Top, Top)
        };
        if (IsVisible) panel.Owner = this;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        timer.Tick += (_, _) => Refresh();
        panel.Closed += (_, _) => { timer.Stop(); _testPanel = null; };
        _testPanel = panel;
        Refresh();
        panel.Show();
        timer.Start();
    }
}
#endif
