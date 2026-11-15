using Homewatch.Contracts.Dtos.RemoteStorageDtos;

namespace Homewatch.SmbBenchmark
{
    public class BenchmarkSettings
    {
        public required RemoteStorageTargetDto Target { get; init; }

        public required List<int> ConnectionCounts { get; init; }

        public required TimeSpan RunDuration { get; init; }

        public required int FileSizeInBytes { get; init; }
    }
}
