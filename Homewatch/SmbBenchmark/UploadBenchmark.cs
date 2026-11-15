using Homewatch.Contracts.Abstractions.Services;
using Homewatch.Contracts.Dtos.RemoteStorageDtos;
using Homewatch.Core.Utilities;
using System.Diagnostics;
using System.Security.Cryptography;

namespace Homewatch.SmbBenchmark
{
    public class UploadBenchmark
    {
        private const double _bytesPerMegabyte = 1024 * 1024;

        private readonly BenchmarkSettingsPrompt _settingsPrompt;
        private readonly ISmbConnectionService _smbConnectionService;

        public UploadBenchmark(BenchmarkSettingsPrompt settingsPrompt, ISmbConnectionService smbConnectionService)
        {
            _settingsPrompt = settingsPrompt;
            _smbConnectionService = smbConnectionService;
        }

        public async Task RunAsync(CancellationToken cancellationToken)
        {
            var settings = await _settingsPrompt.PromptAsync();

            if (settings == null || !CanDecryptPassword(settings.Target))
            {
                return;
            }

            var content = RandomNumberGenerator.GetBytes(settings.FileSizeInBytes);
            var benchmarkFolder = $"homewatch-benchmark-{DateTime.Now:yyyyMMdd-HHmmss}";
            var summaries = new List<string>();

            Console.WriteLine();
            Console.WriteLine($"Uploading {settings.FileSizeInBytes / _bytesPerMegabyte:0.00} megabyte files to {settings.Target.SharePath}\\{benchmarkFolder} for {settings.RunDuration.TotalSeconds:0} seconds per run. Press Ctrl+C to stop.");

            try
            {
                foreach (var connectionCount in settings.ConnectionCounts)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        break;
                    }

                    var runFolder = $@"{benchmarkFolder}\{connectionCount}-connections-{summaries.Count + 1}";

                    Console.WriteLine();
                    Console.WriteLine($"Running {connectionCount} parallel connections...");

                    var result = await RunWithConnectionsAsync(settings, content, runFolder, connectionCount, cancellationToken);
                    var summary = result.ToSummary();

                    summaries.Add(summary);
                    Console.WriteLine(summary);

                    await DeleteFolderAsync(settings.Target, runFolder);
                }
            }
            finally
            {
                await DeleteFolderAsync(settings.Target, benchmarkFolder);
            }

            Console.WriteLine();
            Console.WriteLine("Results:");

            foreach (var summary in summaries)
            {
                Console.WriteLine(summary);
            }
        }

        private bool CanDecryptPassword(RemoteStorageTargetDto target)
        {
            try
            {
                _smbConnectionService.UnprotectPassword(target.ProtectedPassword);

                return true;
            }
            catch (CryptographicException exception)
            {
                Console.WriteLine($"The stored password for {target.SharePath} cannot be decrypted ({exception.Message}). Run the benchmark on the server, as the Windows user that runs Homewatch.");

                return false;
            }
        }

        private async Task<RunResult> RunWithConnectionsAsync(
            BenchmarkSettings settings,
            byte[] content,
            string runFolder,
            int connectionCount,
            CancellationToken cancellationToken)
        {
            var connections = Enumerable.Range(1, connectionCount)
                .Select(x => new BenchmarkConnection(_smbConnectionService, settings.Target, $@"{runFolder}\connection-{x}"))
                .ToList();

            try
            {
                await Task.WhenAll(connections.Select(x => SmbOperationRunner.RunAsync(x.Open)));

                var runStartTimestamp = Stopwatch.GetTimestamp();

                await Task.WhenAll(connections
                    .Where(x => x.IsOpen)
                    .Select(x => SmbOperationRunner.RunAsync(() => x.UploadUntil(content, runStartTimestamp, settings.RunDuration, cancellationToken))));

                return new RunResult(connectionCount, Stopwatch.GetElapsedTime(runStartTimestamp), connections);
            }
            finally
            {
                await Task.WhenAll(connections.Select(x => SmbOperationRunner.RunAsync(x.Dispose)));
            }
        }

        private async Task DeleteFolderAsync(RemoteStorageTargetDto target, string folder)
        {
            try
            {
                await SmbOperationRunner.RunAsync(() =>
                {
                    using var session = _smbConnectionService.OpenSession(target);

                    session.DeleteDirectory(folder);
                });
            }
            catch (Exception exception)
            {
                Console.WriteLine($"Could not delete {target.SharePath}\\{folder}: {exception.Message} Delete it manually.");
            }
        }
    }
}
