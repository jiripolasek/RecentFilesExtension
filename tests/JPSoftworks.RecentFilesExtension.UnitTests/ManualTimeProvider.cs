// ------------------------------------------------------------
//
// Copyright (c) Jiří Polášek. All rights reserved.
//
// ------------------------------------------------------------

namespace JPSoftworks.RecentFilesExtension.UnitTests;

internal sealed class ManualTimeProvider : TimeProvider
{
    private readonly Lock _syncRoot = new();
    private readonly List<ManualTimer> _timers = [];
    private long _ticks;

    public int CallbackCount { get; private set; }

    public override DateTimeOffset GetUtcNow()
    {
        lock (this._syncRoot)
        {
            return DateTimeOffset.UnixEpoch.AddTicks(this._ticks);
        }
    }

    public TimeSpan? NextTimerDelay
    {
        get
        {
            lock (this._syncRoot)
            {
                var next = this._timers.MinBy(static timer => timer.Due);
                return next is { Due: < long.MaxValue } ? TimeSpan.FromTicks(next.Due - this._ticks) : null;
            }
        }
    }

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        lock (this._syncRoot)
        {
            var timer = new ManualTimer(this, callback, state);
            timer.Change(dueTime, period);
            this._timers.Add(timer);
            return timer;
        }
    }

    public void Advance(TimeSpan elapsed)
    {
        long target;
        lock (this._syncRoot)
        {
            target = this._ticks + elapsed.Ticks;
        }
        while (true)
        {
            ManualTimer? timer;
            lock (this._syncRoot)
            {
                timer = this._timers.Where(timer => timer.Due <= target).MinBy(static timer => timer.Due);
                if (timer is null)
                {
                    this._ticks = target;
                    return;
                }
                this._ticks = timer.Due;
                timer.Due = long.MaxValue;
                this.CallbackCount++;
            }
            timer.Fire();
        }
    }

    private sealed class ManualTimer(ManualTimeProvider time, TimerCallback callback, object? state) : ITimer
    {
        public long Due { get; set; } = long.MaxValue;
        private bool _disposed;

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            Assert.AreEqual(Timeout.InfiniteTimeSpan, period);
            lock (time._syncRoot)
            {
                if (this._disposed)
                {
                    return false;
                }
                this.Due = dueTime == Timeout.InfiniteTimeSpan ? long.MaxValue : time._ticks + dueTime.Ticks;
                return true;
            }
        }

        public void Fire() => callback(state);

        public void Dispose()
        {
            lock (time._syncRoot)
            {
                this._disposed = true;
                this.Due = long.MaxValue;
            }
        }

        public ValueTask DisposeAsync()
        {
            this.Dispose();
            return ValueTask.CompletedTask;
        }
    }
}