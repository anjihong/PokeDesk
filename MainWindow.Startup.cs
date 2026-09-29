using Avalonia;

namespace DeskPokemon;

public partial class MainWindow
{
    private Task _eggArtLoad = Task.CompletedTask;
    private bool _startupCanEvolve;
    private readonly TaskCompletionSource _startupPresentation = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal Task StartupPresentation => _startupPresentation.Task;

    private bool IsEvolutionTestMode
    {
        get
        {
#if DEBUG
            return _testMode;
#else
            return false;
#endif
        }
    }

    private void InitializeStartupPresentation()
    {
        _startupCanEvolve = !_startServices;
        if (_startServices) Opacity = 0;
        Closed += (_, _) => _startupPresentation.TrySetCanceled();
    }

    private async Task CompleteStartupPresentationAsync()
    {
        try
        {
            SelectGenTab(PokemonIcons.GenOf(_settings.SelectedDex));
            ApplyPresentation();
            RefreshEvolutionUi();
            // Inputs may level the current growth run while egg artwork is loading.
            // Keep evolution deferred until both startup assets are ready, then await
            // its complete sequence before exposing the window at its final size.
            await _eggArtLoad.WaitAsync(_lifetime.Token);
            if (_closed) return;
            _startupCanEvolve = true;
            await CheckEvolutionAsync();
            if (_closed) return;

            UpdateLayout();
            var wa = WorkingArea;
            Position = new PixelPoint(wa.Right - (int)Math.Ceiling(Bounds.Width * DesktopScaling) - 20,
                wa.Bottom - (int)Math.Ceiling(Bounds.Height * DesktopScaling) - 20);
            _placed = true;
            Opacity = 1;
            _startupPresentation.TrySetResult();
#if DEBUG
            if (_testMode) ShowEvolutionTestPanel();
#endif
        }
        catch (OperationCanceledException) when (_closed || _lifetime.IsCancellationRequested)
        {
            _startupPresentation.TrySetCanceled();
        }
    }
}
