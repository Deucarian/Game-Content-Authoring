using System.Collections.Generic;
using Deucarian.Editor;
using UnityEditor;
using UnityEngine.UIElements;

namespace Deucarian.GameContentAuthoring.Editor
{
    internal static class GameContentToolkitLibraryDetails
    {
        internal static void AddDashboard(VisualElement root, GameContentAuthoringSurfaceContext context)
        {
            var form = new DeucarianEditorWorkspaceForm(root);
            form.Section(context.PackContext.DisplayName);
            var entries = context.PackContext.IsAllPacks ? context.PackContext.Catalog.Entries :
                new[] { context.PackContext.SelectedEntry };
            foreach (var entry in entries)
            {
                var pack = entry.Pack;
                var details = context.PackContext.IsAllPacks ? form.Section(pack.DisplayName, true) : form;
                details.ReadOnly(null, "Owner", () => pack.OwningPackageId);
                details.ReadOnly(null, "Pack ID", () => pack.PackId);
                details.ReadOnly(null, "Schema", () => pack.SchemaVersion);
                details.ReadOnly(null, "Source", () => pack.SourcePath);
                details.ReadOnly(null, "Access", () => entry.EffectiveAccess.PersistenceLabel);
                details.ReadOnly(null, "State", () => entry.IsConflict ? "Conflict" : pack.SourceState.ToString());
                foreach (var metadata in pack.Metadata) details.ReadOnly(null, metadata.Label, () => metadata.Value);
                foreach (var category in pack.Categories) details.ReadOnly(null, category.DisplayName, () => category.RecordCount.ToString(System.Globalization.CultureInfo.InvariantCulture));
                GameContentToolkitRecordEditor.AddValidation(details.Root, pack.Validation);
            }
            form.Action("content-dashboard-browse", "Browse content", () => context.OpenLens("all-content"));
        }

        internal static void Add(VisualElement root, GameContentAuthoringSurfaceContext context, GameContentLibraryReport report)
        {
            var item = context.SelectedItem;
            var more = new DeucarianEditorWorkspaceForm(root).Section("References and readiness", true);
            more.ReadOnly(null, "Readiness", () => GameContentLibraryV2Model.GetReadinessLabel(item));
            more.ReadOnly(null, "Summary", () => GameContentLibraryV2Model.BuildSelectedSummary(item));
            var set = report.GetContentSetSummary(item);
            if (set != null) more.Note(() => set.Message);
            var pack = report.GetContentPackSummary(item);
            if (pack != null) more.Note(() => pack.Message);
            References(more, "Dependencies", item.DirectReferences, context);
            References(more, "Used by", item.ReverseReferences, context);
            foreach (var issue in item.Issues) more.Note(() => issue.Path + ": " + issue.Message);
            more.Action("content-copy-report", "Copy report", () => EditorGUIUtility.systemCopyBuffer = GameContentLibraryDetailView.BuildSelectedMarkdown(report, item));
            more.Action("content-open-source", "Open source asset", () => GameContentLibraryViewControls.OpenAsset(item.Asset), () => item.Asset != null);
        }

        private static void References(DeucarianEditorWorkspaceForm parent, string title,
            IReadOnlyList<GameContentLibraryReference> references, GameContentAuthoringSurfaceContext context)
        {
            var section = parent.Section(title, true);
            if (references.Count == 0) section.Note(() => "None.");
            foreach (var reference in references)
            {
                var target = reference.Target;
                section.Action(null, target.DisplayName, () => context.SelectItem(target)).tooltip = reference.PropertyPath;
            }
        }
    }
}
