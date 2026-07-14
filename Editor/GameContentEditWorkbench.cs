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
        private static readonly Dictionary<string, GameContentFieldValue> CollectionAddDrafts =
            new Dictionary<string, GameContentFieldValue>(StringComparer.Ordinal);
        private static readonly Dictionary<string, GameContentStructuredRowKey> StructuredSelections =
            new Dictionary<string, GameContentStructuredRowKey>(StringComparer.Ordinal);
        private static readonly Dictionary<string, IReadOnlyList<GameContentStructuredRowFieldValue>> StructuredAddDrafts =
            new Dictionary<string, IReadOnlyList<GameContentStructuredRowFieldValue>>(StringComparer.Ordinal);

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
            DrawChangeReview(context, active);
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
                if (field.FieldType == GameContentFieldType.OrderedStructuredCollection)
                {
                    DrawStructuredCollectionField(context, active, field, current, enabled);
                }
                else if (field.FieldType.IsOrderedCollection())
                {
                    DrawCollectionField(context, active, field, current, enabled);
                }
                else
                {
                    using (new EditorGUI.DisabledScope(!enabled))
                    {
                        EditorGUI.BeginChangeCheck();
                        using (new EditorGUILayout.HorizontalScope())
                        {
                            EditorGUILayout.LabelField(field.DisplayName, GUILayout.Width(128f));
                            next = DrawValue(context, active, field, current);
                        }
                        if (EditorGUI.EndChangeCheck() && next != null && !next.Equals(current))
                        {
                            GameContentEditOperationResult result = context.EditSessions.Apply(active, field.FieldId, next);
                            if (result.Succeeded) context.EditSessions.Preview(active);
                            context.RequestRepaint();
                        }
                    }
                }

                string detail = BuildFieldDetail(field);
                if (!string.IsNullOrWhiteSpace(detail))
                    EditorGUILayout.LabelField(detail, DeucarianEditorStyles.MutedLabel);
                if (field.IsReadOnly && !string.IsNullOrWhiteSpace(field.ReadOnlyReason))
                    EditorGUILayout.LabelField(field.ReadOnlyReason, DeucarianEditorStyles.MutedLabel);
                if (field.FieldType == GameContentFieldType.RecordReference)
                    DrawReferenceStatus(context, field, current);
                DrawFieldValidation(active.Validation, field);
            }
        }

        private static GameContentFieldValue DrawValue(
            GameContentAuthoringSurfaceContext context,
            GameContentActiveEditSession active,
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
                case GameContentFieldType.RecordReference:
                    DrawReferenceSelector(context, active, field, current);
                    return current;
                default:
                    return GameContentFieldValue.FromString(EditorGUILayout.TextField(current.StringValue ?? string.Empty));
            }
        }

        private static void DrawStructuredCollectionField(
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

            string stateKey = BuildCollectionDraftKey(active, field.FieldId);
            GameContentStructuredRowValue selected = ResolveSelectedStructuredRow(
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

            selected = ResolveSelectedStructuredRow(stateKey, collection);
            if (selected != null)
                DrawStructuredRowDetail(
                    context,
                    active,
                    field,
                    selected,
                    IndexOfStructuredRow(collection, selected.RowKey),
                    enabled);
            if (descriptor.Allows(GameContentStructuredCollectionPermittedOperations.AddRow))
                DrawStructuredRowAdd(context, active, field, collection, enabled, stateKey);

            EditorGUILayout.HelpBox(
                "Rows are embedded values owned by this parent source. Adding or removing one does not create or delete a top-level authored record. Stable and provider-native IDs remain read-only.",
                MessageType.Info);
        }

        private static void DrawStructuredRowListItem(
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
                    StructuredSelections[stateKey] = row.RowKey;
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
                        ApplyStructuredOperation(
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
                        ApplyStructuredOperation(
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
                        ApplyStructuredOperation(
                            context,
                            active,
                            field.FieldId,
                            GameContentStructuredCollectionOperation.RemoveRow(row.RowKey));
                    }
                }
            }
        }

        private static void DrawStructuredRowDetail(
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
                            GameContentFieldValue replacement = DrawScalarValue(rowField, value, true);
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

        private static void DrawStructuredFieldDelta(
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

        private static void DrawStructuredRowAdd(
            GameContentAuthoringSurfaceContext context,
            GameContentActiveEditSession active,
            GameContentFieldDescriptor field,
            GameContentOrderedStructuredCollectionValue collection,
            bool enabled,
            string stateKey)
        {
            GameContentStructuredCollectionFieldDescriptor descriptor = field.StructuredCollection;
            string draftKey = stateKey + "|add";
            if (!StructuredAddDrafts.TryGetValue(draftKey, out IReadOnlyList<GameContentStructuredRowFieldValue> draft))
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
                                StructuredAddDrafts[draftKey] = ToStructuredDraft(descriptor.RowDescriptor, values);
                            });
                    }
                    else
                    {
                        using (new EditorGUI.DisabledScope(!enabled))
                            values[rowField.FieldId] = DrawScalarValue(rowField, current, false);
                    }
                }
            }
            draft = ToStructuredDraft(descriptor.RowDescriptor, values);
            StructuredAddDrafts[draftKey] = draft;
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
                            StructuredAddDrafts[draftKey] = CreateDefaultStructuredDraft(
                                descriptor.RowDescriptor);
                            StructuredSelections[stateKey] = result.RowKey;
                        }
                        context.RequestRepaint();
                    }
                }
            }
            if (!validation.Succeeded && !string.IsNullOrWhiteSpace(validation.Message))
                EditorGUILayout.LabelField(validation.Message, DeucarianEditorStyles.MutedLabel);
        }

        private static void DrawStructuredReferenceSelector(
            GameContentAuthoringSurfaceContext context,
            GameContentActiveEditSession active,
            GameContentFieldDescriptor collectionField,
            GameContentStructuredRowKey rowKey,
            GameContentFieldDescriptor rowField,
            GameContentFieldValue current,
            bool enabled)
        {
            GameContentRecordReferenceValue reference = current.RecordReferenceValue;
            EditorGUILayout.LabelField(DescribeReference(reference), GUILayout.ExpandWidth(true));
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

        private static void DrawStructuredDraftReferenceSelector(
            GameContentAuthoringSurfaceContext context,
            GameContentActiveEditSession active,
            GameContentFieldDescriptor collectionField,
            GameContentFieldDescriptor rowField,
            GameContentFieldValue current,
            bool enabled,
            Action<GameContentRecordReferenceValue> selected)
        {
            EditorGUILayout.LabelField(DescribeReference(current.RecordReferenceValue), GUILayout.ExpandWidth(true));
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

        private static GameContentStructuredRowValue ResolveSelectedStructuredRow(
            string stateKey,
            GameContentOrderedStructuredCollectionValue collection)
        {
            if (collection == null || collection.Count == 0)
            {
                StructuredSelections.Remove(stateKey);
                return null;
            }
            if (!StructuredSelections.TryGetValue(stateKey, out GameContentStructuredRowKey selected) ||
                !collection.TryGetRow(selected, out GameContentStructuredRowValue row))
            {
                row = collection.Rows[0];
                StructuredSelections[stateKey] = row.RowKey;
            }
            return row;
        }

        private static IReadOnlyList<GameContentStructuredRowFieldValue> CreateDefaultStructuredDraft(
            GameContentStructuredRowDescriptor descriptor)
        {
            return descriptor.Fields
                .Where(field => !field.IsReadOnly)
                .Select(field => new GameContentStructuredRowFieldValue(
                    field.FieldId,
                    CreateDefaultStructuredValue(field)))
                .ToArray();
        }

        private static GameContentFieldValue CreateDefaultStructuredValue(GameContentFieldDescriptor field)
        {
            return field.FieldType == GameContentFieldType.RecordReference
                ? GameContentFieldValue.FromRecordReference(GameContentRecordReferenceValue.None())
                : CreateDefaultScalarValue(field);
        }

        private static IReadOnlyList<GameContentStructuredRowFieldValue> ToStructuredDraft(
            GameContentStructuredRowDescriptor descriptor,
            IReadOnlyDictionary<string, GameContentFieldValue> values)
        {
            return descriptor.Fields
                .Where(field => !field.IsReadOnly && values.ContainsKey(field.FieldId))
                .Select(field => new GameContentStructuredRowFieldValue(field.FieldId, values[field.FieldId]))
                .ToArray();
        }

        private static void ApplyStructuredOperation(
            GameContentAuthoringSurfaceContext context,
            GameContentActiveEditSession active,
            string fieldId,
            GameContentStructuredCollectionOperation operation)
        {
            context.EditSessions.ApplyStructuredOperation(active, fieldId, operation);
            context.RequestRepaint();
        }

        private static string BuildStructuredCountLabel(
            GameContentStructuredCollectionFieldDescriptor descriptor,
            GameContentOrderedStructuredCollectionValue collection)
        {
            string maximum = descriptor?.MaximumCount.HasValue == true
                ? descriptor.MaximumCount.Value.ToString(CultureInfo.InvariantCulture)
                : "any";
            return (collection?.Count ?? 0) + " rows | min " + (descriptor?.MinimumCount ?? 0) +
                   " | max " + maximum;
        }

        private static void DrawStructuredFieldValidation(
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
                EditorGUILayout.HelpBox(issues[i].Message, ToMessageType(issues[i].Severity));
        }

        private static int IndexOfStructuredRow(
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

        private static GameContentEditValidationState GetStructuredRowValidationState(
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

        private static void DrawCollectionField(
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
                EditorGUILayout.LabelField(field.DisplayName, EditorStyles.boldLabel);
                GUILayout.FlexibleSpace();
                EditorGUILayout.LabelField(
                    BuildCollectionCountLabel(field, collection),
                    DeucarianEditorStyles.MutedLabel,
                    GUILayout.Width(180f));
            }

            if (collection == null || descriptor == null)
            {
                EditorGUILayout.HelpBox("The ordered collection is unavailable.", MessageType.Error);
                return;
            }

            if (collection.Items.Count == 0)
                EditorGUILayout.LabelField("No items.", DeucarianEditorStyles.MutedLabel);
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
                    if (GUILayout.Button(
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

        private static void DrawCollectionItem(
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
                    EditorGUILayout.LabelField((index + 1).ToString(CultureInfo.InvariantCulture), GUILayout.Width(24f));
                    if (field.FieldType == GameContentFieldType.OrderedRecordReferenceCollection)
                        DrawCollectionReferenceValue(context, active, field, item, enabled);
                    else
                        DrawCollectionScalarValue(context, active, field, item, enabled);

                    using (new EditorGUI.DisabledScope(!enabled || index <= 0))
                    {
                        if (GUILayout.Button(new GUIContent("Up", "Move this item one position earlier."), GUILayout.Width(42f)))
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
                        if (GUILayout.Button(new GUIContent("Down", "Move this item one position later."), GUILayout.Width(48f)))
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
                        if (GUILayout.Button(new GUIContent("Remove", removeReason), GUILayout.Width(62f)))
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

        private static void DrawCollectionScalarValue(
            GameContentAuthoringSurfaceContext context,
            GameContentActiveEditSession active,
            GameContentFieldDescriptor field,
            GameContentCollectionItem item,
            bool enabled)
        {
            using (new EditorGUI.DisabledScope(!enabled))
            {
                EditorGUI.BeginChangeCheck();
                GameContentFieldValue replacement = DrawScalarValue(
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

        private static void DrawCollectionReferenceValue(
            GameContentAuthoringSurfaceContext context,
            GameContentActiveEditSession active,
            GameContentFieldDescriptor field,
            GameContentCollectionItem item,
            bool enabled)
        {
            GameContentRecordReferenceValue reference = item.Value.RecordReferenceValue;
            EditorGUILayout.LabelField(
                DescribeReference(reference),
                reference != null && reference.IsBroken ? EditorStyles.boldLabel : EditorStyles.label,
                GUILayout.ExpandWidth(true));
            using (new EditorGUI.DisabledScope(!enabled))
            {
                if (GUILayout.Button(new GUIContent("Replace...", "Choose another compatible record."), GUILayout.Width(76f)))
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

            GameContentRecordDescriptor target = ResolveCurrentTarget(context, reference);
            using (new EditorGUI.DisabledScope(target == null))
            {
                if (GUILayout.Button(new GUIContent("Open", "Open the referenced record without editing it."), GUILayout.Width(48f)))
                    OpenTarget(context, target);
            }
        }

        private static void DrawScalarCollectionAdd(
            GameContentAuthoringSurfaceContext context,
            GameContentActiveEditSession active,
            GameContentFieldDescriptor field,
            GameContentOrderedCollectionValue collection,
            bool enabled)
        {
            string draftKey = BuildCollectionDraftKey(active, field.FieldId);
            if (!CollectionAddDrafts.TryGetValue(draftKey, out GameContentFieldValue draft) ||
                draft == null || draft.FieldType != field.Collection.ItemDescriptor.FieldType)
            {
                draft = CreateDefaultScalarValue(field.Collection.ItemDescriptor);
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("New Item", GUILayout.Width(72f));
                using (new EditorGUI.DisabledScope(!enabled))
                    draft = DrawScalarValue(field.Collection.ItemDescriptor, draft, false);
                CollectionAddDrafts[draftKey] = draft;

                GameContentCollectionOperation operation = GameContentCollectionOperation.Add(draft);
                GameContentEditOperationResult validation = context.EditSessions.ValidateCollectionOperation(
                    active,
                    field.FieldId,
                    operation);
                using (new EditorGUI.DisabledScope(!enabled || !validation.Succeeded))
                {
                    if (GUILayout.Button(new GUIContent("Add", validation.Message), GUILayout.Width(48f)))
                    {
                        GameContentEditOperationResult result = context.EditSessions.ApplyCollectionOperation(
                            active,
                            field.FieldId,
                            operation);
                        if (result.Succeeded)
                            CollectionAddDrafts[draftKey] = CreateDefaultScalarValue(field.Collection.ItemDescriptor);
                        context.RequestRepaint();
                    }
                }
            }
        }

        private static void DrawReferenceCollectionAdd(
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
                EditorGUILayout.LabelField("New Reference", GUILayout.Width(100f));
                using (new EditorGUI.DisabledScope(!canAdd))
                {
                    if (GUILayout.Button(
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
                    EditorGUILayout.LabelField(reason, DeucarianEditorStyles.MutedLabel);
                }
            }
        }

        private static void DrawCollectionItemValidation(
            GameContentAuthoringSurfaceContext context,
            GameContentActiveEditSession active,
            GameContentFieldDescriptor field,
            GameContentCollectionItem item,
            int index)
        {
            if (!field.Collection.ItemDescriptor.Accepts(item.Value, out string reason))
            {
                EditorGUILayout.HelpBox("Item " + (index + 1) + ": " + reason, MessageType.Error);
                return;
            }
            if (item.Value.FieldType != GameContentFieldType.RecordReference) return;

            GameContentRecordReferenceValue reference = item.Value.RecordReferenceValue;
            if (reference == null || reference.IsBroken)
            {
                EditorGUILayout.HelpBox(
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
                EditorGUILayout.HelpBox("Item " + (index + 1) + ": " + evaluation.Reason, MessageType.Error);
            GameContentRecordLensBrowser.DrawRow(
                "Target " + (index + 1) + " ID",
                reference.TargetKey.SourceRecordId);
        }

        private static GameContentFieldValue DrawScalarValue(
            GameContentFieldDescriptor descriptor,
            GameContentFieldValue current,
            bool delayed)
        {
            switch (descriptor.FieldType)
            {
                case GameContentFieldType.Integer:
                    return GameContentFieldValue.FromInteger(delayed
                        ? DrawDelayedInteger(current.IntegerValue)
                        : EditorGUILayout.LongField(current.IntegerValue));
                case GameContentFieldType.Number:
                    return GameContentFieldValue.FromNumber(delayed
                        ? EditorGUILayout.DelayedDoubleField(current.NumberValue)
                        : EditorGUILayout.DoubleField(current.NumberValue));
                case GameContentFieldType.Boolean:
                    return GameContentFieldValue.FromBoolean(EditorGUILayout.Toggle(current.BooleanValue));
                case GameContentFieldType.Enum:
                    return DrawEnum(descriptor, current);
                default:
                    return GameContentFieldValue.FromString(delayed
                        ? EditorGUILayout.DelayedTextField(current.StringValue ?? string.Empty)
                        : EditorGUILayout.TextField(current.StringValue ?? string.Empty));
            }
        }

        private static long DrawDelayedInteger(long current)
        {
            string text = EditorGUILayout.DelayedTextField(current.ToString(CultureInfo.InvariantCulture));
            return long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out long value)
                ? value
                : current;
        }

        private static GameContentFieldValue CreateDefaultScalarValue(GameContentFieldDescriptor descriptor)
        {
            switch (descriptor.FieldType)
            {
                case GameContentFieldType.Integer:
                    return GameContentFieldValue.FromInteger(
                        descriptor.MinimumNumber.HasValue
                            ? (long)Math.Ceiling(descriptor.MinimumNumber.Value)
                            : 0L);
                case GameContentFieldType.Number:
                    return GameContentFieldValue.FromNumber(descriptor.MinimumNumber ?? 0d);
                case GameContentFieldType.Boolean:
                    return GameContentFieldValue.FromBoolean(false);
                case GameContentFieldType.Enum:
                    return GameContentFieldValue.FromEnum(
                        descriptor.EnumOptions.Count == 0 ? string.Empty : descriptor.EnumOptions[0].Token);
                default:
                    return GameContentFieldValue.FromString(string.Empty);
            }
        }

        private static void ApplyCollectionOperation(
            GameContentAuthoringSurfaceContext context,
            GameContentActiveEditSession active,
            string fieldId,
            GameContentCollectionOperation operation)
        {
            context.EditSessions.ApplyCollectionOperation(active, fieldId, operation);
            context.RequestRepaint();
        }

        private static string BuildCollectionDraftKey(
            GameContentActiveEditSession active,
            string fieldId)
        {
            return active.BackendId + "|" + active.SourceTarget.LockKey + "|" +
                   active.GetHashCode().ToString(CultureInfo.InvariantCulture) + "|" + fieldId;
        }

        private static string BuildCollectionCountLabel(
            GameContentFieldDescriptor field,
            GameContentOrderedCollectionValue collection)
        {
            int minimum = Math.Max(field.Collection?.MinimumCount ?? 0, field.Required ? 1 : 0);
            string maximum = field.Collection?.MaximumCount.HasValue == true
                ? field.Collection.MaximumCount.Value.ToString(CultureInfo.InvariantCulture)
                : "any";
            return (collection?.Count ?? 0) + " items | min " + minimum + " | max " + maximum;
        }

        private static string DescribeReference(GameContentRecordReferenceValue reference)
        {
            if (reference == null) return "Unavailable";
            if (reference.IsBroken) return "Broken: " + reference.OriginalReference;
            if (reference.IsNone) return "None";
            string display = string.IsNullOrWhiteSpace(reference.TargetDisplayName)
                ? reference.TargetKey?.SourceRecordId ?? string.Empty
                : reference.TargetDisplayName;
            return reference.TargetKey == null
                ? display
                : display + " (" + reference.TargetKey.SourceRecordId + ")";
        }

        private static void DrawReferenceSelector(
            GameContentAuthoringSurfaceContext context,
            GameContentActiveEditSession active,
            GameContentFieldDescriptor field,
            GameContentFieldValue current)
        {
            GameContentRecordReferenceValue reference = current.RecordReferenceValue;
            string label = reference == null ? "Unavailable" : reference.ToDisplayString();
            Rect selectorRect = GUILayoutUtility.GetRect(
                new GUIContent(label),
                EditorStyles.popup,
                GUILayout.ExpandWidth(true));
            if (EditorGUI.DropdownButton(selectorRect, new GUIContent(label), FocusType.Keyboard, EditorStyles.popup))
            {
                GameContentReferenceCandidateSet targets = context.EditSessions.GetReferenceCandidates(
                    active,
                    field.FieldId);
                var dropdown = new GameContentReferenceDropdown(
                    field.RecordReference?.TargetLabel ?? "Record",
                    targets,
                    !field.Required && (field.RecordReference?.AllowClear ?? false),
                    selected =>
                    {
                        GameContentEditOperationResult result = context.EditSessions.Apply(
                            active,
                            field.FieldId,
                            GameContentFieldValue.FromRecordReference(selected));
                        if (result.Succeeded) context.EditSessions.Preview(active);
                        context.RequestRepaint();
                    });
                dropdown.Show(selectorRect);
            }

            GameContentRecordDescriptor target = ResolveCurrentTarget(context, reference);
            using (new EditorGUI.DisabledScope(target == null))
            {
                if (GUILayout.Button("Open", GUILayout.Width(48f)))
                    OpenTarget(context, target);
            }
        }

        private static void OpenTarget(
            GameContentAuthoringSurfaceContext context,
            GameContentRecordDescriptor target)
        {
            if (context == null || target == null) return;
            GameContentLensDescriptor lens = context.Lenses
                .Where(value => value != null && value.Matches(target))
                .OrderBy(value => value.SortOrder)
                .FirstOrDefault();
            if (lens != null) context.OpenLens(lens.LensId, target);
            else context.SelectRecord(target);
        }

        private static void DrawReferenceStatus(
            GameContentAuthoringSurfaceContext context,
            GameContentFieldDescriptor field,
            GameContentFieldValue current)
        {
            GameContentRecordReferenceValue reference = current?.RecordReferenceValue;
            if (reference == null) return;
            if (reference.IsBroken)
            {
                EditorGUILayout.HelpBox(
                    "Broken reference '" + reference.OriginalReference + "': " + reference.BrokenReason,
                    MessageType.Error);
                return;
            }

            if (reference.IsNone)
            {
                if (field.Required)
                    EditorGUILayout.HelpBox("Select a valid target before committing.", MessageType.Error);
                return;
            }

            GameContentRecordDescriptor target = ResolveCurrentTarget(context, reference);
            if (target == null)
            {
                EditorGUILayout.HelpBox(
                    "The selected target is no longer present in this content pack.",
                    MessageType.Error);
                return;
            }

            GameContentRecordLensBrowser.DrawRow("Target ID", target.CanonicalKey.SourceRecordId);
            GameContentRecordLensBrowser.DrawRow("Target Pack", target.CanonicalKey.PackId);
            GameContentRecordLensBrowser.DrawRow(
                "Target Type",
                string.Join(", ", target.Capabilities.Select(value => value.Id).ToArray()));
            GameContentRecordLensBrowser.DrawRow(
                "Target Validation",
                target.Validation.ErrorCount > 0
                    ? "Invalid"
                    : target.Validation.WarningCount > 0 ? "Warning" : "Valid");
        }

        private static GameContentRecordDescriptor ResolveCurrentTarget(
            GameContentAuthoringSurfaceContext context,
            GameContentRecordReferenceValue reference)
        {
            return reference != null && reference.IsResolved && reference.TargetKey != null
                ? context.PackContext?.ResolveRecord(reference.TargetKey)
                : null;
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
            if (field.FieldType == GameContentFieldType.OrderedStructuredCollection &&
                field.StructuredCollection != null)
            {
                string maximum = field.StructuredCollection.MaximumCount.HasValue
                    ? field.StructuredCollection.MaximumCount.Value.ToString(CultureInfo.InvariantCulture)
                    : "any";
                constraints = "Rows: " + field.StructuredCollection.MinimumCount + " to " + maximum + ". " +
                              field.StructuredCollection.OrderingSemantics + " Duplicates: " +
                              field.StructuredCollection.DuplicatePolicy + ". Runtime impact: " +
                              field.StructuredCollection.RuntimeImpact + ".";
            }
            else if (field.FieldType.IsOrderedCollection() && field.Collection != null)
            {
                int minimum = Math.Max(field.Collection.MinimumCount, field.Required ? 1 : 0);
                string maximum = field.Collection.MaximumCount.HasValue
                    ? field.Collection.MaximumCount.Value.ToString(CultureInfo.InvariantCulture)
                    : "any";
                constraints = "Count: " + minimum + " to " + maximum + ". " +
                              (field.Collection.AllowDuplicates ? "Duplicates allowed. " : "Duplicates are not allowed. ") +
                              field.Collection.OrderingDescription + " Runtime impact: " +
                              field.Collection.RuntimeImpact + ".";
            }
            else if (field.MinimumNumber.HasValue || field.MaximumNumber.HasValue)
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

        private static void DrawChangeReview(
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

        private static void DrawStructuredCollectionReview(GameContentStructuredCollectionChangeReview review)
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
                EditorGUILayout.HelpBox(issue.Path + ": " + issue.Message, ToMessageType(issue.Severity));
            }
            GameContentRecordLensBrowser.DrawRow("Runtime Impact", review.RuntimeImpact.ToString());
            EditorGUILayout.HelpBox(
                "Adding or removing an embedded row changes only its parent source. It does not create or delete a canonical authored record, and stable identities remain read-only.",
                MessageType.Info);
        }

        private static string DescribeStructuredRows(IReadOnlyList<GameContentStructuredRowValue> rows)
        {
            return rows == null || rows.Count == 0
                ? "Empty"
                : string.Join(" -> ", rows.Select(DescribeStructuredRow).ToArray());
        }

        private static string DescribeStructuredRow(GameContentStructuredRowValue row)
        {
            if (row == null) return string.Empty;
            string summary = string.IsNullOrWhiteSpace(row.DisplaySummary) ? "Row" : row.DisplaySummary;
            return string.IsNullOrWhiteSpace(row.NativeKeyDisplayMetadata)
                ? summary
                : summary + " [" + row.NativeKeyDisplayMetadata + "]";
        }

        private static void DrawCollectionReview(GameContentCollectionChangeReview review)
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

        private static string DescribeCollection(GameContentOrderedCollectionValue collection)
        {
            if (collection == null || collection.Items.Count == 0) return "Empty";
            return string.Join(
                " -> ",
                collection.Items.Select(item =>
                    item.Value.FieldType == GameContentFieldType.RecordReference
                        ? DescribeReference(item.Value.RecordReferenceValue)
                        : item.Value.ToDisplayString()).ToArray());
        }

        private static void DrawReferenceReview(GameContentReferenceChangeReview review)
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

        private static void DrawReviewTarget(
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

        private sealed class GameContentReferenceDropdown : AdvancedDropdown
        {
            private readonly string _targetLabel;
            private readonly GameContentReferenceCandidateSet _targets;
            private readonly bool _allowNone;
            private readonly Action<GameContentRecordReferenceValue> _selected;

            public GameContentReferenceDropdown(
                string targetLabel,
                GameContentReferenceCandidateSet targets,
                bool allowNone,
                Action<GameContentRecordReferenceValue> selected)
                : base(new AdvancedDropdownState())
            {
                _targetLabel = string.IsNullOrWhiteSpace(targetLabel) ? "Record" : targetLabel.Trim();
                _targets = targets ?? new GameContentReferenceCandidateSet(string.Empty, null, null);
                _allowNone = allowNone;
                _selected = selected;
                minimumSize = new Vector2(520f, 280f);
            }

            protected override AdvancedDropdownItem BuildRoot()
            {
                var root = new AdvancedDropdownItem("Select " + _targetLabel);
                if (_allowNone)
                    root.AddChild(new GameContentReferenceDropdownItem("None", null, true));
                for (int i = 0; i < _targets.Candidates.Count; i++)
                {
                    GameContentReferenceCandidate candidate = _targets.Candidates[i];
                    root.AddChild(new GameContentReferenceDropdownItem(
                        BuildCandidateLabel(candidate),
                        candidate,
                        false));
                }
                if (_targets.Candidates.Count == 0)
                {
                    string message = string.IsNullOrWhiteSpace(_targets.Message)
                        ? "No compatible targets"
                        : _targets.Message;
                    root.AddChild(new GameContentReferenceDropdownItem(message, null, false));
                }
                return root;
            }

            protected override void ItemSelected(AdvancedDropdownItem item)
            {
                if (!(item is GameContentReferenceDropdownItem referenceItem)) return;
                if (referenceItem.IsNone)
                {
                    _selected?.Invoke(GameContentRecordReferenceValue.None());
                    return;
                }
                if (referenceItem.Candidate?.Record?.CanonicalKey == null) return;
                GameContentRecordDescriptor record = referenceItem.Candidate.Record;
                _selected?.Invoke(GameContentRecordReferenceValue.Resolved(
                    record.CanonicalKey,
                    record.DisplayName,
                    record.SourcePath));
            }

            private static string BuildCandidateLabel(GameContentReferenceCandidate candidate)
            {
                GameContentRecordDescriptor record = candidate.Record;
                string capabilities = string.Join(",", record.Capabilities.Select(value => value.Id).ToArray());
                string validation = candidate.Evaluation.ValidationState.ToString();
                string source = string.IsNullOrWhiteSpace(record.SourcePath) ? string.Empty : " | " + record.SourcePath;
                return record.DisplayName + " | " + record.CanonicalKey.SourceRecordId + " | " +
                       record.CanonicalKey.PackId + " | " + capabilities + " | " + validation + source;
            }
        }

        private sealed class GameContentReferenceDropdownItem : AdvancedDropdownItem
        {
            public GameContentReferenceDropdownItem(
                string name,
                GameContentReferenceCandidate candidate,
                bool isNone)
                : base(name)
            {
                Candidate = candidate;
                IsNone = isNone;
            }

            public GameContentReferenceCandidate Candidate { get; }
            public bool IsNone { get; }
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
