using System;
using System.Collections.Generic;
using System.Linq;

namespace Deucarian.GameContentAuthoring.Editor
{
    internal sealed class GameContentEditTransaction
    {
        private readonly GameContentEditSessionRegistry _sessions;
        private readonly GameContentEditValidation _validation;
        private readonly Action<GameContentActiveEditSession> _refresh;

        internal GameContentEditTransaction(GameContentEditSessionRegistry sessions, GameContentEditValidation validation, Action<GameContentActiveEditSession> refresh)
        {
            _sessions = sessions;
            _validation = validation;
            _refresh = refresh;
        }

        public GameContentCommitResult Commit(GameContentActiveEditSession active, bool confirmWarnings)
        {
            if (!_sessions.Owns(active)) return GameContentCommitResult.Failure("The edit session is no longer active.", active?.OriginalRevision);
            if (active.State != GameContentEditSessionState.Dirty)
                return GameContentCommitResult.Failure("Only a dirty edit session can be committed.", active.OriginalRevision);

            GameContentStaleCheckResult stale = _validation.CheckStale(active);
            if (stale.IsStale)
                return GameContentCommitResult.Failure(active.Message, active.OriginalRevision);

            GameContentValidationPreview preview = _validation.Preview(active);
            if (!preview.CanCommit)
                return GameContentCommitResult.Failure("Fix validation errors before committing.", active.OriginalRevision);
            if (preview.RequiresWarningConfirmation && !confirmWarnings)
                return GameContentCommitResult.Failure("Confirm the validation warnings before committing.", active.OriginalRevision);

            IReadOnlyList<GameContentAuthoringValidationIssue> freshReferenceIssues = GameContentReferencePolicy.EvaluateStagedReferences(active);
            if (freshReferenceIssues.Any(value => value.Severity == GameContentAuthoringValidationSeverity.Error))
            {
                active.Validation = GameContentReferencePolicy.MergeReferenceValidation(preview, freshReferenceIssues);
                active.Message = GameContentEditValidation.BuildValidationMessage(active.Validation);
                return GameContentCommitResult.Failure(
                    "A staged collection or record reference is no longer valid. Review it before committing.",
                    active.OriginalRevision);
            }
            if (!confirmWarnings && freshReferenceIssues.Any(value =>
                    value.Severity == GameContentAuthoringValidationSeverity.Warning))
            {
                active.Validation = GameContentReferencePolicy.MergeReferenceValidation(preview, freshReferenceIssues);
                active.Message = GameContentEditValidation.BuildValidationMessage(active.Validation);
                return GameContentCommitResult.Failure(
                    "Confirm the staged collection or record-reference validation warnings before committing.",
                    active.OriginalRevision);
            }

            active.State = GameContentEditSessionState.Committing;
            active.Message = "Committing source changes.";
            try
            {
                GameContentCommitResult result = active.BackendSession.Commit(confirmWarnings);
                if (result == null)
                    return CommitException(active, new InvalidOperationException("The editing backend returned no commit result."));

                bool refreshed = GameContentEditBackendState.RefreshFromBackend(active, true);
                if (!refreshed)
                {
                    GameContentRecoveryRecord recovery = result.Recovery ?? active.Recovery ??
                                                         GameContentEditBackendState.BuildRecovery(active, "Commit state refresh exception", active.Message);
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
                    _refresh(active);
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
            if (!_sessions.Owns(active)) return GameContentRollbackResult.Failure("The edit session is no longer active.", active?.OriginalRevision);
            if (active.State == GameContentEditSessionState.Committing)
                return GameContentRollbackResult.Failure("Rollback is disabled while committing.", active.OriginalRevision);
            try
            {
                GameContentRollbackResult result = active.BackendSession.Rollback()
                    ?? GameContentRollbackResult.Failure("The editing backend returned no rollback result.", active.OriginalRevision);
                bool refreshed = GameContentEditBackendState.RefreshFromBackend(active, true);
                if (!refreshed)
                {
                    GameContentRecoveryRecord recovery = result.Recovery ?? active.Recovery ??
                                                         GameContentEditBackendState.BuildRecovery(active, "Rollback state refresh exception", active.Message);
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
                    _sessions.Remove(active, true);
                    _refresh(active);
                }
                else if (result.Recovery != null)
                {
                    active.State = GameContentEditSessionState.RecoveryRequired;
                }
                return result;
            }
            catch (Exception exception)
            {
                GameContentRecoveryRecord recovery = GameContentEditBackendState.BuildRecovery(active, "Rollback exception", exception.GetBaseException().Message);
                active.State = GameContentEditSessionState.RecoveryRequired;
                active.Recovery = recovery;
                active.Message = "Rollback failed: " + exception.GetBaseException().Message;
                active.RollbackResult = GameContentRollbackResult.Failure(active.Message, active.OriginalRevision, recovery);
                return active.RollbackResult;
            }
        }

        private GameContentCommitResult CommitException(GameContentActiveEditSession active, Exception exception)
        {
            string detail = exception.GetBaseException().Message;
            GameContentRecoveryRecord recovery = GameContentEditBackendState.BuildRecovery(active, "Commit exception", detail);
            active.State = GameContentEditSessionState.RecoveryRequired;
            active.Message = "Commit failed and requires recovery review: " + detail;
            active.Recovery = recovery;
            active.CommitResult = GameContentCommitResult.Failure(active.Message, active.OriginalRevision, recovery);
            return active.CommitResult;
        }
    }
}
