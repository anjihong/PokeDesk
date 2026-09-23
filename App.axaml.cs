using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace DeskPokemon;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode = Avalonia.Controls.ShutdownMode.OnMainWindowClose;
            try { ShowStartup(desktop, Settings.Load()); }
            catch (Exception ex)
            {
                desktop.MainWindow = AppDialog.StartupError($"세이브를 불러오거나 변환하지 못했습니다.\n{ex.Message}");
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
}
