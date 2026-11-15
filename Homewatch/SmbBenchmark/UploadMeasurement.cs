namespace Homewatch.SmbBenchmark
{
    public class UploadMeasurement
    {
        public required TimeSpan UploadDuration { get; init; }

        public required TimeSpan RenameDuration { get; init; }

        public required long SizeInBytes { get; init; }
    }
}
