using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;

namespace Deucarian.GameContentAuthoring.Editor
{
    internal static class GameContentProjectPackProjection
    {
        public const string OwningPackageId = "com.deucarian.game-content-authoring.project";
        public const string PackId = "project-content";

        public static GameContentPackDescriptor BuildPack(
            string providerId,
            GameContentLibraryReport report)
        {
            IReadOnlyList<GameContentRecordDescriptor> records = BuildRecords(report);
            return new GameContentPackDescriptor(
                PackId,
                OwningPackageId,
                providerId,
                "Project Content",
                "Synthetic writable pack for existing ScriptableObject content under Assets/GameContent.",
                "1",
                new[] { "project", "scriptable-object", "writable" },
                GameContentPackSourceKind.Project,
                GameContentPackSourceState.Available,
                GameContentLibraryProvider.DefaultRoot,
                null,
                null,
                null,
                null,
                null,
                BuildCategories(records),
                new[]
                {
                    new GameContentActionDescriptor(
                        "validate-project-content",
                        "Validate",
                        "Rescan and validate Project Content.",
                        true,
                        string.Empty,
                        GameContentActionKind.Validate),
                    new GameContentActionDescriptor(
                        "reveal-project-content",
                        "Reveal Source",
                        "Reveal Assets/GameContent in the Project window.",
                        AssetDatabase.IsValidFolder(GameContentLibraryProvider.DefaultRoot),
                        "Assets/GameContent does not exist yet.",
                        GameContentActionKind.RevealSource)
                },
                report == null ? GameContentAuthoringValidationResult.Valid : report.ToValidationResult(),
                records.Count,
                GameContentPackAccessDescriptor.WritableProjectContent);
        }

        public static IReadOnlyList<GameContentRecordDescriptor> BuildRecords(GameContentLibraryReport report)
        {
            if (report == null || report.Items.Count == 0) return Array.Empty<GameContentRecordDescriptor>();

            var keys = report.Items.ToDictionary(
                item => item,
                item => new GameContentRecordKey(
                    OwningPackageId,
                    PackId,
                    GetSourceRecordId(item),
                    GetSourceId(item.Kind),
                    item.Path));
            var result = new List<GameContentRecordDescriptor>(report.Items.Count);
            for (int i = 0; i < report.Items.Count; i++)
            {
                GameContentLibraryItem item = report.Items[i];
                GameContentRecordKey key = keys[item];
                GameContentRecordCapability[] capabilities = GetCapabilities(item.Kind);
                result.Add(new GameContentRecordDescriptor(
                    PackId + "::" + key.SourceId + "::" + key.SourceRecordId,
                    key.SourceRecordId,
                    GetCategoryId(item.Kind),
                    null,
                    item.DisplayName,
                    "Project-authored " + item.Category + " ScriptableObject.",
                    item.ValidationLabel,
                    new[]
                    {
                        new GameContentMetadataDescriptor("Type", item.Category),
                        new GameContentMetadataDescriptor("Path", item.Path)
                    },
                    item.Asset,
                    item.Path,
                    item.Path,
                    BuildReferences(item.DirectReferences, keys),
                    BuildReferences(item.ReverseReferences, keys),
                    new GameContentAuthoringValidationResult(item.Issues.Select(issue =>
                        new GameContentAuthoringValidationIssue(issue.Severity, issue.Path, issue.Message)).ToArray()),
                    i,
                    item.Asset,
                    GetCategoryId(item.Kind),
                    key,
                    capabilities));
            }

            return result;
        }

        private static IReadOnlyList<GameContentRecordReferenceDescriptor> BuildReferences(
            IEnumerable<GameContentLibraryReference> references,
            IReadOnlyDictionary<GameContentLibraryItem, GameContentRecordKey> keys)
        {
            if (references == null) return Array.Empty<GameContentRecordReferenceDescriptor>();
            return references
                .Where(reference => reference != null && reference.Target != null && keys.ContainsKey(reference.Target))
                .Select(reference =>
                {
                    GameContentRecordKey target = keys[reference.Target];
                    return new GameContentRecordReferenceDescriptor(
                        target.SourceRecordId,
                        GetCategoryId(reference.Target.Kind),
                        target.PackId,
                        string.IsNullOrWhiteSpace(reference.PropertyPath) ? "references" : reference.PropertyPath,
                        false,
                        true,
                        target.OwningPackageId,
                        target);
                })
                .ToArray();
        }

        private static IReadOnlyList<GameContentCategoryDescriptor> BuildCategories(
            IReadOnlyList<GameContentRecordDescriptor> records)
        {
            return records
                .GroupBy(record => record.CategoryId, StringComparer.OrdinalIgnoreCase)
                .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
                .Select((group, order) => new GameContentCategoryDescriptor(
                    group.Key,
                    group.First().CategoryId,
                    "Project Content " + group.Key + " records.",
                    group.Key,
                    order,
                    group.Count()))
                .ToArray();
        }

        private static string GetSourceRecordId(GameContentLibraryItem item)
        {
            if (item == null) return string.Empty;
            if (!string.IsNullOrWhiteSpace(item.Id)) return item.Id;
            string guid = string.IsNullOrWhiteSpace(item.Path) ? string.Empty : AssetDatabase.AssetPathToGUID(item.Path);
            return string.IsNullOrWhiteSpace(guid) ? item.Key : guid;
        }

        private static string GetSourceId(GameContentLibraryKind kind)
        {
            return GetCategoryId(kind);
        }

        private static string GetCategoryId(GameContentLibraryKind kind)
        {
            switch (kind)
            {
                case GameContentLibraryKind.Attack: return "attacks";
                case GameContentLibraryKind.Enemy: return "enemies";
                case GameContentLibraryKind.Wave: return "waves";
                case GameContentLibraryKind.Weapon: return "weapons";
                case GameContentLibraryKind.Upgrade: return "upgrades";
                case GameContentLibraryKind.ContentSet: return "content-sets";
                case GameContentLibraryKind.ContentPack: return "content-packs";
                default: return "content";
            }
        }

        private static GameContentRecordCapability[] GetCapabilities(GameContentLibraryKind kind)
        {
            switch (kind)
            {
                case GameContentLibraryKind.Attack:
                    return new[] { GameContentRecordCapabilities.Attack };
                case GameContentLibraryKind.Enemy:
                    return new[] { GameContentRecordCapabilities.Enemy };
                case GameContentLibraryKind.Wave:
                    return new[] { GameContentRecordCapabilities.Encounter, GameContentRecordCapabilities.Wave };
                case GameContentLibraryKind.Weapon:
                    return new[] { GameContentRecordCapabilities.Weapon };
                case GameContentLibraryKind.Upgrade:
                    return new[] { GameContentRecordCapabilities.Upgrade };
                default:
                    return Array.Empty<GameContentRecordCapability>();
            }
        }
    }
}
