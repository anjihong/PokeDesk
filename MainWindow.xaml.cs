using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace DeskPokemon;

public partial class MainWindow : Window
{
    private const int DexId = 4; // 파이리

    private readonly InputHook _hook = new();
    private SpriteAtlas? _atlas;
    private int _frame;

    public MainWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Closed += (_, _) => _hook.Dispose();
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            _atlas = await SpriteAtlas.LoadAsync(DexId);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"스프라이트 로드 실패: {ex.Message}", "DeskPokemon");
            Close();
            return;
        }

        Stage.Width = _atlas.Width;
        Stage.Height = _atlas.Height;
        ShowFrame(0);

        // PokeRogue와 동일: frameRate 10, 무한 반복
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        timer.Tick += (_, _) => ShowFrame((_frame + 1) % _atlas.Frames.Length);
        timer.Start();

        var bounce = (Storyboard)Resources["Bounce"];
        // 훅 콜백은 빨리 반환해야 하므로 애니메이션 시작은 큐에 넘김
        _hook.Triggered += () => Dispatcher.BeginInvoke(() => bounce.Begin(this, true));

        UpdateLayout();
        var wa = SystemParameters.WorkArea;
        Left = wa.Right - ActualWidth - 20;
        Top = wa.Bottom - ActualHeight - 20;
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

    private void OnDrag(object sender, MouseButtonEventArgs e) => DragMove();

    private void OnExit(object sender, RoutedEventArgs e) => Application.Current.Shutdown();
}
