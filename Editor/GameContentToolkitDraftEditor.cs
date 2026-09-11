using System;
using Deucarian.Editor;
using UnityEditor;
using UnityEngine.UIElements;
using Ui = Deucarian.Editor.DeucarianEditorWorkspaceControls;

namespace Deucarian.GameContentAuthoring.Editor
{
    public static class GameContentToolkitDraftEditor
    {
        private sealed class Draft<T> { internal T Value; internal string SourceRevision; }

        public static VisualElement Create<T>(GameContentAuthoringSurfaceContext context, Func<T> load,
            Func<T, string> fingerprint, Func<T, GameContentAuthoringValidationResult> validate,
            Func<T, GameContentCreationResult> save, Action<VisualElement, T> fields) where T : class
        {
            if (context == null || load == null || fingerprint == null || validate == null || save == null || fields == null)
                throw new ArgumentNullException("Draft editors require a context, state factory and domain operations.");
            bool creating = context.SelectedItem == null;
            string key = context.Provider.ProviderId + "|" + (context.SelectedItem?.Key ?? "new");
            var draft = context.EditWorkbenchState.GetViewDraft(key, () =>
            {
                var state = load(); return new Draft<T> { Value = state, SourceRevision = fingerprint(state) };
            });
            var root = Ui.Region("content-draft-editor", "dw-content-record");
            root.Add(Ui.Label(creating ? "New " + context.Provider.DisplayName : context.SelectedItem.DisplayName, "dw-section-title"));
            bool writable = creating ? context.CanCreate : context.PackContext.Access.CanEditExisting;
            var editor = new VisualElement(); root.Add(editor);
            fields(editor, draft.Value); editor.SetEnabled(writable);
            var status = new VisualElement(); root.Add(status);
            var saveButton = Ui.Button(creating ? "Create content" : "Save changes", () =>
            {
                if (!writable) return;
                status.Clear();
                if (!creating && !string.Equals(draft.SourceRevision, fingerprint(load()), StringComparison.Ordinal))
                {
                    status.Add(new DeucarianEditorMessageRow("Source changed", "Reload the source before saving. Your draft has not been written.", DeucarianEditorStatus.Warning, string.Empty));
                    return;
                }
                var report = validate(draft.Value); GameContentToolkitRecordEditor.AddValidation(status, report);
                if (!report.IsValid) return;
                if (report.WarningCount > 0 && !EditorUtility.DisplayDialog("Save with warnings?",
                    "Validation reported warnings. Save this content anyway?", "Save", "Cancel")) return;
                var result = save(draft.Value);
                if (result?.Succeeded == true)
                {
                    context.EditWorkbenchState.RemoveViewDraft(key);
                    context.RefreshLibrary();
                }
                context.Authoring.SetCreationResult(result);
                if (result != null) status.Add(Ui.Label(result.Message, "dw-note"));
            }, true);
            saveButton.name = "content-save-draft";
            saveButton.SetEnabled(writable);
            root.Add(Ui.EndActions(saveButton, Ui.Button("Validate", () =>
            {
                status.Clear(); var report = validate(draft.Value); GameContentToolkitRecordEditor.AddValidation(status, report);
                if (report.Issues.Count == 0) status.Add(Ui.Label("No issues found.", "dw-note"));
            })));
            if (!creating)
            {
                var more = new DeucarianEditorWorkspaceForm(root).Section("Source and recovery", true);
                more.ReadOnly(null, "Source", () => context.SelectedItem.Path);
                more.Action("content-reload-draft", "Reload source", () =>
                {
                    if (!EditorUtility.DisplayDialog("Reload source?", "Discard this draft and reload the current source?", "Reload", "Keep editing")) return;
                    context.EditWorkbenchState.RemoveViewDraft(key); context.RequestRepaint();
                });
                more.Action("content-select-source", "Select source", () =>
                {
                    Selection.activeObject = context.SelectedItem.Asset; EditorGUIUtility.PingObject(context.SelectedItem.Asset);
                });
            }
            return root;
        }
    }
}
