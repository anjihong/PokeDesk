using System.Windows;

namespace DeskPokemon;

public partial class App : Application
{
    /// <summary>세이브가 없으면 스타팅 선택 창부터. 선택 창을 닫으면 종료.</summary>
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown; // 선택 창이 닫혀도 바로 종료되지 않게

        var settings = Settings.Load();
        if (settings == null)
        {
            var picker = new StarterWindow();
            if (picker.ShowDialog() != true)
            {
                Shutdown();
                return;
            }
            settings = Settings.New(picker.SelectedDex);
            settings.Save();
        }

        MainWindow = new MainWindow(settings);
        ShutdownMode = ShutdownMode.OnMainWindowClose;
        MainWindow.Show();
    }
}
