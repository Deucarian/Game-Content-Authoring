using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Deucarian.Editor;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
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

        internal static void DrawAvailability(
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

        internal static void DrawSession(
            GameContentAuthoringSurfaceContext context,
            GameContentActiveEditSession active)
        {
            DrawSessionHeader(context, active);
            DrawFields(context, active);
            GameContentEditReviewRenderer.DrawChangeReview(context, active);
            DrawValidation(active.Validation);
            DrawRecovery(active);
            DrawSessionControls(context, active);
        }

        internal static void DrawSessionHeader(
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

        internal static void DrawFields(
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
                GameContentEditFieldRenderer.DrawField(context, active, field);
            }
        }

        internal static void DrawValidation(GameContentValidationPreview preview)
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
                EditorGUILayout.HelpBox(issue.Path + ": " + issue.Message, GameContentEditFieldRenderer.ToMessageType(issue.Severity));
            }
        }

        internal static void DrawRecovery(GameContentActiveEditSession active)
        {
            if (active.Recovery == null) return;
            EditorGUILayout.LabelField("Recovery", DeucarianEditorStyles.SectionTitle);
            EditorGUILayout.HelpBox(active.Recovery.ActionableMessage, MessageType.Error);
            GameContentRecordLensBrowser.DrawRow("Phase", active.Recovery.Phase);
            GameContentRecordLensBrowser.DrawRow("Recorded", active.Recovery.TimestampUtc.ToString("u", CultureInfo.InvariantCulture));
        }

        internal static void DrawSessionControls(
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

        internal static DeucarianEditorStatus GetStateStatus(GameContentEditSessionState state)
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

        internal static MessageType GetMessageType(GameContentActiveEditSession active)
        {
            if (active.State == GameContentEditSessionState.Stale ||
                active.State == GameContentEditSessionState.Conflict ||
                active.State == GameContentEditSessionState.RecoveryRequired)
                return MessageType.Error;
            if (active.Validation != null && active.Validation.WarningCount > 0) return MessageType.Warning;
            return MessageType.Info;
        }
    }
}
