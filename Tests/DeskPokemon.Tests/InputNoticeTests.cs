using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using DeskPokemon.Platform;
using Xunit;

namespace DeskPokemon.Tests;

public partial class UiTests
{
    [Theory]
    [InlineData("/Applications/DeskPokemon.app/Contents/MacOS/DeskPokemon", "/Applications/DeskPokemon.app")]
    [InlineData("/Users/test/새 빌드/DeskPokemon.app/Contents/MacOS/DeskPokemon", "/Users/test/새 빌드/DeskPokemon.app")]
    [InlineData("/usr/local/share/dotnet/dotnet", "/usr/local/share/dotnet/dotnet")]
    [InlineData("/tmp/DeskPokemon", "/tmp/DeskPokemon")]
    public void InputPermissionIdentityShowsTheRunningBundleOrActualHost(string executable, string expected) =>
        Assert.Equal(expected, MainWindow.InputApplicationPath(executable));

    [AvaloniaFact]
    public async Task InputNoticeSeparatesPromptFreeRetryFromExplicitPermissionAndHidesAfterConnection()
    {
        using var monitor = new NoticeMonitor();
        using var hook = new InputHook(monitor);
        var window = new MainWindow(Settings.NewPreview(4), false, null, hook);
        try
        {
            ShowAndLayout(window);
            await EventuallyAsync(window, () => hook.Status == InputHookStatus.PermissionRequired);
            Invoke(window, "RefreshInputStatus");
            Assert.True(window.FindControl<Border>("InputNotice")!.IsVisible);
            Assert.Contains("현재 실행 중인 앱", window.FindControl<TextBlock>("InputStatusText")!.Text);
            Assert.Equal(0, monitor.Requests);
            int before = monitor.Attempts;
            window.FindControl<Button>("InputRetryButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await EventuallyAsync(window, () => monitor.Attempts > before);
            Assert.Equal(0, monitor.Requests);

            window.FindControl<Button>("InputPermissionButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await EventuallyAsync(window, () => monitor.Requests == 1);
            monitor.Grant = true;
            window.FindControl<Button>("InputRetryButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await EventuallyAsync(window, () => !window.FindControl<Border>("InputNotice")!.IsVisible);
            Assert.True(hook.IsActive);
            Assert.Equal(1, monitor.Requests);
        }
        finally { window.Close(); }
        Assert.Equal(InputHookStatus.Disposed, hook.Status);
    }

    private sealed class NoticeMonitor : NativeInputMonitor
    {
        private readonly ManualResetEventSlim _stop = new();
        private int _attempts, _requests;
        public volatile bool Grant;
        public int Attempts => Volatile.Read(ref _attempts);
        public int Requests => Volatile.Read(ref _requests);
        public override void RequestPermissionAndRetry()
        {
            Interlocked.Increment(ref _requests);
            Retry();
        }
        protected override void WakeNativeLoop() => _stop.Set();
        protected override void RunMonitor()
        {
            Interlocked.Increment(ref _attempts);
            SetStatus(Grant ? InputHookStatus.Active : InputHookStatus.PermissionRequired, "입력 연결 테스트");
            if (Grant) _stop.Wait(TimeSpan.FromSeconds(5));
        }
    }
}
