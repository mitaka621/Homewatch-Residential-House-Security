using Homewatch.Contracts.Abstractions.Services;
using Homewatch.Contracts.Dtos.RemoteStorageDtos;
using Homewatch.Core.Utilities;

namespace Homewatch.Core.Services.RemoteStorage
{
    public class RemoteStorageCopySession
    {
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

            if (_createdDirectories.Add(remoteDirectory))
            {
                session.CreateDirectory(remoteDirectory);
            }

            long chunkSizeInBytes;
            string partialRemoteChunkPath;

            using (var source = new FileStream(localChunkPath, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete))
            {
                chunkSizeInBytes = source.Length;
                partialRemoteChunkPath = UploadPartialChunk(session, source, remoteChunkPath);
            }

            session.RenameFile(partialRemoteChunkPath, remoteChunkPath);

            return chunkSizeInBytes;
        }

        // A partial file that was being written when its connection dropped stays locked on the share until the server notices the dropped connection, which can take hours.
        private string UploadPartialChunk(ISmbShareSession session, Stream source, string remoteChunkPath)
        {
            foreach (var partialRemoteChunkPath in VideoChunkStorageUtilities.GetPartialChunkPaths(remoteChunkPath))
            {
                if (session.TryUploadFile(source, partialRemoteChunkPath))
                {
                    return partialRemoteChunkPath;
                }
            }

            throw new IOException($"Uploading {remoteChunkPath} failed: all of its partial files are in use by other connections.");
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
