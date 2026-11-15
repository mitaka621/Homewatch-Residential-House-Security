using Homewatch.Contracts.Abstractions.Repositories;
using Homewatch.Contracts.Dtos.RemoteStorageDtos;
using System.Globalization;

namespace Homewatch.SmbBenchmark
{
    public class BenchmarkSettingsPrompt
    {
        private const double _bytesPerMegabyte = 1024 * 1024;
        private const int _maximumConnectionCount = 32;
        private const double _defaultRunDurationInSeconds = 30;
        private const double _defaultFileSizeInMegabytes = 1.03;

        private readonly List<int> _defaultConnectionCounts = [1, 2, 3, 4, 6];
        private readonly IRemoteStorageTargetRepository _remoteStorageTargetRepository;

        public BenchmarkSettingsPrompt(IRemoteStorageTargetRepository remoteStorageTargetRepository)
        {
            _remoteStorageTargetRepository = remoteStorageTargetRepository;
        }

        public async Task<BenchmarkSettings?> PromptAsync()
        {
            var targets = await _remoteStorageTargetRepository.GetTargetsAsync();

            if (targets.Count == 0)
            {
                Console.WriteLine("No remote storage shares are saved in the database.");

                return null;
            }

            Console.WriteLine("Press Enter to keep the value in brackets.");
            Console.WriteLine();

            var target = PromptTarget(targets);
            var connectionCounts = PromptConnectionCounts();
            var runDurationInSeconds = PromptNumber("Seconds per run", _defaultRunDurationInSeconds, 5, 600);
            var fileSizeInMegabytes = PromptNumber("File size in megabytes (a camera chunk is about 1.03)", _defaultFileSizeInMegabytes, 0.01, 100);

            return new BenchmarkSettings
            {
                Target = target,
                ConnectionCounts = connectionCounts,
                RunDuration = TimeSpan.FromSeconds(runDurationInSeconds),
                FileSizeInBytes = (int)(fileSizeInMegabytes * _bytesPerMegabyte)
            };
        }

        private RemoteStorageTargetDto PromptTarget(List<RemoteStorageTargetDto> targets)
        {
            if (targets.Count == 1)
            {
                Console.WriteLine($"Share: {targets[0].SharePath}");

                return targets[0];
            }

            for (var index = 0; index < targets.Count; index++)
            {
                Console.WriteLine($"  {index + 1}. {targets[index].SharePath}");
            }

            var targetNumber = (int)PromptNumber("Share number", 1, 1, targets.Count);

            return targets[targetNumber - 1];
        }

        private List<int> PromptConnectionCounts()
        {
            while (true)
            {
                var input = ReadInput($"Parallel connections for each run [{string.Join(",", _defaultConnectionCounts)}]: ");

                if (input.Length == 0)
                {
                    return _defaultConnectionCounts;
                }

                var connectionCounts = new List<int>();

                foreach (var value in input.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    if (!int.TryParse(value, out var connectionCount) || connectionCount < 1 || connectionCount > _maximumConnectionCount)
                    {
                        connectionCounts.Clear();
                        break;
                    }

                    connectionCounts.Add(connectionCount);
                }

                if (connectionCounts.Count > 0)
                {
                    return connectionCounts;
                }

                Console.WriteLine($"Enter whole numbers from 1 to {_maximumConnectionCount} separated by commas, for example 1,2,4.");
            }
        }

        private double PromptNumber(string label, double defaultValue, double minimum, double maximum)
        {
            while (true)
            {
                var input = ReadInput($"{label} [{defaultValue}]: ");

                if (input.Length == 0)
                {
                    return defaultValue;
                }

                if (double.TryParse(input.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
                    && value >= minimum
                    && value <= maximum)
                {
                    return value;
                }

                Console.WriteLine($"Enter a number from {minimum} to {maximum}.");
            }
        }

        private string ReadInput(string prompt)
        {
            Console.Write(prompt);

            return Console.ReadLine()?.Trim() ?? string.Empty;
        }
    }
}
