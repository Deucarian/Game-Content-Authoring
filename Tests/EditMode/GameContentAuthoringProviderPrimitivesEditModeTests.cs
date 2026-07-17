using NUnit.Framework;
using UnityEngine;

namespace Deucarian.GameContentAuthoring.Editor.Tests
{
    public sealed class GameContentAuthoringProviderPrimitivesEditModeTests
    {
        [Test]
        public void ProviderSessionState_ManagesPreviewAndEditingLifecycle()
        {
            var state = new GameContentAuthoringProviderSessionState<object>
            {
                SearchText = "weapon",
                Creating = true,
                DetailPage = 3,
                WizardStep = 2,
                ListScroll = new Vector2(4f, 5f),
                DetailScroll = new Vector2(6f, 7f),
                PreviewScroll = new Vector2(8f, 9f),
                EditingState = new object(),
                EditingContext = new GameContentAuthoringObjectEditorContext(null, "baseline"),
                LastEditResult = new GameContentCreationResult(true, "saved", null)
            };
            int stopped = 0;

            Assert.That(state.SetPreviewSource("selected", () => stopped++), Is.True);
            Assert.That(state.SetPreviewSource("selected", () => stopped++), Is.False);
            Assert.That(stopped, Is.EqualTo(1));
            Assert.That(state.ActivePreviewKey, Is.EqualTo("selected"));
            Assert.That(state.PreviewPlaying, Is.True);
            Assert.That(state.PausedNormalizedTime, Is.Zero);
            Assert.That(state.PreviewStatus, Is.EqualTo("Previewing"));

            state.StopPreview();
            Assert.That(state.PreviewPlaying, Is.False);
            Assert.That(state.PreviewStartTime, Is.Zero);
            Assert.That(state.PausedNormalizedTime, Is.EqualTo(0.5f));
            Assert.That(state.PreviewStatus, Is.EqualTo("Preview stopped"));

            state.ResetProviderSession();
            Assert.That(state.SearchText, Is.EqualTo("weapon"));
            Assert.That(state.Creating, Is.False);
            Assert.That(state.DetailPage, Is.Zero);
            Assert.That(state.WizardStep, Is.Zero);
            Assert.That(state.ListScroll, Is.EqualTo(Vector2.zero));
            Assert.That(state.DetailScroll, Is.EqualTo(Vector2.zero));
            Assert.That(state.PreviewScroll, Is.EqualTo(Vector2.zero));
            Assert.That(state.ActivePreviewKey, Is.Empty);
            Assert.That(state.PreviewStatus, Is.EqualTo("Preview idle"));
            Assert.That(state.EditingState, Is.Null);
            Assert.That(state.EditingContext, Is.Null);
            Assert.That(state.LastEditResult, Is.Null);
        }

        [Test]
        public void ValidationSummary_ProjectsStableLabelsAndMessages()
        {
            var result = new GameContentAuthoringValidationResult(new[]
            {
                GameContentAuthoringValidationIssue.Error(string.Empty, "Missing ID"),
                GameContentAuthoringValidationIssue.Warning("Stats", "Damage is low"),
                GameContentAuthoringValidationIssue.Info("Preview", "Using fallback")
            });

            var summary = new GameContentAuthoringValidationSummary(result);

            Assert.That(summary.IsPending, Is.False);
            Assert.That(summary.IsReady, Is.False);
            Assert.That(summary.ReadinessLabel, Is.EqualTo("1 blocker(s)"));
            Assert.That(summary.CountLabel, Is.EqualTo("1 blocker(s), 1 warning(s)."));
            Assert.That(summary.EditLabel, Is.EqualTo("1 edit blocker(s)."));
            Assert.That(summary.BuildMessages(false), Is.EqualTo(new[]
            {
                "Missing ID",
                "Stats: Damage is low"
            }));
            Assert.That(summary.BuildMessages(true), Has.Count.EqualTo(3));

            var pending = new GameContentAuthoringValidationSummary(null);
            Assert.That(pending.IsPending, Is.True);
            Assert.That(pending.ReadinessLabel, Is.EqualTo("Pending"));
            Assert.That(pending.CountLabel, Is.Empty);
        }

        [Test]
        public void FormatReference_UsesTargetDisplayNameAndPropertyPath()
        {
            var target = new GameContentLibraryItem(
                "weapon:laser",
                null,
                GameContentLibraryKind.Weapon,
                "Weapons",
                "Assets/Weapons/Laser.asset",
                "laser",
                "Laser");
            var reference = new GameContentLibraryReference(target, "Attack");

            Assert.That(GameContentAuthoringProviderGUI.FormatReference(reference), Is.EqualTo("Laser - Attack"));
            Assert.That(GameContentAuthoringProviderGUI.FormatReference(null), Is.Empty);
        }
    }
}
