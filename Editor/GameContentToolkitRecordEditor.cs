using System;
using System.Linq;
using Deucarian.Editor;
using UnityEditor;
using UnityEngine.UIElements;
using Ui = Deucarian.Editor.DeucarianEditorWorkspaceControls;

namespace Deucarian.GameContentAuthoring.Editor
{
    internal static class GameContentToolkitRecordEditor
    {
        internal static VisualElement Create(GameContentAuthoringSurfaceContext context, GameContentRecordDescriptor record, string lens,
            VisualElement domainDetails = null)
        {
            var root = Ui.Region("content-record-editor", "dw-content-record");
            root.Add(Ui.Label(record.DisplayName, "dw-section-title"));
            if (!string.IsNullOrEmpty(record.Description)) root.Add(Ui.Label(record.Description, "dw-note"));
            var sessions = context.EditSessions;
            if (!context.PackContext.IsAllPacks && sessions.TryGetSession(record.CanonicalKey, out var active))
            {
                var fields = new VisualElement(); root.Add(fields);
                GameContentToolkitFields.Build(fields, context, active);
                if (domainDetails != null)
                {
                    var original = new DeucarianEditorWorkspaceForm(root).Section("Saved source preview", true);
                    original.Root.Add(domainDetails);
                }
                var review = new DeucarianEditorWorkspaceForm(root).Section("Review changes", true);
                foreach (var change in active.Changes)
                    review.ReadOnly(null, change.DisplayName, () => change.OldValue?.ToDisplayString() + " → " + change.ProposedValue?.ToDisplayString());
                foreach (var issue in active.Validation.Issues) AddIssue(root, issue);
                if (!string.IsNullOrWhiteSpace(active.Message)) root.Add(Ui.Label(active.Message, "dw-note"));
                if (active.Recovery != null) root.Add(new DeucarianEditorMessageRow("Recovery required", active.Recovery.ActionableMessage,
                    DeucarianEditorStatus.Error, active.Recovery.Phase));
                var advanced = new DeucarianEditorWorkspaceForm(root).Section("Advanced data", true);
                advanced.ReadOnly(null, "Source", () => active.SourceTarget.SourceLabel);
                advanced.ReadOnly(null, "Revision", () => active.OriginalRevision.Token);
                advanced.ReadOnly(null, "State", () => active.State.ToString());
                Action<Action> run = action => { action(); context.RequestRepaint(); };
                advanced.Action("content-undo", "Undo", () => run(() => sessions.Undo(active)), () => active.CanUndo);
                advanced.Action("content-redo", "Redo", () => run(() => sessions.Redo(active)), () => active.CanRedo);
                advanced.Action("content-check-source", "Check source", () => run(() => sessions.CheckStale(active)));
                var save = Ui.Button(active.IsTerminal ? "Done" : "Save changes", () =>
                {
                    if (active.IsTerminal) { sessions.Dismiss(active); context.RequestRepaint(); return; }
                    var validation = sessions.Preview(active);
                    if (!validation.CanCommit) { context.RequestRepaint(); return; }
                    if (validation.RequiresWarningConfirmation && !EditorUtility.DisplayDialog("Save with warnings?",
                        "Validation reported warnings. Review them before saving this source.", "Save", "Cancel")) return;
                    sessions.Commit(active, validation.RequiresWarningConfirmation); context.RequestRepaint();
                }, true);
                save.name = "content-save";
                save.SetEnabled(active.IsTerminal || active.State == GameContentEditSessionState.Dirty && active.Validation.CanCommit);
                root.Add(Ui.EndActions(save, Ui.Button("Validate", () => run(() => sessions.Preview(active)))));
                if (active.State == GameContentEditSessionState.Committed)
                    advanced.Action("content-rollback", "Rollback saved changes", () =>
                    {
                        if (EditorUtility.DisplayDialog("Rollback saved changes?", "Restore the source revision from before this edit session?", "Rollback", "Cancel"))
                            run(() => sessions.Rollback(active));
                    });
                else if (!active.IsTerminal)
                    advanced.Action("content-cancel", "Discard draft", () =>
                    {
                        if (active.Changes.Count == 0 || EditorUtility.DisplayDialog("Discard draft?", "Discard the staged edits to this record?", "Discard", "Keep editing"))
                            run(() => sessions.Cancel(active));
                    });
                return root;
            }
            var form = new DeucarianEditorWorkspaceForm(root);
            if (domainDetails != null) root.Add(domainDetails);
            else if (record.Preview != null) GameContentToolkitPreview.Add(root, () => record.Preview, null);
            var metadata = domainDetails == null ? form : form.Section("Source metadata", true);
            foreach (var value in record.PlayerFacingMetadata) metadata.ReadOnly(null, value.Label, () => value.Value);
            var availability = sessions.GetAvailability(context.PackContext, record, lens);
            if (!availability.IsEditable) form.Note(() => availability.DisabledReason);
            var data = form.Section("Advanced data", true);
            data.ReadOnly(null, "Record ID", () => record.SourceRecordId);
            data.ReadOnly(null, "Source", () => record.SourcePath);
            data.Action("content-reveal", "Select source", () =>
            {
                Selection.activeObject = record.SourceAsset; EditorGUIUtility.PingObject(record.SourceAsset);
            }, () => record.SourceAsset != null);
            foreach (var reference in record.OutboundReferences)
            {
                var target = context.ResolveReference(record, reference);
                data.Action(null, reference.RelationshipLabel + " · " + reference.TargetRecordId,
                    () => { if (target != null) context.SelectRecord(target); }, () => target != null);
            }
            AddValidation(root, record.Validation);
            var edit = Ui.Button("Edit record", () =>
            {
                var result = sessions.BeginEdit(context.PackContext, record, lens);
                if (!result.Succeeded) context.Authoring.SetValidation(new GameContentAuthoringValidationResult(new[]
                    { GameContentAuthoringValidationIssue.Error("Edit", result.Message) }));
                context.RequestRepaint();
            }, true);
            edit.name = "content-edit"; edit.SetEnabled(availability.IsEditable); root.Add(Ui.EndActions(edit));
            return root;
        }

        internal static void AddValidation(VisualElement root, GameContentAuthoringValidationResult validation)
        {
            if (validation == null) return;
            foreach (var issue in validation.Issues) AddIssue(root, issue);
        }
        private static void AddIssue(VisualElement root, GameContentAuthoringValidationIssue issue) =>
            root.Add(new DeucarianEditorMessageRow(issue.Path, issue.Message,
                issue.Severity == GameContentAuthoringValidationSeverity.Error ? DeucarianEditorStatus.Error
                : issue.Severity == GameContentAuthoringValidationSeverity.Warning ? DeucarianEditorStatus.Warning : DeucarianEditorStatus.Info,
                string.Empty));
    }
}
