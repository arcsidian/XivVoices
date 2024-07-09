using System;
using System.Threading;
using System.Threading.Tasks;

namespace XivVoices
{
    public class AsyncLock : IDisposable
    {
        private readonly SemaphoreSlim _semaphore = new SemaphoreSlim(1, 1);
        private readonly Task<IDisposable> _releaser;
        private bool _disposed;

        public AsyncLock()
        {
            _releaser = Task.FromResult((IDisposable)new Releaser(this));
        }

        public Task<IDisposable> LockAsync()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(AsyncLock));

            Plugin.PluginLog.Information("AsyncLock ---> Waiting to acquire lock");
            var wait = _semaphore.WaitAsync();
            return wait.IsCompleted ?
                _releaser :
                wait.ContinueWith((_, state) =>
                {
                    Plugin.PluginLog.Information("AsyncLock ---> Lock acquired");
                    return (IDisposable)state;
                }, _releaser.Result,
                CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }

        private class Releaser : IDisposable
        {
            private readonly AsyncLock _toRelease;
            public Releaser(AsyncLock toRelease) { _toRelease = toRelease; }

            public void Dispose()
            {
                if (_toRelease._disposed) return;
                _toRelease._semaphore.Release();
                Plugin.PluginLog.Information("AsyncLock ---> Lock released");
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _semaphore.Dispose();
            _disposed = true;
        }
    }
}
