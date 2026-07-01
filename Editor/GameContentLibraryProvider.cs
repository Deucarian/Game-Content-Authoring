using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Deucarian.Editor;
using UnityEditor;
using UnityEngine;

namespace Deucarian.GameContentAuthoring.Editor
{
    [InitializeOnLoad]
    internal static class GameContentLibraryProviderRegistration
    {
        static GameContentLibraryProviderRegistration()
        {
            GameContentAuthoringProviderRegistry.Register(new GameContentLibraryProvider());
        }
    }

    public sealed class GameContentLibraryProvider : IGameContentAuthoringProvider, IGameContentAuthoringSurfaceProvider
    {
        public const string ContentLibraryProviderId = "com.deucarian.game-content-authoring.content-library";
        public const string DefaultRoot = "Assets/GameContent";

        private string _rootPath = DefaultRoot;
        private GameContentLibraryReport _report;
        private string _selectedKey;
        private readonly GameContentLibraryV2State _v2State = new GameContentLibraryV2State();
        private readonly GameContentLibraryProviderV2View _v2View = new GameContentLibraryProviderV2View();

        public string ProviderId => ContentLibraryProviderId;
        public string DisplayName => "Content Library";
        public string Description => "Browse, inspect, validate, and understand authored game content.";
        public int SortOrder => 1000;
        public bool Enabled => true;

        public void OnSelected()
        {
            Refresh(false);
            _v2State.ResetSession();
        }

        public void DrawCustomAuthoringSurface(GameContentAuthoringSurfaceContext context)
        {
            if (_report == null) Refresh(false);
            context.Authoring.SetValidation(_report.ToValidationResult());
            _v2View.Draw(
                context,
                _report,
                _v2State,
                _rootPath,
                value => _rootPath = value,
                () =>
                {
                    Refresh(true);
                    context.RefreshLibrary();
                });
        }

        public void Draw(GameContentAuthoringContext context)
        {
            if (_report == null) Refresh(false);

            context.DrawSection("Library", () =>
            {
                _rootPath = context.DrawTextField("Content Root", _rootPath, "The library scans authored content under this project-relative folder.");

                using (new EditorGUILayout.HorizontalScope())
                {
                    if (context.DrawSecondaryButton("Validate All Game Content", true, GUILayout.Width(168f)))
                        Refresh(true);

                    if (context.DrawSecondaryButton("Ping Root", AssetDatabase.IsValidFolder(NormalizedRoot), GUILayout.Width(92f)))
                    {
                        UnityEngine.Object folder = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(NormalizedRoot);
                        if (folder != null) EditorGUIUtility.PingObject(folder);
                    }

                    if (context.DrawSecondaryButton("Copy Summary", _report != null, GUILayout.Width(112f)))
                        EditorGUIUtility.systemCopyBuffer = GameContentLibraryReportWriter.ToMarkdown(_report);

                    GUILayout.FlexibleSpace();
                }
            });

            DrawReportSummary(context);
            DrawGroups(context);
        }

        public void DrawPreview(GameContentAuthoringPreviewContext context)
        {
            if (_report == null) Refresh(false);

            GameContentLibraryItem selected = SelectedItem;
            if (selected == null)
            {
                context.DrawCard("Selection", () =>
                {
                    EditorGUILayout.LabelField("Select a content asset from the library to inspect validation, dependencies, and reverse references.", context.MutedStyle);
                });
                return;
            }

            context.DrawCard("Selected Asset", () =>
            {
                context.DrawSummaryRows(new[]
                {
                    new GameContentAuthoringPreviewRow("ID", string.IsNullOrWhiteSpace(selected.Id) ? "(missing)" : selected.Id),
                    new GameContentAuthoringPreviewRow("Name", selected.DisplayName),
                    new GameContentAuthoringPreviewRow("Type", selected.Category),
                    new GameContentAuthoringPreviewRow("State", selected.ValidationLabel)
                });

                using (new EditorGUILayout.HorizontalScope())
                {
                    if (context.DrawSecondaryButton("Ping", selected.Asset != null, GUILayout.Width(56f)) && selected.Asset != null)
                        EditorGUIUtility.PingObject(selected.Asset);
                    if (context.DrawSecondaryButton("Select", selected.Asset != null, GUILayout.Width(64f)) && selected.Asset != null)
                        Selection.activeObject = selected.Asset;
                    if (context.DrawSecondaryButton("Open Folder", AssetDatabase.IsValidFolder(selected.Folder), GUILayout.Width(92f)))
                    {
                        UnityEngine.Object folder = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(selected.Folder);
                        if (folder != null)
                        {
                            Selection.activeObject = folder;
                            EditorGUIUtility.PingObject(folder);
                        }
                    }

                    if (context.DrawSecondaryButton("Revalidate", true, GUILayout.Width(88f)))
                        Refresh(true);
                }
            });

            DrawSelectedValidation(context, selected);
            DrawReferenceList(context, "Direct References", selected.DirectReferences, "No authored direct references found.");
            DrawReferenceList(context, "Referenced By", selected.ReverseReferences, "No authored assets reference this asset.");
            DrawLibraryAdvanced(context, selected);

            if (selected.Kind == GameContentLibraryKind.ContentSet)
            {
                GameContentLibraryContentSetSummary summary = _report.GetContentSetSummary(selected);
                if (summary != null)
                {
                    context.DrawCard("Ready To Play", () =>
                    {
                        DeucarianEditorStatusPanel.DrawStatusCard(summary.Message, summary.Ready ? DeucarianEditorStatus.Success : DeucarianEditorStatus.Warning);
                        context.DrawSummaryRows(new[]
                        {
                            new GameContentAuthoringPreviewRow("Weapons", summary.WeaponCount.ToString(CultureInfo.InvariantCulture)),
                            new GameContentAuthoringPreviewRow("Enemies", summary.EnemyCount.ToString(CultureInfo.InvariantCulture)),
                            new GameContentAuthoringPreviewRow("Waves", summary.WaveCount.ToString(CultureInfo.InvariantCulture)),
                            new GameContentAuthoringPreviewRow("Upgrades", summary.UpgradeCount.ToString(CultureInfo.InvariantCulture))
                        });

                        if (context.DrawSecondaryButton("Copy Content Set Summary", true, GUILayout.Width(172f)))
                            EditorGUIUtility.systemCopyBuffer = GameContentLibraryReportWriter.ToContentSetMarkdown(_report, selected);
                    });
                }
            }

            if (selected.Kind == GameContentLibraryKind.ContentPack)
            {
                GameContentLibraryContentPackSummary summary = _report.GetContentPackSummary(selected);
                if (summary != null)
                {
                    context.DrawCard("Ready To Install", () =>
                    {
                        DeucarianEditorStatusPanel.DrawStatusCard(summary.Message, summary.Ready ? DeucarianEditorStatus.Success : DeucarianEditorStatus.Warning);
                        context.DrawSummaryRows(new[]
                        {
                            new GameContentAuthoringPreviewRow("Content Sets", summary.ContentSetCount.ToString(CultureInfo.InvariantCulture)),
                            new GameContentAuthoringPreviewRow("Weapons", summary.WeaponCount.ToString(CultureInfo.InvariantCulture)),
                            new GameContentAuthoringPreviewRow("Enemies", summary.EnemyCount.ToString(CultureInfo.InvariantCulture)),
                            new GameContentAuthoringPreviewRow("Waves", summary.WaveCount.ToString(CultureInfo.InvariantCulture)),
                            new GameContentAuthoringPreviewRow("Upgrades", summary.UpgradeCount.ToString(CultureInfo.InvariantCulture))
                        });

                        if (context.DrawSecondaryButton("Copy Content Pack Summary", true, GUILayout.Width(176f)))
                            EditorGUIUtility.systemCopyBuffer = GameContentLibraryReportWriter.ToContentPackMarkdown(_report, selected);
                    });
                }
            }
        }

        public void StopPreview()
        {
            _v2State.StopPreview();
        }

        private string NormalizedRoot => GameContentAuthoringEditorPaths.NormalizeAssetFolderPath(_rootPath, DefaultRoot);

        private GameContentLibraryItem SelectedItem
        {
            get
            {
                if (_report == null || string.IsNullOrWhiteSpace(_selectedKey)) return null;
                return _report.Items.FirstOrDefault(item => string.Equals(item.Key, _selectedKey, StringComparison.Ordinal));
            }
        }

        private void Refresh(bool forceSelection)
        {
            string previous = _selectedKey;
            _report = GameContentLibraryService.Scan(NormalizedRoot);
            if (forceSelection || string.IsNullOrWhiteSpace(previous) || _report.Items.All(item => !string.Equals(item.Key, previous, StringComparison.Ordinal)))
                _selectedKey = _report.Items.Count > 0 ? _report.Items[0].Key : string.Empty;
        }

        private void DrawReportSummary(GameContentAuthoringContext context)
        {
            if (_report == null) return;

            GameContentAuthoringValidationResult validation = _report.ToValidationResult();
            string ready = _report.Items.Count == 0
                ? "No authored game content was found under " + _report.RootPath + "."
                : _report.ReadyContentSetCount.ToString(CultureInfo.InvariantCulture) + " ready content set(s), " + _report.ReadyContentPackCount.ToString(CultureInfo.InvariantCulture) + " ready content pack(s), " + _report.Items.Count.ToString(CultureInfo.InvariantCulture) + " authored asset(s).";
            context.DrawValidation(validation, ready);

            context.DrawInlineCard(() =>
            {
                EditorGUILayout.LabelField("Batch Validation", context.SectionTitleStyle);
                EditorGUILayout.LabelField(_report.BlockerCount.ToString(CultureInfo.InvariantCulture) + " blocker(s), " + _report.WarningCount.ToString(CultureInfo.InvariantCulture) + " warning(s), " + _report.InfoCount.ToString(CultureInfo.InvariantCulture) + " info item(s).", context.MutedStyle);
                DrawSeverityRows(_report);
            });
        }

        private static void DrawSeverityRows(GameContentLibraryReport report)
        {
            DrawIssueBucket("Blockers", report.BlockerCount, report.AllIssues.Where(issue => issue.Severity == GameContentAuthoringValidationSeverity.Error));
            DrawIssueBucket("Warnings", report.WarningCount, report.AllIssues.Where(issue => issue.Severity == GameContentAuthoringValidationSeverity.Warning));
            DrawIssueBucket("Info", report.InfoCount, report.AllIssues.Where(issue => issue.Severity == GameContentAuthoringValidationSeverity.Info));
        }

        private static void DrawIssueBucket(string label, int count, IEnumerable<GameContentLibraryIssue> issues)
        {
            if (count == 0) return;
            EditorGUILayout.LabelField(label, DeucarianEditorStyles.SectionTitle);
            foreach (GameContentLibraryIssue issue in issues.Take(6))
                EditorGUILayout.LabelField(issue.Path + ": " + issue.Message, DeucarianEditorStyles.MutedLabel);
        }

        private void DrawGroups(GameContentAuthoringContext context)
        {
            if (_report == null) return;

            foreach (GameContentLibraryGroup group in _report.Groups)
            {
                context.DrawSection(group.Name + " (" + group.Items.Count.ToString(CultureInfo.InvariantCulture) + ")", () =>
                {
                    if (group.Items.Count == 0)
                    {
                        EditorGUILayout.LabelField("No authored assets found.", context.MutedStyle);
                        return;
                    }

                    for (int i = 0; i < group.Items.Count; i++)
                    {
                        GameContentLibraryItem item = group.Items[i];
                        bool selected = string.Equals(item.Key, _selectedKey, StringComparison.Ordinal);
                        Rect rect = EditorGUILayout.BeginHorizontal(GUIStyle.none, GUILayout.MinHeight(40f));
                        if (Event.current.type == EventType.Repaint)
                        {
                            Color background = selected
                                ? new Color(0.07f, 0.30f, 0.32f, 0.82f)
                                : new Color(0.07f, 0.13f, 0.17f, 0.58f);
                            Color border = selected ? DeucarianEditorTheme.Accent : DeucarianEditorTheme.BorderSubtle;
                            DeucarianEditorVisualShell.DrawInsetSurface(rect, background, border, 5f);
                        }

                        if (Event.current.type == EventType.MouseDown && rect.Contains(Event.current.mousePosition))
                        {
                            _selectedKey = item.Key;
                            GUI.FocusControl(null);
                            Event.current.Use();
                        }

                        GUILayout.Space(8f);
                        EditorGUILayout.BeginVertical();
                        EditorGUILayout.LabelField(item.DisplayName, DeucarianEditorStyles.SectionTitle);
                        EditorGUILayout.LabelField(item.IdAndPathLabel, DeucarianEditorStyles.MutedLabel);
                        EditorGUILayout.EndVertical();
                        GUILayout.FlexibleSpace();
                        EditorGUILayout.LabelField(item.ValidationLabel, ValidationMiniStyle(item), GUILayout.Width(84f));
                        GUILayout.Space(8f);
                        EditorGUILayout.EndHorizontal();
                    }
                });
            }
        }

        private static GUIStyle ValidationMiniStyle(GameContentLibraryItem item)
        {
            GUIStyle style = new GUIStyle(EditorStyles.miniBoldLabel)
            {
                alignment = TextAnchor.MiddleRight,
                wordWrap = false
            };
            if (item.ErrorCount > 0) style.normal.textColor = DeucarianEditorTheme.Error;
            else if (item.WarningCount > 0) style.normal.textColor = DeucarianEditorTheme.Warning;
            else style.normal.textColor = DeucarianEditorTheme.Success;
            return style;
        }

        private static void DrawSelectedValidation(GameContentAuthoringPreviewContext context, GameContentLibraryItem selected)
        {
            context.DrawCard("Validation", () =>
            {
                if (selected.Issues.Count == 0)
                {
                    DeucarianEditorStatusBadge.Draw("Ready", DeucarianEditorStatus.Success, GUILayout.Width(72f));
                    return;
                }

                List<string> messages = new List<string>();
                for (int i = 0; i < selected.Issues.Count; i++)
                    messages.Add(selected.Issues[i].Path + ": " + selected.Issues[i].Message);
                DeucarianEditorStatus status = selected.ErrorCount > 0 ? DeucarianEditorStatus.Error : DeucarianEditorStatus.Warning;
                DeucarianEditorStatusPanel.DrawValidationCard(selected.ValidationLabel, messages, status);
            });
        }

        private static void DrawReferenceList(GameContentAuthoringPreviewContext context, string title, IReadOnlyList<GameContentLibraryReference> references, string emptyText)
        {
            context.DrawCard(title, () =>
            {
                if (references == null || references.Count == 0)
                {
                    EditorGUILayout.LabelField(emptyText, context.MutedStyle);
                    return;
                }

                for (int i = 0; i < references.Count; i++)
                {
                    GameContentLibraryReference reference = references[i];
                    context.DrawInlineCard(() =>
                    {
                        using (new EditorGUILayout.HorizontalScope())
                        {
                            EditorGUILayout.LabelField(reference.Target.DisplayName, DeucarianEditorStyles.SectionTitle);
                            GUILayout.FlexibleSpace();
                            EditorGUILayout.LabelField(reference.Target.Category, DeucarianEditorStyles.MutedLabel, GUILayout.Width(112f));
                        }

                        EditorGUILayout.LabelField(string.IsNullOrWhiteSpace(reference.Target.Id) ? reference.Target.ValidationLabel : reference.Target.Id, context.MutedStyle);
                    });
                }
            });
        }

        private static void DrawLibraryAdvanced(GameContentAuthoringPreviewContext context, GameContentLibraryItem selected)
        {
            string key = DeucarianEditorAccordion.BuildStateKey("game-content-library", "advanced", selected.Key);
            bool open = DeucarianEditorAccordion.DrawFoldoutCard(key, "Advanced", "Raw paths and dependency diagnostics.", () =>
            {
                context.DrawInlineCard(() =>
                {
                    context.DrawSummaryRows(new[]
                    {
                        new GameContentAuthoringPreviewRow("Path", selected.Path)
                    });
                    if (context.DrawSecondaryButton("Copy Path", !string.IsNullOrWhiteSpace(selected.Path), GUILayout.Width(84f), GUILayout.Height(22f)))
                        EditorGUIUtility.systemCopyBuffer = selected.Path;
                });

                DrawReferenceProperties(context, "Direct Property Uses", selected.DirectReferences);
                DrawReferenceProperties(context, "Referenced By Properties", selected.ReverseReferences);
                DrawDependencyGraph(context, selected);
            }, false);

            if (open)
            {
                GUILayout.Space(DeucarianEditorSpacing.Tiny);
            }
        }

        private static void DrawReferenceProperties(GameContentAuthoringPreviewContext context, string title, IReadOnlyList<GameContentLibraryReference> references)
        {
            context.DrawInlineCard(() =>
            {
                DeucarianEditorSectionHeader.Draw(title);
                if (references == null || references.Count == 0)
                {
                    EditorGUILayout.LabelField("None", context.MutedStyle);
                    return;
                }

                for (int i = 0; i < references.Count; i++)
                {
                    GameContentLibraryReference reference = references[i];
                    if (reference == null || reference.Target == null) continue;
                    EditorGUILayout.LabelField(reference.Target.DisplayName + " - " + reference.PropertyPath, context.MutedStyle);
                }
            });
        }

        private static void DrawDependencyGraph(GameContentAuthoringPreviewContext context, GameContentLibraryItem selected)
        {
            context.DrawCard("Dependency Graph", () =>
            {
                List<string> lines = GameContentLibraryReportWriter.BuildDependencyLines(selected, 3);
                if (lines.Count == 0)
                {
                    EditorGUILayout.LabelField("No authored dependency edges found.", context.MutedStyle);
                    return;
                }

                for (int i = 0; i < lines.Count; i++)
                    EditorGUILayout.LabelField(lines[i], i == 0 ? DeucarianEditorStyles.SectionTitle : context.MutedStyle);
            });
        }
    }
}
