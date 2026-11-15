using Homewatch.Contracts.Abstractions.Services;
using Homewatch.Contracts.Dtos.RemoteStorageDtos;
using System.Diagnostics;

namespace Homewatch.SmbBenchmark
{
    // Uploads the same way RemoteStorageTargetWorker copies a video chunk: write to a .partial file, then rename it.
    public class BenchmarkConnection : IDisposable
    {
        private const string _partialFileExtension = ".partial";

        private readonly ISmbConnectionService _smbConnectionService;
        private readonly RemoteStorageTargetDto _target;
        private readonly string _folder;

        private ISmbShareSession? _session;

        public BenchmarkConnection(ISmbConnectionService smbConnectionService, RemoteStorageTargetDto target, string folder)
        {
            _smbConnectionService = smbConnectionService;
            _target = target;
            _folder = folder;
        }

        public TimeSpan ConnectDuration { get; private set; }

        public List<UploadMeasurement> Uploads { get; } = new();

        public string? Error { get; private set; }

        public bool IsOpen => _session != null;

        public void Open()
        {
            try
            {
                var connectStartTimestamp = Stopwatch.GetTimestamp();

                _session = _smbConnectionService.OpenSession(_target);

                ConnectDuration = Stopwatch.GetElapsedTime(connectStartTimestamp);

                _session.CreateDirectory(_folder);
            }
            catch (Exception exception)
            {
                Error = exception.Message;
                Dispose();
            }
        }

        public void UploadUntil(byte[] content, long runStartTimestamp, TimeSpan runDuration, CancellationToken cancellationToken)
        {
            if (_session == null)
            {
                return;
            }

            var fileNumber = 0;

            try
            {
                while (!cancellationToken.IsCancellationRequested && Stopwatch.GetElapsedTime(runStartTimestamp) < runDuration)
                {
                    fileNumber++;

                    var remoteFilePath = $@"{_folder}\file-{fileNumber}.ts";
                    var partialRemoteFilePath = remoteFilePath + _partialFileExtension;
                    var uploadStartTimestamp = Stopwatch.GetTimestamp();

                    using (var source = new MemoryStream(content, false))
                    {
                        _session.UploadFile(source, partialRemoteFilePath);
                    }

                    var uploadDuration = Stopwatch.GetElapsedTime(uploadStartTimestamp);
                    var renameStartTimestamp = Stopwatch.GetTimestamp();

                    _session.RenameFile(partialRemoteFilePath, remoteFilePath);

                    Uploads.Add(new UploadMeasurement
                    {
                        UploadDuration = uploadDuration,
                        RenameDuration = Stopwatch.GetElapsedTime(renameStartTimestamp),
                        SizeInBytes = content.Length
                    });
                }
            }
            catch (Exception exception)
            {
                Error = exception.Message;
            }
        }

        public void Dispose()
        {
            _session?.Dispose();
            _session = null;
        }
    }
}
