using System;
using System.Collections.Generic;
using System.Linq;

namespace Deucarian.GameContentAuthoring.Editor
{
    internal sealed class GameContentEditSessionOpening
    {
        private readonly GameContentEditSessionRegistry _sessions;

        internal GameContentEditSessionOpening(GameContentEditSessionRegistry sessions)
        {
            _sessions = sessions;
        }

        public GameContentEditAvailability GetAvailability(
            GameContentPackContext context,
            GameContentRecordDescriptor record,
            string lensId = null)
        {
            if (_sessions.IsDisposed) return GameContentEditAvailability.ReadOnly("The edit-session coordinator is unavailable.");
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

            string providerId = GameContentEditSessionContract.GetProviderId(context);
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

            if (_sessions.TryGetSource(availability.SourceTarget.LockKey, out GameContentActiveEditSession locked))
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
                _sessions.TryGetRecord(record.CanonicalKey.StableKey, out GameContentActiveEditSession existing))
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

                _sessions.Remove(existing, true);
            }

            string providerId = GameContentEditSessionContract.GetProviderId(context);
            var request = new GameContentEditRequest(context.SelectionKey, record.CanonicalKey, providerId, lensId);
            IGameContentEditSession backendSession;
            try
            {
                backendSession = ((IGameContentPackEditProvider)context.Provider).BeginEdit(request);
            }
            catch (Exception exception)
            {
                return GameContentEditSessionContract.BeginFailure(
                    availability,
                    "The editing backend could not begin a session: " + exception.GetBaseException().Message);
            }

            if (backendSession == null) return GameContentEditSessionContract.BeginFailure(availability, "The editing backend returned no edit session.");

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

                string contractError = GameContentEditSessionContract.ValidateSessionContract(
                    request,
                    availability,
                    backendId,
                    sessionRecordKey,
                    sourceTarget,
                    originalRevision,
                    snapshot,
                    fields,
                    backendSession);
                if (!string.IsNullOrWhiteSpace(contractError))
                {
                    GameContentEditBackendState.DisposeBackend(backendSession);
                    return GameContentEditSessionContract.BeginFailure(availability, contractError);
                }

                if (_sessions.TryGetSource(sourceTarget.LockKey, out GameContentActiveEditSession locked))
                {
                    GameContentEditBackendState.DisposeBackend(backendSession);
                    if (locked.RecordKey.Equals(record.CanonicalKey))
                    {
                        return new GameContentEditBeginResult(
                            true,
                            "Attached to the existing source edit session.",
                            availability,
                            locked,
                            true);
                    }

                    return GameContentEditSessionContract.BeginFailure(
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
                    fields,
                    context.Records);
                if (!GameContentEditBackendState.RefreshFromBackend(active, false) || active.State != GameContentEditSessionState.Clean)
                {
                    GameContentEditBackendState.DisposeBackend(backendSession);
                    return GameContentEditSessionContract.BeginFailure(
                        availability,
                        string.IsNullOrWhiteSpace(active.Message)
                            ? "A new edit session must begin in the Clean state."
                            : active.Message);
                }
            }
            catch (Exception exception)
            {
                GameContentEditBackendState.DisposeBackend(backendSession);
                return GameContentEditSessionContract.BeginFailure(
                    availability,
                    "The editing backend returned an invalid session: " + exception.GetBaseException().Message);
            }

            _sessions.Add(active);
            return new GameContentEditBeginResult(true, "Editing session started.", availability, active, false);
        }
    }
}
