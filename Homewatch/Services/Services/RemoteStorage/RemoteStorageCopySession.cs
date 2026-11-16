using Homewatch.Contracts.Abstractions.Services;
using Homewatch.Contracts.Dtos.RemoteStorageDtos;

namespace Homewatch.Core.Services.RemoteStorage
{
    public class RemoteStorageCopySession
    {
        private const string _partialFileExtension = ".partial";

        private readonly HashSet<string> _createdDirectories = new(StringComparer.OrdinalIgnoreCase);
        private readonly ISmbConnectionService _smbConnectionService;
        private readonly RemoteStorageTargetDto _target;

        private ISmbShareSession? _session;

        public RemoteStorageCopySession(ISmbConnectionService smbConnectionService, RemoteStorageTargetDto target)
        {
            _smbConnectionService = smbConnectionService;
            _target = target;
        }

        public long CopyVideoChunk(string localChunkPath, VideoChunkReplicationItemDto item)
        {
            var session = GetSession();

            var dayFolderName = Path.GetFileName(Path.GetDirectoryName(localChunkPath)) ?? string.Empty;
            var remoteDirectory = $@"{item.DeviceId}\{dayFolderName}";
            var remoteChunkPath = $@"{remoteDirectory}\{item.FileName}";
            var partialRemoteChunkPath = remoteChunkPath + _partialFileExtension;

            if (_createdDirectories.Add(remoteDirectory))
            {
                session.CreateDirectory(remoteDirectory);
            }

            long chunkSizeInBytes;

            using (var source = new FileStream(localChunkPath, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete))
            {
                chunkSizeInBytes = source.Length;
                session.UploadFile(source, partialRemoteChunkPath);
            }

            session.RenameFile(partialRemoteChunkPath, remoteChunkPath);

            return chunkSizeInBytes;
        }

        public void Close()
        {
            _session?.Dispose();
            _session = null;
            _createdDirectories.Clear();
        }

        private ISmbShareSession GetSession()
        {
            if (_session is { IsConnected: true })
            {
                return _session;
            }

            Close();

            _session = _smbConnectionService.OpenSession(_target);

            return _session;
        }
    }
}
