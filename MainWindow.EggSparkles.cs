using Avalonia.Threading;

namespace DeskPokemon;

public partial class MainWindow
{
    private SpriteFrame[]? _eggSparkleFrames;
    private int _eggSparkleFrame;
    private DispatcherTimer? _eggSparkleTimer;

    private bool CanShowEggSparkles => !_closed && _settings.PendingEgg?.Kind == EggKind.Shiny &&
        _eggState is EggState.Waiting or EggState.Ready;

    private void StartEggSparkles(SpriteFrame[] frames)
    {
        if (!CanShowEggSparkles || frames.Length == 0) return;
        if (_eggSparkleTimer is null)
        {
            _eggSparkleTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
            _eggSparkleTimer.Tick += OnEggSparkleTick;
            Closed += OnEggSparklesClosed;
        }
        _eggSparkleFrames = frames; // 공유 캐시 소유: 창은 참조만 보관한다.
        _eggSparkleFrame = 0;
        EggSparkles.Source = frames[0].Bitmap;
        EggSparkles.IsVisible = true;
        _eggSparkleTimer.Start();
    }

    private void OnEggSparkleTick(object? sender, EventArgs e) => AdvanceEggSparkles();

    private void AdvanceEggSparkles()
    {
        if (!CanShowEggSparkles)
        {
            ClearEggSparkles();
            return;
        }
        if (_eggSparkleFrames is not { Length: > 0 } frames) return;
        _eggSparkleFrame = (_eggSparkleFrame + 1) % frames.Length;
        EggSparkles.Source = frames[_eggSparkleFrame].Bitmap;
    }

    private void ClearEggSparkles()
    {
        _eggSparkleTimer?.Stop();
        _eggSparkleFrames = null;
        _eggSparkleFrame = 0;
        EggSparkles.Source = null;
        EggSparkles.IsVisible = false;
    }

    private void OnEggSparklesClosed(object? sender, EventArgs e)
    {
        ClearEggSparkles();
        if (_eggSparkleTimer is not null) _eggSparkleTimer.Tick -= OnEggSparkleTick;
        _eggSparkleTimer = null;
        Closed -= OnEggSparklesClosed;
    }
}
