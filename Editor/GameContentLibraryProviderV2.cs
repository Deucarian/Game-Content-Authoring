using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Deucarian.Editor;
using UnityEditor;
using UnityEngine;

namespace Deucarian.GameContentAuthoring.Editor
{
    public enum GameContentLibraryV2SeverityFilter
    {
        All = 0,
        Blockers = 1,
        Warnings = 2,
        Info = 3,
        Ready = 4
    }

    public enum GameContentLibraryV2ReadinessFilter
    {
        All = 0,
        Ready = 1,
        Warnings = 2,
        Blockers = 3
    }

    public static class GameContentLibraryV2UiContract
    {
        public static readonly string[] DetailPages =
        {
            "Overview",
            "Dependencies",
            "Used By",
            "Validation",
            "Readiness",
            "Advanced"
        };

        public static readonly string[] MainRowActionLabels =
        {
            "Ping",
            "Open"
        };

        public static readonly string[] GraphRelations =
        {
            "Content Pack -> Content Sets",
            "Content Set -> Weapons",
            "Weapon -> Attack",
            "Content Set -> Waves",
            "Wave -> Enemies",
            "Content Set -> Upgrades",
            "Upgrade -> Target"
        };
    }

    public sealed class GameContentLibraryV2State
    {
        private readonly HashSet<GameContentLibraryKind> _collapsedGroups = new HashSet<GameContentLibraryKind>();

        public string SearchText = string.Empty;
        public int TypeFilterIndex;
        public GameContentLibraryV2SeverityFilter SeverityFilter;
        public GameContentLibraryV2ReadinessFilter ReadinessFilter;
        public int DetailPage;
        public bool DebugGraph;
        public Vector2 ListScroll;
        public Vector2 DetailScroll;
        public Vector2 GraphScroll;
        public string SelectedKey = string.Empty;
        public string StatusMessage = "Library ready";

        public void ResetSession()
        {
            SearchText = string.Empty;
            TypeFilterIndex = 0;
            SeverityFilter = GameContentLibraryV2SeverityFilter.All;
            ReadinessFilter = GameContentLibraryV2ReadinessFilter.All;
            DetailPage = 0;
            DebugGraph = false;
            ListScroll = Vector2.zero;
            DetailScroll = Vector2.zero;
            GraphScroll = Vector2.zero;
            SelectedKey = string.Empty;
            StatusMessage = "Library ready";
            _collapsedGroups.Clear();
        }

        public void StopPreview()
        {
            DebugGraph = false;
            StatusMessage = "Library preview stopped";
        }

        public void EnsureSelection(GameContentLibraryReport report)
        {
            if (report == null || report.Items.Count == 0)
            {
                SelectedKey = string.Empty;
                return;
            }

            if (report.Items.Any(item => string.Equals(item.Key, SelectedKey, StringComparison.Ordinal)))
                return;

            SelectedKey = report.Items[0].Key;
        }

        public GameContentLibraryItem GetSelected(GameContentLibraryReport report)
        {
            if (report == null || string.IsNullOrWhiteSpace(SelectedKey))
                return null;
            return report.Items.FirstOrDefault(item => string.Equals(item.Key, SelectedKey, StringComparison.Ordinal));
        }

        public void Select(GameContentLibraryItem item)
        {
            if (item == null) return;
            SelectedKey = item.Key;
            DetailScroll = Vector2.zero;
            GraphScroll = Vector2.zero;
            StatusMessage = "Selected " + item.DisplayName;
            GUI.FocusControl(null);
        }

        public bool IsGroupExpanded(GameContentLibraryKind kind)
        {
            return !_collapsedGroups.Contains(kind);
        }

        public void ToggleGroup(GameContentLibraryKind kind)
        {
            if (!_collapsedGroups.Add(kind))
                _collapsedGroups.Remove(kind);
        }
    }

    public sealed class GameContentLibraryV2Dashboard
    {
        public GameContentLibraryV2Dashboard(
            int totalAssets,
            int readyContentPacks,
            int readyContentSets,
            int blockers,
            int warnings,
            int duplicateIds,
            int missingReferences)
        {
            TotalAssets = totalAssets;
            ReadyContentPacks = readyContentPacks;
            ReadyContentSets = readyContentSets;
            Blockers = blockers;
            Warnings = warnings;
            DuplicateIds = duplicateIds;
            MissingReferences = missingReferences;
        }

        public int TotalAssets { get; }
        public int ReadyContentPacks { get; }
        public int ReadyContentSets { get; }
        public int Blockers { get; }
        public int Warnings { get; }
        public int DuplicateIds { get; }
        public int MissingReferences { get; }
    }

    public sealed class GameContentLibraryV2GroupModel
    {
        public GameContentLibraryV2GroupModel(GameContentLibraryKind kind, string name, int totalCount, IReadOnlyList<GameContentLibraryV2ItemModel> items)
        {
            Kind = kind;
            Name = name ?? string.Empty;
            TotalCount = totalCount;
            Items = items == null ? Array.Empty<GameContentLibraryV2ItemModel>() : items.ToArray();
            BlockerCount = Items.Count(item => item.Source.ErrorCount > 0);
            WarningCount = Items.Count(item => item.Source.ErrorCount == 0 && item.Source.WarningCount > 0);
            ReadyCount = Items.Count(item => item.Source.ErrorCount == 0 && item.Source.WarningCount == 0);
        }

        public GameContentLibraryKind Kind { get; }
        public string Name { get; }
        public int TotalCount { get; }
        public IReadOnlyList<GameContentLibraryV2ItemModel> Items { get; }
        public int BlockerCount { get; }
        public int WarningCount { get; }
        public int ReadyCount { get; }
    }

    public sealed class GameContentLibraryV2ItemModel
    {
        public GameContentLibraryV2ItemModel(GameContentLibraryItem source)
        {
            Source = source;
            DisplayName = source == null ? string.Empty : source.DisplayName;
            StableId = source == null || string.IsNullOrWhiteSpace(source.Id) ? "(missing id)" : source.Id;
            TypeLabel = source == null ? string.Empty : GameContentLibraryV2Model.GetKindChipLabel(source.Kind);
            DirectDependencyCount = source == null ? 0 : source.DirectReferences.Count;
            ReverseReferenceCount = source == null ? 0 : source.ReverseReferences.Count;
            ReadinessLabel = GameContentLibraryV2Model.GetReadinessLabel(source);
            ReadinessStatus = GameContentLibraryV2Model.GetStatus(source);
        }

        public GameContentLibraryItem Source { get; }
        public string DisplayName { get; }
        public string StableId { get; }
        public string TypeLabel { get; }
        public int DirectDependencyCount { get; }
        public int ReverseReferenceCount { get; }
        public string ReadinessLabel { get; }
        public DeucarianEditorStatus ReadinessStatus { get; }
    }

    public sealed class GameContentLibraryV2GraphEdge
    {
        public GameContentLibraryV2GraphEdge(GameContentLibraryItem from, GameContentLibraryItem to, string relation, string context)
        {
            From = from;
            To = to;
            Relation = relation ?? string.Empty;
            Context = context ?? string.Empty;
        }

        public GameContentLibraryItem From { get; }
        public GameContentLibraryItem To { get; }
        public string Relation { get; }
        public string Context { get; }
    }

    public static class GameContentLibraryV2Model
    {
        private static readonly GameContentLibraryKind[] KindOrder =
        {
            GameContentLibraryKind.ContentPack,
            GameContentLibraryKind.ContentSet,
            GameContentLibraryKind.Weapon,
            GameContentLibraryKind.Attack,
            GameContentLibraryKind.Wave,
            GameContentLibraryKind.Enemy,
            GameContentLibraryKind.Upgrade
        };

        private static readonly string[] TypeFilterLabels =
        {
            "All Types",
            "Content Packs",
            "Game / Run Content Sets",
            "Tower / Weapon",
            "Attacks",
            "Waves",
            "Enemies",
            "Upgrades"
        };

        public static IReadOnlyList<string> GetTypeFilterLabels()
        {
            return TypeFilterLabels;
        }

        public static GameContentLibraryKind? GetKindForTypeFilter(int typeFilterIndex)
        {
            switch (typeFilterIndex)
            {
                case 1: return GameContentLibraryKind.ContentPack;
                case 2: return GameContentLibraryKind.ContentSet;
                case 3: return GameContentLibraryKind.Weapon;
                case 4: return GameContentLibraryKind.Attack;
                case 5: return GameContentLibraryKind.Wave;
                case 6: return GameContentLibraryKind.Enemy;
                case 7: return GameContentLibraryKind.Upgrade;
                default: return null;
            }
        }

        public static string GetKindLabel(GameContentLibraryKind kind)
        {
            switch (kind)
            {
                case GameContentLibraryKind.ContentPack:
                    return "Content Pack";
                case GameContentLibraryKind.ContentSet:
                    return "Game / Run Content Set";
                case GameContentLibraryKind.Weapon:
                    return "Tower / Weapon";
                case GameContentLibraryKind.Attack:
                    return "Attack";
                case GameContentLibraryKind.Wave:
                    return "Wave";
                case GameContentLibraryKind.Enemy:
                    return "Enemy";
                case GameContentLibraryKind.Upgrade:
                    return "Upgrade";
                default:
                    return kind.ToString();
            }
        }

        public static string GetKindChipLabel(GameContentLibraryKind kind)
        {
            switch (kind)
            {
                case GameContentLibraryKind.ContentPack:
                    return "Pack";
                case GameContentLibraryKind.ContentSet:
                    return "Content Set";
                case GameContentLibraryKind.Weapon:
                    return "Weapon";
                case GameContentLibraryKind.Attack:
                    return "Attack";
                case GameContentLibraryKind.Wave:
                    return "Wave";
                case GameContentLibraryKind.Enemy:
                    return "Enemy";
                case GameContentLibraryKind.Upgrade:
                    return "Upgrade";
                default:
                    return kind.ToString();
            }
        }

        public static GameContentLibraryV2Dashboard BuildDashboard(GameContentLibraryReport report)
        {
            if (report == null)
                return new GameContentLibraryV2Dashboard(0, 0, 0, 0, 0, 0, 0);

            int duplicates = report.AllIssues.Count(issue =>
                string.Equals(issue.Path, "Duplicate IDs", StringComparison.OrdinalIgnoreCase)
                || issue.Message.IndexOf("Duplicate ", StringComparison.OrdinalIgnoreCase) >= 0);
            int missing = report.AllIssues.Count(issue =>
                issue.Message.IndexOf("missing", StringComparison.OrdinalIgnoreCase) >= 0
                || issue.Message.IndexOf("Broken object reference", StringComparison.OrdinalIgnoreCase) >= 0);

            return new GameContentLibraryV2Dashboard(
                report.Items.Count,
                report.ReadyContentPackCount,
                report.ReadyContentSetCount,
                report.BlockerCount,
                report.WarningCount,
                duplicates,
                missing);
        }

        public static IReadOnlyList<GameContentLibraryV2GroupModel> BuildGroups(
            GameContentLibraryReport report,
            string searchText,
            int typeFilterIndex,
            GameContentLibraryV2SeverityFilter severityFilter,
            GameContentLibraryV2ReadinessFilter readinessFilter)
        {
            if (report == null)
                return Array.Empty<GameContentLibraryV2GroupModel>();

            GameContentLibraryKind? kindFilter = GetKindForTypeFilter(typeFilterIndex);
            var groups = new List<GameContentLibraryV2GroupModel>();
            for (int i = 0; i < KindOrder.Length; i++)
            {
                GameContentLibraryKind kind = KindOrder[i];
                GameContentLibraryItem[] allForKind = report.Items.Where(item => item.Kind == kind).ToArray();
                GameContentLibraryV2ItemModel[] filtered = allForKind
                    .Where(item => Matches(item, searchText, kindFilter, severityFilter, readinessFilter))
                    .Select(item => new GameContentLibraryV2ItemModel(item))
                    .ToArray();
                groups.Add(new GameContentLibraryV2GroupModel(kind, GetGroupLabel(kind), allForKind.Length, filtered));
            }

            return groups;
        }

        public static bool Matches(
            GameContentLibraryItem item,
            string searchText,
            GameContentLibraryKind? kindFilter,
            GameContentLibraryV2SeverityFilter severityFilter,
            GameContentLibraryV2ReadinessFilter readinessFilter)
        {
            if (item == null)
                return false;
            if (kindFilter.HasValue && item.Kind != kindFilter.Value)
                return false;
            if (!MatchesSearch(item, searchText))
                return false;
            if (!MatchesSeverity(item, severityFilter))
                return false;
            return MatchesReadiness(item, readinessFilter);
        }

        public static IReadOnlyList<GameContentLibraryV2GraphEdge> BuildGraphEdges(GameContentLibraryItem selected)
        {
            if (selected == null)
                return Array.Empty<GameContentLibraryV2GraphEdge>();

            var edges = new List<GameContentLibraryV2GraphEdge>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            AddDirectEdges(selected, edges, seen);

            if (selected.Kind == GameContentLibraryKind.ContentPack)
            {
                foreach (GameContentLibraryReference reference in selected.DirectReferences)
                {
                    if (reference.Target == null || reference.Target.Kind != GameContentLibraryKind.ContentSet)
                        continue;
                    AddContentSetDeepEdges(reference.Target, edges, seen);
                }
            }
            else if (selected.Kind == GameContentLibraryKind.ContentSet)
            {
                AddContentSetDeepEdges(selected, edges, seen);
            }
            else
            {
                foreach (GameContentLibraryReference reference in selected.DirectReferences)
                {
                    if (reference.Target == null)
                        continue;
                    if (reference.Target.Kind == GameContentLibraryKind.Weapon
                        || reference.Target.Kind == GameContentLibraryKind.Wave
                        || reference.Target.Kind == GameContentLibraryKind.Upgrade)
                        AddDirectEdges(reference.Target, edges, seen);
                }
            }

            return edges;
        }

        public static string GetReadinessLabel(GameContentLibraryItem item)
        {
            if (item == null) return "Unknown";
            if (item.ErrorCount > 0)
                return item.ErrorCount.ToString(CultureInfo.InvariantCulture) + " blocker(s)";
            if (item.WarningCount > 0)
                return item.WarningCount.ToString(CultureInfo.InvariantCulture) + " warning(s)";
            return "Ready";
        }

        public static DeucarianEditorStatus GetStatus(GameContentLibraryItem item)
        {
            if (item == null) return DeucarianEditorStatus.Disabled;
            if (item.ErrorCount > 0) return DeucarianEditorStatus.Error;
            if (item.WarningCount > 0) return DeucarianEditorStatus.Warning;
            return DeucarianEditorStatus.Success;
        }

        public static string BuildSelectedSummary(GameContentLibraryItem item)
        {
            if (item == null) return string.Empty;
            string dependencyText = item.DirectReferences.Count == 1 ? "direct dependency" : "direct dependencies";
            return GetKindLabel(item.Kind) + " with "
                + item.DirectReferences.Count.ToString(CultureInfo.InvariantCulture)
                + " "
                + dependencyText
                + " and "
                + item.ReverseReferences.Count.ToString(CultureInfo.InvariantCulture)
                + " reverse reference(s).";
        }

        private static string GetGroupLabel(GameContentLibraryKind kind)
        {
            switch (kind)
            {
                case GameContentLibraryKind.ContentPack:
                    return "Content Packs";
                case GameContentLibraryKind.ContentSet:
                    return "Game / Run Content Sets";
                case GameContentLibraryKind.Weapon:
                    return "Tower / Weapon";
                case GameContentLibraryKind.Attack:
                    return "Attacks";
                case GameContentLibraryKind.Wave:
                    return "Waves";
                case GameContentLibraryKind.Enemy:
                    return "Enemies";
                case GameContentLibraryKind.Upgrade:
                    return "Upgrades";
                default:
                    return kind.ToString();
            }
        }

        private static bool MatchesSearch(GameContentLibraryItem item, string searchText)
        {
            if (string.IsNullOrWhiteSpace(searchText))
                return true;

            string text = searchText.Trim();
            return Contains(item.DisplayName, text)
                || Contains(item.Id, text)
                || Contains(item.Category, text)
                || Contains(GetReadinessLabel(item), text)
                || item.DirectReferences.Any(reference => reference.Target != null && Contains(reference.Target.DisplayName, text))
                || item.ReverseReferences.Any(reference => reference.Target != null && Contains(reference.Target.DisplayName, text));
        }

        private static bool MatchesSeverity(GameContentLibraryItem item, GameContentLibraryV2SeverityFilter filter)
        {
            switch (filter)
            {
                case GameContentLibraryV2SeverityFilter.Blockers:
                    return item.ErrorCount > 0;
                case GameContentLibraryV2SeverityFilter.Warnings:
                    return item.WarningCount > 0;
                case GameContentLibraryV2SeverityFilter.Info:
                    return item.Issues.Any(issue => issue.Severity == GameContentAuthoringValidationSeverity.Info);
                case GameContentLibraryV2SeverityFilter.Ready:
                    return item.ErrorCount == 0 && item.WarningCount == 0;
                default:
                    return true;
            }
        }

        private static bool MatchesReadiness(GameContentLibraryItem item, GameContentLibraryV2ReadinessFilter filter)
        {
            switch (filter)
            {
                case GameContentLibraryV2ReadinessFilter.Ready:
                    return item.ErrorCount == 0 && item.WarningCount == 0;
                case GameContentLibraryV2ReadinessFilter.Warnings:
                    return item.ErrorCount == 0 && item.WarningCount > 0;
                case GameContentLibraryV2ReadinessFilter.Blockers:
                    return item.ErrorCount > 0;
                default:
                    return true;
            }
        }

        private static bool Contains(string value, string searchText)
        {
            return !string.IsNullOrWhiteSpace(value)
                && value.IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static void AddContentSetDeepEdges(
            GameContentLibraryItem contentSet,
            List<GameContentLibraryV2GraphEdge> edges,
            HashSet<string> seen)
        {
            AddDirectEdges(contentSet, edges, seen);
            foreach (GameContentLibraryReference reference in contentSet.DirectReferences)
            {
                GameContentLibraryItem target = reference.Target;
                if (target == null)
                    continue;

                if (target.Kind == GameContentLibraryKind.Weapon
                    || target.Kind == GameContentLibraryKind.Wave
                    || target.Kind == GameContentLibraryKind.Upgrade)
                    AddDirectEdges(target, edges, seen);
            }
        }

        private static void AddDirectEdges(
            GameContentLibraryItem source,
            List<GameContentLibraryV2GraphEdge> edges,
            HashSet<string> seen)
        {
            if (source == null)
                return;

            for (int i = 0; i < source.DirectReferences.Count; i++)
            {
                GameContentLibraryReference reference = source.DirectReferences[i];
                if (reference.Target == null)
                    continue;

                string relation = GetRelationLabel(source.Kind, reference.Target.Kind);
                string key = source.Key + "->" + reference.Target.Key + "::" + relation;
                if (!seen.Add(key))
                    continue;
                edges.Add(new GameContentLibraryV2GraphEdge(source, reference.Target, relation, reference.PropertyPath));
            }
        }

        private static string GetRelationLabel(GameContentLibraryKind from, GameContentLibraryKind to)
        {
            if (from == GameContentLibraryKind.ContentPack && to == GameContentLibraryKind.ContentSet)
                return "Content Pack -> Content Sets";
            if (from == GameContentLibraryKind.ContentSet && to == GameContentLibraryKind.Weapon)
                return "Content Set -> Weapons";
            if (from == GameContentLibraryKind.Weapon && to == GameContentLibraryKind.Attack)
                return "Weapon -> Attack";
            if (from == GameContentLibraryKind.ContentSet && to == GameContentLibraryKind.Wave)
                return "Content Set -> Waves";
            if (from == GameContentLibraryKind.Wave && to == GameContentLibraryKind.Enemy)
                return "Wave -> Enemies";
            if (from == GameContentLibraryKind.ContentSet && to == GameContentLibraryKind.Upgrade)
                return "Content Set -> Upgrades";
            if (from == GameContentLibraryKind.Upgrade)
                return "Upgrade -> Target";
            if (from == GameContentLibraryKind.ContentSet && to == GameContentLibraryKind.Enemy)
                return "Content Set -> Enemies";
            return GetKindLabel(from) + " -> " + GetKindLabel(to);
        }
    }

    internal sealed class GameContentLibraryProviderV2View
    {
        private static readonly string[] SeverityFilterLabels =
        {
            "All Issues",
            "Blockers",
            "Warnings",
            "Info",
            "Ready"
        };

        private static readonly string[] ReadinessFilterLabels =
        {
            "All States",
            "Ready",
            "Warnings",
            "Blockers"
        };

        private static readonly string[] GraphModeLabels =
        {
            "Game",
            "Debug"
        };

        public void Draw(
            GameContentAuthoringSurfaceContext context,
            GameContentLibraryReport report,
            GameContentLibraryV2State state,
            string rootPath,
            Action<string> setRootPath,
            Action refresh)
        {
            if (context == null || report == null || state == null)
                return;

            state.EnsureSelection(report);
            GameContentAuthoringWorkbench.Draw(
                context,
                () => DrawLibraryList(context, report, state, refresh),
                () => DrawSelectedDetail(context, report, state),
                () => DrawGraphPreview(context, report, state, rootPath, setRootPath, refresh));
        }

        private static void DrawLibraryList(
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

        private static void DrawFilterRow(GameContentLibraryV2State state)
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

        private static void DrawDashboard(GameContentLibraryV2Dashboard dashboard)
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

        private static void DrawGroupHeader(GameContentLibraryV2State state, GameContentLibraryV2GroupModel group)
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

        private static void DrawItemCard(GameContentAuthoringSurfaceContext context, GameContentLibraryV2State state, GameContentLibraryV2ItemModel model)
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
                        OpenAsset(item.Asset);
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

        private static void DrawEmptyListState(GameContentLibraryReport report)
        {
            DeucarianEditorCards.DrawInlineCard(() =>
            {
                DeucarianEditorStatusBadge.Draw(report != null && report.Items.Count == 0 ? "Empty" : "Filtered", DeucarianEditorStatus.Info, GUILayout.Width(74f));
                EditorGUILayout.LabelField(report != null && report.Items.Count == 0 ? "No authored content found." : "No assets match the current filters.", DeucarianEditorStyles.MutedLabel);
            });
        }

        private static void DrawSelectedDetail(GameContentAuthoringSurfaceContext context, GameContentLibraryReport report, GameContentLibraryV2State state)
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

        private static void DrawNoSelection(GameContentLibraryReport report)
        {
            DeucarianEditorCards.DrawInlineCard(() =>
            {
                DeucarianEditorStatusBadge.Draw("Library", DeucarianEditorStatus.Info, GUILayout.Width(72f));
                EditorGUILayout.LabelField(report != null && report.Items.Count == 0 ? "No authored content found." : "Select an authored asset.", DeucarianEditorStyles.MutedLabel);
            });
        }

        private static void DrawSelectedHeader(GameContentLibraryItem selected)
        {
            EditorGUILayout.LabelField(selected.DisplayName, HeaderStyle);
            EditorGUILayout.LabelField(string.IsNullOrWhiteSpace(selected.Id) ? "(missing id)" : selected.Id, DeucarianEditorStyles.MutedLabel);
            DeucarianEditorStatusChipRow.Draw(new[]
            {
                new DeucarianEditorStatusChip(GameContentLibraryV2Model.GetKindLabel(selected.Kind), DeucarianEditorStatus.Info),
                new DeucarianEditorStatusChip(GameContentLibraryV2Model.GetReadinessLabel(selected), GameContentLibraryV2Model.GetStatus(selected)),
                new DeucarianEditorStatusChip(selected.DirectReferences.Count.ToString(CultureInfo.InvariantCulture) + " deps", selected.DirectReferences.Count > 0 ? DeucarianEditorStatus.Success : DeucarianEditorStatus.Disabled),
                new DeucarianEditorStatusChip(selected.ReverseReferences.Count.ToString(CultureInfo.InvariantCulture) + " used by", selected.ReverseReferences.Count > 0 ? DeucarianEditorStatus.Success : DeucarianEditorStatus.Disabled)
            });
        }

        private static void DrawOverview(GameContentLibraryReport report, GameContentLibraryItem selected)
        {
            DeucarianEditorCards.DrawInlineCard(() =>
            {
                DrawSummaryRow("Type", GameContentLibraryV2Model.GetKindLabel(selected.Kind));
                DrawSummaryRow("Readiness", GameContentLibraryV2Model.GetReadinessLabel(selected));
                DrawSummaryRow("Summary", GameContentLibraryV2Model.BuildSelectedSummary(selected));
            });

            DrawReadiness(report, selected);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (DeucarianEditorButtons.Secondary("Ping", selected.Asset != null, GUILayout.Width(64f), GUILayout.Height(24f)))
                    EditorGUIUtility.PingObject(selected.Asset);
                if (DeucarianEditorButtons.Secondary("Open", selected.Asset != null, GUILayout.Width(64f), GUILayout.Height(24f)))
                    OpenAsset(selected.Asset);
                if (DeucarianEditorButtons.Secondary("Copy Summary", true, GUILayout.Width(112f), GUILayout.Height(24f)))
                    EditorGUIUtility.systemCopyBuffer = BuildSelectedMarkdown(report, selected);
            }
        }

        private static void DrawReferenceCards(
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
                    EditorGUILayout.LabelField(emptyText, DeucarianEditorStyles.MutedLabel);
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
                            OpenAsset(target.Asset);
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

        private static void DrawValidation(GameContentLibraryItem selected)
        {
            DeucarianEditorCards.DrawInlineCard(() =>
            {
                if (selected.Issues.Count == 0)
                {
                    DeucarianEditorStatusBadge.Draw("Ready", DeucarianEditorStatus.Success, GUILayout.Width(72f));
                    EditorGUILayout.LabelField("No blockers or warnings.", DeucarianEditorStyles.MutedLabel);
                    return;
                }

                DeucarianEditorStatus status = selected.ErrorCount > 0 ? DeucarianEditorStatus.Error : DeucarianEditorStatus.Warning;
                DeucarianEditorStatusBadge.Draw(selected.ValidationLabel, status, GUILayout.Width(104f));
                for (int i = 0; i < selected.Issues.Count; i++)
                    EditorGUILayout.LabelField(selected.Issues[i].Path + ": " + selected.Issues[i].Message, DeucarianEditorStyles.MutedLabel);
            });
        }

        private static void DrawReadiness(GameContentLibraryReport report, GameContentLibraryItem selected)
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
                        DrawSummaryRow("Assigned Attack", CountDirect(selected, GameContentLibraryKind.Attack).ToString(CultureInfo.InvariantCulture));
                        DrawSummaryRow("Content Sets Using It", CountReverse(selected, GameContentLibraryKind.ContentSet).ToString(CultureInfo.InvariantCulture));
                        break;
                    case GameContentLibraryKind.Attack:
                        DrawSummaryRow("Weapons Using It", CountReverse(selected, GameContentLibraryKind.Weapon).ToString(CultureInfo.InvariantCulture));
                        DrawSummaryRow("Presentation References", selected.DirectReferences.Count.ToString(CultureInfo.InvariantCulture));
                        break;
                    case GameContentLibraryKind.Wave:
                        DrawSummaryRow("Enemy Entries", CountDirect(selected, GameContentLibraryKind.Enemy).ToString(CultureInfo.InvariantCulture));
                        DrawSummaryRow("Content Sets Using It", CountReverse(selected, GameContentLibraryKind.ContentSet).ToString(CultureInfo.InvariantCulture));
                        break;
                    case GameContentLibraryKind.Enemy:
                        DrawSummaryRow("Waves Using It", CountReverse(selected, GameContentLibraryKind.Wave).ToString(CultureInfo.InvariantCulture));
                        DrawSummaryRow("Content Sets Using It", CountReverse(selected, GameContentLibraryKind.ContentSet).ToString(CultureInfo.InvariantCulture));
                        break;
                    case GameContentLibraryKind.Upgrade:
                        DrawSummaryRow("Target References", selected.DirectReferences.Count.ToString(CultureInfo.InvariantCulture));
                        DrawSummaryRow("Content Sets Using It", CountReverse(selected, GameContentLibraryKind.ContentSet).ToString(CultureInfo.InvariantCulture));
                        break;
                }
            });
        }

        private static void DrawContentPackReadiness(GameContentLibraryReport report, GameContentLibraryItem selected)
        {
            GameContentLibraryContentPackSummary summary = report.GetContentPackSummary(selected);
            if (summary == null)
                return;

            DeucarianEditorStatusBadge.Draw(summary.Ready ? "Ready" : "Needs Fixes", summary.Ready ? DeucarianEditorStatus.Success : DeucarianEditorStatus.Warning, GUILayout.Width(94f));
            DrawSummaryRow("Default / Included Sets", summary.ContentSetCount.ToString(CultureInfo.InvariantCulture));
            DrawSummaryRow("Weapons", summary.WeaponCount.ToString(CultureInfo.InvariantCulture));
            DrawSummaryRow("Waves", summary.WaveCount.ToString(CultureInfo.InvariantCulture));
            DrawSummaryRow("Enemies", summary.EnemyCount.ToString(CultureInfo.InvariantCulture));
            DrawSummaryRow("Upgrades", summary.UpgradeCount.ToString(CultureInfo.InvariantCulture));
        }

        private static void DrawContentSetReadiness(GameContentLibraryReport report, GameContentLibraryItem selected)
        {
            GameContentLibraryContentSetSummary summary = report.GetContentSetSummary(selected);
            if (summary == null)
                return;

            DeucarianEditorStatusBadge.Draw(summary.Ready ? "Ready" : "Needs Fixes", summary.Ready ? DeucarianEditorStatus.Success : DeucarianEditorStatus.Warning, GUILayout.Width(94f));
            DrawSummaryRow("Starting Weapon", CountDirect(selected, GameContentLibraryKind.Weapon) > 0 ? "Assigned" : "Missing");
            DrawSummaryRow("Weapons", summary.WeaponCount.ToString(CultureInfo.InvariantCulture));
            DrawSummaryRow("Waves", summary.WaveCount.ToString(CultureInfo.InvariantCulture));
            DrawSummaryRow("Enemies", summary.EnemyCount.ToString(CultureInfo.InvariantCulture));
            DrawSummaryRow("Upgrades", summary.UpgradeCount.ToString(CultureInfo.InvariantCulture));
        }

        private static void DrawAdvanced(GameContentLibraryItem selected, GameContentLibraryReport report)
        {
            DeucarianEditorDiagnosticsDrawer.Draw(
                DeucarianEditorAccordion.BuildStateKey("content-library-v2", "advanced", selected.Key),
                "Raw Details",
                () =>
                {
                    DrawSummaryRow("Path", selected.Path);
                    DrawSummaryRow("Folder", selected.Folder);
                    DrawRawReferenceList("Serialized Dependencies", selected.DirectReferences);
                    DrawRawReferenceList("Serialized Used By", selected.ReverseReferences);
                    if (DeucarianEditorButtons.Secondary("Copy Raw Report", true, GUILayout.Width(116f), GUILayout.Height(24f)))
                        EditorGUIUtility.systemCopyBuffer = BuildSelectedMarkdown(report, selected);
                },
                false);
        }

        private static void DrawRawReferenceList(string title, IReadOnlyList<GameContentLibraryReference> references)
        {
            EditorGUILayout.LabelField(title, DeucarianEditorStyles.SectionTitle);
            if (references == null || references.Count == 0)
            {
                EditorGUILayout.LabelField("None", DeucarianEditorStyles.MutedLabel);
                return;
            }

            for (int i = 0; i < references.Count; i++)
            {
                GameContentLibraryReference reference = references[i];
                if (reference == null || reference.Target == null)
                    continue;
                EditorGUILayout.LabelField(reference.Target.DisplayName + " - " + reference.PropertyPath, DeucarianEditorStyles.MutedLabel);
            }
        }

        private static void DrawGraphPreview(
            GameContentAuthoringSurfaceContext context,
            GameContentLibraryReport report,
            GameContentLibraryV2State state,
            string rootPath,
            Action<string> setRootPath,
            Action refresh)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("Graph / Readiness", DeucarianEditorStyles.SectionTitle);
                GUILayout.FlexibleSpace();
                int mode = DeucarianEditorSegmentedControl.Draw(state.DebugGraph ? 1 : 0, GraphModeLabels, GUILayout.Width(132f));
                state.DebugGraph = mode == 1;
            }

            DrawDashboard(GameContentLibraryV2Model.BuildDashboard(report));
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

        private static void DrawGraphEmptyState(
            GameContentLibraryReport report,
            string rootPath,
            Action<string> setRootPath,
            Action refresh)
        {
            DeucarianEditorCards.DrawInlineCard(() =>
            {
                DeucarianEditorStatusBadge.Draw(report != null && report.Items.Count == 0 ? "Empty" : "Select", DeucarianEditorStatus.Info, GUILayout.Width(72f));
                EditorGUILayout.LabelField(report != null && report.Items.Count == 0 ? "No authored content found." : "Select an asset to see relationships.", DeucarianEditorStyles.MutedLabel);
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

        private static void DrawGameGraph(GameContentAuthoringSurfaceContext context, GameContentLibraryV2State state, GameContentLibraryItem selected)
        {
            IReadOnlyList<GameContentLibraryV2GraphEdge> edges = GameContentLibraryV2Model.BuildGraphEdges(selected);
            DeucarianEditorCards.DrawInlineCard(() =>
            {
                DeucarianEditorStatusBadge.Draw(GameContentLibraryV2Model.GetKindLabel(selected.Kind), DeucarianEditorStatus.Info, GUILayout.Width(128f));
                EditorGUILayout.LabelField(selected.DisplayName, DeucarianEditorStyles.SectionTitle);
                EditorGUILayout.LabelField(GameContentLibraryV2Model.GetReadinessLabel(selected), DeucarianEditorStyles.MutedLabel);
            });

            if (edges.Count == 0)
            {
                DeucarianEditorCards.DrawInlineCard(() =>
                {
                    DeucarianEditorStatusBadge.Draw("No Edges", DeucarianEditorStatus.Disabled, GUILayout.Width(82f));
                    EditorGUILayout.LabelField("No authored dependency edges found.", DeucarianEditorStyles.MutedLabel);
                });
                return;
            }

            foreach (IGrouping<string, GameContentLibraryV2GraphEdge> group in edges.GroupBy(edge => edge.Relation))
            {
                EditorGUILayout.LabelField(group.Key, DeucarianEditorStyles.SectionTitle);
                foreach (GameContentLibraryV2GraphEdge edge in group)
                    DrawGraphEdge(context, state, edge);
            }
        }

        private static void DrawGraphEdge(GameContentAuthoringSurfaceContext context, GameContentLibraryV2State state, GameContentLibraryV2GraphEdge edge)
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

        private static void DrawDebugGraph(GameContentLibraryItem selected)
        {
            IReadOnlyList<GameContentLibraryV2GraphEdge> edges = GameContentLibraryV2Model.BuildGraphEdges(selected);
            DeucarianEditorCards.DrawInlineCard(() =>
            {
                DrawSummaryRow("Selected Key", selected.Key);
                DrawSummaryRow("Path", selected.Path);
                DrawSummaryRow("Direct", selected.DirectReferences.Count.ToString(CultureInfo.InvariantCulture));
                DrawSummaryRow("Used By", selected.ReverseReferences.Count.ToString(CultureInfo.InvariantCulture));
            });

            for (int i = 0; i < edges.Count; i++)
            {
                GameContentLibraryV2GraphEdge edge = edges[i];
                DeucarianEditorCards.DrawInlineCard(() =>
                {
                    DrawSummaryRow("Relation", edge.Relation);
                    DrawSummaryRow("From", edge.From.DisplayName + " | " + edge.From.Path);
                    DrawSummaryRow("To", edge.To.DisplayName + " | " + edge.To.Path);
                    DrawSummaryRow("Property", edge.Context);
                });
            }
        }

        private static int CountDirect(GameContentLibraryItem item, GameContentLibraryKind kind)
        {
            return item == null ? 0 : item.DirectReferences.Count(reference => reference.Target != null && reference.Target.Kind == kind);
        }

        private static int CountReverse(GameContentLibraryItem item, GameContentLibraryKind kind)
        {
            return item == null ? 0 : item.ReverseReferences.Count(reference => reference.Target != null && reference.Target.Kind == kind);
        }

        private static void DrawSummaryRow(string label, string value)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(label, DeucarianEditorStyles.MutedLabel, GUILayout.Width(128f));
                EditorGUILayout.LabelField(value ?? string.Empty, DeucarianEditorStyles.MutedLabel);
            }
        }

        private static string BuildSelectedMarkdown(GameContentLibraryReport report, GameContentLibraryItem selected)
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

        private static void OpenAsset(UnityEngine.Object asset)
        {
            if (asset == null)
                return;
            Selection.activeObject = asset;
            EditorGUIUtility.PingObject(asset);
        }

        private static GUIStyle headerStyle;

        private static GUIStyle HeaderStyle
        {
            get
            {
                if (headerStyle == null)
                {
                    headerStyle = new GUIStyle(EditorStyles.boldLabel)
                    {
                        fontSize = 14,
                        wordWrap = true
                    };
                    headerStyle.normal.textColor = DeucarianEditorTheme.Text;
                }

                return headerStyle;
            }
        }
    }
}
