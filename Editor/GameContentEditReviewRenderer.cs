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
    internal static class GameContentEditReviewRenderer
    {
        internal static void DrawChangeReview(
            GameContentAuthoringSurfaceContext context,
            GameContentActiveEditSession active)
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
                GameContentReferenceChangeReview referenceReview =
                    context.EditSessions.GetReferenceChangeReview(active, change);
                GameContentCollectionChangeReview collectionReview =
                    context.EditSessions.GetCollectionChangeReview(active, change);
                GameContentStructuredCollectionChangeReview structuredReview =
                    context.EditSessions.GetStructuredCollectionChangeReview(active, change);
                DeucarianEditorCards.DrawInlineCard(() =>
                {
                    EditorGUILayout.LabelField(change.DisplayName, EditorStyles.boldLabel);
                    if (structuredReview != null)
                    {
                        DrawStructuredCollectionReview(structuredReview);
                    }
                    else if (collectionReview != null)
                    {
                        DrawCollectionReview(collectionReview);
                    }
                    else
                    {
                        GameContentRecordLensBrowser.DrawRow("Before", change.OldValue?.ToDisplayString() ?? string.Empty);
                        GameContentRecordLensBrowser.DrawRow("After", change.ProposedValue?.ToDisplayString() ?? string.Empty);
                    }
                    if (referenceReview != null) DrawReferenceReview(referenceReview);
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

        internal static void DrawStructuredCollectionReview(GameContentStructuredCollectionChangeReview review)
        {
            GameContentRecordLensBrowser.DrawRow(
                "Source Record",
                review.SourceRecordKey?.SourceRecordId ?? string.Empty);
            GameContentRecordLensBrowser.DrawRow(
                "Original Order",
                DescribeStructuredRows(review.OriginalOrder));
            GameContentRecordLensBrowser.DrawRow(
                "Proposed Order",
                DescribeStructuredRows(review.ProposedOrder));
            for (int i = 0; i < review.AddedRows.Count; i++)
                GameContentRecordLensBrowser.DrawRow("Added Row", DescribeStructuredRow(review.AddedRows[i]));
            for (int i = 0; i < review.RemovedRows.Count; i++)
                GameContentRecordLensBrowser.DrawRow("Removed Row", DescribeStructuredRow(review.RemovedRows[i]));
            for (int i = 0; i < review.MovedRows.Count; i++)
            {
                GameContentStructuredRowMove move = review.MovedRows[i];
                GameContentRecordLensBrowser.DrawRow(
                    "Moved Row",
                    move.Summary + " | " + (move.OldIndex + 1) + " -> " + (move.NewIndex + 1));
            }
            for (int i = 0; i < review.FieldChanges.Count; i++)
            {
                GameContentStructuredRowFieldChange change = review.FieldChanges[i];
                string before = change.OldValue?.ToDisplayString() ?? "Not set";
                string after = change.NewValue?.ToDisplayString() ?? "Not set";
                GameContentRecordLensBrowser.DrawRow(
                    "Changed " + change.RowFieldId,
                    change.RowSummary + " | " + before + " -> " + after);
                if (change.IsReference)
                {
                    GameContentRecordLensBrowser.DrawRow(
                        "Reference Targets",
                        before + " -> " + after);
                }
            }
            for (int i = 0; i < review.ValidationFindings.Count; i++)
            {
                GameContentAuthoringValidationIssue issue = review.ValidationFindings[i];
                EditorGUILayout.HelpBox(issue.Path + ": " + issue.Message, GameContentEditFieldRenderer.ToMessageType(issue.Severity));
            }
            GameContentRecordLensBrowser.DrawRow("Runtime Impact", review.RuntimeImpact.ToString());
            EditorGUILayout.HelpBox(
                "Adding or removing an embedded row changes only its parent source. It does not create or delete a canonical authored record, and stable identities remain read-only.",
                MessageType.Info);
        }

        internal static string DescribeStructuredRows(IReadOnlyList<GameContentStructuredRowValue> rows)
        {
            return rows == null || rows.Count == 0
                ? "Empty"
                : string.Join(" -> ", rows.Select(DescribeStructuredRow).ToArray());
        }

        internal static string DescribeStructuredRow(GameContentStructuredRowValue row)
        {
            if (row == null) return string.Empty;
            string summary = string.IsNullOrWhiteSpace(row.DisplaySummary) ? "Row" : row.DisplaySummary;
            return string.IsNullOrWhiteSpace(row.NativeKeyDisplayMetadata)
                ? summary
                : summary + " [" + row.NativeKeyDisplayMetadata + "]";
        }

        internal static void DrawCollectionReview(GameContentCollectionChangeReview review)
        {
            GameContentRecordLensBrowser.DrawRow(
                "Source Record",
                review.SourceRecordKey?.SourceRecordId ?? string.Empty);
            GameContentRecordLensBrowser.DrawRow("Before", DescribeCollection(review.OriginalValue));
            GameContentRecordLensBrowser.DrawRow("After", DescribeCollection(review.ProposedValue));
            for (int i = 0; i < review.Changes.Count; i++)
            {
                GameContentCollectionProposedChange change = review.Changes[i];
                GameContentRecordLensBrowser.DrawRow(change.Operation.ToString(), change.Summary);
            }
            GameContentRecordLensBrowser.DrawRow("Runtime Impact", review.RuntimeImpact.ToString());
            if (review.ContainsRecordReferences)
            {
                EditorGUILayout.HelpBox(
                    "Removing a reference changes only this collection. It does not delete or modify the target record.",
                    MessageType.Info);
            }
        }

        internal static string DescribeCollection(GameContentOrderedCollectionValue collection)
        {
            if (collection == null || collection.Items.Count == 0) return "Empty";
            return string.Join(
                " -> ",
                collection.Items.Select(item =>
                    item.Value.FieldType == GameContentFieldType.RecordReference
                        ? GameContentEditReferenceRenderer.DescribeReference(item.Value.RecordReferenceValue)
                        : item.Value.ToDisplayString()).ToArray());
        }

        internal static void DrawReferenceReview(GameContentReferenceChangeReview review)
        {
            GameContentRecordLensBrowser.DrawRow("Source Record", review.SourceRecordKey?.SourceRecordId ?? string.Empty);
            DrawReviewTarget("Old Target", review.OldValue, review.OldTarget);
            DrawReviewTarget("New Target", review.NewValue, review.NewTarget);
            if (review.OldTargetInboundDelta != 0)
                GameContentRecordLensBrowser.DrawRow("Old Target Inbound", review.OldTargetInboundDelta.ToString(CultureInfo.InvariantCulture));
            if (review.NewTargetInboundDelta != 0)
                GameContentRecordLensBrowser.DrawRow("New Target Inbound", "+" + review.NewTargetInboundDelta.ToString(CultureInfo.InvariantCulture));
            GameContentRecordLensBrowser.DrawRow(
                "Source Inbound References",
                review.SourceInboundReferenceCount.ToString(CultureInfo.InvariantCulture));
            GameContentRecordLensBrowser.DrawRow("Runtime Impact", review.RuntimeImpact.ToString());
        }

        internal static void DrawReviewTarget(
            string label,
            GameContentRecordReferenceValue value,
            GameContentRecordDescriptor target)
        {
            string display = target != null
                ? target.DisplayName + " (" + target.CanonicalKey.SourceRecordId + ")"
                : value?.ToDisplayString() ?? string.Empty;
            GameContentRecordLensBrowser.DrawRow(label, display);
            if (target == null) return;
            GameContentRecordLensBrowser.DrawRow(label + " Pack", target.CanonicalKey.PackId);
            GameContentRecordLensBrowser.DrawRow(
                label + " Type",
                string.Join(", ", target.Capabilities.Select(capability => capability.Id).ToArray()));
            GameContentRecordLensBrowser.DrawRow(
                label + " Validation",
                target.Validation.ErrorCount > 0
                    ? "Invalid"
                    : target.Validation.WarningCount > 0 ? "Warning" : "Valid");
        }
    }
}
