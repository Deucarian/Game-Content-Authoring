using System;

namespace Deucarian.GameContentAuthoring.Editor
{
    public static class GameContentPackActionDispatcher
    {
        public static GameContentAuthoringValidationResult Validate(
            IGameContentPackProvider provider,
            GameContentPackDescriptor pack)
        {
            if (provider == null)
                return new GameContentAuthoringValidationResult(new[]
                {
                    GameContentAuthoringValidationIssue.Error("Content Pack", "Content-pack provider is unavailable.")
                });
            if (pack == null)
                return new GameContentAuthoringValidationResult(new[]
                {
                    GameContentAuthoringValidationIssue.Error("Content Pack", "No content pack is selected.")
                });
            try
            {
                return provider.ValidatePack(pack.PackId) ?? new GameContentAuthoringValidationResult(new[]
                {
                    GameContentAuthoringValidationIssue.Error("Content Pack", "The provider returned no validation result.")
                });
            }
            catch (Exception exception)
            {
                return new GameContentAuthoringValidationResult(new[]
                {
                    GameContentAuthoringValidationIssue.Error(
                        "Content Pack",
                        "Content-pack validation failed: " + exception.GetBaseException().Message)
                });
            }
        }

        public static GameContentActionResult Execute(
            IGameContentPackProvider provider,
            GameContentPackDescriptor pack,
            GameContentActionDescriptor action)
        {
            if (provider == null) return GameContentActionResult.Failure("Content-pack provider is unavailable.");
            if (pack == null) return GameContentActionResult.Failure("No content pack is selected.");
            if (action == null) return GameContentActionResult.Failure("No content-pack action is selected.");
            if (!action.Enabled)
            {
                string reason = string.IsNullOrWhiteSpace(action.DisabledReason) ? "This action is unavailable." : action.DisabledReason;
                return GameContentActionResult.Failure(reason);
            }

            try
            {
                return provider.ExecuteAction(pack.PackId, action.DispatchToken)
                    ?? GameContentActionResult.Failure("The provider returned no action result.");
            }
            catch (Exception exception)
            {
                return GameContentActionResult.Failure("Content-pack action failed: " + exception.GetBaseException().Message);
            }
        }
    }
}
