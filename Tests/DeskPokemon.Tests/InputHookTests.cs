using DeskPokemon.Platform;
using Xunit;

namespace DeskPokemon.Tests;

public class InputHookTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    [Fact]
    public void KeyRepeatIsIgnoredUntilReleaseAndDifferentKeysRemainIndependent()
    {
        var keys = new PressedKeyTracker();

        Assert.True(keys.KeyDown(42));
        Assert.False(keys.KeyDown(42));
        Assert.True(keys.KeyDown(43));
        keys.KeyUp(42);
        Assert.True(keys.KeyDown(42));
        Assert.False(keys.KeyDown(43));
        keys.Clear();
        Assert.True(keys.KeyDown(43));
    }

    [Fact]
    public void MacModifiersCountPressesIncludingBothShiftKeysCapsLockAndFn()
    {
        var modifiers = new MacModifierTracker();

        Assert.True(modifiers.FlagsChanged(0x20002)); // Left Shift down.
        Assert.False(modifiers.FlagsChanged(0x20002));
        Assert.True(modifiers.FlagsChanged(0x20006)); // Right Shift down while left is held.
        Assert.False(modifiers.FlagsChanged(0x20004)); // Left Shift up.
        Assert.False(modifiers.FlagsChanged(0)); // Right Shift up.
        Assert.True(modifiers.FlagsChanged(0x10000)); // Caps Lock on.
        Assert.True(modifiers.FlagsChanged(0)); // Caps Lock off is another press.
        Assert.True(modifiers.FlagsChanged(0x800000)); // Fn down.
        Assert.False(modifiers.FlagsChanged(0)); // Fn up.

        modifiers.Reset(0x20002); // A modifier was already held when monitoring began.
        Assert.False(modifiers.FlagsChanged(0));
    }

    [Fact]
    public async Task FailedNativeSetupCanRetryAndDisposeReleasesTheNativeLoop()
    {
        var failed = Signal();
        var active = Signal();
        var cleanedUp = Signal();
        using var stopNativeLoop = new ManualResetEventSlim();
        using var monitor = new ScriptedMonitor(self =>
        {
            if (self.Attempts == 1) throw new InvalidOperationException("Native setup failed.");
            try
            {
                self.Report(InputHookStatus.Active);
                stopNativeLoop.Wait(Timeout);
            }
            finally { cleanedUp.TrySetResult(); }
        }, stopNativeLoop.Set);
        monitor.StatusChanged += () =>
        {
            if (monitor.Status == InputHookStatus.Unavailable) failed.TrySetResult();
            if (monitor.Status == InputHookStatus.Active) active.TrySetResult();
        };

        monitor.Start();
        await failed.Task.WaitAsync(Timeout);
        Assert.Equal(InputHookStatus.Unavailable, monitor.Status);
        monitor.Retry();
        await active.Task.WaitAsync(Timeout);
        Assert.Equal(2, monitor.Attempts);

        monitor.Dispose();
        await cleanedUp.Task.WaitAsync(Timeout);
        Assert.Equal(InputHookStatus.Disposed, monitor.Status);
        monitor.Retry();
        monitor.Start();
        monitor.Dispose();
        Assert.Equal(2, monitor.Attempts);
    }

    [Fact]
    public void DisposeBeforeStartPreventsNativeSetup()
    {
        using var monitor = new ScriptedMonitor(_ => throw new Exception("Must never run."));

        monitor.Dispose();
        monitor.Start();
        monitor.Retry();

        Assert.Equal(InputHookStatus.Disposed, monitor.Status);
        Assert.Equal(0, monitor.Attempts);
    }

    [Fact]
    public async Task DisposingInsideAStatusCallbackDoesNotDeadlockOrPublishMoreInput()
    {
        var cleanedUp = Signal();
        int triggers = 0;
        using var monitor = new ScriptedMonitor(self =>
        {
            self.Report(InputHookStatus.Active);
            self.Trigger();
            cleanedUp.TrySetResult();
        });
        monitor.StatusChanged += () =>
        {
            if (monitor.Status == InputHookStatus.Active) monitor.Dispose();
        };
        monitor.Triggered += () => Interlocked.Increment(ref triggers);

        monitor.Start();
        await cleanedUp.Task.WaitAsync(Timeout);

        Assert.Equal(InputHookStatus.Disposed, monitor.Status);
        Assert.Equal(0, Volatile.Read(ref triggers));
    }

    [Fact]
    public async Task DisposeDuringNativeSetupKeepsDisposedStatusAndSuppressesInput()
    {
        var setupStarted = Signal();
        var disposeStarted = Signal();
        using var finishSetup = new ManualResetEventSlim();
        int triggers = 0;
        using var monitor = new ScriptedMonitor(self =>
        {
            setupStarted.TrySetResult();
            finishSetup.Wait(Timeout);
            self.Report(InputHookStatus.Active);
            self.Trigger();
        }, () => disposeStarted.TrySetResult());
        monitor.Triggered += () => Interlocked.Increment(ref triggers);

        monitor.Start();
        await setupStarted.Task.WaitAsync(Timeout);
        var disposing = Task.Run(monitor.Dispose);
        await disposeStarted.Task.WaitAsync(Timeout);
        finishSetup.Set();
        await disposing.WaitAsync(Timeout);

        Assert.Equal(InputHookStatus.Disposed, monitor.Status);
        Assert.Equal(0, Volatile.Read(ref triggers));
    }

    [Fact]
    public async Task UnexpectedNativeLoopExitDoesNotLeaveAnActiveStatus()
    {
        var stopped = Signal();
        using var monitor = new ScriptedMonitor(self => self.Report(InputHookStatus.Active));
        monitor.StatusChanged += () =>
        {
            if (monitor.Status == InputHookStatus.Unavailable) stopped.TrySetResult();
        };

        monitor.Start();
        await stopped.Task.WaitAsync(Timeout);

        Assert.Equal(InputHookStatus.Unavailable, monitor.Status);
    }

    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private sealed class ScriptedMonitor(Action<ScriptedMonitor> run, Action? wake = null) : NativeInputMonitor
    {
        private int _attempts;
        public int Attempts => Volatile.Read(ref _attempts);
        public void Report(InputHookStatus status) => SetStatus(status, status.ToString());
        public void Trigger() => PublishTrigger();
        protected override void WakeNativeLoop() => wake?.Invoke();
        protected override void RunMonitor()
        {
            Interlocked.Increment(ref _attempts);
            run(this);
        }
    }
}
