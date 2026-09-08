using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Deucarian.Editor;
using UnityEditor;
using UnityEngine;

namespace Deucarian.GameContentAuthoring.Editor
{
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
}
