using Homewatch.Contracts.Abstractions.Services;
using Homewatch.Contracts.Dtos.RemoteStorageDtos;
using Homewatch.Core.Utilities;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Diagnostics;

namespace Homewatch.Core.Services.RemoteStorage
{
    public class SmbReadSessionManager : ISmbReadSessionManager, IDisposable
    {
        private const int MaxSessionsPerTarget = 4;
        private const double _bytesPerMegabyte = 1024 * 1024;

        private static readonly TimeSpan _idleSessionTimeout = TimeSpan.FromMinutes(5);

        private readonly ISmbConnectionService _smbConnectionService;
        private readonly ILogger<SmbReadSessionManager> _logger;
        private readonly ConcurrentDictionary<Guid, SemaphoreSlim> _sessionLimits = new();
        private readonly ConcurrentDictionary<Guid, ConcurrentQueue<(ISmbShareSession Session, string ConnectionKey, DateTime LastUsedUtc)>> _idleSessions = new();

        public SmbReadSessionManager(ISmbConnectionService smbConnectionService, ILogger<SmbReadSessionManager> logger)
        {
            _smbConnectionService = smbConnectionService;
            _logger = logger;
        }

        public async Task<byte[]?> ReadFileAsync(RemoteStorageTargetDto target, string relativePath, CancellationToken cancellationToken)
        {
            var sessionLimit = _sessionLimits.GetOrAdd(target.Id, _ => new SemaphoreSlim(MaxSessionsPerTarget, MaxSessionsPerTarget));

            var sessionWaitStartTimestamp = Stopwatch.GetTimestamp();

            await sessionLimit.WaitAsync(cancellationToken);

            var sessionWaitDuration = Stopwatch.GetElapsedTime(sessionWaitStartTimestamp);

            try
            {
                return await SmbOperationRunner.RunAsync(() => ReadWithPooledSession(target, relativePath, sessionWaitDuration), cancellationToken);
            }
            finally
            {
                sessionLimit.Release();
            }
        }

        public void Dispose()
        {
            foreach (var idleSessions in _idleSessions.Values)
            {
                while (idleSessions.TryDequeue(out var idleSession))
                {
                    idleSession.Session.Dispose();
                }
            }
        }

        private byte[]? ReadWithPooledSession(RemoteStorageTargetDto target, string relativePath, TimeSpan sessionWaitDuration)
        {
            var readStartTimestamp = Stopwatch.GetTimestamp();
            var connectionKey = $"{target.SharePath}|{target.Username}|{target.ProtectedPassword}";
            var idleSessions = _idleSessions.GetOrAdd(target.Id, _ => new());
            var session = TakeIdleSession(idleSessions, connectionKey);
            var isSessionReused = session != null;

            session ??= _smbConnectionService.OpenSession(target);

            var connectDuration = Stopwatch.GetElapsedTime(readStartTimestamp);

            try
            {
                var content = session.ReadAllBytes(relativePath);

                idleSessions.Enqueue((session, connectionKey, DateTime.UtcNow));

                _logger.LogInformation(
                    "Remote read of {RelativePath} from {SharePath}: {SizeInMegabytes:0.00} megabytes in {ReadMilliseconds:0} milliseconds ({SessionState} session, connect {ConnectMilliseconds:0}, waited {SessionWaitMilliseconds:0} for a free session).",
                    relativePath,
                    target.SharePath,
                    (content?.Length ?? 0) / _bytesPerMegabyte,
                    Stopwatch.GetElapsedTime(readStartTimestamp).TotalMilliseconds,
                    isSessionReused ? "reused" : "new",
                    connectDuration.TotalMilliseconds,
                    sessionWaitDuration.TotalMilliseconds);

                return content;
            }
            catch
            {
                session.Dispose();
                throw;
            }
        }

        private ISmbShareSession? TakeIdleSession(
            ConcurrentQueue<(ISmbShareSession Session, string ConnectionKey, DateTime LastUsedUtc)> idleSessions,
            string connectionKey)
        {
            while (idleSessions.TryDequeue(out var idleSession))
            {
                if (idleSession.ConnectionKey == connectionKey
                    && idleSession.Session.IsConnected
                    && DateTime.UtcNow - idleSession.LastUsedUtc < _idleSessionTimeout)
                {
                    return idleSession.Session;
                }

                idleSession.Session.Dispose();
            }

            return null;
        }
    }
}
