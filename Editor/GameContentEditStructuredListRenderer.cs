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
    internal static class GameContentEditStructuredListRenderer
    {
        internal static void DrawStructuredCollectionField(
            GameContentAuthoringSurfaceContext context,
            GameContentActiveEditSession active,
            GameContentFieldDescriptor field,
            GameContentFieldValue current,
            bool enabled)
        {
            GameContentOrderedStructuredCollectionValue collection = current?.OrderedStructuredCollectionValue;
            GameContentStructuredCollectionFieldDescriptor descriptor = field.StructuredCollection;
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(field.DisplayName, EditorStyles.boldLabel);
                GUILayout.FlexibleSpace();
                EditorGUILayout.LabelField(
                    BuildStructuredCountLabel(descriptor, collection),
                    DeucarianEditorStyles.MutedLabel,
                    GUILayout.Width(180f));
            }
            if (collection == null || descriptor == null)
            {
                EditorGUILayout.HelpBox("The ordered structured collection is unavailable.", MessageType.Error);
                return;
            }

            string stateKey = field.FieldId;
            GameContentStructuredRowValue selected = ResolveSelectedStructuredRow(context.EditWorkbenchState.ForSession(active),
                stateKey,
                collection);
            EditorGUILayout.LabelField("Rows", EditorStyles.boldLabel);
            if (collection.Count == 0)
                EditorGUILayout.LabelField("No rows.", DeucarianEditorStyles.MutedLabel);
            for (int i = 0; i < collection.Rows.Count; i++)
            {
                GameContentStructuredRowValue row = collection.Rows[i];
                DrawStructuredRowListItem(
                    context,
                    active,
                    field,
                    collection,
                    row,
                    i,
                    enabled,
                    stateKey,
                    selected);
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.FlexibleSpace();
                bool canRestore = enabled &&
                                  descriptor.Allows(
                                      GameContentStructuredCollectionPermittedOperations.RestoreOriginalOrder) &&
                                  GameContentStructuredCollectionMutation.NeedsRestoreOriginalOrder(collection);
                using (new EditorGUI.DisabledScope(!canRestore))
                {
                    if (GUILayout.Button(
                            new GUIContent(
                                "Restore Original Order",
                                "Restore surviving original rows by session-start position; added rows remain after them."),
                            GUILayout.Width(150f)))
                    {
                        context.EditSessions.RestoreOriginalStructuredOrder(active, field.FieldId);
                        context.RequestRepaint();
                    }
                }
            }

            selected = ResolveSelectedStructuredRow(context.EditWorkbenchState.ForSession(active), stateKey, collection);
            if (selected != null)
                GameContentEditStructuredFieldsRenderer.DrawStructuredRowDetail(
                    context,
                    active,
                    field,
                    selected,
                    IndexOfStructuredRow(collection, selected.RowKey),
                    enabled);
            if (descriptor.Allows(GameContentStructuredCollectionPermittedOperations.AddRow))
                GameContentEditStructuredFieldsRenderer.DrawStructuredRowAdd(context, active, field, collection, enabled, stateKey);

            EditorGUILayout.HelpBox(
                "Rows are embedded values owned by this parent source. Adding or removing one does not create or delete a top-level authored record. Stable and provider-native IDs remain read-only.",
                MessageType.Info);
        }

        internal static void DrawStructuredRowListItem(
            GameContentAuthoringSurfaceContext context,
            GameContentActiveEditSession active,
            GameContentFieldDescriptor field,
            GameContentOrderedStructuredCollectionValue collection,
            GameContentStructuredRowValue row,
            int index,
            bool enabled,
            string stateKey,
            GameContentStructuredRowValue selected)
        {
            GameContentStructuredCollectionFieldDescriptor descriptor = field.StructuredCollection;
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField((index + 1).ToString(CultureInfo.InvariantCulture), GUILayout.Width(24f));
                bool isSelected = selected != null && selected.RowKey.Equals(row.RowKey);
                string summary = string.IsNullOrWhiteSpace(row.DisplaySummary) ? "Row " + (index + 1) : row.DisplaySummary;
                if (GUILayout.Toggle(isSelected, summary, "Button", GUILayout.MinWidth(140f)))
                    context.EditWorkbenchState.ForSession(active).StructuredSelections[stateKey] = row.RowKey;
                if (!string.IsNullOrWhiteSpace(row.NativeKeyDisplayMetadata))
                {
                    EditorGUILayout.LabelField(
                        row.NativeKeyDisplayMetadata,
                        DeucarianEditorStyles.MutedLabel,
                        GUILayout.MaxWidth(120f));
                }
                GameContentEditValidationState validationState = GetStructuredRowValidationState(
                    active.Validation,
                    field.FieldId,
                    index,
                    row.ValidationState);
                DeucarianEditorStatusBadge.Draw(
                    validationState.ToString(),
                    validationState == GameContentEditValidationState.Invalid
                        ? DeucarianEditorStatus.Error
                        : validationState == GameContentEditValidationState.Warning
                            ? DeucarianEditorStatus.Warning
                            : DeucarianEditorStatus.Success,
                    GUILayout.Width(62f));

                bool canMove = enabled && descriptor.Allows(
                    GameContentStructuredCollectionPermittedOperations.MoveRow);
                using (new EditorGUI.DisabledScope(!canMove || index <= 0))
                {
                    if (GUILayout.Button(new GUIContent("Up", "Move this row one position earlier."), GUILayout.Width(42f)))
                    {
                        GameContentEditStructuredFieldsRenderer.ApplyStructuredOperation(
                            context,
                            active,
                            field.FieldId,
                            GameContentStructuredCollectionOperation.MoveRow(row.RowKey, index - 1));
                    }
                }
                using (new EditorGUI.DisabledScope(!canMove || index >= collection.Count - 1))
                {
                    if (GUILayout.Button(new GUIContent("Down", "Move this row one position later."), GUILayout.Width(48f)))
                    {
                        GameContentEditStructuredFieldsRenderer.ApplyStructuredOperation(
                            context,
                            active,
                            field.FieldId,
                            GameContentStructuredCollectionOperation.MoveRow(row.RowKey, index + 1));
                    }
                }
                bool canRemove = enabled &&
                                 descriptor.Allows(GameContentStructuredCollectionPermittedOperations.RemoveRow) &&
                                 collection.Count > descriptor.MinimumCount;
                using (new EditorGUI.DisabledScope(!canRemove))
                {
                    if (GUILayout.Button(
                            new GUIContent(
                                "Remove",
                                canRemove
                                    ? "Remove this embedded row from its parent. Referenced records are not deleted."
                                    : "The collection is at its minimum row count."),
                            GUILayout.Width(62f)))
                    {
                        GameContentEditStructuredFieldsRenderer.ApplyStructuredOperation(
                            context,
                            active,
                            field.FieldId,
                            GameContentStructuredCollectionOperation.RemoveRow(row.RowKey));
                    }
                }
            }
        }

        internal static GameContentStructuredRowValue ResolveSelectedStructuredRow(
            GameContentEditDrafts drafts,
            string stateKey,
            GameContentOrderedStructuredCollectionValue collection)
        {
            if (collection == null || collection.Count == 0)
            {
                drafts.StructuredSelections.Remove(stateKey);
                return null;
            }
            if (!drafts.StructuredSelections.TryGetValue(stateKey, out GameContentStructuredRowKey selected) ||
                !collection.TryGetRow(selected, out GameContentStructuredRowValue row))
            {
                row = collection.Rows[0];
                drafts.StructuredSelections[stateKey] = row.RowKey;
            }
            return row;
        }

        internal static string BuildStructuredCountLabel(
            GameContentStructuredCollectionFieldDescriptor descriptor,
            GameContentOrderedStructuredCollectionValue collection)
        {
            string maximum = descriptor?.MaximumCount.HasValue == true
                ? descriptor.MaximumCount.Value.ToString(CultureInfo.InvariantCulture)
                : "any";
            return (collection?.Count ?? 0) + " rows | min " + (descriptor?.MinimumCount ?? 0) +
                   " | max " + maximum;
        }

        internal static int IndexOfStructuredRow(
            GameContentOrderedStructuredCollectionValue collection,
            GameContentStructuredRowKey rowKey)
        {
            if (collection == null || rowKey == null) return -1;
            for (int i = 0; i < collection.Rows.Count; i++)
            {
                if (collection.Rows[i].RowKey.Equals(rowKey)) return i;
            }
            return -1;
        }

        internal static GameContentEditValidationState GetStructuredRowValidationState(
            GameContentValidationPreview preview,
            string collectionFieldId,
            int rowIndex,
            GameContentEditValidationState providerState)
        {
            string prefix = collectionFieldId + "[" + (rowIndex + 1) + "]";
            GameContentAuthoringValidationIssue[] issues = (preview?.Issues ??
                Array.Empty<GameContentAuthoringValidationIssue>())
                .Where(issue => issue != null && issue.Path.StartsWith(prefix, StringComparison.Ordinal))
                .ToArray();
            if (providerState == GameContentEditValidationState.Invalid ||
                issues.Any(issue => issue.Severity == GameContentAuthoringValidationSeverity.Error))
                return GameContentEditValidationState.Invalid;
            if (providerState == GameContentEditValidationState.Warning ||
                issues.Any(issue => issue.Severity == GameContentAuthoringValidationSeverity.Warning))
                return GameContentEditValidationState.Warning;
            return GameContentEditValidationState.Valid;
        }
    }
}
