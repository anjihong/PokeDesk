using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace DeskPokemon;

public partial class App : Application
{
#if DEBUG
    internal static bool EvolutionTestMode { get; set; }
    internal static string EvolutionTestSavePath => Path.Combine(AppPaths.DataDirectory, "evolution-test.json");
#endif
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode = Avalonia.Controls.ShutdownMode.OnMainWindowClose;
            try
            {
#if DEBUG
                EvolutionTestMode = desktop.Args?.Contains("--evolution-test") == true;
                ShowStartup(desktop, LoadStartupSettings(EvolutionTestMode));
#else
                ShowStartup(desktop, Settings.Load());
#endif
            }
            catch (Exception ex)
            {
                desktop.MainWindow = AppDialog.StartupError($"세이브를 불러오거나 변환하지 못했습니다. 원본 파일은 유지됩니다.\n{ex.Message}");
            }
        }
        base.OnFrameworkInitializationCompleted();
    }

    private static void ShowStartup(IClassicDesktopStyleApplicationLifetime desktop, Settings? settings)
    {
        if (settings != null)
        {
            desktop.MainWindow = new MainWindow(settings);
            return;
        }

        var picker = new StarterWindow();
        desktop.MainWindow = picker;
        picker.Selected += async dex =>
        {
            Settings next;
            try { next = Settings.New(dex); }
            catch (Exception ex)
            {
                await AppDialog.ShowAsync(picker, $"새 게임을 저장하지 못했습니다.\n{ex.Message}", "저장 실패");
                return;
            }
            var main = new MainWindow(next);
            // Transfer ownership before closing the picker, or the desktop lifetime shuts down.
            desktop.MainWindow = main;
            main.Show();
            picker.Close();
        };
    }

    internal static void ResetSave()
    {
        if (Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop) return;
        var previous = desktop.MainWindow;
#if DEBUG
        if (EvolutionTestMode)
        {
            if (previous is MainWindow testWindow) _ = testWindow.ResetEvolutionTestSaveAsync();
            return;
        }
#endif
        try { Settings.Delete(); }
        catch (Exception ex)
        {
            if (previous != null) _ = AppDialog.ShowAsync(previous, $"세이브를 삭제하지 못했습니다.\n{ex.Message}", "초기화 실패");
            return;
        }
        if (previous is MainWindow main) main.DiscardSaveOnClose();
        ShowStartup(desktop, null);
        desktop.MainWindow!.Show();
        previous?.Close();
    }

#if DEBUG
    internal static Settings? LoadStartupSettings(bool testMode, string? testSavePath = null) => testMode
        ? Settings.LoadFrom(testSavePath ?? EvolutionTestSavePath) ?? Settings.NewAt(4, testSavePath ?? EvolutionTestSavePath)
        : Settings.Load();

    // Recreate the window instead of spawning a process: both .app and dotnet launchers
    // preserve their desktop lifetime, and a canceled save keeps the current window open.
    internal static async Task SwitchEvolutionTestModeAsync(MainWindow previous, bool testMode, bool reset = false)
    {
        if (Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop) return;
        var previousMode = EvolutionTestMode;
        var previousMain = desktop.MainWindow;
        try
        {
            if (reset)
            {
                if (!testMode) throw new InvalidOperationException("Only the evolution test save can be reset here.");
                Settings.NewAt(4, EvolutionTestSavePath); // atomic replacement; preserve the old file if writing fails
            }
            var next = LoadStartupSettings(testMode);
            EvolutionTestMode = testMode;
            ShowStartup(desktop, next);
            desktop.MainWindow!.Show();
        }
        catch (Exception ex)
        {
            EvolutionTestMode = previousMode;
            if (desktop.MainWindow != previousMain)
            {
                var failed = desktop.MainWindow;
                desktop.MainWindow = previousMain;
                if (failed is MainWindow failedMain) failedMain.DiscardSaveOnClose();
                failed?.Close();
            }
            await AppDialog.ShowAsync(previous, $"모드를 전환하지 못했습니다.\n{ex.Message}", "진화 테스트");
            return;
        }
        previous.DiscardSaveOnClose();
        previous.Close();
    }
#endif
}
