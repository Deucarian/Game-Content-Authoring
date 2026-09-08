using System;
using System.Collections.Generic;
using System.Linq;

namespace Deucarian.GameContentAuthoring.Editor
{
    internal sealed class GameContentEditValidation
    {
        private readonly GameContentEditSessionRegistry _sessions;

        internal GameContentEditValidation(GameContentEditSessionRegistry sessions)
        {
            _sessions = sessions;
        }

        public GameContentValidationPreview Preview(GameContentActiveEditSession active)
        {
            if (!_sessions.Owns(active)) return GameContentValidationPreview.Error("Editing", "The edit session is no longer active.");
            if (active.State == GameContentEditSessionState.Committing)
                return GameContentValidationPreview.Error("Editing", "Validation is unavailable while the source is committing.");
            try
            {
                GameContentValidationPreview backendPreview = active.BackendSession.Preview()
                    ?? GameContentValidationPreview.Error("Editing", "The editing backend returned no validation preview.");
                active.Validation = GameContentReferencePolicy.MergeReferenceValidation(
                    backendPreview,
                    GameContentReferencePolicy.EvaluateStagedReferences(active));
                active.Message = BuildValidationMessage(active.Validation);
                GameContentEditBackendState.RefreshFromBackend(active, false);
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
            if (!_sessions.Owns(active))
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
                    GameContentEditBackendState.RefreshFromBackend(active, false);
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

        internal static string BuildValidationMessage(GameContentValidationPreview preview)
        {
            if (preview == null) return "No validation preview is available.";
            if (preview.ErrorCount > 0) return preview.ErrorCount + " validation error(s).";
            if (preview.WarningCount > 0) return preview.WarningCount + " warning(s) require review.";
            return "Validation passed.";
        }
    }
}
