using System;
using System.Collections.Generic;
using System.Linq;

namespace Deucarian.GameContentAuthoring.Editor
{
    internal static class GameContentEditBackendState
    {
        internal static bool RefreshFromBackend(GameContentActiveEditSession active, bool recoveryOnFailure)
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

        internal static GameContentEditOperationResult OperationException(
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

        internal static bool CanMutate(GameContentActiveEditSession active)
        {
            return active.State == GameContentEditSessionState.Clean ||
                   active.State == GameContentEditSessionState.Dirty;
        }

        internal static GameContentRecoveryRecord BuildRecovery(
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

        internal static void DisposeBackend(IGameContentEditSession backendSession)
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
