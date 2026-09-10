using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Deucarian.Editor;
using UnityEditor;
using UnityEngine;

namespace Deucarian.GameContentAuthoring.Editor
{
    internal static class GameContentLibraryDetailView
    {
        internal static void DrawSelectedDetail(GameContentAuthoringSurfaceContext context, GameContentLibraryReport report, GameContentLibraryV2State state)
        {
            GameContentLibraryItem selected = state.GetSelected(report);
            state.DetailScroll = EditorGUILayout.BeginScrollView(state.DetailScroll);
            if (selected == null)
            {
                DrawNoSelection(report);
                EditorGUILayout.EndScrollView();
                return;
            }

            DrawSelectedHeader(selected);
            state.DetailPage = DeucarianEditorSegmentedControl.DrawPageChips(
                Mathf.Clamp(state.DetailPage, 0, GameContentLibraryV2UiContract.DetailPages.Length - 1),
                GameContentLibraryV2UiContract.DetailPages);

            switch (state.DetailPage)
            {
                case 1:
                    DrawReferenceCards(context, state, selected.DirectReferences, "No direct dependencies.");
                    break;
                case 2:
                    DrawReferenceCards(context, state, selected.ReverseReferences, "No authored assets use this asset.");
                    break;
                case 3:
                    DrawValidation(selected);
                    break;
                case 4:
                    DrawReadiness(report, selected);
                    break;
                case 5:
                    DrawAdvanced(selected, report);
                    break;
                default:
                    DrawOverview(report, selected);
                    break;
            }

            EditorGUILayout.EndScrollView();
        }

        internal static void DrawNoSelection(GameContentLibraryReport report)
        {
            DeucarianEditorCards.DrawInlineCard(() =>
            {
                DeucarianEditorStatusBadge.Draw("Library", DeucarianEditorStatus.Info, GUILayout.Width(72f));
                DeucarianEditorTextGUI.LabelField(report != null && report.Items.Count == 0 ? "No authored content found." : "Select an authored asset.", DeucarianEditorStyles.MutedLabel);
            });
        }

        internal static void DrawSelectedHeader(GameContentLibraryItem selected)
        {
            DeucarianEditorTextGUI.LabelField(selected.DisplayName, GameContentLibraryViewControls.HeaderStyle);
            DeucarianEditorTextGUI.LabelField(string.IsNullOrWhiteSpace(selected.Id) ? "(missing id)" : selected.Id, DeucarianEditorStyles.MutedLabel);
            DeucarianEditorStatusChipRow.Draw(new[]
            {
                new DeucarianEditorStatusChip(GameContentLibraryV2Model.GetKindLabel(selected.Kind), DeucarianEditorStatus.Info),
                new DeucarianEditorStatusChip(GameContentLibraryV2Model.GetReadinessLabel(selected), GameContentLibraryV2Model.GetStatus(selected)),
                new DeucarianEditorStatusChip(selected.DirectReferences.Count.ToString(CultureInfo.InvariantCulture) + " deps", selected.DirectReferences.Count > 0 ? DeucarianEditorStatus.Success : DeucarianEditorStatus.Disabled),
                new DeucarianEditorStatusChip(selected.ReverseReferences.Count.ToString(CultureInfo.InvariantCulture) + " used by", selected.ReverseReferences.Count > 0 ? DeucarianEditorStatus.Success : DeucarianEditorStatus.Disabled)
            });
        }

        internal static void DrawOverview(GameContentLibraryReport report, GameContentLibraryItem selected)
        {
            DeucarianEditorCards.DrawInlineCard(() =>
            {
                GameContentLibraryViewControls.DrawSummaryRow("Type", GameContentLibraryV2Model.GetKindLabel(selected.Kind));
                GameContentLibraryViewControls.DrawSummaryRow("Readiness", GameContentLibraryV2Model.GetReadinessLabel(selected));
                GameContentLibraryViewControls.DrawSummaryRow("Summary", GameContentLibraryV2Model.BuildSelectedSummary(selected));
            });

            DrawReadiness(report, selected);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (DeucarianEditorButtons.Secondary("Ping", selected.Asset != null, GUILayout.Width(64f), GUILayout.Height(24f)))
                    EditorGUIUtility.PingObject(selected.Asset);
                if (DeucarianEditorButtons.Secondary("Open", selected.Asset != null, GUILayout.Width(64f), GUILayout.Height(24f)))
                    GameContentLibraryViewControls.OpenAsset(selected.Asset);
                if (DeucarianEditorButtons.Secondary("Copy Summary", true, GUILayout.Width(112f), GUILayout.Height(24f)))
                    EditorGUIUtility.systemCopyBuffer = BuildSelectedMarkdown(report, selected);
            }
        }

        internal static void DrawReferenceCards(
            GameContentAuthoringSurfaceContext context,
            GameContentLibraryV2State state,
            IReadOnlyList<GameContentLibraryReference> references,
            string emptyText)
        {
            if (references == null || references.Count == 0)
            {
                DeucarianEditorCards.DrawInlineCard(() =>
                {
                    DeucarianEditorStatusBadge.Draw("None", DeucarianEditorStatus.Disabled, GUILayout.Width(64f));
                    DeucarianEditorTextGUI.LabelField(emptyText, DeucarianEditorStyles.MutedLabel);
                });
                return;
            }

            for (int i = 0; i < references.Count; i++)
            {
                GameContentLibraryReference reference = references[i];
                if (reference == null || reference.Target == null)
                    continue;

                GameContentLibraryItem target = reference.Target;
                bool clicked = DeucarianEditorCompactObjectCard.Draw(
                    target.DisplayName,
                    string.IsNullOrWhiteSpace(target.Id) ? "(missing id)" : target.Id,
                    false,
                    new[]
                    {
                        new DeucarianEditorStatusChip(GameContentLibraryV2Model.GetKindChipLabel(target.Kind), DeucarianEditorStatus.Info),
                        new DeucarianEditorStatusChip(GameContentLibraryV2Model.GetReadinessLabel(target), GameContentLibraryV2Model.GetStatus(target))
                    },
                    () =>
                    {
                        DeucarianEditorMiniToolbar.PingButton(target.Asset);
                        if (DeucarianEditorMiniToolbar.Button("Open", target.Asset != null, GUILayout.Width(48f), GUILayout.Height(22f)))
                            GameContentLibraryViewControls.OpenAsset(target.Asset);
                    },
                    null,
                    GUILayout.ExpandWidth(true));

                if (clicked)
                {
                    state.Select(target);
                    context.RequestRepaint();
                    if (Event.current != null)
                        Event.current.Use();
                }
            }
        }

        internal static void DrawValidation(GameContentLibraryItem selected)
        {
            DeucarianEditorCards.DrawInlineCard(() =>
            {
                if (selected.Issues.Count == 0)
                {
                    DeucarianEditorStatusBadge.Draw("Ready", DeucarianEditorStatus.Success, GUILayout.Width(72f));
                    DeucarianEditorTextGUI.LabelField("No blockers or warnings.", DeucarianEditorStyles.MutedLabel);
                    return;
                }

                DeucarianEditorStatus status = selected.ErrorCount > 0 ? DeucarianEditorStatus.Error : DeucarianEditorStatus.Warning;
                DeucarianEditorStatusBadge.Draw(selected.ValidationLabel, status, GUILayout.Width(104f));
                for (int i = 0; i < selected.Issues.Count; i++)
                    DeucarianEditorTextGUI.LabelField(selected.Issues[i].Path + ": " + selected.Issues[i].Message, DeucarianEditorStyles.MutedLabel);
            });
        }

        internal static void DrawReadiness(GameContentLibraryReport report, GameContentLibraryItem selected)
        {
            DeucarianEditorCards.DrawInlineCard(() =>
            {
                switch (selected.Kind)
                {
                    case GameContentLibraryKind.ContentPack:
                        DrawContentPackReadiness(report, selected);
                        break;
                    case GameContentLibraryKind.ContentSet:
                        DrawContentSetReadiness(report, selected);
                        break;
                    case GameContentLibraryKind.Weapon:
                        GameContentLibraryViewControls.DrawSummaryRow("Assigned Attack", CountDirect(selected, GameContentLibraryKind.Attack).ToString(CultureInfo.InvariantCulture));
                        GameContentLibraryViewControls.DrawSummaryRow("Content Sets Using It", CountReverse(selected, GameContentLibraryKind.ContentSet).ToString(CultureInfo.InvariantCulture));
                        break;
                    case GameContentLibraryKind.Attack:
                        GameContentLibraryViewControls.DrawSummaryRow("Weapons Using It", CountReverse(selected, GameContentLibraryKind.Weapon).ToString(CultureInfo.InvariantCulture));
                        GameContentLibraryViewControls.DrawSummaryRow("Presentation References", selected.DirectReferences.Count.ToString(CultureInfo.InvariantCulture));
                        break;
                    case GameContentLibraryKind.Wave:
                        GameContentLibraryViewControls.DrawSummaryRow("Enemy Entries", CountDirect(selected, GameContentLibraryKind.Enemy).ToString(CultureInfo.InvariantCulture));
                        GameContentLibraryViewControls.DrawSummaryRow("Content Sets Using It", CountReverse(selected, GameContentLibraryKind.ContentSet).ToString(CultureInfo.InvariantCulture));
                        break;
                    case GameContentLibraryKind.Enemy:
                        GameContentLibraryViewControls.DrawSummaryRow("Waves Using It", CountReverse(selected, GameContentLibraryKind.Wave).ToString(CultureInfo.InvariantCulture));
                        GameContentLibraryViewControls.DrawSummaryRow("Content Sets Using It", CountReverse(selected, GameContentLibraryKind.ContentSet).ToString(CultureInfo.InvariantCulture));
                        break;
                    case GameContentLibraryKind.Upgrade:
                        GameContentLibraryViewControls.DrawSummaryRow("Target References", selected.DirectReferences.Count.ToString(CultureInfo.InvariantCulture));
                        GameContentLibraryViewControls.DrawSummaryRow("Content Sets Using It", CountReverse(selected, GameContentLibraryKind.ContentSet).ToString(CultureInfo.InvariantCulture));
                        break;
                }
            });
        }

        internal static void DrawContentPackReadiness(GameContentLibraryReport report, GameContentLibraryItem selected)
        {
            GameContentLibraryContentPackSummary summary = report.GetContentPackSummary(selected);
            if (summary == null)
                return;

            DeucarianEditorStatusBadge.Draw(summary.Ready ? "Ready" : "Needs Fixes", summary.Ready ? DeucarianEditorStatus.Success : DeucarianEditorStatus.Warning, GUILayout.Width(94f));
            GameContentLibraryViewControls.DrawSummaryRow("Default / Included Sets", summary.ContentSetCount.ToString(CultureInfo.InvariantCulture));
            GameContentLibraryViewControls.DrawSummaryRow("Weapons", summary.WeaponCount.ToString(CultureInfo.InvariantCulture));
            GameContentLibraryViewControls.DrawSummaryRow("Waves", summary.WaveCount.ToString(CultureInfo.InvariantCulture));
            GameContentLibraryViewControls.DrawSummaryRow("Enemies", summary.EnemyCount.ToString(CultureInfo.InvariantCulture));
            GameContentLibraryViewControls.DrawSummaryRow("Upgrades", summary.UpgradeCount.ToString(CultureInfo.InvariantCulture));
        }

        internal static void DrawContentSetReadiness(GameContentLibraryReport report, GameContentLibraryItem selected)
        {
            GameContentLibraryContentSetSummary summary = report.GetContentSetSummary(selected);
            if (summary == null)
                return;

            DeucarianEditorStatusBadge.Draw(summary.Ready ? "Ready" : "Needs Fixes", summary.Ready ? DeucarianEditorStatus.Success : DeucarianEditorStatus.Warning, GUILayout.Width(94f));
            GameContentLibraryViewControls.DrawSummaryRow("Starting Weapon", CountDirect(selected, GameContentLibraryKind.Weapon) > 0 ? "Assigned" : "Missing");
            GameContentLibraryViewControls.DrawSummaryRow("Weapons", summary.WeaponCount.ToString(CultureInfo.InvariantCulture));
            GameContentLibraryViewControls.DrawSummaryRow("Waves", summary.WaveCount.ToString(CultureInfo.InvariantCulture));
            GameContentLibraryViewControls.DrawSummaryRow("Enemies", summary.EnemyCount.ToString(CultureInfo.InvariantCulture));
            GameContentLibraryViewControls.DrawSummaryRow("Upgrades", summary.UpgradeCount.ToString(CultureInfo.InvariantCulture));
        }

        internal static void DrawAdvanced(GameContentLibraryItem selected, GameContentLibraryReport report)
        {
            DeucarianEditorDiagnosticsDrawer.Draw(
                DeucarianEditorAccordion.BuildStateKey("content-library-v2", "advanced", selected.Key),
                "Raw Details",
                () =>
                {
                    GameContentLibraryViewControls.DrawSummaryRow("Path", selected.Path);
                    GameContentLibraryViewControls.DrawSummaryRow("Folder", selected.Folder);
                    GameContentAuthoringProviderGUI.DrawReferenceList("Serialized Dependencies", selected.DirectReferences);
                    GameContentAuthoringProviderGUI.DrawReferenceList("Serialized Used By", selected.ReverseReferences);
                    if (DeucarianEditorButtons.Secondary("Copy Raw Report", true, GUILayout.Width(116f), GUILayout.Height(24f)))
                        EditorGUIUtility.systemCopyBuffer = BuildSelectedMarkdown(report, selected);
                },
                false);
        }

        internal static int CountDirect(GameContentLibraryItem item, GameContentLibraryKind kind)
        {
            return item == null ? 0 : item.DirectReferences.Count(reference => reference.Target != null && reference.Target.Kind == kind);
        }

        internal static int CountReverse(GameContentLibraryItem item, GameContentLibraryKind kind)
        {
            return item == null ? 0 : item.ReverseReferences.Count(reference => reference.Target != null && reference.Target.Kind == kind);
        }

        internal static string BuildSelectedMarkdown(GameContentLibraryReport report, GameContentLibraryItem selected)
        {
            if (selected == null)
                return string.Empty;
            if (selected.Kind == GameContentLibraryKind.ContentPack)
                return GameContentLibraryReportWriter.ToContentPackMarkdown(report, selected);
            if (selected.Kind == GameContentLibraryKind.ContentSet)
                return GameContentLibraryReportWriter.ToContentSetMarkdown(report, selected);
            return "# " + selected.DisplayName + Environment.NewLine
                + "- ID: " + selected.Id + Environment.NewLine
                + "- Type: " + GameContentLibraryV2Model.GetKindLabel(selected.Kind) + Environment.NewLine
                + "- Readiness: " + GameContentLibraryV2Model.GetReadinessLabel(selected) + Environment.NewLine
                + "- Direct dependencies: " + selected.DirectReferences.Count.ToString(CultureInfo.InvariantCulture) + Environment.NewLine
                + "- Used by: " + selected.ReverseReferences.Count.ToString(CultureInfo.InvariantCulture) + Environment.NewLine;
        }
    }
}
