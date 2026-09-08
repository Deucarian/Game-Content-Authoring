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
    internal static class GameContentEditStructuredFieldsRenderer
    {
        internal static void DrawStructuredRowDetail(
            GameContentAuthoringSurfaceContext context,
            GameContentActiveEditSession active,
            GameContentFieldDescriptor field,
            GameContentStructuredRowValue row,
            int rowIndex,
            bool enabled)
        {
            GameContentStructuredRowDescriptor rowDescriptor = field.StructuredCollection.RowDescriptor;
            GUILayout.Space(DeucarianEditorSpacing.Small);
            EditorGUILayout.LabelField("Selected " + rowDescriptor.DisplayName, EditorStyles.boldLabel);
            if (!string.IsNullOrWhiteSpace(rowDescriptor.HelpText))
                EditorGUILayout.LabelField(rowDescriptor.HelpText, DeucarianEditorStyles.MutedLabel);
            if (rowDescriptor.NativeKey != null)
            {
                GameContentRecordLensBrowser.DrawRow(
                    rowDescriptor.NativeKey.DisplayName,
                    string.IsNullOrWhiteSpace(row.NativeKeyDisplayMetadata)
                        ? "Not supplied"
                        : row.NativeKeyDisplayMetadata);
                if (!string.IsNullOrWhiteSpace(rowDescriptor.NativeKey.HelpText))
                    EditorGUILayout.LabelField(rowDescriptor.NativeKey.HelpText, DeucarianEditorStyles.MutedLabel);
            }

            for (int i = 0; i < rowDescriptor.Fields.Count; i++)
            {
                GameContentFieldDescriptor rowField = rowDescriptor.Fields[i];
                bool hasStagedValue = row.TryGetFieldValue(rowField.FieldId, out GameContentFieldValue value);
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField(rowField.DisplayName, GUILayout.Width(128f));
                    if (value == null && rowField.IsReadOnly)
                    {
                        EditorGUILayout.LabelField("Not set", DeucarianEditorStyles.MutedLabel);
                    }
                    else if (rowField.FieldType == GameContentFieldType.RecordReference)
                    {
                        value = value ?? CreateDefaultStructuredValue(rowField);
                        DrawStructuredReferenceSelector(
                            context,
                            active,
                            field,
                            row.RowKey,
                            rowField,
                            value,
                            enabled && !rowField.IsReadOnly);
                    }
                    else
                    {
                        value = value ?? CreateDefaultStructuredValue(rowField);
                        using (new EditorGUI.DisabledScope(!enabled || rowField.IsReadOnly))
                        {
                            EditorGUI.BeginChangeCheck();
                            GameContentFieldValue replacement = GameContentEditFieldRenderer.DrawScalarValue(rowField, value, true);
                            if (EditorGUI.EndChangeCheck() && replacement != null && !replacement.Equals(value))
                            {
                                ApplyStructuredOperation(
                                    context,
                                    active,
                                    field.FieldId,
                                    GameContentStructuredCollectionOperation.ReplaceRowField(
                                        row.RowKey,
                                        rowField.FieldId,
                                        replacement));
                            }
                        }
                    }
                }
                if (!string.IsNullOrWhiteSpace(rowField.Description))
                    EditorGUILayout.LabelField(rowField.Description, DeucarianEditorStyles.MutedLabel);
                if (rowField.IsReadOnly)
                    EditorGUILayout.LabelField(rowField.ReadOnlyReason, DeucarianEditorStyles.MutedLabel);
                if (hasStagedValue)
                    DrawStructuredFieldDelta(active, field.FieldId, row, rowField.FieldId, value);
                DrawStructuredFieldValidation(
                    active.Validation,
                    field.FieldId,
                    rowIndex,
                    rowField.FieldId);
            }
        }

        internal static void DrawStructuredFieldDelta(
            GameContentActiveEditSession active,
            string collectionFieldId,
            GameContentStructuredRowValue stagedRow,
            string rowFieldId,
            GameContentFieldValue stagedValue)
        {
            GameContentOrderedStructuredCollectionValue originalCollection = null;
            if (active?.Snapshot != null &&
                active.Snapshot.TryGetValue(collectionFieldId, out GameContentFieldValue originalField))
                originalCollection = originalField?.OrderedStructuredCollectionValue;
            GameContentStructuredRowValue originalRow = null;
            originalCollection?.TryGetRow(stagedRow.RowKey, out originalRow);
            GameContentFieldValue originalValue = null;
            originalRow?.TryGetFieldValue(rowFieldId, out originalValue);
            if (originalRow != null && Equals(originalValue, stagedValue)) return;

            GameContentRecordLensBrowser.DrawRow(
                "Before",
                originalRow == null ? "New embedded row" : originalValue?.ToDisplayString() ?? "Not set");
            GameContentRecordLensBrowser.DrawRow("Staged", stagedValue?.ToDisplayString() ?? "Not set");
        }

        internal static void DrawStructuredRowAdd(
            GameContentAuthoringSurfaceContext context,
            GameContentActiveEditSession active,
            GameContentFieldDescriptor field,
            GameContentOrderedStructuredCollectionValue collection,
            bool enabled,
            string stateKey)
        {
            GameContentStructuredCollectionFieldDescriptor descriptor = field.StructuredCollection;
            string draftKey = stateKey + "|add";
            if (!context.EditWorkbenchState.ForSession(active).StructuredAddDrafts.TryGetValue(draftKey, out IReadOnlyList<GameContentStructuredRowFieldValue> draft))
                draft = CreateDefaultStructuredDraft(descriptor.RowDescriptor);

            GUILayout.Space(DeucarianEditorSpacing.Small);
            EditorGUILayout.LabelField("Add " + descriptor.RowDescriptor.DisplayName, EditorStyles.boldLabel);
            var values = draft.ToDictionary(value => value.FieldId, value => value.Value, StringComparer.Ordinal);
            for (int i = 0; i < descriptor.RowDescriptor.Fields.Count; i++)
            {
                GameContentFieldDescriptor rowField = descriptor.RowDescriptor.Fields[i];
                if (rowField.IsReadOnly) continue;
                values.TryGetValue(rowField.FieldId, out GameContentFieldValue current);
                if (current == null) current = CreateDefaultStructuredValue(rowField);
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField(rowField.DisplayName, GUILayout.Width(128f));
                    if (rowField.FieldType == GameContentFieldType.RecordReference)
                    {
                        GameContentFieldValue captured = current;
                        DrawStructuredDraftReferenceSelector(
                            context,
                            active,
                            field,
                            rowField,
                            captured,
                            enabled,
                            selected =>
                            {
                                values[rowField.FieldId] = GameContentFieldValue.FromRecordReference(selected);
                                context.EditWorkbenchState.ForSession(active).StructuredAddDrafts[draftKey] = ToStructuredDraft(descriptor.RowDescriptor, values);
                            });
                    }
                    else
                    {
                        using (new EditorGUI.DisabledScope(!enabled))
                            values[rowField.FieldId] = GameContentEditFieldRenderer.DrawScalarValue(rowField, current, false);
                    }
                }
            }
            draft = ToStructuredDraft(descriptor.RowDescriptor, values);
            context.EditWorkbenchState.ForSession(active).StructuredAddDrafts[draftKey] = draft;
            GameContentStructuredCollectionOperation operation =
                GameContentStructuredCollectionOperation.AddRow(draft);
            GameContentStructuredCollectionOperationResult validation =
                context.EditSessions.ValidateStructuredOperation(active, field.FieldId, operation);
            bool belowMaximum = !descriptor.MaximumCount.HasValue || collection.Count < descriptor.MaximumCount.Value;
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.FlexibleSpace();
                using (new EditorGUI.DisabledScope(!enabled || !belowMaximum || !validation.Succeeded))
                {
                    if (GUILayout.Button(
                            new GUIContent("Add Row", validation.Message),
                            GUILayout.Width(76f)))
                    {
                        GameContentStructuredCollectionOperationResult result =
                            context.EditSessions.ApplyStructuredOperation(active, field.FieldId, operation);
                        if (result.Succeeded)
                        {
                            context.EditWorkbenchState.ForSession(active).StructuredAddDrafts[draftKey] = CreateDefaultStructuredDraft(
                                descriptor.RowDescriptor);
                            context.EditWorkbenchState.ForSession(active).StructuredSelections[stateKey] = result.RowKey;
                        }
                        context.RequestRepaint();
                    }
                }
            }
            if (!validation.Succeeded && !string.IsNullOrWhiteSpace(validation.Message))
                EditorGUILayout.LabelField(validation.Message, DeucarianEditorStyles.MutedLabel);
        }

        internal static void DrawStructuredReferenceSelector(
            GameContentAuthoringSurfaceContext context,
            GameContentActiveEditSession active,
            GameContentFieldDescriptor collectionField,
            GameContentStructuredRowKey rowKey,
            GameContentFieldDescriptor rowField,
            GameContentFieldValue current,
            bool enabled)
        {
            GameContentRecordReferenceValue reference = current.RecordReferenceValue;
            EditorGUILayout.LabelField(GameContentEditReferenceRenderer.DescribeReference(reference), GUILayout.ExpandWidth(true));
            using (new EditorGUI.DisabledScope(!enabled))
            {
                if (GUILayout.Button(new GUIContent("Choose...", "Select a compatible same-pack record."), GUILayout.Width(70f)))
                {
                    Rect rect = GUILayoutUtility.GetLastRect();
                    GameContentReferenceCandidateSet targets = context.EditSessions.GetStructuredReferenceCandidates(
                        active,
                        collectionField.FieldId,
                        rowKey,
                        rowField.FieldId);
                    var dropdown = new GameContentReferenceDropdown(
                        rowField.RecordReference?.TargetLabel ?? "Record",
                        targets,
                        !rowField.Required && (rowField.RecordReference?.AllowClear ?? false),
                        selected => ApplyStructuredOperation(
                            context,
                            active,
                            collectionField.FieldId,
                            GameContentStructuredCollectionOperation.ReplaceRowField(
                                rowKey,
                                rowField.FieldId,
                                GameContentFieldValue.FromRecordReference(selected))));
                    dropdown.Show(rect);
                }
            }
        }

        internal static void DrawStructuredDraftReferenceSelector(
            GameContentAuthoringSurfaceContext context,
            GameContentActiveEditSession active,
            GameContentFieldDescriptor collectionField,
            GameContentFieldDescriptor rowField,
            GameContentFieldValue current,
            bool enabled,
            Action<GameContentRecordReferenceValue> selected)
        {
            EditorGUILayout.LabelField(GameContentEditReferenceRenderer.DescribeReference(current.RecordReferenceValue), GUILayout.ExpandWidth(true));
            using (new EditorGUI.DisabledScope(!enabled))
            {
                if (GUILayout.Button(new GUIContent("Choose...", "Select a compatible same-pack record."), GUILayout.Width(70f)))
                {
                    Rect rect = GUILayoutUtility.GetLastRect();
                    GameContentReferenceCandidateSet targets = context.EditSessions.GetStructuredReferenceCandidates(
                        active,
                        collectionField.FieldId,
                        null,
                        rowField.FieldId);
                    var dropdown = new GameContentReferenceDropdown(
                        rowField.RecordReference?.TargetLabel ?? "Record",
                        targets,
                        !rowField.Required && (rowField.RecordReference?.AllowClear ?? false),
                        value =>
                        {
                            selected?.Invoke(value);
                            context.RequestRepaint();
                        });
                    dropdown.Show(rect);
                }
            }
        }

        internal static IReadOnlyList<GameContentStructuredRowFieldValue> CreateDefaultStructuredDraft(
            GameContentStructuredRowDescriptor descriptor)
        {
            return descriptor.Fields
                .Where(field => !field.IsReadOnly)
                .Select(field => new GameContentStructuredRowFieldValue(
                    field.FieldId,
                    CreateDefaultStructuredValue(field)))
                .ToArray();
        }

        internal static GameContentFieldValue CreateDefaultStructuredValue(GameContentFieldDescriptor field)
        {
            return field.FieldType == GameContentFieldType.RecordReference
                ? GameContentFieldValue.FromRecordReference(GameContentRecordReferenceValue.None())
                : GameContentEditFieldRenderer.CreateDefaultScalarValue(field);
        }

        internal static IReadOnlyList<GameContentStructuredRowFieldValue> ToStructuredDraft(
            GameContentStructuredRowDescriptor descriptor,
            IReadOnlyDictionary<string, GameContentFieldValue> values)
        {
            return descriptor.Fields
                .Where(field => !field.IsReadOnly && values.ContainsKey(field.FieldId))
                .Select(field => new GameContentStructuredRowFieldValue(field.FieldId, values[field.FieldId]))
                .ToArray();
        }

        internal static void ApplyStructuredOperation(
            GameContentAuthoringSurfaceContext context,
            GameContentActiveEditSession active,
            string fieldId,
            GameContentStructuredCollectionOperation operation)
        {
            context.EditSessions.ApplyStructuredOperation(active, fieldId, operation);
            context.RequestRepaint();
        }

        internal static void DrawStructuredFieldValidation(
            GameContentValidationPreview preview,
            string collectionFieldId,
            int rowIndex,
            string rowFieldId)
        {
            if (preview == null || rowIndex < 0) return;
            string path = collectionFieldId + "[" + (rowIndex + 1) + "]." + rowFieldId;
            GameContentAuthoringValidationIssue[] issues = preview.Issues.Where(issue =>
                issue != null && string.Equals(issue.Path, path, StringComparison.Ordinal)).ToArray();
            for (int i = 0; i < issues.Length; i++)
                EditorGUILayout.HelpBox(issues[i].Message, GameContentEditFieldRenderer.ToMessageType(issues[i].Severity));
        }
    }
}
