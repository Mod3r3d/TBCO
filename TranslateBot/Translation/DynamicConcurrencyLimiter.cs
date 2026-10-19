using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace TranslateBot.Translation
{
    /// <summary>
    /// Bộ điều tiết luồng đồng thời động (P0-8, P1-9).
    /// Hỗ trợ tăng/giảm giới hạn concurrency động tại runtime mà không gây rò rỉ hoặc SemaphoreFullException.
    /// </summary>
    public class DynamicConcurrencyLimiter : IDisposable
    {
        private int _maxConcurrency;
        private int _activeCount = 0;
        private readonly object _lock = new();
        private readonly Queue<TaskCompletionSource<bool>> _waiters = new();
        private bool _isDisposed = false;

        public int MaxConcurrency
        {
            get { lock (_lock) { return _maxConcurrency; } }
            set
            {
                lock (_lock)
                {
                    if (value < 1) value = 1;
                    _maxConcurrency = value;
                    DrainWaitersUnderLock();
                }
            }
        }

        public int ActiveCount
        {
            get { lock (_lock) { return _activeCount; } }
        }

        public int WaiterCount
        {
            get { lock (_lock) { return _waiters.Count; } }
        }

        public DynamicConcurrencyLimiter(int initialLimit)
        {
            _maxConcurrency = Math.Max(1, initialLimit);
        }

        public Task WaitAsync(CancellationToken token = default)
        {
            lock (_lock)
            {
                if (_isDisposed)
                    throw new ObjectDisposedException(nameof(DynamicConcurrencyLimiter));

                token.ThrowIfCancellationRequested();

                if (_activeCount < _maxConcurrency)
                {
                    _activeCount++;
                    return Task.CompletedTask;
                }

                var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                if (token.CanBeCanceled)
                {
                    var registration = token.Register(() =>
                    {
                        lock (_lock)
                        {
                            tcs.TrySetCanceled(token);
                        }
                    });
                    tcs.Task.ContinueWith(_ => registration.Dispose(), TaskScheduler.Default);
                }

                _waiters.Enqueue(tcs);
                return tcs.Task;
            }
        }

        public void Release()
        {
            lock (_lock)
            {
                if (_isDisposed) return;

                if (_activeCount > 0)
                {
                    _activeCount--;
                }

                DrainWaitersUnderLock();
            }
        }

        private void DrainWaitersUnderLock()
        {
            while (_activeCount < _maxConcurrency && _waiters.Count > 0)
            {
                var waiter = _waiters.Dequeue();
                if (waiter.Task.IsCompleted) continue; // Bỏ qua task đã hủy

                _activeCount++;
                waiter.TrySetResult(true);
            }
        }

        public void Dispose()
        {
            lock (_lock)
            {
                if (_isDisposed) return;
                _isDisposed = true;

                while (_waiters.Count > 0)
                {
                    var waiter = _waiters.Dequeue();
                    waiter.TrySetCanceled();
                }
            }
        }
    }
}
