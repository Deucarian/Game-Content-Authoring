using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Deucarian.Editor;
using UnityEditor;
using UnityEngine;

namespace Deucarian.GameContentAuthoring.Editor
{
    internal static class GameContentLibraryGraphView
    {
        internal static readonly string[] GraphModeLabels =
        {
            "Game",
            "Debug"
        };

        internal static void DrawGraphPreview(
            GameContentAuthoringSurfaceContext context,
            GameContentLibraryReport report,
            GameContentLibraryV2State state,
            string rootPath,
            Action<string> setRootPath,
            Action refresh)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                DeucarianEditorTextGUI.LabelField("Graph / Readiness", DeucarianEditorStyles.SectionTitle);
                GUILayout.FlexibleSpace();
                int mode = DeucarianEditorSegmentedControl.Draw(state.DebugGraph ? 1 : 0, GraphModeLabels, GUILayout.Width(132f));
                state.DebugGraph = mode == 1;
            }

            GameContentLibraryListView.DrawDashboard(GameContentLibraryV2Model.BuildDashboard(report));
            GameContentLibraryItem selected = state.GetSelected(report);

            state.GraphScroll = EditorGUILayout.BeginScrollView(state.GraphScroll);
            if (selected == null)
            {
                DrawGraphEmptyState(report, rootPath, setRootPath, refresh);
                EditorGUILayout.EndScrollView();
                return;
            }

            if (state.DebugGraph)
                DrawDebugGraph(selected);
            else
                DrawGameGraph(context, state, selected);
            EditorGUILayout.EndScrollView();
        }

        internal static void DrawGraphEmptyState(
            GameContentLibraryReport report,
            string rootPath,
            Action<string> setRootPath,
            Action refresh)
        {
            DeucarianEditorCards.DrawInlineCard(() =>
            {
                DeucarianEditorStatusBadge.Draw(report != null && report.Items.Count == 0 ? "Empty" : "Select", DeucarianEditorStatus.Info, GUILayout.Width(72f));
                DeucarianEditorTextGUI.LabelField(report != null && report.Items.Count == 0 ? "No authored content found." : "Select an asset to see relationships.", DeucarianEditorStyles.MutedLabel);
            });

            DeucarianEditorDiagnosticsDrawer.Draw(
                DeucarianEditorAccordion.BuildStateKey("content-library-v2", "empty-root", "advanced"),
                "Scan Root",
                () =>
                {
                    string next = DeucarianEditorFieldRow.TextField("Root", rootPath, "Project-relative authored content root.");
                    if (!string.Equals(next, rootPath, StringComparison.Ordinal))
                        setRootPath?.Invoke(next);
                    if (DeucarianEditorButtons.Secondary("Refresh", true, GUILayout.Width(82f), GUILayout.Height(24f)))
                        refresh?.Invoke();
                },
                false);
        }

        internal static void DrawGameGraph(GameContentAuthoringSurfaceContext context, GameContentLibraryV2State state, GameContentLibraryItem selected)
        {
            IReadOnlyList<GameContentLibraryV2GraphEdge> edges = GameContentLibraryV2Model.BuildGraphEdges(selected);
            DeucarianEditorCards.DrawInlineCard(() =>
            {
                DeucarianEditorStatusBadge.Draw(GameContentLibraryV2Model.GetKindLabel(selected.Kind), DeucarianEditorStatus.Info, GUILayout.Width(128f));
                DeucarianEditorTextGUI.LabelField(selected.DisplayName, DeucarianEditorStyles.SectionTitle);
                DeucarianEditorTextGUI.LabelField(GameContentLibraryV2Model.GetReadinessLabel(selected), DeucarianEditorStyles.MutedLabel);
            });

            if (edges.Count == 0)
            {
                DeucarianEditorCards.DrawInlineCard(() =>
                {
                    DeucarianEditorStatusBadge.Draw("No Edges", DeucarianEditorStatus.Disabled, GUILayout.Width(82f));
                    DeucarianEditorTextGUI.LabelField("No authored dependency edges found.", DeucarianEditorStyles.MutedLabel);
                });
                return;
            }

            foreach (IGrouping<string, GameContentLibraryV2GraphEdge> group in edges.GroupBy(edge => edge.Relation))
            {
                DeucarianEditorTextGUI.LabelField(group.Key, DeucarianEditorStyles.SectionTitle);
                foreach (GameContentLibraryV2GraphEdge edge in group)
                    DrawGraphEdge(context, state, edge);
            }
        }

        internal static void DrawGraphEdge(GameContentAuthoringSurfaceContext context, GameContentLibraryV2State state, GameContentLibraryV2GraphEdge edge)
        {
            if (edge == null || edge.To == null)
                return;

            bool clicked = DeucarianEditorCompactObjectCard.Draw(
                edge.To.DisplayName,
                edge.From.DisplayName + " -> " + (string.IsNullOrWhiteSpace(edge.To.Id) ? "(missing id)" : edge.To.Id),
                false,
                new[]
                {
                    new DeucarianEditorStatusChip(GameContentLibraryV2Model.GetKindChipLabel(edge.To.Kind), DeucarianEditorStatus.Info),
                    new DeucarianEditorStatusChip(GameContentLibraryV2Model.GetReadinessLabel(edge.To), GameContentLibraryV2Model.GetStatus(edge.To))
                },
                () => DeucarianEditorMiniToolbar.PingButton(edge.To.Asset),
                null,
                GUILayout.ExpandWidth(true));

            if (clicked)
            {
                state.Select(edge.To);
                context.RequestRepaint();
                if (Event.current != null)
                    Event.current.Use();
            }
        }

        internal static void DrawDebugGraph(GameContentLibraryItem selected)
        {
            IReadOnlyList<GameContentLibraryV2GraphEdge> edges = GameContentLibraryV2Model.BuildGraphEdges(selected);
            DeucarianEditorCards.DrawInlineCard(() =>
            {
                GameContentLibraryViewControls.DrawSummaryRow("Selected Key", selected.Key);
                GameContentLibraryViewControls.DrawSummaryRow("Path", selected.Path);
                GameContentLibraryViewControls.DrawSummaryRow("Direct", selected.DirectReferences.Count.ToString(CultureInfo.InvariantCulture));
                GameContentLibraryViewControls.DrawSummaryRow("Used By", selected.ReverseReferences.Count.ToString(CultureInfo.InvariantCulture));
            });

            for (int i = 0; i < edges.Count; i++)
            {
                GameContentLibraryV2GraphEdge edge = edges[i];
                DeucarianEditorCards.DrawInlineCard(() =>
                {
                    GameContentLibraryViewControls.DrawSummaryRow("Relation", edge.Relation);
                    GameContentLibraryViewControls.DrawSummaryRow("From", edge.From.DisplayName + " | " + edge.From.Path);
                    GameContentLibraryViewControls.DrawSummaryRow("To", edge.To.DisplayName + " | " + edge.To.Path);
                    GameContentLibraryViewControls.DrawSummaryRow("Property", edge.Context);
                });
            }
        }
    }
}
