using System;
using System.Collections.Generic;
using System.Linq;

namespace Deucarian.GameContentAuthoring.Editor
{
    public sealed class GameContentEditBeginResult
    {
        public GameContentEditBeginResult(
            bool succeeded,
            string message,
            GameContentEditAvailability availability,
            GameContentActiveEditSession session,
            bool attachedExisting)
        {
            Succeeded = succeeded;
            Message = message ?? string.Empty;
            Availability = availability;
            Session = session;
            AttachedExisting = attachedExisting;
        }

        public bool Succeeded { get; }
        public string Message { get; }
        public GameContentEditAvailability Availability { get; }
        public GameContentActiveEditSession Session { get; }
        public bool AttachedExisting { get; }
    }

    public sealed class GameContentActiveEditSession
    {
        internal GameContentActiveEditSession(
            GameContentEditRequest request,
            IGameContentEditSession backendSession,
            string backendId,
            GameContentRecordKey recordKey,
            GameContentSourceTarget sourceTarget,
            GameContentSourceRevision originalRevision,
            GameContentEditSnapshot snapshot,
            IReadOnlyList<GameContentFieldDescriptor> fields,
            IReadOnlyList<GameContentRecordDescriptor> packRecords)
        {
            Request = request;
            BackendSession = backendSession;
            BackendId = backendId;
            RecordKey = recordKey;
            SourceTarget = sourceTarget;
            OriginalRevision = originalRevision;
            Snapshot = snapshot;
            Fields = fields ?? Array.Empty<GameContentFieldDescriptor>();
            PackRecords = packRecords ?? Array.Empty<GameContentRecordDescriptor>();
            Changes = Array.Empty<GameContentProposedChange>();
            Validation = GameContentValidationPreview.Valid;
            StaleCheck = GameContentStaleCheckResult.Current(originalRevision);
            State = GameContentEditSessionState.Clean;
        }

        internal GameContentEditRequest Request { get; }
        internal IGameContentEditSession BackendSession { get; }
        public string BackendId { get; }
        public GameContentRecordKey RecordKey { get; }
        public GameContentSourceTarget SourceTarget { get; }
        public GameContentSourceRevision OriginalRevision { get; }
        public GameContentEditSnapshot Snapshot { get; }
        public IReadOnlyList<GameContentFieldDescriptor> Fields { get; }
        internal IReadOnlyList<GameContentRecordDescriptor> PackRecords { get; set; }
        public IReadOnlyList<GameContentProposedChange> Changes { get; internal set; }
        public GameContentEditSessionState State { get; internal set; }
        public GameContentValidationPreview Validation { get; internal set; }
        public GameContentStaleCheckResult StaleCheck { get; internal set; }
        public GameContentRecoveryRecord Recovery { get; internal set; }
        public GameContentCommitResult CommitResult { get; internal set; }
        public GameContentRollbackResult RollbackResult { get; internal set; }
        public string Message { get; internal set; } = string.Empty;
        public bool CanUndo { get; internal set; }
        public bool CanRedo { get; internal set; }
        public bool IsTerminal => State == GameContentEditSessionState.Committed ||
                                  State == GameContentEditSessionState.RolledBack;

        public GameContentFieldValue GetEffectiveValue(string fieldId)
        {
            GameContentProposedChange change = Changes.LastOrDefault(value =>
                string.Equals(value.FieldId, fieldId, StringComparison.Ordinal));
            if (change != null) return change.ProposedValue;
            return Snapshot != null && Snapshot.TryGetValue(fieldId, out GameContentFieldValue value)
                ? value
                : null;
        }
    }
}
