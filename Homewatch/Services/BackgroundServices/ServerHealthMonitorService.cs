using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace Homewatch.Core.BackgroundServices
{
    public class ServerHealthMonitorService : BackgroundService
    {
        private const double _bytesPerMegabyte = 1024 * 1024;

        private static readonly TimeSpan _checkInterval = TimeSpan.FromSeconds(1);
        private static readonly TimeSpan _stallThreshold = TimeSpan.FromSeconds(2);
        private static readonly TimeSpan _summaryInterval = TimeSpan.FromMinutes(1);

        private readonly ILogger<ServerHealthMonitorService> _logger;

        private TimeSpan _largestCheckDelay = TimeSpan.Zero;
        private int _stallCount;
        private int _fullGarbageCollectionCountAtSummaryStart = GC.CollectionCount(2);

        public ServerHealthMonitorService(ILogger<ServerHealthMonitorService> logger)
        {
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            using var timer = new PeriodicTimer(_checkInterval);

            var previousCheckTimestamp = Stopwatch.GetTimestamp();
            var summaryStartTimestamp = previousCheckTimestamp;

            try
            {
                while (await timer.WaitForNextTickAsync(stoppingToken))
                {
                    var checkTimestamp = Stopwatch.GetTimestamp();
                    var checkDelay = Stopwatch.GetElapsedTime(previousCheckTimestamp, checkTimestamp) - _checkInterval;

                    previousCheckTimestamp = checkTimestamp;

                    RecordCheckDelay(checkDelay);

                    if (Stopwatch.GetElapsedTime(summaryStartTimestamp, checkTimestamp) >= _summaryInterval)
                    {
                        LogSummary();
                        summaryStartTimestamp = checkTimestamp;
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
            }
        }

        private void RecordCheckDelay(TimeSpan checkDelay)
        {
            if (checkDelay > _largestCheckDelay)
            {
                _largestCheckDelay = checkDelay;
            }

            if (checkDelay < _stallThreshold)
            {
                return;
            }

            _stallCount++;

            _logger.LogWarning(
                "Server stalled for {StallSeconds:0.0} seconds. Thread pool: {ThreadCount} threads, {PendingWorkItemCount} queued work items.",
                checkDelay.TotalSeconds,
                ThreadPool.ThreadCount,
                ThreadPool.PendingWorkItemCount);
        }

        private void LogSummary()
        {
            var fullGarbageCollectionCount = GC.CollectionCount(2);

            _logger.LogInformation(
                "Server health: {ThreadCount} thread pool threads, {PendingWorkItemCount} queued work items, largest timer delay {LargestDelayMilliseconds:0} milliseconds, {StallCount} stalls, {WorkingSetInMegabytes:0} megabytes working set, {ManagedMemoryInMegabytes:0} megabytes managed memory, {FullGarbageCollectionCount} full garbage collections in the last minute.",
                ThreadPool.ThreadCount,
                ThreadPool.PendingWorkItemCount,
                Math.Max(_largestCheckDelay.TotalMilliseconds, 0),
                _stallCount,
                Environment.WorkingSet / _bytesPerMegabyte,
                GC.GetTotalMemory(false) / _bytesPerMegabyte,
                fullGarbageCollectionCount - _fullGarbageCollectionCountAtSummaryStart);

            _largestCheckDelay = TimeSpan.Zero;
            _stallCount = 0;
            _fullGarbageCollectionCountAtSummaryStart = fullGarbageCollectionCount;
        }
    }
}
