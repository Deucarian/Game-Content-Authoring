using System;
using System.Collections.Generic;
using System.Globalization;
using Deucarian.Editor;
using UnityEditor;
using UnityEngine;

namespace Deucarian.GameContentAuthoring.Editor
{
    /// <summary>
    /// Selects the label convention used for provider validation summaries.
    /// </summary>
    public enum GameContentAuthoringValidationSummaryStyle
    {
        /// <summary>Shows pending, blocker, warning, or ready state.</summary>
        Readiness = 0,
        /// <summary>Shows both blocker and warning counts.</summary>
        Counts = 1,
        /// <summary>Shows edit-specific blocker or warning text.</summary>
        Edit = 2
    }

    /// <summary>
    /// Immutable editor-facing projection of a provider validation result.
    /// </summary>
    public sealed class GameContentAuthoringValidationSummary
    {
        private readonly GameContentAuthoringValidationResult _result;

        /// <summary>
        /// Creates an immutable summary for the supplied result, or a pending summary for <see langword="null"/>.
        /// </summary>
        /// <param name="result">The provider validation result to summarize.</param>
        public GameContentAuthoringValidationSummary(GameContentAuthoringValidationResult result)
        {
            _result = result;
            IsPending = result == null;
            ErrorCount = result == null ? 0 : result.ErrorCount;
            WarningCount = result == null ? 0 : result.WarningCount;
            InfoCount = result == null ? 0 : result.InfoCount;
        }

        /// <summary>Gets whether validation has not produced a result yet.</summary>
        public bool IsPending { get; }
        /// <summary>Gets the number of blocking errors.</summary>
        public int ErrorCount { get; }
        /// <summary>Gets the number of warnings.</summary>
        public int WarningCount { get; }
        /// <summary>Gets the number of informational findings.</summary>
        public int InfoCount { get; }
        /// <summary>Gets whether validation completed without errors or warnings.</summary>
        public bool IsReady => !IsPending && ErrorCount == 0 && WarningCount == 0;

        /// <summary>Gets the compact pending, blocker, warning, or ready label.</summary>
        public string ReadinessLabel
        {
            get
            {
                if (IsPending) return "Pending";
                if (ErrorCount > 0) return ErrorCount.ToString(CultureInfo.InvariantCulture) + " blocker(s)";
                if (WarningCount > 0) return WarningCount.ToString(CultureInfo.InvariantCulture) + " warning(s)";
                return "Ready";
            }
        }

        /// <summary>Gets a label containing both blocker and warning counts.</summary>
        public string CountLabel => IsPending
            ? string.Empty
            : ErrorCount.ToString(CultureInfo.InvariantCulture) + " blocker(s), " +
              WarningCount.ToString(CultureInfo.InvariantCulture) + " warning(s).";

        /// <summary>Gets a label phrased for an authoring edit operation.</summary>
        public string EditLabel => IsPending
            ? string.Empty
            : ErrorCount > 0
                ? ErrorCount.ToString(CultureInfo.InvariantCulture) + " edit blocker(s)."
                : WarningCount.ToString(CultureInfo.InvariantCulture) + " edit warning(s).";

        /// <summary>
        /// Gets the label for the requested presentation style.
        /// </summary>
        /// <param name="style">The desired label convention.</param>
        /// <returns>A stable, culture-invariant label.</returns>
        public string GetLabel(GameContentAuthoringValidationSummaryStyle style)
        {
            switch (style)
            {
                case GameContentAuthoringValidationSummaryStyle.Counts:
                    return CountLabel;
                case GameContentAuthoringValidationSummaryStyle.Edit:
                    return EditLabel;
                default:
                    return ReadinessLabel;
            }
        }

        /// <summary>
        /// Formats validation findings for display.
        /// </summary>
        /// <param name="includeInfo">Whether informational findings are included.</param>
        /// <returns>Path-prefixed messages in source order.</returns>
        public IReadOnlyList<string> BuildMessages(bool includeInfo)
        {
            if (_result == null || _result.Issues.Count == 0)
                return Array.Empty<string>();

            var messages = new List<string>(_result.Issues.Count);
            for (int i = 0; i < _result.Issues.Count; i++)
            {
                GameContentAuthoringValidationIssue issue = _result.Issues[i];
                if (!includeInfo && issue.Severity == GameContentAuthoringValidationSeverity.Info)
                    continue;

                string prefix = string.IsNullOrWhiteSpace(issue.Path) ? string.Empty : issue.Path + ": ";
                messages.Add(prefix + issue.Message);
            }

            return messages;
        }
    }

    /// <summary>
    /// Shared state and lifecycle behavior for editor-only three-pane authoring providers.
    /// </summary>
    /// <typeparam name="TEditingState">The provider-owned editable-state type.</typeparam>
    public class GameContentAuthoringProviderSessionState<TEditingState>
    {
        /// <summary>Gets or sets the current list search text.</summary>
        public string SearchText { get; set; } = string.Empty;
        /// <summary>Gets or sets whether the provider is creating a new item.</summary>
        public bool Creating { get; set; }
        /// <summary>Gets or sets the selected detail page.</summary>
        public int DetailPage { get; set; }
        /// <summary>Gets or sets the selected creation-wizard step.</summary>
        public int WizardStep { get; set; }
        /// <summary>Gets or sets the list pane scroll position.</summary>
        public Vector2 ListScroll { get; set; }
        /// <summary>Gets or sets the detail pane scroll position.</summary>
        public Vector2 DetailScroll { get; set; }
        /// <summary>Gets or sets the preview pane scroll position.</summary>
        public Vector2 PreviewScroll { get; set; }
        /// <summary>Gets or sets whether preview audio is muted.</summary>
        public bool PreviewMuted { get; set; } = true;
        /// <summary>Gets or sets whether the preview loops.</summary>
        public bool PreviewLoop { get; set; } = true;
        /// <summary>Gets or sets the preview playback speed.</summary>
        public float PreviewSpeed { get; set; } = 1f;
        /// <summary>Gets or sets whether the preview is playing.</summary>
        public bool PreviewPlaying { get; set; } = true;
        /// <summary>Gets or sets the preview render mode.</summary>
        public GameContentAuthoringActionPreviewRenderMode PreviewRenderMode { get; set; } = GameContentAuthoringActionPreviewRenderMode.Game;
        /// <summary>Gets or sets the editor time at which preview playback began.</summary>
        public double PreviewStartTime { get; set; }
        /// <summary>Gets or sets the normalized preview time retained while paused.</summary>
        public float PausedNormalizedTime { get; set; } = 0.5f;
        /// <summary>Gets or sets the stable key for the active preview source.</summary>
        public string ActivePreviewKey { get; set; } = string.Empty;
        /// <summary>Gets or sets the preview status text.</summary>
        public string PreviewStatus { get; set; } = "Preview idle";
        /// <summary>Gets or sets the provider-owned editable state.</summary>
        public TEditingState EditingState { get; set; }
        /// <summary>Gets or sets the shared editing context for the selected asset.</summary>
        public GameContentAuthoringObjectEditorContext EditingContext { get; set; }
        /// <summary>Gets or sets the most recent edit operation result.</summary>
        public GameContentCreationResult LastEditResult { get; set; }

        /// <summary>Stops preview playback while retaining the standard paused position.</summary>
        public void StopPreview()
        {
            PreviewPlaying = false;
            PreviewStartTime = 0d;
            PausedNormalizedTime = 0.5f;
            PreviewStatus = "Preview stopped";
            OnPreviewStopped();
        }

        /// <summary>Resets navigation, preview-source, and editing state when a provider is selected.</summary>
        public void ResetProviderSession()
        {
            Creating = false;
            DetailPage = 0;
            WizardStep = 0;
            ListScroll = Vector2.zero;
            DetailScroll = Vector2.zero;
            PreviewScroll = Vector2.zero;
            ActivePreviewKey = string.Empty;
            PreviewStatus = "Preview idle";
            ClearEditingState();
            OnProviderSessionReset();
        }

        /// <summary>
        /// Changes the active preview source and restarts preview playback.
        /// </summary>
        /// <param name="key">The stable preview-source key.</param>
        /// <param name="stopCurrentPreview">Optional callback that stops provider-owned preview resources.</param>
        /// <returns><see langword="true"/> when the source changed.</returns>
        public bool SetPreviewSource(string key, Action stopCurrentPreview = null)
        {
            key = key ?? string.Empty;
            if (string.Equals(ActivePreviewKey, key, StringComparison.Ordinal))
                return false;

            stopCurrentPreview?.Invoke();
            ActivePreviewKey = key;
            PreviewPlaying = true;
            PreviewStartTime = EditorApplication.timeSinceStartup;
            PausedNormalizedTime = 0f;
            PreviewStatus = "Previewing";
            OnPreviewSourceChanged();
            return true;
        }

        /// <summary>Clears provider-owned editable state and its latest result.</summary>
        public void ClearEditingState()
        {
            EditingState = default(TEditingState);
            EditingContext = null;
            LastEditResult = null;
        }

        /// <summary>Allows providers to reset additional state after preview playback stops.</summary>
        protected virtual void OnPreviewStopped()
        {
        }

        /// <summary>Allows providers to reset additional state after the common session reset.</summary>
        protected virtual void OnProviderSessionReset()
        {
        }

        /// <summary>Allows providers to reset additional state after the preview source changes.</summary>
        protected virtual void OnPreviewSourceChanged()
        {
        }
    }

    /// <summary>
    /// Reusable drawing primitives for provider validation, summary rows, and raw references.
    /// </summary>
    public static class GameContentAuthoringProviderGUI
    {
        /// <summary>
        /// Draws provider validation findings using shared status-card conventions.
        /// </summary>
        /// <param name="validation">The validation result to display.</param>
        /// <param name="summaryStyle">The summary label convention.</param>
        /// <param name="includeInfo">Whether informational findings are shown.</param>
        public static void DrawValidationIssues(
            GameContentAuthoringValidationResult validation,
            GameContentAuthoringValidationSummaryStyle summaryStyle = GameContentAuthoringValidationSummaryStyle.Readiness,
            bool includeInfo = true)
        {
            var summary = new GameContentAuthoringValidationSummary(validation);
            IReadOnlyList<string> messages = summary.BuildMessages(includeInfo);
            if (messages.Count == 0)
                return;

            DeucarianEditorStatus status = summary.ErrorCount > 0
                ? DeucarianEditorStatus.Error
                : summary.WarningCount > 0
                    ? DeucarianEditorStatus.Warning
                    : DeucarianEditorStatus.Info;
            DeucarianEditorStatusPanel.DrawValidationCard(summary.GetLabel(summaryStyle), messages, status);
        }

        /// <summary>Draws standard preview summary rows.</summary>
        /// <param name="rows">The rows to draw.</param>
        public static void DrawSummaryRows(params GameContentAuthoringPreviewRow[] rows)
        {
            DrawSummaryRows((IReadOnlyList<GameContentAuthoringPreviewRow>)rows, false);
        }

        /// <summary>
        /// Draws standard preview summary rows.
        /// </summary>
        /// <param name="rows">The rows to draw.</param>
        /// <param name="muted">Whether values use the muted editor style.</param>
        public static void DrawSummaryRows(IReadOnlyList<GameContentAuthoringPreviewRow> rows, bool muted = false)
        {
            if (rows == null || rows.Count == 0)
                return;

            GUIStyle style = muted ? DeucarianEditorStyles.MutedLabel : EditorStyles.label;
            for (int i = 0; i < rows.Count; i++)
            {
                GameContentAuthoringPreviewRow row = rows[i];
                DeucarianEditorFieldRow.Draw(
                    row.Label,
                    () => EditorGUILayout.LabelField(row.Value ?? string.Empty, style));
            }
        }

        /// <summary>
        /// Draws raw authored references with a consistent empty state.
        /// </summary>
        /// <param name="title">The section heading.</param>
        /// <param name="references">The references to draw.</param>
        /// <param name="emptyText">Text shown when no valid references exist.</param>
        public static void DrawReferenceList(
            string title,
            IReadOnlyList<GameContentLibraryReference> references,
            string emptyText = "None")
        {
            EditorGUILayout.LabelField(title ?? string.Empty, DeucarianEditorStyles.SectionTitle);
            if (references == null || references.Count == 0)
            {
                EditorGUILayout.LabelField(emptyText ?? "None", DeucarianEditorStyles.MutedLabel);
                return;
            }

            bool drewReference = false;
            for (int i = 0; i < references.Count; i++)
            {
                string label = FormatReference(references[i]);
                if (string.IsNullOrWhiteSpace(label))
                    continue;
                EditorGUILayout.LabelField(label, DeucarianEditorStyles.MutedLabel);
                drewReference = true;
            }

            if (!drewReference)
                EditorGUILayout.LabelField(emptyText ?? "None", DeucarianEditorStyles.MutedLabel);
        }

        /// <summary>
        /// Formats a raw authored reference for provider diagnostics.
        /// </summary>
        /// <param name="reference">The reference to format.</param>
        /// <returns>The target and property-path label, or an empty string for an invalid reference.</returns>
        public static string FormatReference(GameContentLibraryReference reference)
        {
            if (reference == null || reference.Target == null)
                return string.Empty;
            return reference.Target.DisplayName + " - " + reference.PropertyPath;
        }
    }
}
