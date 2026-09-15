using System;
using Deucarian.Editor;
using UnityEngine.UIElements;
using Ui = Deucarian.Editor.DeucarianEditorWorkspaceControls;

namespace Deucarian.GameContentAuthoring.Editor
{
    internal static class GameContentToolkitCollections
    {
        internal static void Build(VisualElement root, GameContentAuthoringSurfaceContext context,
            GameContentActiveEditSession active, GameContentFieldDescriptor field, bool enabled)
        {
            var collection = active.GetEffectiveValue(field.FieldId)?.OrderedCollectionValue;
            var descriptor = field.Collection;
            var section = new DeucarianEditorWorkspaceForm(root).Section(field.DisplayName, true);
            if (collection == null || descriptor == null) { section.Note(() => "Collection unavailable."); return; }
            section.Root.tooltip = GameContentEditFieldRenderer.BuildFieldDetail(field);
            for (int i = 0; i < collection.Count; i++)
            {
                int index = i; var item = collection.Items[i];
                var input = GameContentToolkitFields.Value(descriptor.ItemDescriptor, item.Value,
                    value => Apply(context, active, field.FieldId, GameContentCollectionOperation.Replace(item.ItemKey, value)),
                    () => context.EditSessions.GetReferenceCandidates(active, field.FieldId, item.ItemKey));
                input.SetEnabled(enabled && !descriptor.ItemDescriptor.IsReadOnly);
                section.Root.Add(Ui.Field("Item " + (i + 1), input));
                var up = Ui.Button("Up", () => Apply(context, active, field.FieldId, GameContentCollectionOperation.Move(item.ItemKey, index - 1)));
                var down = Ui.Button("Down", () => Apply(context, active, field.FieldId, GameContentCollectionOperation.Move(item.ItemKey, index + 1)));
                var remove = Ui.Button("Remove", () => Apply(context, active, field.FieldId, GameContentCollectionOperation.Remove(item.ItemKey)));
                up.SetEnabled(enabled && i > 0); down.SetEnabled(enabled && i < collection.Count - 1);
                remove.SetEnabled(enabled && context.EditSessions.ValidateCollectionOperation(active, field.FieldId, GameContentCollectionOperation.Remove(item.ItemKey)).Succeeded);
                section.Root.Add(Ui.EndActions(up, down, remove));
            }
            var drafts = context.EditWorkbenchState.ForSession(active).CollectionAddDrafts;
            if (!drafts.TryGetValue(field.FieldId, out var draft)) draft = GameContentToolkitFields.DefaultValue(descriptor.ItemDescriptor);
            var add = Ui.Button("Add item", () =>
            {
                var result = context.EditSessions.ApplyCollectionOperation(active, field.FieldId, GameContentCollectionOperation.Add(draft));
                if (result.Succeeded) drafts.Remove(field.FieldId);
                Report(context, result); context.RequestRepaint();
            });
            Action refreshAdd = () =>
            {
                var validation = context.EditSessions.ValidateCollectionOperation(active, field.FieldId, GameContentCollectionOperation.Add(draft));
                add.SetEnabled(enabled && validation.Succeeded); add.tooltip = validation.Message;
            };
            var newValue = GameContentToolkitFields.Value(descriptor.ItemDescriptor, draft,
                value => { draft = value; drafts[field.FieldId] = value; refreshAdd(); },
                () => context.EditSessions.GetReferenceCandidates(active, field.FieldId));
            newValue.SetEnabled(enabled && !descriptor.ItemDescriptor.IsReadOnly);
            section.Root.Add(Ui.Field("New item", newValue)); section.Root.Add(Ui.EndActions(add)); refreshAdd();
            section.Action("content-restore-order-" + field.FieldId, "Restore original order", () =>
            {
                Report(context, context.EditSessions.RestoreOriginalCollectionOrder(active, field.FieldId)); context.RequestRepaint();
            }, () => enabled && GameContentCollectionMutation.BuildRestoreOriginalOrderOperations(collection).Count > 0);
        }

        private static void Apply(GameContentAuthoringSurfaceContext context, GameContentActiveEditSession active,
            string fieldId, GameContentCollectionOperation operation)
        {
            var result = context.EditSessions.ApplyCollectionOperation(active, fieldId, operation);
            Report(context, result); context.RequestRepaint();
        }
        internal static void Report(GameContentAuthoringSurfaceContext context, GameContentEditOperationResult result)
        {
            if (!result.Succeeded) context.Authoring.SetValidation(new GameContentAuthoringValidationResult(new[]
                { GameContentAuthoringValidationIssue.Error("Edit", result.Message) }));
        }
    }
}
