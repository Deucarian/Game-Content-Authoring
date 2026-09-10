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
    internal static class GameContentEditCollectionRenderer
    {
        internal static void DrawCollectionField(
            GameContentAuthoringSurfaceContext context,
            GameContentActiveEditSession active,
            GameContentFieldDescriptor field,
            GameContentFieldValue current,
            bool enabled)
        {
            GameContentOrderedCollectionValue collection = current?.OrderedCollectionValue;
            GameContentCollectionFieldDescriptor descriptor = field.Collection;
            using (new EditorGUILayout.HorizontalScope())
            {
                DeucarianEditorTextGUI.LabelField(field.DisplayName, DeucarianEditorWorkbenchGUI.BoldLabelStyle);
                GUILayout.FlexibleSpace();
                DeucarianEditorTextGUI.LabelField(
                    BuildCollectionCountLabel(field, collection),
                    DeucarianEditorStyles.MutedLabel,
                    GUILayout.Width(180f));
            }

            if (collection == null || descriptor == null)
            {
                DeucarianEditorTextGUI.HelpBox("The ordered collection is unavailable.", MessageType.Error);
                return;
            }

            if (collection.Items.Count == 0)
                DeucarianEditorTextGUI.LabelField("No items.", DeucarianEditorStyles.MutedLabel);
            for (int i = 0; i < collection.Items.Count; i++)
                DrawCollectionItem(context, active, field, collection, collection.Items[i], i, enabled);

            GUILayout.Space(DeucarianEditorSpacing.Small);
            if (field.FieldType == GameContentFieldType.OrderedRecordReferenceCollection)
                DrawReferenceCollectionAdd(context, active, field, collection, enabled);
            else
                DrawScalarCollectionAdd(context, active, field, collection, enabled);

            IReadOnlyList<GameContentCollectionOperation> restoreOperations =
                GameContentCollectionMutation.BuildRestoreOriginalOrderOperations(collection);
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.FlexibleSpace();
                using (new EditorGUI.DisabledScope(!enabled || restoreOperations.Count == 0))
                {
                    if (DeucarianEditorActionGUI.Button(
                            new GUIContent(
                                "Restore Original Order",
                                "Reorder surviving original items by their session-start positions. Added items remain after them."),
                            GUILayout.Width(150f)))
                    {
                        context.EditSessions.RestoreOriginalCollectionOrder(active, field.FieldId);
                        context.RequestRepaint();
                    }
                }
            }
        }

        internal static void DrawCollectionItem(
            GameContentAuthoringSurfaceContext context,
            GameContentActiveEditSession active,
            GameContentFieldDescriptor field,
            GameContentOrderedCollectionValue collection,
            GameContentCollectionItem item,
            int index,
            bool enabled)
        {
            using (new EditorGUILayout.VerticalScope())
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    DeucarianEditorTextGUI.LabelField((index + 1).ToString(CultureInfo.InvariantCulture), GUILayout.Width(24f));
                    if (field.FieldType == GameContentFieldType.OrderedRecordReferenceCollection)
                        DrawCollectionReferenceValue(context, active, field, item, enabled);
                    else
                        DrawCollectionScalarValue(context, active, field, item, enabled);

                    using (new EditorGUI.DisabledScope(!enabled || index <= 0))
                    {
                        if (DeucarianEditorActionGUI.Button(new GUIContent("Up", "Move this item one position earlier."), GUILayout.Width(42f)))
                        {
                            ApplyCollectionOperation(
                                context,
                                active,
                                field.FieldId,
                                GameContentCollectionOperation.Move(item.ItemKey, index - 1));
                        }
                    }
                    using (new EditorGUI.DisabledScope(!enabled || index >= collection.Items.Count - 1))
                    {
                        if (DeucarianEditorActionGUI.Button(new GUIContent("Down", "Move this item one position later."), GUILayout.Width(48f)))
                        {
                            ApplyCollectionOperation(
                                context,
                                active,
                                field.FieldId,
                                GameContentCollectionOperation.Move(item.ItemKey, index + 1));
                        }
                    }

                    int minimum = Math.Max(field.Collection.MinimumCount, field.Required ? 1 : 0);
                    bool canRemove = collection.Count > minimum;
                    string removeReason = canRemove
                        ? "Remove this reference or scalar value. The target record is not deleted."
                        : "The collection is already at its minimum count.";
                    using (new EditorGUI.DisabledScope(!enabled || !canRemove))
                    {
                        if (DeucarianEditorActionGUI.Button(new GUIContent("Remove", removeReason), GUILayout.Width(62f)))
                        {
                            ApplyCollectionOperation(
                                context,
                                active,
                                field.FieldId,
                                GameContentCollectionOperation.Remove(item.ItemKey));
                        }
                    }
                }
                DrawCollectionItemValidation(context, active, field, item, index);
            }
        }

        internal static void DrawCollectionScalarValue(
            GameContentAuthoringSurfaceContext context,
            GameContentActiveEditSession active,
            GameContentFieldDescriptor field,
            GameContentCollectionItem item,
            bool enabled)
        {
            using (new EditorGUI.DisabledScope(!enabled))
            {
                EditorGUI.BeginChangeCheck();
                GameContentFieldValue replacement = GameContentEditFieldRenderer.DrawScalarValue(
                    field.Collection.ItemDescriptor,
                    item.Value,
                    true);
                if (EditorGUI.EndChangeCheck() && replacement != null && !replacement.Equals(item.Value))
                {
                    ApplyCollectionOperation(
                        context,
                        active,
                        field.FieldId,
                        GameContentCollectionOperation.Replace(item.ItemKey, replacement));
                }
            }
        }

        internal static void DrawCollectionReferenceValue(
            GameContentAuthoringSurfaceContext context,
            GameContentActiveEditSession active,
            GameContentFieldDescriptor field,
            GameContentCollectionItem item,
            bool enabled)
        {
            GameContentRecordReferenceValue reference = item.Value.RecordReferenceValue;
            DeucarianEditorTextGUI.LabelField(
                GameContentEditReferenceRenderer.DescribeReference(reference),
                reference != null && reference.IsBroken ? DeucarianEditorWorkbenchGUI.BoldLabelStyle : DeucarianEditorWorkbenchGUI.LabelStyle,
                GUILayout.ExpandWidth(true));
            using (new EditorGUI.DisabledScope(!enabled))
            {
                if (DeucarianEditorActionGUI.Button(new GUIContent("Replace...", "Choose another compatible record."), GUILayout.Width(76f)))
                {
                    Rect rect = GUILayoutUtility.GetLastRect();
                    GameContentReferenceCandidateSet targets = context.EditSessions.GetReferenceCandidates(
                        active,
                        field.FieldId,
                        item.ItemKey);
                    var dropdown = new GameContentReferenceDropdown(
                        field.Collection.ItemDescriptor.RecordReference?.TargetLabel ?? "Record",
                        targets,
                        false,
                        selected =>
                        {
                            ApplyCollectionOperation(
                                context,
                                active,
                                field.FieldId,
                                GameContentCollectionOperation.Replace(
                                    item.ItemKey,
                                    GameContentFieldValue.FromRecordReference(selected)));
                        });
                    dropdown.Show(rect);
                }
            }

            GameContentRecordDescriptor target = GameContentEditReferenceRenderer.ResolveCurrentTarget(context, reference);
            using (new EditorGUI.DisabledScope(target == null))
            {
                if (DeucarianEditorActionGUI.Button(new GUIContent("Open", "Open the referenced record without editing it."), GUILayout.Width(48f)))
                    GameContentEditReferenceRenderer.OpenTarget(context, target);
            }
        }

        internal static void DrawScalarCollectionAdd(
            GameContentAuthoringSurfaceContext context,
            GameContentActiveEditSession active,
            GameContentFieldDescriptor field,
            GameContentOrderedCollectionValue collection,
            bool enabled)
        {
            string draftKey = field.FieldId;
            if (!context.EditWorkbenchState.ForSession(active).CollectionAddDrafts.TryGetValue(draftKey, out GameContentFieldValue draft) ||
                draft == null || draft.FieldType != field.Collection.ItemDescriptor.FieldType)
            {
                draft = GameContentEditFieldRenderer.CreateDefaultScalarValue(field.Collection.ItemDescriptor);
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                DeucarianEditorTextGUI.LabelField("New Item", GUILayout.Width(72f));
                using (new EditorGUI.DisabledScope(!enabled))
                    draft = GameContentEditFieldRenderer.DrawScalarValue(field.Collection.ItemDescriptor, draft, false);
                context.EditWorkbenchState.ForSession(active).CollectionAddDrafts[draftKey] = draft;

                GameContentCollectionOperation operation = GameContentCollectionOperation.Add(draft);
                GameContentEditOperationResult validation = context.EditSessions.ValidateCollectionOperation(
                    active,
                    field.FieldId,
                    operation);
                using (new EditorGUI.DisabledScope(!enabled || !validation.Succeeded))
                {
                    if (DeucarianEditorActionGUI.Button(new GUIContent("Add", validation.Message), GUILayout.Width(48f)))
                    {
                        GameContentEditOperationResult result = context.EditSessions.ApplyCollectionOperation(
                            active,
                            field.FieldId,
                            operation);
                        if (result.Succeeded)
                            context.EditWorkbenchState.ForSession(active).CollectionAddDrafts[draftKey] = GameContentEditFieldRenderer.CreateDefaultScalarValue(field.Collection.ItemDescriptor);
                        context.RequestRepaint();
                    }
                }
            }
        }

        internal static void DrawReferenceCollectionAdd(
            GameContentAuthoringSurfaceContext context,
            GameContentActiveEditSession active,
            GameContentFieldDescriptor field,
            GameContentOrderedCollectionValue collection,
            bool enabled)
        {
            bool belowMaximum = !field.Collection.MaximumCount.HasValue ||
                                collection.Count < field.Collection.MaximumCount.Value;
            GameContentReferenceCandidateSet targets = context.EditSessions.GetReferenceCandidates(active, field.FieldId);
            bool canAdd = enabled && belowMaximum && targets.Candidates.Count > 0;
            using (new EditorGUILayout.HorizontalScope())
            {
                DeucarianEditorTextGUI.LabelField("New Reference", GUILayout.Width(100f));
                using (new EditorGUI.DisabledScope(!canAdd))
                {
                    if (DeucarianEditorActionGUI.Button(
                            new GUIContent(
                                "Add Compatible...",
                                belowMaximum ? targets.Message : "The collection is already at its maximum count."),
                            GUILayout.Width(126f)))
                    {
                        Rect rect = GUILayoutUtility.GetLastRect();
                        var dropdown = new GameContentReferenceDropdown(
                            field.Collection.ItemDescriptor.RecordReference?.TargetLabel ?? "Record",
                            targets,
                            false,
                            selected =>
                            {
                                ApplyCollectionOperation(
                                    context,
                                    active,
                                    field.FieldId,
                                    GameContentCollectionOperation.Add(
                                        GameContentFieldValue.FromRecordReference(selected)));
                            });
                        dropdown.Show(rect);
                    }
                }
                if (!canAdd)
                {
                    string reason = !belowMaximum
                        ? "Maximum count reached."
                        : string.IsNullOrWhiteSpace(targets.Message) ? "No compatible target is available." : targets.Message;
                    DeucarianEditorTextGUI.LabelField(reason, DeucarianEditorStyles.MutedLabel);
                }
            }
        }

        internal static void DrawCollectionItemValidation(
            GameContentAuthoringSurfaceContext context,
            GameContentActiveEditSession active,
            GameContentFieldDescriptor field,
            GameContentCollectionItem item,
            int index)
        {
            if (!field.Collection.ItemDescriptor.Accepts(item.Value, out string reason))
            {
                DeucarianEditorTextGUI.HelpBox("Item " + (index + 1) + ": " + reason, MessageType.Error);
                return;
            }
            if (item.Value.FieldType != GameContentFieldType.RecordReference) return;

            GameContentRecordReferenceValue reference = item.Value.RecordReferenceValue;
            if (reference == null || reference.IsBroken)
            {
                DeucarianEditorTextGUI.HelpBox(
                    "Item " + (index + 1) + " is broken: " +
                    (reference?.BrokenReason ?? "No reference value is available."),
                    MessageType.Error);
                return;
            }
            if (!reference.IsResolved || reference.TargetKey == null) return;
            GameContentReferenceEvaluation evaluation = context.EditSessions.EvaluateReferenceTarget(
                active,
                field.FieldId,
                reference.TargetKey);
            if (!evaluation.IsValid)
                DeucarianEditorTextGUI.HelpBox("Item " + (index + 1) + ": " + evaluation.Reason, MessageType.Error);
            GameContentRecordLensBrowser.DrawRow(
                "Target " + (index + 1) + " ID",
                reference.TargetKey.SourceRecordId);
        }

        internal static void ApplyCollectionOperation(
            GameContentAuthoringSurfaceContext context,
            GameContentActiveEditSession active,
            string fieldId,
            GameContentCollectionOperation operation)
        {
            context.EditSessions.ApplyCollectionOperation(active, fieldId, operation);
            context.RequestRepaint();
        }

        internal static string BuildCollectionCountLabel(
            GameContentFieldDescriptor field,
            GameContentOrderedCollectionValue collection)
        {
            int minimum = Math.Max(field.Collection?.MinimumCount ?? 0, field.Required ? 1 : 0);
            string maximum = field.Collection?.MaximumCount.HasValue == true
                ? field.Collection.MaximumCount.Value.ToString(CultureInfo.InvariantCulture)
                : "any";
            return (collection?.Count ?? 0) + " items | min " + minimum + " | max " + maximum;
        }
    }
}
