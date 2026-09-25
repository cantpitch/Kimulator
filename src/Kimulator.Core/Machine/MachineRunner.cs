using System.Collections.Concurrent;
using System.Diagnostics;

namespace Kimulator.Core.Machine;

/// <summary>
/// Runs an <see cref="IMachine"/> on a dedicated thread, paced against the wall clock.
/// The machine itself is not thread-safe: other threads interact with it through <see cref="Post"/>,
/// which runs the action on the emulation thread between time slices.
/// </summary>
public sealed class MachineRunner : IDisposable
{
    private const double SliceSeconds = 0.001;
    private const double MaxLagSeconds = 0.1;

    private readonly IMachine _machine;
    private readonly ConcurrentQueue<Action> _commands = new();
    private readonly AutoResetEvent _wake = new(false);
    private Thread? _thread;
    private volatile bool _stopRequested;
    private volatile bool _paused;
    private double _speed = 1.0;

    public MachineRunner(IMachine machine) => _machine = machine;

    /// <summary>Emulation speed relative to the real clock (1.0 = real time).</summary>
    public double Speed
    {
        get => Volatile.Read(ref _speed);
        set => Volatile.Write(ref _speed, Math.Clamp(value, 0.01, 1000));
    }

    /// <summary>When true the machine runs as fast as the host allows.</summary>
    public bool Unthrottled { get; set; }

    public bool Paused
    {
        get => _paused;
        set
        {
            _paused = value;
            _wake.Set();
        }
    }

    public bool IsRunning => _thread is { IsAlive: true };

    /// <summary>Raised on the emulation thread when an exception escapes the machine; the runner pauses.</summary>
    public event Action<Exception>? Faulted;

    /// <summary>Queues an action to run on the emulation thread.</summary>
    public void Post(Action action)
    {
        _commands.Enqueue(action);
        _wake.Set();
    }

    /// <summary>Runs a function on the emulation thread and returns its result.</summary>
    public Task<T> InvokeAsync<T>(Func<T> func)
    {
        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        Post(() =>
        {
            try { tcs.SetResult(func()); }
            catch (Exception ex) { tcs.SetException(ex); }
        });
        return tcs.Task;
    }

    public void Start()
    {
        if (IsRunning) return;
        _stopRequested = false;
        _thread = new Thread(Loop) { IsBackground = true, Name = "Emulation" };
        _thread.Start();
    }

    public void Stop()
    {
        _stopRequested = true;
        _wake.Set();
        _thread?.Join();
        _thread = null;
    }

    public void Dispose()
    {
        Stop();
        _wake.Dispose();
    }

    private void Loop()
    {
        var clock = Stopwatch.StartNew();
        double baseSeconds = 0;
        long baseCycles = _machine.Cycles;
        double speed = Speed;

        while (!_stopRequested)
        {
            DrainCommands();

            if (_paused)
            {
                _wake.WaitOne(50);
                baseSeconds = clock.Elapsed.TotalSeconds;
                baseCycles = _machine.Cycles;
                continue;
            }

            // Re-base the timeline if speed changed so we don't jump.
            if (Speed != speed)
            {
                speed = Speed;
                baseSeconds = clock.Elapsed.TotalSeconds;
                baseCycles = _machine.Cycles;
            }

            long sliceCycles = (long)(_machine.ClockHz * SliceSeconds * speed);
            try
            {
                if (Unthrottled)
                {
                    _machine.RunUntil(_machine.Cycles + Math.Max(1, (long)(_machine.ClockHz * SliceSeconds * 10)));
                    baseSeconds = clock.Elapsed.TotalSeconds;
                    baseCycles = _machine.Cycles;
                    continue;
                }

                double now = clock.Elapsed.TotalSeconds;
                long target = baseCycles + (long)((now - baseSeconds) * _machine.ClockHz * speed);
                if (target - _machine.Cycles > _machine.ClockHz * MaxLagSeconds * speed)
                {
                    // Host stalled (debugger, sleep, window drag): don't try to catch up.
                    baseSeconds = now;
                    baseCycles = _machine.Cycles;
                    target = baseCycles;
                }

                if (_machine.Cycles < target)
                    _machine.RunUntil(Math.Min(target, _machine.Cycles + Math.Max(1, sliceCycles)));
                else
                    _wake.WaitOne(1);
            }
            catch (Exception ex)
            {
                _paused = true;
                Faulted?.Invoke(ex);
            }
        }

        DrainCommands();
    }

    private void DrainCommands()
    {
        while (_commands.TryDequeue(out var action))
            action();
    }
}
