using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Deucarian.Editor;
using UnityEditor;
using UnityEngine;

namespace Deucarian.GameContentAuthoring.Editor
{
    internal static class GameContentLibraryListView
    {
        internal static readonly string[] SeverityFilterLabels =
        {
            "All Issues",
            "Blockers",
            "Warnings",
            "Info",
            "Ready"
        };

        internal static readonly string[] ReadinessFilterLabels =
        {
            "All States",
            "Ready",
            "Warnings",
            "Blockers"
        };

        internal static void DrawLibraryList(
            GameContentAuthoringSurfaceContext context,
            GameContentLibraryReport report,
            GameContentLibraryV2State state,
            Action refresh)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("Content Library", DeucarianEditorStyles.SectionTitle);
                GUILayout.FlexibleSpace();
                if (DeucarianEditorMiniToolbar.Button("Refresh", true, GUILayout.Width(62f), GUILayout.Height(22f)))
                    refresh?.Invoke();
            }

            state.SearchText = DeucarianEditorSearchField.Draw(state.SearchText, "Search library", GUILayout.ExpandWidth(true));

            using (new EditorGUILayout.HorizontalScope())
            {
                if (DeucarianEditorButtons.Primary("Validate All", true, GUILayout.Width(96f), GUILayout.Height(24f)))
                    refresh?.Invoke();
                if (DeucarianEditorButtons.Secondary("Copy Summary", report != null, GUILayout.Width(104f), GUILayout.Height(24f)))
                    EditorGUIUtility.systemCopyBuffer = GameContentLibraryReportWriter.ToMarkdown(report);
            }

            DrawFilterRow(state);
            DrawDashboard(GameContentLibraryV2Model.BuildDashboard(report));

            IReadOnlyList<GameContentLibraryV2GroupModel> groups = GameContentLibraryV2Model.BuildGroups(
                report,
                state.SearchText,
                state.TypeFilterIndex,
                state.SeverityFilter,
                state.ReadinessFilter);

            state.ListScroll = EditorGUILayout.BeginScrollView(state.ListScroll);
            int shown = 0;
            for (int i = 0; i < groups.Count; i++)
            {
                GameContentLibraryV2GroupModel group = groups[i];
                if (GameContentLibraryV2Model.GetKindForTypeFilter(state.TypeFilterIndex).HasValue && group.Items.Count == 0 && group.TotalCount == 0)
                    continue;

                DrawGroupHeader(state, group);
                if (!state.IsGroupExpanded(group.Kind))
                    continue;

                for (int j = 0; j < group.Items.Count; j++)
                {
                    shown++;
                    DrawItemCard(context, state, group.Items[j]);
                }
            }

            if (shown == 0)
                DrawEmptyListState(report);
            EditorGUILayout.EndScrollView();
        }

        internal static void DrawFilterRow(GameContentLibraryV2State state)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                IReadOnlyList<string> typeLabels = GameContentLibraryV2Model.GetTypeFilterLabels();
                state.TypeFilterIndex = EditorGUILayout.Popup(
                    Mathf.Clamp(state.TypeFilterIndex, 0, typeLabels.Count - 1),
                    typeLabels.ToArray(),
                    GUILayout.MinWidth(112f));
                state.SeverityFilter = (GameContentLibraryV2SeverityFilter)EditorGUILayout.Popup(
                    (int)state.SeverityFilter,
                    SeverityFilterLabels,
                    GUILayout.MinWidth(96f));
                state.ReadinessFilter = (GameContentLibraryV2ReadinessFilter)EditorGUILayout.Popup(
                    (int)state.ReadinessFilter,
                    ReadinessFilterLabels,
                    GUILayout.MinWidth(96f));
            }
        }

        internal static void DrawDashboard(GameContentLibraryV2Dashboard dashboard)
        {
            if (dashboard == null)
                return;

            DeucarianEditorStatusChipRow.Draw(new[]
            {
                new DeucarianEditorStatusChip(dashboard.TotalAssets.ToString(CultureInfo.InvariantCulture) + " assets", DeucarianEditorStatus.Info),
                new DeucarianEditorStatusChip(dashboard.ReadyContentPacks.ToString(CultureInfo.InvariantCulture) + " ready packs", dashboard.ReadyContentPacks > 0 ? DeucarianEditorStatus.Success : DeucarianEditorStatus.Warning),
                new DeucarianEditorStatusChip(dashboard.ReadyContentSets.ToString(CultureInfo.InvariantCulture) + " ready sets", dashboard.ReadyContentSets > 0 ? DeucarianEditorStatus.Success : DeucarianEditorStatus.Warning),
                new DeucarianEditorStatusChip(dashboard.Blockers.ToString(CultureInfo.InvariantCulture) + " blockers", dashboard.Blockers == 0 ? DeucarianEditorStatus.Success : DeucarianEditorStatus.Error),
                new DeucarianEditorStatusChip(dashboard.Warnings.ToString(CultureInfo.InvariantCulture) + " warnings", dashboard.Warnings == 0 ? DeucarianEditorStatus.Success : DeucarianEditorStatus.Warning)
            });
        }

        internal static void DrawGroupHeader(GameContentLibraryV2State state, GameContentLibraryV2GroupModel group)
        {
            if (group == null)
                return;

            bool expanded = state.IsGroupExpanded(group.Kind);
            var chips = new[]
            {
                new DeucarianEditorStatusChip(group.Items.Count.ToString(CultureInfo.InvariantCulture) + "/" + group.TotalCount.ToString(CultureInfo.InvariantCulture), DeucarianEditorStatus.Info),
                new DeucarianEditorStatusChip(group.ReadyCount.ToString(CultureInfo.InvariantCulture) + " ready", group.ReadyCount > 0 ? DeucarianEditorStatus.Success : DeucarianEditorStatus.Disabled),
                new DeucarianEditorStatusChip(group.BlockerCount.ToString(CultureInfo.InvariantCulture) + " blockers", group.BlockerCount == 0 ? DeucarianEditorStatus.Success : DeucarianEditorStatus.Error),
                new DeucarianEditorStatusChip(group.WarningCount.ToString(CultureInfo.InvariantCulture) + " warnings", group.WarningCount == 0 ? DeucarianEditorStatus.Success : DeucarianEditorStatus.Warning)
            };

            bool clicked = DeucarianEditorCompactObjectCard.Draw(
                group.Name,
                expanded ? "Expanded" : "Collapsed",
                false,
                chips,
                () => DeucarianEditorMiniToolbar.Button(expanded ? "Hide" : "Show", true, GUILayout.Width(48f), GUILayout.Height(22f)),
                null,
                GUILayout.ExpandWidth(true));

            if (clicked)
            {
                state.ToggleGroup(group.Kind);
                if (Event.current != null)
                    Event.current.Use();
            }
        }

        internal static void DrawItemCard(GameContentAuthoringSurfaceContext context, GameContentLibraryV2State state, GameContentLibraryV2ItemModel model)
        {
            if (model == null || model.Source == null)
                return;

            GameContentLibraryItem item = model.Source;
            bool selected = string.Equals(state.SelectedKey, item.Key, StringComparison.Ordinal);
            var chips = new[]
            {
                new DeucarianEditorStatusChip(model.TypeLabel, DeucarianEditorStatus.Info),
                new DeucarianEditorStatusChip(model.ReadinessLabel, model.ReadinessStatus),
                new DeucarianEditorStatusChip(model.DirectDependencyCount.ToString(CultureInfo.InvariantCulture) + " deps", model.DirectDependencyCount > 0 ? DeucarianEditorStatus.Success : DeucarianEditorStatus.Disabled),
                new DeucarianEditorStatusChip(model.ReverseReferenceCount.ToString(CultureInfo.InvariantCulture) + " used by", model.ReverseReferenceCount > 0 ? DeucarianEditorStatus.Success : DeucarianEditorStatus.Disabled)
            };

            bool clicked = DeucarianEditorCompactObjectCard.Draw(
                model.DisplayName,
                model.StableId,
                selected,
                chips,
                () =>
                {
                    DeucarianEditorMiniToolbar.PingButton(item.Asset);
                    if (DeucarianEditorMiniToolbar.Button("Open", item.Asset != null, GUILayout.Width(48f), GUILayout.Height(22f)))
                        GameContentLibraryViewControls.OpenAsset(item.Asset);
                },
                null,
                GUILayout.ExpandWidth(true));

            if (clicked)
            {
                state.Select(item);
                context.RequestRepaint();
                if (Event.current != null)
                    Event.current.Use();
            }
        }

        internal static void DrawEmptyListState(GameContentLibraryReport report)
        {
            DeucarianEditorCards.DrawInlineCard(() =>
            {
                DeucarianEditorStatusBadge.Draw(report != null && report.Items.Count == 0 ? "Empty" : "Filtered", DeucarianEditorStatus.Info, GUILayout.Width(74f));
                EditorGUILayout.LabelField(report != null && report.Items.Count == 0 ? "No authored content found." : "No assets match the current filters.", DeucarianEditorStyles.MutedLabel);
            });
        }
    }
}
