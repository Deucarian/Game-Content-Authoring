using System;
using System.Collections.Generic;
using System.Linq;

namespace Deucarian.GameContentAuthoring.Editor
{
    internal sealed class GameContentEditSessionRegistry : IDisposable
    {
        private readonly Dictionary<string, GameContentActiveEditSession> _sessionsBySource =
            new Dictionary<string, GameContentActiveEditSession>(StringComparer.Ordinal);
        private readonly Dictionary<string, GameContentActiveEditSession> _sessionsByRecord =
            new Dictionary<string, GameContentActiveEditSession>(StringComparer.OrdinalIgnoreCase);
        private bool _disposed;

        internal bool IsDisposed => _disposed;
        internal int ActiveSourceCount => _sessionsBySource.Count;
        internal int TrackedSessionCount => _sessionsByRecord.Values.Distinct().Count();

        internal bool TryGetSource(string key, out GameContentActiveEditSession session)
            => _sessionsBySource.TryGetValue(key, out session);
        internal bool TryGetRecord(string key, out GameContentActiveEditSession session)
            => _sessionsByRecord.TryGetValue(key, out session);

        internal void Add(GameContentActiveEditSession active)
        {
            _sessionsBySource.Add(active.SourceTarget.LockKey, active);
            _sessionsByRecord.Add(active.RecordKey.StableKey, active);
        }

        internal bool Owns(GameContentActiveEditSession active)
        {
            return active != null &&
                   _sessionsByRecord.TryGetValue(active.RecordKey.StableKey, out GameContentActiveEditSession tracked) &&
                   ReferenceEquals(active, tracked);
        }

        internal void Remove(GameContentActiveEditSession active, bool disposeBackend)
        {
            if (active == null) return;
            if (_sessionsBySource.TryGetValue(active.SourceTarget.LockKey, out GameContentActiveEditSession source) &&
                ReferenceEquals(source, active))
                _sessionsBySource.Remove(active.SourceTarget.LockKey);
            if (_sessionsByRecord.TryGetValue(active.RecordKey.StableKey, out GameContentActiveEditSession record) &&
                ReferenceEquals(record, active))
                _sessionsByRecord.Remove(active.RecordKey.StableKey);
            if (disposeBackend) GameContentEditBackendState.DisposeBackend(active.BackendSession);
        }

        internal void CloseForReset(GameContentActiveEditSession active)
        {
            if (active == null) return;
            if (!active.IsTerminal && active.State != GameContentEditSessionState.RecoveryRequired &&
                active.State != GameContentEditSessionState.Committing)
            {
                try
                {
                    active.BackendSession.Rollback();
                    active.State = GameContentEditSessionState.RolledBack;
                }
                catch
                {
                    active.State = GameContentEditSessionState.RecoveryRequired;
                }
            }
            Remove(active, true);
        }

        public void Reconcile(GameContentPackCatalog catalog)
        {
            if (_disposed) return;
            GameContentActiveEditSession[] sessions = _sessionsByRecord.Values.Distinct().ToArray();
            for (int i = 0; i < sessions.Length; i++)
            {
                GameContentActiveEditSession active = sessions[i];
                GameContentPackCatalogEntry entry = catalog?.Find(active.Request.SelectedPackKey);
                bool stillAvailable = entry != null &&
                                      !entry.IsConflict &&
                                      entry.Pack.SourceState == GameContentPackSourceState.Available &&
                                       string.Equals(entry.Pack.ProviderId, active.BackendId, StringComparison.OrdinalIgnoreCase) &&
                                       entry.Records.Any(record => record.CanonicalKey.Equals(active.RecordKey));
                if (!stillAvailable)
                {
                    CloseForReset(active);
                    continue;
                }

                active.PackRecords = entry.Records ?? Array.Empty<GameContentRecordDescriptor>();
            }
        }

        public void Reset()
        {
            if (_disposed) return;
            GameContentActiveEditSession[] sessions = _sessionsByRecord.Values.Distinct().ToArray();
            for (int i = 0; i < sessions.Length; i++) CloseForReset(sessions[i]);
            _sessionsByRecord.Clear();
            _sessionsBySource.Clear();
        }

        public bool TryGetSession(GameContentRecordKey recordKey, out GameContentActiveEditSession session)
        {
            session = null;
            return recordKey != null && _sessionsByRecord.TryGetValue(recordKey.StableKey, out session);
        }

        public void Dispose()
        {
            if (_disposed) return;
            Reset();
            _disposed = true;
        }
    }
}
