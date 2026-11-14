namespace Homewatch.Core.Utilities
{
    // SMBLibrary blocks the calling thread until each reply arrives, and replies are delivered on thread pool threads, so every operation gets its own thread instead of a thread pool thread.
    public static class SmbOperationRunner
    {
        public static Task RunAsync(Action operation, CancellationToken cancellationToken = default)
        {
            return Task.Factory.StartNew(operation, cancellationToken, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        }

        public static Task<TResult> RunAsync<TResult>(Func<TResult> operation, CancellationToken cancellationToken = default)
        {
            return Task.Factory.StartNew(operation, cancellationToken, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        }
    }
}
