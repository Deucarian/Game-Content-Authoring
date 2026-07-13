using System;
using System.Globalization;
using System.Linq;
using Deucarian.Editor;
using UnityEditor;
using UnityEngine;

namespace Deucarian.GameContentAuthoring.Editor
{
    public static class GameContentEditWorkbench
    {
        public static void Draw(
            GameContentAuthoringSurfaceContext context,
            GameContentRecordDescriptor record,
            string lensId)
        {
            if (context == null || record == null || context.EditSessions == null) return;
            GUILayout.Space(DeucarianEditorSpacing.Small);
            EditorGUILayout.LabelField("Record Editing", DeucarianEditorStyles.SectionTitle);

            if (context.PackContext != null && !context.PackContext.IsAllPacks &&
                context.EditSessions.TryGetSession(record.CanonicalKey, out GameContentActiveEditSession active))
            {
                DrawSession(context, active);
                return;
            }

            GameContentEditAvailability availability = context.EditSessions.GetAvailability(
                context.PackContext,
                record,
                lensId);
            DrawAvailability(context, record, lensId, availability);
        }

        private static void DrawAvailability(
            GameContentAuthoringSurfaceContext context,
            GameContentRecordDescriptor record,
            string lensId,
            GameContentEditAvailability availability)
        {
            string beginMessage = string.Empty;
            bool beginFailed = false;
            DeucarianEditorCards.DrawInlineCard(() =>
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    DeucarianEditorStatusBadge.Draw(
                        availability.IsEditable ? "Editable" : "Read-only",
                        availability.IsEditable ? DeucarianEditorStatus.Success : DeucarianEditorStatus.Info,
                        GUILayout.Width(82f));
                    if (!string.IsNullOrWhiteSpace(availability.BackendId))
                        EditorGUILayout.LabelField(availability.BackendId, DeucarianEditorStyles.MutedLabel);
                    GUILayout.FlexibleSpace();
                    if (DeucarianEditorButtons.Primary(
                            "Edit",
                            availability.IsEditable,
                            GUILayout.Width(68f),
                            GUILayout.Height(24f)))
                    {
                        GameContentEditBeginResult result = context.EditSessions.BeginEdit(
                            context.PackContext,
                            record,
                            lensId);
                        beginMessage = result.Message;
                        beginFailed = !result.Succeeded;
                        context.RequestRepaint();
                    }
                }

                if (availability.SourceTarget != null)
                    GameContentRecordLensBrowser.DrawRow("Source", availability.SourceTarget.SourceLabel);
                if (!availability.IsEditable)
                    EditorGUILayout.LabelField(availability.DisabledReason, DeucarianEditorStyles.MutedLabel);
            });

            if (!string.IsNullOrWhiteSpace(beginMessage))
                EditorGUILayout.HelpBox(beginMessage, beginFailed ? MessageType.Error : MessageType.Info);
        }

        private static void DrawSession(
            GameContentAuthoringSurfaceContext context,
            GameContentActiveEditSession active)
        {
            DrawSessionHeader(context, active);
            DrawFields(context, active);
            DrawChangeReview(active);
            DrawValidation(active.Validation);
            DrawRecovery(active);
            DrawSessionControls(context, active);
        }

        private static void DrawSessionHeader(
            GameContentAuthoringSurfaceContext context,
            GameContentActiveEditSession active)
        {
            DeucarianEditorCards.DrawInlineCard(() =>
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    DeucarianEditorStatusBadge.Draw(
                        active.State.ToString(),
                        GetStateStatus(active.State),
                        GUILayout.MinWidth(82f));
                    EditorGUILayout.LabelField(active.BackendId, DeucarianEditorStyles.MutedLabel);
                    GUILayout.FlexibleSpace();
                    DeucarianEditorStatusBadge.Draw(
                        active.StaleCheck != null && active.StaleCheck.IsStale ? "Stale" : "Revision current",
                        active.StaleCheck != null && active.StaleCheck.IsStale
                            ? DeucarianEditorStatus.Error
                            : DeucarianEditorStatus.Success,
                        GUILayout.MinWidth(104f));
                }
                GameContentRecordLensBrowser.DrawRow("Pack", active.Request.SelectedPackKey);
                GameContentRecordLensBrowser.DrawRow("Record", active.RecordKey.SourceRecordId);
                GameContentRecordLensBrowser.DrawRow("Source", active.SourceTarget.SourceLabel);
                if (!string.IsNullOrWhiteSpace(active.SourceTarget.ProjectRelativeDescription))
                    GameContentRecordLensBrowser.DrawRow("Location", active.SourceTarget.ProjectRelativeDescription);
                GameContentRecordLensBrowser.DrawRow("Revision", active.OriginalRevision.Token);
                if (!string.IsNullOrWhiteSpace(active.Message))
                    EditorGUILayout.HelpBox(active.Message, GetMessageType(active));
            });
        }

        private static void DrawFields(
            GameContentAuthoringSurfaceContext context,
            GameContentActiveEditSession active)
        {
            EditorGUILayout.LabelField("Editable Fields", DeucarianEditorStyles.SectionTitle);
            string currentGroup = string.Empty;
            for (int i = 0; i < active.Fields.Count; i++)
            {
                GameContentFieldDescriptor field = active.Fields[i];
                if (!string.Equals(currentGroup, field.Group, StringComparison.Ordinal))
                {
                    currentGroup = field.Group;
                    EditorGUILayout.LabelField(currentGroup, EditorStyles.boldLabel);
                }
                DrawField(context, active, field);
            }
        }

        private static void DrawField(
            GameContentAuthoringSurfaceContext context,
            GameContentActiveEditSession active,
            GameContentFieldDescriptor field)
        {
            GameContentFieldValue current = active.GetEffectiveValue(field.FieldId);
            bool sessionWritable = active.State == GameContentEditSessionState.Clean ||
                                   active.State == GameContentEditSessionState.Dirty;
            bool enabled = sessionWritable && !field.IsReadOnly && current != null;
            GameContentFieldValue next = current;

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUI.DisabledScope(!enabled))
                {
                    EditorGUI.BeginChangeCheck();
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        EditorGUILayout.LabelField(field.DisplayName, GUILayout.Width(128f));
                        next = DrawValue(field, current);
                    }
                    if (EditorGUI.EndChangeCheck() && next != null && !next.Equals(current))
                    {
                        GameContentEditOperationResult result = context.EditSessions.Apply(active, field.FieldId, next);
                        if (result.Succeeded) context.EditSessions.Preview(active);
                        context.RequestRepaint();
                    }
                }

                string detail = BuildFieldDetail(field);
                if (!string.IsNullOrWhiteSpace(detail))
                    EditorGUILayout.LabelField(detail, DeucarianEditorStyles.MutedLabel);
                if (field.IsReadOnly && !string.IsNullOrWhiteSpace(field.ReadOnlyReason))
                    EditorGUILayout.LabelField(field.ReadOnlyReason, DeucarianEditorStyles.MutedLabel);
                DrawFieldValidation(active.Validation, field);
            }
        }

        private static GameContentFieldValue DrawValue(
            GameContentFieldDescriptor field,
            GameContentFieldValue current)
        {
            if (current == null)
            {
                EditorGUILayout.LabelField("Unavailable", DeucarianEditorStyles.MutedLabel);
                return null;
            }

            switch (field.FieldType)
            {
                case GameContentFieldType.Integer:
                    return GameContentFieldValue.FromInteger(EditorGUILayout.LongField(current.IntegerValue));
                case GameContentFieldType.Number:
                    return GameContentFieldValue.FromNumber(EditorGUILayout.DoubleField(current.NumberValue));
                case GameContentFieldType.Boolean:
                    return GameContentFieldValue.FromBoolean(EditorGUILayout.Toggle(current.BooleanValue));
                case GameContentFieldType.Enum:
                    return DrawEnum(field, current);
                default:
                    return GameContentFieldValue.FromString(EditorGUILayout.TextField(current.StringValue ?? string.Empty));
            }
        }

        private static GameContentFieldValue DrawEnum(
            GameContentFieldDescriptor field,
            GameContentFieldValue current)
        {
            if (field.EnumOptions.Count == 0)
            {
                EditorGUILayout.LabelField(current.StringValue, DeucarianEditorStyles.MutedLabel);
                return current;
            }

            string[] tokens = field.EnumOptions.Select(value => value.Token).ToArray();
            string[] labels = field.EnumOptions.Select(value => value.DisplayName).ToArray();
            int currentIndex = Array.FindIndex(tokens, value => string.Equals(value, current.StringValue, StringComparison.Ordinal));
            if (currentIndex < 0) currentIndex = 0;
            int nextIndex = EditorGUILayout.Popup(currentIndex, labels);
            return GameContentFieldValue.FromEnum(tokens[Mathf.Clamp(nextIndex, 0, tokens.Length - 1)]);
        }

        private static string BuildFieldDetail(GameContentFieldDescriptor field)
        {
            string detail = field.Description;
            string constraints = string.Empty;
            if (field.MinimumNumber.HasValue || field.MaximumNumber.HasValue)
            {
                string minimum = field.MinimumNumber.HasValue
                    ? field.MinimumNumber.Value.ToString("0.###", CultureInfo.InvariantCulture)
                    : "any";
                string maximum = field.MaximumNumber.HasValue
                    ? field.MaximumNumber.Value.ToString("0.###", CultureInfo.InvariantCulture)
                    : "any";
                constraints = "Range: " + minimum + " to " + maximum + ".";
            }
            else if (field.MinimumLength.HasValue || field.MaximumLength.HasValue)
            {
                string minimum = field.MinimumLength.HasValue ? field.MinimumLength.Value.ToString(CultureInfo.InvariantCulture) : "0";
                string maximum = field.MaximumLength.HasValue ? field.MaximumLength.Value.ToString(CultureInfo.InvariantCulture) : "any";
                constraints = "Length: " + minimum + " to " + maximum + ".";
            }
            if (field.Required) constraints = string.IsNullOrWhiteSpace(constraints) ? "Required." : constraints + " Required.";
            return string.IsNullOrWhiteSpace(detail)
                ? constraints
                : string.IsNullOrWhiteSpace(constraints) ? detail : detail + " " + constraints;
        }

        private static void DrawFieldValidation(
            GameContentValidationPreview preview,
            GameContentFieldDescriptor field)
        {
            if (preview == null) return;
            GameContentAuthoringValidationIssue[] issues = preview.Issues.Where(issue =>
                string.Equals(issue.Path, field.FieldId, StringComparison.Ordinal) ||
                (!string.IsNullOrWhiteSpace(field.SemanticId) &&
                 string.Equals(issue.Path, field.SemanticId, StringComparison.Ordinal))).ToArray();
            for (int i = 0; i < issues.Length; i++)
                EditorGUILayout.HelpBox(issues[i].Message, ToMessageType(issues[i].Severity));
        }

        private static void DrawChangeReview(GameContentActiveEditSession active)
        {
            EditorGUILayout.LabelField("Change Review", DeucarianEditorStyles.SectionTitle);
            if (active.Changes.Count == 0)
            {
                EditorGUILayout.LabelField("No staged changes.", DeucarianEditorStyles.MutedLabel);
                return;
            }

            for (int i = 0; i < active.Changes.Count; i++)
            {
                GameContentProposedChange change = active.Changes[i];
                DeucarianEditorCards.DrawInlineCard(() =>
                {
                    EditorGUILayout.LabelField(change.DisplayName, EditorStyles.boldLabel);
                    GameContentRecordLensBrowser.DrawRow("Before", change.OldValue?.ToDisplayString() ?? string.Empty);
                    GameContentRecordLensBrowser.DrawRow("After", change.ProposedValue?.ToDisplayString() ?? string.Empty);
                });
            }

            GameContentRecordLensBrowser.DrawRow("Affected Source", active.SourceTarget.SourceLabel);
            if (active.CommitResult != null)
            {
                GameContentRecordLensBrowser.DrawRow("Refresh", active.CommitResult.RequiresRefresh ? "Required" : "Not required");
                GameContentRecordLensBrowser.DrawRow("Rebind", active.CommitResult.RequiresRebind ? "Required" : "Not required");
                GameContentRecordLensBrowser.DrawRow("Restart", active.CommitResult.RequiresRestart ? "Required" : "Not required");
            }
        }

        private static void DrawValidation(GameContentValidationPreview preview)
        {
            preview = preview ?? GameContentValidationPreview.Valid;
            EditorGUILayout.LabelField("Edit Validation", DeucarianEditorStyles.SectionTitle);
            DeucarianEditorStatusBadge.Draw(
                preview.State.ToString(),
                preview.State == GameContentEditValidationState.Invalid
                    ? DeucarianEditorStatus.Error
                    : preview.State == GameContentEditValidationState.Warning
                        ? DeucarianEditorStatus.Warning
                        : DeucarianEditorStatus.Success,
                GUILayout.Width(82f));
            if (preview.Issues.Count == 0)
            {
                EditorGUILayout.LabelField("No edit validation issues.", DeucarianEditorStyles.MutedLabel);
                return;
            }
            for (int i = 0; i < preview.Issues.Count; i++)
            {
                GameContentAuthoringValidationIssue issue = preview.Issues[i];
                EditorGUILayout.HelpBox(issue.Path + ": " + issue.Message, ToMessageType(issue.Severity));
            }
        }

        private static void DrawRecovery(GameContentActiveEditSession active)
        {
            if (active.Recovery == null) return;
            EditorGUILayout.LabelField("Recovery", DeucarianEditorStyles.SectionTitle);
            EditorGUILayout.HelpBox(active.Recovery.ActionableMessage, MessageType.Error);
            GameContentRecordLensBrowser.DrawRow("Phase", active.Recovery.Phase);
            GameContentRecordLensBrowser.DrawRow("Recorded", active.Recovery.TimestampUtc.ToString("u", CultureInfo.InvariantCulture));
        }

        private static void DrawSessionControls(
            GameContentAuthoringSurfaceContext context,
            GameContentActiveEditSession active)
        {
            bool committing = active.State == GameContentEditSessionState.Committing;
            using (new EditorGUILayout.HorizontalScope())
            {
                if (active.State == GameContentEditSessionState.Committed)
                {
                    if (DeucarianEditorButtons.Secondary("Rollback", !committing, GUILayout.Width(82f), GUILayout.Height(24f)))
                        context.EditSessions.Rollback(active);
                    GUILayout.FlexibleSpace();
                    if (DeucarianEditorButtons.Primary("Done", true, GUILayout.Width(68f), GUILayout.Height(24f)))
                        context.EditSessions.Dismiss(active);
                    return;
                }

                if (DeucarianEditorButtons.Secondary("Undo", !committing && active.CanUndo, GUILayout.Width(64f), GUILayout.Height(24f)))
                    context.EditSessions.Undo(active);
                if (DeucarianEditorButtons.Secondary("Redo", !committing && active.CanRedo, GUILayout.Width(64f), GUILayout.Height(24f)))
                    context.EditSessions.Redo(active);
                if (DeucarianEditorButtons.Secondary("Preview", !committing, GUILayout.Width(72f), GUILayout.Height(24f)))
                    context.EditSessions.Preview(active);
                if (DeucarianEditorButtons.Secondary("Check Source", !committing, GUILayout.Width(98f), GUILayout.Height(24f)))
                    context.EditSessions.CheckStale(active);
                GUILayout.FlexibleSpace();
                if (DeucarianEditorButtons.Secondary("Cancel", !committing, GUILayout.Width(68f), GUILayout.Height(24f)))
                    context.EditSessions.Cancel(active);

                bool commitEnabled = active.State == GameContentEditSessionState.Dirty && active.Validation.CanCommit;
                if (DeucarianEditorButtons.Primary("Commit", !committing && commitEnabled, GUILayout.Width(76f), GUILayout.Height(24f)))
                {
                    GameContentValidationPreview preview = context.EditSessions.Preview(active);
                    bool confirmWarnings = !preview.RequiresWarningConfirmation || EditorUtility.DisplayDialog(
                        "Commit With Warnings?",
                        "Validation reported warnings. Review them before committing this source.",
                        "Commit",
                        "Cancel");
                    if (confirmWarnings) context.EditSessions.Commit(active, preview.RequiresWarningConfirmation);
                }
            }
        }

        private static DeucarianEditorStatus GetStateStatus(GameContentEditSessionState state)
        {
            switch (state)
            {
                case GameContentEditSessionState.Dirty:
                case GameContentEditSessionState.Committing:
                    return DeucarianEditorStatus.Warning;
                case GameContentEditSessionState.Stale:
                case GameContentEditSessionState.Conflict:
                case GameContentEditSessionState.RecoveryRequired:
                    return DeucarianEditorStatus.Error;
                case GameContentEditSessionState.Committed:
                case GameContentEditSessionState.RolledBack:
                    return DeucarianEditorStatus.Success;
                default:
                    return DeucarianEditorStatus.Info;
            }
        }

        private static MessageType GetMessageType(GameContentActiveEditSession active)
        {
            if (active.State == GameContentEditSessionState.Stale ||
                active.State == GameContentEditSessionState.Conflict ||
                active.State == GameContentEditSessionState.RecoveryRequired)
                return MessageType.Error;
            if (active.Validation != null && active.Validation.WarningCount > 0) return MessageType.Warning;
            return MessageType.Info;
        }

        private static MessageType ToMessageType(GameContentAuthoringValidationSeverity severity)
        {
            if (severity == GameContentAuthoringValidationSeverity.Error) return MessageType.Error;
            if (severity == GameContentAuthoringValidationSeverity.Warning) return MessageType.Warning;
            return MessageType.Info;
        }
    }
}
