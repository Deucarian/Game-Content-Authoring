using System;
using System.Linq;
using Deucarian.Editor;
using UnityEngine.UIElements;
using Ui = Deucarian.Editor.DeucarianEditorWorkspaceControls;

namespace Deucarian.GameContentAuthoring.Editor
{
    internal static class GameContentToolkitStructuredRows
    {
        internal static void Build(VisualElement root, GameContentAuthoringSurfaceContext context,
            GameContentActiveEditSession active, GameContentFieldDescriptor field, bool enabled)
        {
            var collection = active.GetEffectiveValue(field.FieldId)?.OrderedStructuredCollectionValue;
            var descriptor = field.StructuredCollection;
            var section = new DeucarianEditorWorkspaceForm(root).Section(field.DisplayName, true);
            if (collection == null || descriptor == null) { section.Note(() => "Rows unavailable."); return; }
            section.Root.tooltip = GameContentEditFieldRenderer.BuildFieldDetail(field);
            for (int i = 0; i < collection.Count; i++)
            {
                int index = i; var row = collection.Rows[i];
                var detail = section.Section(string.IsNullOrWhiteSpace(row.DisplaySummary) ? "Row " + (i + 1) : row.DisplaySummary, true);
                if (!string.IsNullOrWhiteSpace(row.NativeKeyDisplayMetadata))
                    detail.ReadOnly(null, descriptor.RowDescriptor.NativeKey?.DisplayName ?? "ID", () => row.NativeKeyDisplayMetadata);
                foreach (var member in descriptor.RowDescriptor.Fields)
                {
                    row.TryGetFieldValue(member.FieldId, out var value);
                    var input = GameContentToolkitFields.Value(member, value ?? GameContentToolkitFields.DefaultValue(member),
                        replacement => Apply(context, active, field.FieldId, GameContentStructuredCollectionOperation.ReplaceRowField(row.RowKey, member.FieldId, replacement)),
                        () => context.EditSessions.GetStructuredReferenceCandidates(active, field.FieldId, row.RowKey, member.FieldId));
                    input.SetEnabled(enabled && !member.IsReadOnly && descriptor.Allows(GameContentStructuredCollectionPermittedOperations.ReplaceRowField));
                    detail.Root.Add(Ui.Field(member.DisplayName, input));
                }
                var up = Operation("Up", context, active, field, enabled && i > 0,
                    GameContentStructuredCollectionOperation.MoveRow(row.RowKey, Math.Max(0, index - 1)));
                var down = Operation("Down", context, active, field, enabled && i < collection.Count - 1,
                    GameContentStructuredCollectionOperation.MoveRow(row.RowKey, Math.Min(collection.Count - 1, index + 1)));
                var remove = Operation("Remove row", context, active, field, enabled,
                    GameContentStructuredCollectionOperation.RemoveRow(row.RowKey));
                detail.Root.Add(Ui.EndActions(up, down, remove));
            }
            if (descriptor.Allows(GameContentStructuredCollectionPermittedOperations.AddRow)) AddDraft(section, context, active, field, enabled);
            section.Root.Add(Operation("Restore original order", context, active, field,
                enabled && GameContentStructuredCollectionMutation.NeedsRestoreOriginalOrder(collection),
                GameContentStructuredCollectionOperation.RestoreOriginalOrder()));
        }

        private static void AddDraft(DeucarianEditorWorkspaceForm section, GameContentAuthoringSurfaceContext context,
            GameContentActiveEditSession active, GameContentFieldDescriptor field, bool enabled)
        {
            var descriptor = field.StructuredCollection.RowDescriptor;
            var drafts = context.EditWorkbenchState.ForSession(active).StructuredAddDrafts;
            string key = field.FieldId + "|add";
            if (!drafts.TryGetValue(key, out var initial)) initial = GameContentEditStructuredFieldsRenderer.CreateDefaultStructuredDraft(descriptor);
            var values = initial.ToDictionary(value => value.FieldId, value => value.Value, StringComparer.Ordinal);
            Func<GameContentStructuredRowFieldValue[]> snapshot = () => descriptor.Fields.Where(member => !member.IsReadOnly && values.ContainsKey(member.FieldId))
                .Select(member => new GameContentStructuredRowFieldValue(member.FieldId, values[member.FieldId])).ToArray();
            var newRow = section.Section("New " + descriptor.DisplayName, true);
            var add = Ui.Button("Add row", () =>
            {
                var result = context.EditSessions.ApplyStructuredOperation(active, field.FieldId, GameContentStructuredCollectionOperation.AddRow(snapshot()));
                if (result.Succeeded) drafts.Remove(key); Report(context, result); context.RequestRepaint();
            });
            Action refresh = () =>
            {
                var validation = context.EditSessions.ValidateStructuredOperation(active, field.FieldId, GameContentStructuredCollectionOperation.AddRow(snapshot()));
                add.SetEnabled(enabled && validation.Succeeded); add.tooltip = validation.Message;
            };
            foreach (var member in descriptor.Fields.Where(value => !value.IsReadOnly))
            {
                if (!values.TryGetValue(member.FieldId, out var value)) values[member.FieldId] = value = GameContentToolkitFields.DefaultValue(member);
                var input = GameContentToolkitFields.Value(member, value, replacement =>
                {
                    values[member.FieldId] = replacement; drafts[key] = snapshot(); refresh();
                }, () => context.EditSessions.GetStructuredReferenceCandidates(active, field.FieldId, null, member.FieldId));
                input.SetEnabled(enabled); newRow.Root.Add(Ui.Field(member.DisplayName, input));
            }
            newRow.Root.Add(Ui.EndActions(add)); refresh();
        }

        private static Button Operation(string label, GameContentAuthoringSurfaceContext context, GameContentActiveEditSession active,
            GameContentFieldDescriptor field, bool enabled, GameContentStructuredCollectionOperation operation)
        {
            var button = Ui.Button(label, () => Apply(context, active, field.FieldId, operation));
            var validation = context.EditSessions.ValidateStructuredOperation(active, field.FieldId, operation);
            button.SetEnabled(enabled && validation.Succeeded); button.tooltip = validation.Message; return button;
        }
        private static void Apply(GameContentAuthoringSurfaceContext context, GameContentActiveEditSession active,
            string fieldId, GameContentStructuredCollectionOperation operation)
        {
            Report(context, context.EditSessions.ApplyStructuredOperation(active, fieldId, operation)); context.RequestRepaint();
        }
        private static void Report(GameContentAuthoringSurfaceContext context, GameContentStructuredCollectionOperationResult result) =>
            GameContentToolkitCollections.Report(context, new GameContentEditOperationResult(result.Succeeded, result.Message));
    }
}
