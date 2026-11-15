namespace Homewatch.SmbBenchmark
{
    public class RunResult
    {
        private const double _bytesPerMegabyte = 1024 * 1024;
        private const double _bitsPerMegabit = 1_000_000;

        private readonly int _connectionCount;
        private readonly TimeSpan _duration;
        private readonly List<BenchmarkConnection> _connections;

        public RunResult(int connectionCount, TimeSpan duration, List<BenchmarkConnection> connections)
        {
            _connectionCount = connectionCount;
            _duration = duration;
            _connections = connections;
        }

        public string ToSummary()
        {
            var uploads = _connections.SelectMany(x => x.Uploads).ToList();
            var connectedCount = _connections.Count(x => x.ConnectDuration > TimeSpan.Zero);
            var failedConnections = _connections.Where(x => x.Error != null).ToList();

            if (uploads.Count == 0)
            {
                return $"{_connectionCount} connections: no files uploaded. {DescribeErrors(failedConnections)}";
            }

            var totalBytes = uploads.Sum(x => x.SizeInBytes);
            var totalMegabytes = totalBytes / _bytesPerMegabyte;
            var combinedMegabytesPerSecond = totalMegabytes / _duration.TotalSeconds;
            var combinedMegabitsPerSecond = totalBytes * 8 / _bitsPerMegabit / _duration.TotalSeconds;
            var megabytesPerSecondPerConnection = combinedMegabytesPerSecond / Math.Max(connectedCount, 1);

            return $"{_connectionCount} connections: {uploads.Count} files, {totalMegabytes:0.0} megabytes in {_duration.TotalSeconds:0.0} seconds. "
                + $"Combined {combinedMegabytesPerSecond:0.00} megabytes per second ({combinedMegabitsPerSecond:0.0} megabits per second), {megabytesPerSecondPerConnection:0.00} per connection. "
                + $"Median upload {Median(uploads.Select(x => x.UploadDuration)):0} milliseconds, slowest {uploads.Max(x => x.UploadDuration).TotalMilliseconds:0}, "
                + $"median rename {Median(uploads.Select(x => x.RenameDuration)):0} milliseconds, "
                + $"median connect {Median(_connections.Where(x => x.ConnectDuration > TimeSpan.Zero).Select(x => x.ConnectDuration)):0} milliseconds. "
                + DescribeErrors(failedConnections);
        }

        private string DescribeErrors(List<BenchmarkConnection> failedConnections)
        {
            return failedConnections.Count == 0
                ? "No errors."
                : $"{failedConnections.Count} connections failed: {failedConnections[0].Error}";
        }

        private double Median(IEnumerable<TimeSpan> durations)
        {
            var sortedMilliseconds = durations.Select(x => x.TotalMilliseconds).Order().ToList();

            if (sortedMilliseconds.Count == 0)
            {
                return 0;
            }

            var middle = sortedMilliseconds.Count / 2;

            return sortedMilliseconds.Count % 2 == 1
                ? sortedMilliseconds[middle]
                : (sortedMilliseconds[middle - 1] + sortedMilliseconds[middle]) / 2;
        }
    }
}
