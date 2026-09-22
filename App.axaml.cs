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
            ShowStartup(desktop, Settings.Load());
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
        picker.Selected += dex =>
        {
            var next = Settings.New(dex);
            next.Save();
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
        if (previous is MainWindow main) main.DiscardSaveOnClose();
        Settings.Delete();
        ShowStartup(desktop, null);
        desktop.MainWindow!.Show();
        previous?.Close();
    }
}
