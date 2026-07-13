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
            IReadOnlyList<GameContentFieldDescriptor> fields)
        {
            Request = request;
            BackendSession = backendSession;
            BackendId = backendId;
            RecordKey = recordKey;
            SourceTarget = sourceTarget;
            OriginalRevision = originalRevision;
            Snapshot = snapshot;
            Fields = fields ?? Array.Empty<GameContentFieldDescriptor>();
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

    public sealed class GameContentEditSessionCoordinator : IDisposable
    {
        private static GameContentEditSessionCoordinator shared;
        private readonly Dictionary<string, GameContentActiveEditSession> _sessionsBySource =
            new Dictionary<string, GameContentActiveEditSession>(StringComparer.Ordinal);
        private readonly Dictionary<string, GameContentActiveEditSession> _sessionsByRecord =
            new Dictionary<string, GameContentActiveEditSession>(StringComparer.OrdinalIgnoreCase);
        private bool _disposed;

        public static GameContentEditSessionCoordinator Shared
        {
            get
            {
                if (shared == null || shared._disposed) shared = new GameContentEditSessionCoordinator();
                return shared;
            }
        }

        public event Action RefreshRequested;
        public int ActiveSourceCount => _sessionsBySource.Count;
        public int TrackedSessionCount => _sessionsByRecord.Values.Distinct().Count();

        public GameContentEditAvailability GetAvailability(
            GameContentPackContext context,
            GameContentRecordDescriptor record,
            string lensId = null)
        {
            if (_disposed) return GameContentEditAvailability.ReadOnly("The edit-session coordinator is unavailable.");
            if (context == null) return GameContentEditAvailability.ReadOnly("No content-pack context is available.");
            if (context.IsAllPacks) return GameContentEditAvailability.ReadOnly("All Packs is a read-only browsing context.");
            if (context.SelectedEntry == null || context.Pack == null)
                return GameContentEditAvailability.ReadOnly("No content pack is selected.");
            if (context.SelectedEntry.IsConflict)
                return GameContentEditAvailability.ReadOnly("Resolve the content-pack conflict before editing.");
            if (context.Pack.SourceState != GameContentPackSourceState.Available)
                return GameContentEditAvailability.ReadOnly("The selected content-pack source is unavailable.");
            if (record == null || record.CanonicalKey == null || context.ResolveRecord(record.CanonicalKey) == null)
                return GameContentEditAvailability.ReadOnly("Select a record owned by the current content pack.");
            if (!context.Access.CanEditExisting)
            {
                string reason = string.IsNullOrWhiteSpace(context.Access.DisabledReason)
                    ? "This content pack has not enabled existing-record editing."
                    : context.Access.DisabledReason;
                return GameContentEditAvailability.ReadOnly(reason);
            }

            if (!(context.Provider is IGameContentPackEditProvider editProvider))
            {
                string reason = context.IsProjectContent
                    ? "Project Content continues to use its existing provider-owned editing surface."
                    : "The content-pack provider has no safe editing backend.";
                return GameContentEditAvailability.ReadOnly(reason);
            }

            string providerId = GetProviderId(context);
            if (string.IsNullOrWhiteSpace(providerId))
                return GameContentEditAvailability.ReadOnly("The selected provider has no stable provider ID.");

            var request = new GameContentEditRequest(context.SelectionKey, record.CanonicalKey, providerId, lensId);
            GameContentEditAvailability availability;
            try
            {
                availability = editProvider.CanEdit(request);
            }
            catch (Exception exception)
            {
                return GameContentEditAvailability.ReadOnly(
                    "The editing backend could not report availability: " + exception.GetBaseException().Message,
                    providerId);
            }

            if (availability == null)
                return GameContentEditAvailability.ReadOnly("The editing backend returned no availability result.", providerId);
            if (!string.Equals(availability.BackendId, providerId, StringComparison.OrdinalIgnoreCase))
                return GameContentEditAvailability.ReadOnly("The editing backend identity does not match its registered provider.", providerId);
            if (!availability.IsEditable) return availability;
            if (availability.SupportedFieldCount <= 0)
                return GameContentEditAvailability.ReadOnly("This record exposes no safely editable fields.", providerId);
            if (availability.SourceTarget == null || !availability.SourceTarget.IsValid)
                return GameContentEditAvailability.ReadOnly("The editing backend did not provide a valid physical source target.", providerId);

            if (_sessionsBySource.TryGetValue(availability.SourceTarget.LockKey, out GameContentActiveEditSession locked))
            {
                if (locked.RecordKey.Equals(record.CanonicalKey))
                {
                    switch (locked.State)
                    {
                        case GameContentEditSessionState.Stale:
                            return GameContentEditAvailability.ReadOnly(
                                "The active edit session is stale. Cancel it and reopen the latest source before editing.",
                                providerId,
                                availability.SupportedFieldCount,
                                availability.SourceTarget);
                        case GameContentEditSessionState.Conflict:
                            return GameContentEditAvailability.ReadOnly(
                                "Resolve or cancel the active source conflict before editing.",
                                providerId,
                                availability.SupportedFieldCount,
                                availability.SourceTarget);
                        case GameContentEditSessionState.RecoveryRequired:
                            return GameContentEditAvailability.ReadOnly(
                                "Complete the active source recovery before editing.",
                                providerId,
                                availability.SupportedFieldCount,
                                availability.SourceTarget);
                        case GameContentEditSessionState.Committing:
                            return GameContentEditAvailability.ReadOnly(
                                "The active source transaction is committing.",
                                providerId,
                                availability.SupportedFieldCount,
                                availability.SourceTarget);
                        default:
                            return availability;
                    }
                }
                return GameContentEditAvailability.ReadOnly(
                    "The physical source is already being edited by '" + locked.RecordKey.SourceRecordId + "'. Finish or cancel that session first.",
                    providerId,
                    availability.SupportedFieldCount,
                    availability.SourceTarget);
            }

            return availability;
        }

        public GameContentEditBeginResult BeginEdit(
            GameContentPackContext context,
            GameContentRecordDescriptor record,
            string lensId = null)
        {
            GameContentEditAvailability availability = GetAvailability(context, record, lensId);
            if (!availability.IsEditable)
                return new GameContentEditBeginResult(false, availability.DisabledReason, availability, null, false);

            if (record != null && record.CanonicalKey != null &&
                _sessionsByRecord.TryGetValue(record.CanonicalKey.StableKey, out GameContentActiveEditSession existing))
            {
                if (!existing.IsTerminal)
                {
                    return new GameContentEditBeginResult(
                        true,
                        "Attached to the existing source edit session.",
                        GameContentEditAvailability.Editable(existing.BackendId, existing.Fields.Count, existing.SourceTarget),
                        existing,
                        true);
                }

                Dismiss(existing);
            }

            string providerId = GetProviderId(context);
            var request = new GameContentEditRequest(context.SelectionKey, record.CanonicalKey, providerId, lensId);
            IGameContentEditSession backendSession;
            try
            {
                backendSession = ((IGameContentPackEditProvider)context.Provider).BeginEdit(request);
            }
            catch (Exception exception)
            {
                return BeginFailure(
                    availability,
                    "The editing backend could not begin a session: " + exception.GetBaseException().Message);
            }

            if (backendSession == null) return BeginFailure(availability, "The editing backend returned no edit session.");

            GameContentActiveEditSession active;
            try
            {
                string backendId = backendSession.BackendId;
                GameContentRecordKey sessionRecordKey = backendSession.RecordKey;
                GameContentSourceTarget sourceTarget = backendSession.SourceTarget;
                GameContentSourceRevision originalRevision = backendSession.OriginalRevision;
                GameContentEditSnapshot snapshot = backendSession.Snapshot;
                GameContentFieldDescriptor[] fields = (backendSession.Fields ?? Array.Empty<GameContentFieldDescriptor>())
                    .Where(value => value != null)
                    .OrderBy(value => value.Order)
                    .ThenBy(value => value.DisplayName, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(value => value.FieldId, StringComparer.Ordinal)
                    .ToArray();

                string contractError = ValidateSessionContract(
                    request,
                    availability,
                    backendId,
                    sessionRecordKey,
                    sourceTarget,
                    originalRevision,
                    snapshot,
                    fields);
                if (!string.IsNullOrWhiteSpace(contractError))
                {
                    DisposeBackend(backendSession);
                    return BeginFailure(availability, contractError);
                }

                if (_sessionsBySource.TryGetValue(sourceTarget.LockKey, out GameContentActiveEditSession locked))
                {
                    DisposeBackend(backendSession);
                    if (locked.RecordKey.Equals(record.CanonicalKey))
                    {
                        return new GameContentEditBeginResult(
                            true,
                            "Attached to the existing source edit session.",
                            availability,
                            locked,
                            true);
                    }

                    return BeginFailure(
                        availability,
                        "The physical source became locked by another record before editing could begin.");
                }

                active = new GameContentActiveEditSession(
                    request,
                    backendSession,
                    backendId,
                    sessionRecordKey,
                    sourceTarget,
                    originalRevision,
                    snapshot,
                    fields);
                if (!RefreshFromBackend(active, false) || active.State != GameContentEditSessionState.Clean)
                {
                    DisposeBackend(backendSession);
                    return BeginFailure(
                        availability,
                        string.IsNullOrWhiteSpace(active.Message)
                            ? "A new edit session must begin in the Clean state."
                            : active.Message);
                }
            }
            catch (Exception exception)
            {
                DisposeBackend(backendSession);
                return BeginFailure(
                    availability,
                    "The editing backend returned an invalid session: " + exception.GetBaseException().Message);
            }

            _sessionsBySource.Add(active.SourceTarget.LockKey, active);
            _sessionsByRecord.Add(active.RecordKey.StableKey, active);
            return new GameContentEditBeginResult(true, "Editing session started.", availability, active, false);
        }

        public bool TryGetSession(GameContentRecordKey recordKey, out GameContentActiveEditSession session)
        {
            session = null;
            return recordKey != null && _sessionsByRecord.TryGetValue(recordKey.StableKey, out session);
        }

        public GameContentEditOperationResult Apply(
            GameContentActiveEditSession active,
            string fieldId,
            GameContentFieldValue value)
        {
            if (!Owns(active)) return GameContentEditOperationResult.Failure("The edit session is no longer active.");
            if (!CanMutate(active)) return GameContentEditOperationResult.Failure("The edit session cannot accept changes in its current state.");
            GameContentFieldDescriptor field = active.Fields.FirstOrDefault(candidate =>
                string.Equals(candidate.FieldId, fieldId, StringComparison.Ordinal));
            if (field == null) return GameContentEditOperationResult.Failure("The field is not exposed by this edit session.");
            if (field.IsReadOnly) return GameContentEditOperationResult.Failure(field.ReadOnlyReason);
            if (value == null || value.FieldType != field.FieldType)
                return GameContentEditOperationResult.Failure("The proposed value does not match the field type.");

            try
            {
                GameContentEditOperationResult result = active.BackendSession.Apply(field.FieldId, value)
                    ?? GameContentEditOperationResult.Failure("The editing backend returned no apply result.");
                RefreshFromBackend(active, false);
                active.Message = result.Message;
                return result;
            }
            catch (Exception exception)
            {
                return OperationException(active, "Apply", exception, false);
            }
        }

        public GameContentEditOperationResult Undo(GameContentActiveEditSession active)
        {
            if (!Owns(active)) return GameContentEditOperationResult.Failure("The edit session is no longer active.");
            if (!CanMutate(active) || !active.CanUndo) return GameContentEditOperationResult.Failure("There is no staged change to undo.");
            try
            {
                GameContentEditOperationResult result = active.BackendSession.Undo()
                    ?? GameContentEditOperationResult.Failure("The editing backend returned no Undo result.");
                RefreshFromBackend(active, false);
                active.Message = result.Message;
                return result;
            }
            catch (Exception exception)
            {
                return OperationException(active, "Undo", exception, false);
            }
        }

        public GameContentEditOperationResult Redo(GameContentActiveEditSession active)
        {
            if (!Owns(active)) return GameContentEditOperationResult.Failure("The edit session is no longer active.");
            if (!CanMutate(active) || !active.CanRedo) return GameContentEditOperationResult.Failure("There is no staged change to redo.");
            try
            {
                GameContentEditOperationResult result = active.BackendSession.Redo()
                    ?? GameContentEditOperationResult.Failure("The editing backend returned no Redo result.");
                RefreshFromBackend(active, false);
                active.Message = result.Message;
                return result;
            }
            catch (Exception exception)
            {
                return OperationException(active, "Redo", exception, false);
            }
        }

        public GameContentValidationPreview Preview(GameContentActiveEditSession active)
        {
            if (!Owns(active)) return GameContentValidationPreview.Error("Editing", "The edit session is no longer active.");
            if (active.State == GameContentEditSessionState.Committing)
                return GameContentValidationPreview.Error("Editing", "Validation is unavailable while the source is committing.");
            try
            {
                active.Validation = active.BackendSession.Preview()
                    ?? GameContentValidationPreview.Error("Editing", "The editing backend returned no validation preview.");
                active.Message = BuildValidationMessage(active.Validation);
                RefreshFromBackend(active, false);
                return active.Validation;
            }
            catch (Exception exception)
            {
                active.Validation = GameContentValidationPreview.Error(
                    "Editing",
                    "Validation preview failed: " + exception.GetBaseException().Message);
                active.Message = active.Validation.Issues[0].Message;
                return active.Validation;
            }
        }

        public GameContentStaleCheckResult CheckStale(GameContentActiveEditSession active)
        {
            if (!Owns(active))
                return GameContentStaleCheckResult.Stale("The edit session is no longer active.", active?.OriginalRevision);
            if (active.State == GameContentEditSessionState.Committing)
                return GameContentStaleCheckResult.Stale("The source revision cannot be checked while committing.", active.OriginalRevision);
            try
            {
                active.StaleCheck = active.BackendSession.CheckStale()
                    ?? GameContentStaleCheckResult.Stale("The editing backend returned no stale-source result.", active.OriginalRevision);
                if (active.StaleCheck.IsStale)
                {
                    active.State = GameContentEditSessionState.Stale;
                    active.Message = string.IsNullOrWhiteSpace(active.StaleCheck.Message)
                        ? "The source changed after editing began."
                        : active.StaleCheck.Message;
                }
                else
                {
                    RefreshFromBackend(active, false);
                }
                return active.StaleCheck;
            }
            catch (Exception exception)
            {
                active.State = GameContentEditSessionState.Conflict;
                active.Message = "The source revision could not be verified: " + exception.GetBaseException().Message;
                active.StaleCheck = GameContentStaleCheckResult.Stale(active.Message, active.OriginalRevision);
                return active.StaleCheck;
            }
        }

        public GameContentCommitResult Commit(GameContentActiveEditSession active, bool confirmWarnings)
        {
            if (!Owns(active)) return GameContentCommitResult.Failure("The edit session is no longer active.", active?.OriginalRevision);
            if (active.State != GameContentEditSessionState.Dirty)
                return GameContentCommitResult.Failure("Only a dirty edit session can be committed.", active.OriginalRevision);

            GameContentStaleCheckResult stale = CheckStale(active);
            if (stale.IsStale)
                return GameContentCommitResult.Failure(active.Message, active.OriginalRevision);

            GameContentValidationPreview preview = Preview(active);
            if (!preview.CanCommit)
                return GameContentCommitResult.Failure("Fix validation errors before committing.", active.OriginalRevision);
            if (preview.RequiresWarningConfirmation && !confirmWarnings)
                return GameContentCommitResult.Failure("Confirm the validation warnings before committing.", active.OriginalRevision);

            active.State = GameContentEditSessionState.Committing;
            active.Message = "Committing source changes.";
            try
            {
                GameContentCommitResult result = active.BackendSession.Commit(confirmWarnings);
                if (result == null)
                    return CommitException(active, new InvalidOperationException("The editing backend returned no commit result."));

                bool refreshed = RefreshFromBackend(active, true);
                if (!refreshed)
                {
                    GameContentRecoveryRecord recovery = result.Recovery ?? active.Recovery ??
                                                         BuildRecovery(active, "Commit state refresh exception", active.Message);
                    active.State = GameContentEditSessionState.RecoveryRequired;
                    active.Recovery = recovery;
                    active.CommitResult = new GameContentCommitResult(
                        false,
                        active.Message,
                        result.PreviousRevision ?? active.OriginalRevision,
                        result.NewRevision ?? active.OriginalRevision,
                        result.RequiresRefresh,
                        result.RequiresRebind,
                        result.RequiresRestart,
                        recovery);
                    return active.CommitResult;
                }
                active.Message = result.Message;
                active.Recovery = result.Recovery;
                active.CommitResult = result;
                if (result.Succeeded)
                {
                    active.State = GameContentEditSessionState.Committed;
                    RefreshRequested?.Invoke();
                }
                else if (result.Recovery != null)
                {
                    active.State = GameContentEditSessionState.RecoveryRequired;
                }
                else if (active.State == GameContentEditSessionState.Committing)
                {
                    active.State = GameContentEditSessionState.Dirty;
                }
                return result;
            }
            catch (Exception exception)
            {
                return CommitException(active, exception);
            }
        }

        public GameContentRollbackResult Rollback(GameContentActiveEditSession active)
        {
            if (!Owns(active)) return GameContentRollbackResult.Failure("The edit session is no longer active.", active?.OriginalRevision);
            if (active.State == GameContentEditSessionState.Committing)
                return GameContentRollbackResult.Failure("Rollback is disabled while committing.", active.OriginalRevision);
            try
            {
                GameContentRollbackResult result = active.BackendSession.Rollback()
                    ?? GameContentRollbackResult.Failure("The editing backend returned no rollback result.", active.OriginalRevision);
                bool refreshed = RefreshFromBackend(active, true);
                if (!refreshed)
                {
                    GameContentRecoveryRecord recovery = result.Recovery ?? active.Recovery ??
                                                         BuildRecovery(active, "Rollback state refresh exception", active.Message);
                    active.State = GameContentEditSessionState.RecoveryRequired;
                    active.Recovery = recovery;
                    active.RollbackResult = GameContentRollbackResult.Failure(
                        active.Message,
                        result.RestoredRevision ?? active.OriginalRevision,
                        recovery);
                    return active.RollbackResult;
                }
                active.Message = result.Message;
                active.Recovery = result.Recovery;
                active.RollbackResult = result;
                if (result.Succeeded)
                {
                    active.State = GameContentEditSessionState.RolledBack;
                    Remove(active, true);
                    RefreshRequested?.Invoke();
                }
                else if (result.Recovery != null)
                {
                    active.State = GameContentEditSessionState.RecoveryRequired;
                }
                return result;
            }
            catch (Exception exception)
            {
                GameContentRecoveryRecord recovery = BuildRecovery(active, "Rollback exception", exception.GetBaseException().Message);
                active.State = GameContentEditSessionState.RecoveryRequired;
                active.Recovery = recovery;
                active.Message = "Rollback failed: " + exception.GetBaseException().Message;
                active.RollbackResult = GameContentRollbackResult.Failure(active.Message, active.OriginalRevision, recovery);
                return active.RollbackResult;
            }
        }

        public GameContentRollbackResult Cancel(GameContentActiveEditSession active)
        {
            return Rollback(active);
        }

        public bool Dismiss(GameContentActiveEditSession active)
        {
            if (!Owns(active) || !active.IsTerminal) return false;
            Remove(active, true);
            return true;
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
                if (!stillAvailable) CloseForReset(active);
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

        internal static void ResetSharedForTests()
        {
            if (shared == null) return;
            shared.Reset();
            shared.RefreshRequested = null;
            shared = null;
        }

        public void Dispose()
        {
            if (_disposed) return;
            Reset();
            RefreshRequested = null;
            _disposed = true;
        }

        private static string ValidateSessionContract(
            GameContentEditRequest request,
            GameContentEditAvailability availability,
            string backendId,
            GameContentRecordKey recordKey,
            GameContentSourceTarget sourceTarget,
            GameContentSourceRevision originalRevision,
            GameContentEditSnapshot snapshot,
            IReadOnlyList<GameContentFieldDescriptor> fields)
        {
            if (!string.Equals(backendId, request.ProviderId, StringComparison.OrdinalIgnoreCase))
                return "The edit session backend ID does not match the registered provider.";
            if (recordKey == null || !recordKey.Equals(request.RecordKey))
                return "The edit session does not target the requested canonical record.";
            if (sourceTarget == null || !sourceTarget.IsValid ||
                !sourceTarget.Equals(availability.SourceTarget))
                return "The edit session does not target the source reported by availability.";
            if (originalRevision == null || !originalRevision.IsValid)
                return "The edit session has no valid original source revision.";
            if (snapshot == null || snapshot.RecordKey == null || !snapshot.RecordKey.Equals(recordKey) ||
                snapshot.SourceTarget == null || !snapshot.SourceTarget.Equals(sourceTarget) ||
                snapshot.SourceRevision == null || !snapshot.SourceRevision.Equals(originalRevision))
                return "The edit session snapshot does not match the requested record and source.";
            if (fields == null || fields.Count == 0 || fields.All(value => value.IsReadOnly))
                return "The edit session exposes no writable scalar fields.";
            if (fields.Any(value => !value.IsValid)) return "The edit session exposes a field without a stable field ID.";
            if (fields.GroupBy(value => value.FieldId, StringComparer.Ordinal).Any(group => group.Count() > 1))
                return "The edit session exposes duplicate field IDs.";
            if (fields.Any(value => !Enum.IsDefined(typeof(GameContentFieldType), value.FieldType)))
                return "The edit session exposes an unsupported field type.";
            if (fields.Any(value => !snapshot.FieldValues.ContainsKey(value.FieldId)))
                return "The edit session snapshot is missing an exposed field value.";
            if (fields.Any(value => snapshot.FieldValues[value.FieldId] == null ||
                                    snapshot.FieldValues[value.FieldId].FieldType != value.FieldType))
                return "The edit session snapshot contains a field value with the wrong type.";
            return string.Empty;
        }

        private static string GetProviderId(GameContentPackContext context)
        {
            if (context?.Provider is IGameContentAuthoringProvider authoringProvider)
                return authoringProvider.ProviderId ?? string.Empty;
            return context?.Pack?.ProviderId ?? string.Empty;
        }

        private bool RefreshFromBackend(GameContentActiveEditSession active, bool recoveryOnFailure)
        {
            try
            {
                active.State = active.BackendSession.State;
                active.Changes = (active.BackendSession.Changes ?? Array.Empty<GameContentProposedChange>())
                    .Where(value => value != null)
                    .OrderBy(value => value.Order)
                    .ThenBy(value => value.DisplayName, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(value => value.FieldId, StringComparer.Ordinal)
                    .ToArray();
                active.CanUndo = active.BackendSession.CanUndo;
                active.CanRedo = active.BackendSession.CanRedo;
                return true;
            }
            catch (Exception exception)
            {
                active.State = recoveryOnFailure
                    ? GameContentEditSessionState.RecoveryRequired
                    : GameContentEditSessionState.Conflict;
                active.Message = "The editing backend state could not be read: " + exception.GetBaseException().Message;
                if (recoveryOnFailure)
                    active.Recovery = BuildRecovery(active, "State refresh exception", active.Message);
                return false;
            }
        }

        private GameContentCommitResult CommitException(GameContentActiveEditSession active, Exception exception)
        {
            string detail = exception.GetBaseException().Message;
            GameContentRecoveryRecord recovery = BuildRecovery(active, "Commit exception", detail);
            active.State = GameContentEditSessionState.RecoveryRequired;
            active.Message = "Commit failed and requires recovery review: " + detail;
            active.Recovery = recovery;
            active.CommitResult = GameContentCommitResult.Failure(active.Message, active.OriginalRevision, recovery);
            return active.CommitResult;
        }

        private static GameContentEditOperationResult OperationException(
            GameContentActiveEditSession active,
            string operation,
            Exception exception,
            bool recoveryRequired)
        {
            active.State = recoveryRequired
                ? GameContentEditSessionState.RecoveryRequired
                : GameContentEditSessionState.Conflict;
            active.Message = operation + " failed: " + exception.GetBaseException().Message;
            if (recoveryRequired)
                active.Recovery = BuildRecovery(active, operation + " exception", active.Message);
            return GameContentEditOperationResult.Failure(active.Message);
        }

        private bool Owns(GameContentActiveEditSession active)
        {
            return active != null &&
                   _sessionsByRecord.TryGetValue(active.RecordKey.StableKey, out GameContentActiveEditSession tracked) &&
                   ReferenceEquals(active, tracked);
        }

        private static bool CanMutate(GameContentActiveEditSession active)
        {
            return active.State == GameContentEditSessionState.Clean ||
                   active.State == GameContentEditSessionState.Dirty;
        }

        private static string BuildValidationMessage(GameContentValidationPreview preview)
        {
            if (preview == null) return "No validation preview is available.";
            if (preview.ErrorCount > 0) return preview.ErrorCount + " validation error(s).";
            if (preview.WarningCount > 0) return preview.WarningCount + " warning(s) require review.";
            return "Validation passed.";
        }

        private static GameContentRecoveryRecord BuildRecovery(
            GameContentActiveEditSession active,
            string phase,
            string message)
        {
            return new GameContentRecoveryRecord(
                active?.BackendId,
                active?.SourceTarget?.LockKey,
                active?.SourceTarget?.SourceLabel,
                active?.OriginalRevision,
                active?.StaleCheck?.CurrentRevision ?? active?.OriginalRevision,
                DateTime.UtcNow,
                phase,
                "Review the source with its provider before editing again. " + (message ?? string.Empty));
        }

        private static GameContentEditBeginResult BeginFailure(
            GameContentEditAvailability availability,
            string message)
        {
            return new GameContentEditBeginResult(false, message, availability, null, false);
        }

        private void Remove(GameContentActiveEditSession active, bool disposeBackend)
        {
            if (active == null) return;
            if (_sessionsBySource.TryGetValue(active.SourceTarget.LockKey, out GameContentActiveEditSession source) &&
                ReferenceEquals(source, active))
                _sessionsBySource.Remove(active.SourceTarget.LockKey);
            if (_sessionsByRecord.TryGetValue(active.RecordKey.StableKey, out GameContentActiveEditSession record) &&
                ReferenceEquals(record, active))
                _sessionsByRecord.Remove(active.RecordKey.StableKey);
            if (disposeBackend) DisposeBackend(active.BackendSession);
        }

        private void CloseForReset(GameContentActiveEditSession active)
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

        private static void DisposeBackend(IGameContentEditSession backendSession)
        {
            if (backendSession == null) return;
            try
            {
                backendSession.Dispose();
            }
            catch
            {
                // Disposal is best-effort; providers must surface durable recovery before this point.
            }
        }
    }
}
