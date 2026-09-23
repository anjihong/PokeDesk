using System.Windows;

namespace DeskPokemon;

public partial class App : Application
{
    /// <summary>세이브가 없으면 스타팅 선택 창부터. 선택 창을 닫으면 종료.</summary>
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown; // 선택 창이 닫혀도 바로 종료되지 않게

        Settings? settings;
        try { settings = Settings.Load(); }
        catch (Exception ex)
        {
            MessageBox.Show($"세이브를 불러오거나 변환하지 못했습니다. 원본 파일은 유지됩니다.\n{ex.Message}", "DeskPokemon");
            Shutdown();
            return;
        }
        if (settings == null)
        {
            var picker = new StarterWindow();
            if (picker.ShowDialog() != true)
            {
                Shutdown();
                return;
            }
            try { settings = Settings.New(picker.SelectedDex); }
            catch (Exception ex)
            {
                MessageBox.Show($"새 게임을 저장하지 못했습니다.\n{ex.Message}", "DeskPokemon");
                Shutdown();
                return;
            }
        }

        MainWindow = new MainWindow(settings);
        ShutdownMode = ShutdownMode.OnMainWindowClose;
        MainWindow.Show();
    }
}
