using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;

namespace Deucarian.GameContentAuthoring.Editor
{
    public sealed class GameContentPackManifestEntry
    {
        internal GameContentPackManifestEntry(
            GameContentPackManifest manifest,
            string manifestPath,
            GameContentPackSourceKind sourceKind,
            GameContentPackSourceState sourceState,
            IReadOnlyList<GameContentAuthoringValidationIssue> issues)
        {
            Manifest = manifest;
            ManifestPath = manifestPath ?? string.Empty;
            SourceRoot = string.IsNullOrWhiteSpace(ManifestPath)
                ? string.Empty
                : (Path.GetDirectoryName(ManifestPath) ?? string.Empty).Replace("\\", "/");
            SourceKind = sourceKind;
            SourceState = sourceState;
            SetIssues(issues);
        }

        public GameContentPackManifest Manifest { get; }
        public string ManifestPath { get; }
        public string SourceRoot { get; }
        public string StableKey => Manifest == null ? string.Empty : Manifest.StableKey;
        public GameContentPackSourceKind SourceKind { get; }
        public GameContentPackSourceState SourceState { get; private set; }
        public GameContentAuthoringValidationResult Validation { get; private set; }

        internal void SetConflict(IReadOnlyList<string> sourceLocations)
        {
            SourceState = GameContentPackSourceState.DuplicateConflict;
            var issues = Validation.Issues.ToList();
            issues.Add(GameContentAuthoringValidationIssue.Error(
                ManifestPath,
                "Duplicate content-pack key '" + StableKey + "' appears at: " + string.Join(", ", sourceLocations) + "."));
            SetIssues(issues);
        }

        private void SetIssues(IReadOnlyList<GameContentAuthoringValidationIssue> issues)
        {
            Validation = new GameContentAuthoringValidationResult(issues ?? Array.Empty<GameContentAuthoringValidationIssue>());
        }
    }

    public sealed class GameContentPackDiscoveryReport
    {
        internal GameContentPackDiscoveryReport(IReadOnlyList<GameContentPackManifestEntry> entries)
        {
            Entries = entries ?? Array.Empty<GameContentPackManifestEntry>();
            Validation = new GameContentAuthoringValidationResult(
                Entries.SelectMany(entry => entry.Validation.Issues).ToArray());
        }

        public IReadOnlyList<GameContentPackManifestEntry> Entries { get; }
        public GameContentAuthoringValidationResult Validation { get; }
        public int ConflictCount => Entries.Count(entry => entry.SourceState == GameContentPackSourceState.DuplicateConflict);
    }

    public static class GameContentPackDiscovery
    {
        public static GameContentPackDiscoveryReport Discover(string providerId = null)
        {
            string normalizedProviderId = Normalize(providerId);
            string[] manifestGuids = AssetDatabase.FindAssets("t:GameContentPackManifest");
            var entries = new List<GameContentPackManifestEntry>(manifestGuids.Length);

            string[] paths = manifestGuids
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            for (int i = 0; i < paths.Length; i++)
            {
                GameContentPackManifest manifest = AssetDatabase.LoadAssetAtPath<GameContentPackManifest>(paths[i]);
                if (manifest == null) continue;
                entries.Add(CreateEntry(manifest, paths[i]));
            }

            foreach (IGrouping<string, GameContentPackManifestEntry> duplicate in entries
                         .Where(entry => entry.Manifest != null &&
                                         !string.IsNullOrWhiteSpace(entry.Manifest.OwningPackageId) &&
                                         !string.IsNullOrWhiteSpace(entry.Manifest.PackId))
                         .GroupBy(entry => entry.StableKey, StringComparer.OrdinalIgnoreCase)
                         .Where(group => group.Count() > 1))
            {
                string[] locations = duplicate
                    .Select(entry => entry.ManifestPath)
                    .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                foreach (GameContentPackManifestEntry entry in duplicate) entry.SetConflict(locations);
            }

            GameContentPackManifestEntry[] filtered = entries
                .Where(entry => string.IsNullOrWhiteSpace(normalizedProviderId) ||
                                string.Equals(entry.Manifest.ProviderId, normalizedProviderId, StringComparison.OrdinalIgnoreCase))
                .OrderBy(entry => entry.Manifest.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(entry => entry.StableKey, StringComparer.OrdinalIgnoreCase)
                .ThenBy(entry => entry.ManifestPath, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            return new GameContentPackDiscoveryReport(filtered);
        }

        public static GameContentPackSourceKind ResolveSourceKind(string assetPath)
        {
            string normalized = Normalize(assetPath).Replace("\\", "/");
            if (normalized.StartsWith("Assets/Samples/", StringComparison.OrdinalIgnoreCase))
                return GameContentPackSourceKind.ImportedSample;
            if (normalized.StartsWith("Packages/", StringComparison.OrdinalIgnoreCase))
                return GameContentPackSourceKind.Package;
            if (string.Equals(normalized, "Assets", StringComparison.OrdinalIgnoreCase) ||
                normalized.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
                return GameContentPackSourceKind.Project;
            return GameContentPackSourceKind.Unknown;
        }

        private static GameContentPackManifestEntry CreateEntry(GameContentPackManifest manifest, string path)
        {
            var issues = new List<GameContentAuthoringValidationIssue>();
            bool invalidMetadata = false;
            bool missingSource = false;

            invalidMetadata |= RequireText(manifest.PackId, "Pack ID", path, issues);
            invalidMetadata |= RequireText(manifest.OwningPackageId, "Owning Package ID", path, issues);
            invalidMetadata |= RequireText(manifest.ProviderId, "Provider ID", path, issues);
            invalidMetadata |= RequireText(manifest.DisplayName, "Display Name", path, issues);
            invalidMetadata |= RequireText(manifest.SchemaVersion, "Schema Version", path, issues);

            if (manifest.PlayableScene == null)
            {
                missingSource = true;
                issues.Add(GameContentAuthoringValidationIssue.Error(path + ".PlayableScene", "Content-pack manifest is missing its playable scene."));
            }

            if (manifest.ContentSources.Count == 0)
            {
                missingSource = true;
                issues.Add(GameContentAuthoringValidationIssue.Error(path + ".ContentSources", "Content-pack manifest has no authored content sources."));
            }

            var sourceIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < manifest.ContentSources.Count; i++)
            {
                GameContentPackSourceReference source = manifest.ContentSources[i];
                string sourcePath = path + ".ContentSources[" + i + "]";
                if (source == null)
                {
                    missingSource = true;
                    issues.Add(GameContentAuthoringValidationIssue.Error(sourcePath, "Content source entry is missing."));
                    continue;
                }

                if (string.IsNullOrWhiteSpace(source.SourceId))
                {
                    invalidMetadata = true;
                    issues.Add(GameContentAuthoringValidationIssue.Error(sourcePath + ".SourceId", "Content source ID is required."));
                }
                else if (!sourceIds.Add(source.SourceId))
                {
                    invalidMetadata = true;
                    issues.Add(GameContentAuthoringValidationIssue.Error(sourcePath + ".SourceId", "Duplicate content source ID '" + source.SourceId + "'."));
                }

                if (source.Required && source.TextAsset == null)
                {
                    missingSource = true;
                    issues.Add(GameContentAuthoringValidationIssue.Error(sourcePath + ".TextAsset", "Required content source '" + source.SourceId + "' is missing its TextAsset."));
                }
            }

            bool providerAvailable = GameContentAuthoringProviderRegistry.Providers.Any(provider =>
                provider is IGameContentPackProvider &&
                string.Equals(provider.ProviderId, manifest.ProviderId, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(manifest.ProviderId) && !providerAvailable)
            {
                issues.Add(GameContentAuthoringValidationIssue.Error(
                    path + ".ProviderId",
                    "Content-pack provider '" + manifest.ProviderId + "' is not registered."));
            }

            GameContentPackSourceState state = invalidMetadata
                ? GameContentPackSourceState.InvalidManifest
                : missingSource
                    ? GameContentPackSourceState.MissingSource
                    : !providerAvailable
                        ? GameContentPackSourceState.ProviderUnavailable
                        : GameContentPackSourceState.Available;
            return new GameContentPackManifestEntry(manifest, path, ResolveSourceKind(path), state, issues);
        }

        private static bool RequireText(
            string value,
            string label,
            string path,
            ICollection<GameContentAuthoringValidationIssue> issues)
        {
            if (!string.IsNullOrWhiteSpace(value)) return false;
            issues.Add(GameContentAuthoringValidationIssue.Error(path + "." + label.Replace(" ", string.Empty), label + " is required."));
            return true;
        }

        private static string Normalize(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
        }
    }
}
