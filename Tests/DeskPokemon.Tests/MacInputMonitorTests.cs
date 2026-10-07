using System.Collections.Concurrent;
using System.Diagnostics;
using DeskPokemon.Platform;
using Xunit;

namespace DeskPokemon.Tests;

public class MacInputMonitorTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(20);

    [Theory]
    [InlineData((int)MacListenAccess.Denied)]
    [InlineData((int)MacListenAccess.Unknown)]
    public async Task APermissionChangeRecoversInTheSameProcessWithoutPromptingOrStartingFlicker(int denied)
    {
        using var backend = new FakeBackend { Permission = new(false, (MacListenAccess)denied) };
        using var monitor = new MacInputMonitor(backend, RetryDelay);
        var statuses = Observe(monitor);
        monitor.Start();
        await UntilAsync(() => backend.PermissionReads >= 3);
        Assert.Equal(InputHookStatus.PermissionRequired, monitor.Status);
        Assert.Empty(backend.Attempts);
        Assert.Equal(0, backend.PermissionRequests);
        monitor.Retry();
        backend.Permission = new(true, MacListenAccess.Unknown);
        await UntilAsync(() => monitor.Status == InputHookStatus.Active);
        Assert.Equal(new[] { MacInputTapKind.Keyboard, MacInputTapKind.ButtonsAndModifiers }, backend.Attempts.ToArray());
        Assert.Equal(0, backend.PermissionRequests);
        Assert.DoesNotContain(InputHookStatus.Starting, statuses);
    }

    [Theory]
    [InlineData(true, (int)MacListenAccess.Denied)]
    [InlineData(true, (int)MacListenAccess.Unknown)]
    [InlineData(false, (int)MacListenAccess.Granted)]
    public async Task EitherPermissionApiCanAuthorizeBothPassiveTaps(bool coreGraphics, int hid)
    {
        using var backend = new FakeBackend { Permission = new(coreGraphics, (MacListenAccess)hid) };
        using var monitor = new MacInputMonitor(backend, RetryDelay);
        monitor.Start();
        await UntilAsync(() => monitor.Status == InputHookStatus.Active);
        Assert.Equal(2, backend.Taps.Count);
        Assert.All(backend.Taps, tap => Assert.True(tap.IsValid && tap.IsEnabled));
        Assert.Equal(0, backend.PermissionRequests);
    }

    [Theory]
    [InlineData((int)MacInputTapKind.Keyboard)]
    [InlineData((int)MacInputTapKind.ButtonsAndModifiers)]
    public async Task APartialConnectionNeverClaimsActiveAndPermittedSetupFailureRetries(int failedTap)
    {
        var failingKind = (MacInputTapKind)failedTap;
        using var backend = new FakeBackend();
        var fail = 1;
        backend.CanCreate = kind => kind != failingKind || Volatile.Read(ref fail) == 0;
        using var monitor = new MacInputMonitor(backend, RetryDelay);
        var statuses = Observe(monitor);
        monitor.Start();
        await UntilAsync(() => backend.Attempts.Count(kind => kind == failingKind) >= 2);
        Assert.Equal(InputHookStatus.Unavailable, monitor.Status);
        Assert.DoesNotContain(InputHookStatus.Active, statuses);
        if (failingKind == MacInputTapKind.Keyboard)
            Assert.DoesNotContain(MacInputTapKind.ButtonsAndModifiers, backend.Attempts);
        else Assert.Equal(1, backend.Taps.First().Disposals); // The first partial attempt was released before retrying.

        Volatile.Write(ref fail, 0);
        await UntilAsync(() => monitor.Status == InputHookStatus.Active);
        Assert.Equal(2, backend.Taps.Count(tap => tap.IsValid && tap.IsEnabled));
        Assert.Equal(0, backend.PermissionRequests);
    }

    [Fact]
    public async Task OnlyAnExplicitPermissionActionCanPrompt()
    {
        using var backend = new FakeBackend { Permission = new(false, MacListenAccess.Denied) };
        using var monitor = new MacInputMonitor(backend, RetryDelay);
        monitor.Start();
        await UntilAsync(() => monitor.Status == InputHookStatus.PermissionRequired);
        monitor.Retry();
        var reads = backend.PermissionReads;
        await UntilAsync(() => backend.PermissionReads > reads);
        Assert.Equal(0, backend.PermissionRequests);
        backend.OnRequest = () => backend.Permission = new(false, MacListenAccess.Granted);
        monitor.RequestPermissionAndRetry();
        await UntilAsync(() => monitor.Status == InputHookStatus.Active);
        Assert.Equal(1, backend.PermissionRequests);
        monitor.RequestPermissionAndRetry();
        Assert.Equal(1, backend.PermissionRequests);
    }

    [Fact]
    public async Task HealthyTapsKeepRunningWhenPermissionQueriesBecomeStale()
    {
        using var backend = new FakeBackend();
        using var monitor = new MacInputMonitor(backend, RetryDelay);
        monitor.Start();
        await UntilAsync(() => monitor.Status == InputHookStatus.Active);
        var reads = backend.PermissionReads;
        var slices = backend.Slices;
        backend.Permission = new(false, MacListenAccess.Denied);
        monitor.Retry();
        await UntilAsync(() => backend.Slices >= slices + 8);
        Assert.Equal(InputHookStatus.Active, monitor.Status);
        Assert.Equal(reads, backend.PermissionReads);
        Assert.Equal(2, backend.Taps.Count);
        Assert.All(backend.Taps, tap => Assert.Equal(0, tap.Disposals));
    }

    [Theory]
    [InlineData(0)] // Actual enabled state was revoked.
    [InlineData(1)] // The native port was invalidated.
    [InlineData(2)] // CoreGraphics reports user disable.
    [InlineData(3)] // The shared native run loop stopped.
    public async Task AFailedLiveConnectionUsesFreshPermissionAndReleasesBothTaps(int failure)
    {
        using var backend = new FakeBackend();
        using var monitor = new MacInputMonitor(backend, RetryDelay);
        monitor.Start();
        await UntilAsync(() => monitor.Status == InputHookStatus.Active);
        var buttons = backend.Latest(MacInputTapKind.ButtonsAndModifiers);
        await backend.OnWorkerAsync(() =>
        {
            backend.Permission = new(false, MacListenAccess.Denied);
            switch (failure)
            {
                case 0: buttons.Enabled = false; break;
                case 1: buttons.Valid = false; break;
                case 2: buttons.Deliver(new(MacInputEventType.TapDisabledByUserInput)); break;
                case 3: backend.LoopResult = MacRunLoopResult.Stopped; break;
            }
        });
        await UntilAsync(() => monitor.Status == InputHookStatus.PermissionRequired && backend.Taps.All(tap => tap.Disposals == 1));
        Assert.Equal(2, backend.Attempts.Count);
        Assert.Equal(0, backend.PermissionRequests);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task TimeoutReenablesTheAffectedTapAndChecksWhetherRecoverySucceeded(bool recovers)
    {
        using var backend = new FakeBackend();
        using var monitor = new MacInputMonitor(backend, RetryDelay);
        monitor.Start();
        await UntilAsync(() => monitor.Status == InputHookStatus.Active);
        var keyboard = backend.Latest(MacInputTapKind.Keyboard);
        var buttons = backend.Latest(MacInputTapKind.ButtonsAndModifiers);
        var reads = backend.PermissionReads;
        await backend.OnWorkerAsync(() =>
        {
            buttons.EnableSucceeds = recovers;
            buttons.Enabled = false;
            if (!recovers) backend.CanCreate = _ => false;
            buttons.Deliver(new(MacInputEventType.TapDisabledByTimeout));
        });
        Assert.Equal(2, buttons.EnableCalls);
        Assert.Equal(1, keyboard.EnableCalls);
        if (recovers)
        {
            var slices = backend.Slices;
            await UntilAsync(() => backend.Slices >= slices + 2);
            Assert.Equal(InputHookStatus.Active, monitor.Status);
            Assert.Equal(reads, backend.PermissionReads);
            Assert.Equal(2, backend.Attempts.Count);
        }
        else
        {
            await UntilAsync(() => monitor.Status == InputHookStatus.Unavailable && backend.Attempts.Count > 2);
            Assert.Equal(1, keyboard.Disposals);
            Assert.Equal(1, buttons.Disposals);
            Assert.True(backend.PermissionReads > reads);
        }
    }

    [Fact]
    public async Task OldSessionAndDisposedCallbacksCannotCountOrRestartMonitoring()
    {
        using var backend = new FakeBackend();
        using var monitor = new MacInputMonitor(backend, RetryDelay);
        var triggers = 0;
        monitor.Triggered += () => Interlocked.Increment(ref triggers);
        monitor.Start();
        await UntilAsync(() => monitor.Status == InputHookStatus.Active);
        var oldKeyboard = backend.Latest(MacInputTapKind.Keyboard);
        var oldButtons = backend.Latest(MacInputTapKind.ButtonsAndModifiers);
        await backend.OnWorkerAsync(() =>
        {
            backend.Permission = new(false, MacListenAccess.Denied);
            oldButtons.Deliver(new(MacInputEventType.TapDisabledByUserInput));
        });
        await UntilAsync(() => monitor.Status == InputHookStatus.PermissionRequired && oldKeyboard.Disposals == 1);
        backend.Permission = new(true, MacListenAccess.Granted);
        await UntilAsync(() => monitor.Status == InputHookStatus.Active);
        var keyboard = backend.Latest(MacInputTapKind.Keyboard);
        await backend.OnWorkerAsync(() =>
        {
            oldKeyboard.Deliver(new(MacInputEventType.KeyDown));
            oldButtons.Deliver(new(MacInputEventType.TapDisabledByTimeout));
            keyboard.Deliver(new(MacInputEventType.KeyDown));
        });
        Assert.Equal(1, Volatile.Read(ref triggers));
        Assert.Equal(1, oldButtons.EnableCalls);

        monitor.Dispose();
        var attempts = backend.Attempts.Count;
        var reads = backend.PermissionReads;
        keyboard.Deliver(new(MacInputEventType.KeyDown)); // Simulate a callback already retained by native code.
        oldButtons.Deliver(new(MacInputEventType.LeftMouseDown));
        monitor.Retry();
        monitor.RequestPermissionAndRetry();
        monitor.Start();
        await Task.Delay(RetryDelay * 3);
        Assert.Equal(InputHookStatus.Disposed, monitor.Status);
        Assert.Equal(1, Volatile.Read(ref triggers));
        Assert.Equal(attempts, backend.Attempts.Count);
        Assert.Equal(reads, backend.PermissionReads);
        Assert.All(backend.Taps, tap => Assert.Equal(1, tap.Disposals));
    }

    [Fact]
    public async Task InputCountsExcludeKeyboardRepeatModifierReleaseAndExtraMouseButtons()
    {
        using var backend = new FakeBackend { InitialModifierFlags = 0x20002 };
        using var monitor = new MacInputMonitor(backend, RetryDelay);
        var triggers = 0;
        monitor.Triggered += () => Interlocked.Increment(ref triggers);
        monitor.Start();
        await UntilAsync(() => monitor.Status == InputHookStatus.Active);
        var keyboard = backend.Latest(MacInputTapKind.Keyboard);
        var buttons = backend.Latest(MacInputTapKind.ButtonsAndModifiers);
        await backend.OnWorkerAsync(() =>
        {
            keyboard.Deliver(new(MacInputEventType.KeyDown));
            keyboard.Deliver(new(MacInputEventType.KeyDown, IsKeyboardRepeat: true));
            buttons.Deliver(new(MacInputEventType.LeftMouseDown));
            buttons.Deliver(new(MacInputEventType.RightMouseDown));
            buttons.Deliver(new(MacInputEventType.OtherMouseDown, MouseButton: 2));
            buttons.Deliver(new(MacInputEventType.OtherMouseDown, MouseButton: 3));
            foreach (var flags in new ulong[] { 0, 0x20002, 0x20002, 0x20006, 0x20004, 0, 0x10000, 0, 0x800000, 0 })
                buttons.Deliver(new(MacInputEventType.FlagsChanged, ModifierFlags: flags));
        });
        Assert.Equal(9, Volatile.Read(ref triggers));
    }

    private static ConcurrentQueue<InputHookStatus> Observe(MacInputMonitor monitor)
    {
        var statuses = new ConcurrentQueue<InputHookStatus>();
        monitor.StatusChanged += () => statuses.Enqueue(monitor.Status);
        return statuses;
    }

    private static async Task UntilAsync(Func<bool> condition)
    {
        var clock = Stopwatch.StartNew();
        while (!condition() && clock.Elapsed < Timeout) await Task.Delay(5);
        Assert.True(condition(), "The fake native monitor did not reach the expected state.");
    }

    /// <summary>Both fake sources share one worker queue, matching a single CF run loop.</summary>
    private sealed class FakeBackend : IMacInputBackend, IDisposable
    {
        private readonly object _gate = new();
        private readonly ConcurrentQueue<Action> _work = new();
        private readonly AutoResetEvent _wake = new(false);
        private MacInputPermission _permission = new(true, MacListenAccess.Granted);
        private Func<MacInputTapKind, bool> _canCreate = _ => true;
        private int _permissionReads, _permissionRequests, _slices;
        private int _loopResult = (int)MacRunLoopResult.TimedOut;

        public ConcurrentQueue<MacInputTapKind> Attempts { get; } = new();
        public ConcurrentQueue<FakeTap> Taps { get; } = new();
        public int PermissionReads => Volatile.Read(ref _permissionReads);
        public int PermissionRequests => Volatile.Read(ref _permissionRequests);
        public int Slices => Volatile.Read(ref _slices);
        public ulong InitialModifierFlags { get; set; }
        public Action? OnRequest { get; set; }
        public MacInputPermission Permission { get { lock (_gate) return _permission; } set { lock (_gate) _permission = value; } }
        public Func<MacInputTapKind, bool> CanCreate { get { lock (_gate) return _canCreate; } set { lock (_gate) _canCreate = value; } }
        public MacRunLoopResult LoopResult { get => (MacRunLoopResult)Volatile.Read(ref _loopResult); set => Volatile.Write(ref _loopResult, (int)value); }

        public MacInputPermission ReadPermission() { Interlocked.Increment(ref _permissionReads); return Permission; }
        public void RequestListenPermission() { Interlocked.Increment(ref _permissionRequests); OnRequest?.Invoke(); }
        public IMacInputTap? CreateListenOnlyTap(MacInputTapKind kind, Action<MacInputEvent> onInput)
        {
            Attempts.Enqueue(kind);
            if (!CanCreate(kind)) return null;
            var tap = new FakeTap(this, kind, onInput);
            Taps.Enqueue(tap);
            return tap;
        }

        public FakeTap Latest(MacInputTapKind kind) => Taps.Last(tap => tap.Kind == kind);
        public Task OnWorkerAsync(Action action)
        {
            var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _work.Enqueue(() =>
            {
                try { action(); completed.TrySetResult(); }
                catch (Exception error) { completed.TrySetException(error); }
            });
            _wake.Set();
            return completed.Task.WaitAsync(Timeout);
        }

        public MacRunLoopResult RunSlice()
        {
            _wake.WaitOne(5);
            while (_work.TryDequeue(out var action)) action();
            Interlocked.Increment(ref _slices);
            return LoopResult;
        }
        public void Dispose() => _wake.Dispose();
    }

    private sealed class FakeTap(FakeBackend backend, MacInputTapKind kind, Action<MacInputEvent> callback) : IMacInputTap
    {
        private int _valid = 1, _enabled, _enableCalls, _disposals;
        public MacInputTapKind Kind => kind;
        public bool Valid { get => Volatile.Read(ref _valid) != 0; set => Volatile.Write(ref _valid, value ? 1 : 0); }
        public bool Enabled { get => Volatile.Read(ref _enabled) != 0; set => Volatile.Write(ref _enabled, value ? 1 : 0); }
        public bool EnableSucceeds { get; set; } = true;
        public int EnableCalls => Volatile.Read(ref _enableCalls);
        public int Disposals => Volatile.Read(ref _disposals);
        public bool IsValid => Valid && Disposals == 0;
        public bool IsEnabled => Enabled && Disposals == 0;
        public ulong ModifierFlags => backend.InitialModifierFlags;
        public void Enable() { Interlocked.Increment(ref _enableCalls); Enabled = EnableSucceeds && IsValid; }
        public MacRunLoopResult RunSlice() => backend.RunSlice();
        public void Deliver(MacInputEvent input) => callback(input);
        public void Dispose() { Interlocked.Increment(ref _disposals); Enabled = false; Valid = false; }
    }
}
