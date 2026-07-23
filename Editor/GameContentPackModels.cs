using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Deucarian.GameContentAuthoring.Editor
{
    public enum GameContentPackSourceKind
    {
        Unknown = 0,
        Project = 1,
        ImportedSample = 2,
        Package = 3
    }

    public enum GameContentPackSourceState
    {
        Available = 0,
        MissingSource = 1,
        InvalidManifest = 2,
        DuplicateConflict = 3,
        ProviderUnavailable = 4,
        ValidationFailed = 5,
        SampleNotImported = 6
    }

    public enum GameContentRecordValidationFilter
    {
        All = 0,
        Ready = 1,
        Warnings = 2,
        Errors = 3,
        BrokenReferences = 4
    }

    public enum GameContentRecordSortMode
    {
        SourceOrder = 0,
        DisplayName = 1,
        Category = 2,
        Status = 3
    }

    public enum GameContentActionKind
    {
        Custom = 0,
        OpenScene = 1,
        Play = 2,
        Validate = 3,
        Browse = 4,
        RevealSource = 5,
        OpenPackageInstaller = 6
    }

    public interface IGameContentPackProvider
    {
        IReadOnlyList<GameContentPackDescriptor> GetContentPacks();
        IReadOnlyList<GameContentRecordDescriptor> GetRecords(string packId);
        GameContentAuthoringValidationResult ValidatePack(string packId);
        GameContentActionResult ExecuteAction(string packId, string actionId);
    }

    public sealed class GameContentPackDescriptor
    {
        public GameContentPackDescriptor(
            string packId,
            string owningPackageId,
            string providerId,
            string displayName,
            string description,
            string schemaVersion,
            IEnumerable<string> tags,
            GameContentPackSourceKind sourceKind,
            GameContentPackSourceState sourceState,
            string sourcePath,
            GameContentPackManifest manifest,
            SceneAsset playableScene,
            UnityEngine.Object preview,
            UnityEngine.Object icon,
            UnityEngine.Object defaultTheme,
            IEnumerable<GameContentCategoryDescriptor> categories,
            IEnumerable<GameContentActionDescriptor> actions,
            GameContentAuthoringValidationResult validation,
            int recordCount = 0,
            GameContentPackAccessDescriptor access = null,
            IEnumerable<GameContentMetadataDescriptor> metadata = null)
        {
            PackId = Normalize(packId);
            OwningPackageId = Normalize(owningPackageId);
            ProviderId = Normalize(providerId);
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? PackId : displayName.Trim();
            Description = Normalize(description);
            SchemaVersion = Normalize(schemaVersion);
            Tags = Copy(tags);
            SourceKind = sourceKind;
            SourceState = sourceState;
            SourcePath = Normalize(sourcePath).Replace("\\", "/");
            Manifest = manifest;
            PlayableScene = playableScene;
            Preview = preview;
            Icon = icon;
            DefaultTheme = defaultTheme;
            Categories = Copy(categories);
            Actions = Copy(actions);
            Validation = validation ?? GameContentAuthoringValidationResult.Valid;
            RecordCount = Math.Max(0, recordCount);
            Access = access ?? GameContentPackAccessDescriptor.ReadOnlyJson;
            Metadata = Copy(metadata);
        }

        public string StableKey => BuildStableKey(OwningPackageId, PackId);
        public string PackId { get; }
        public string OwningPackageId { get; }
        public string ProviderId { get; }
        public string DisplayName { get; }
        public string Description { get; }
        public string SchemaVersion { get; }
        public IReadOnlyList<string> Tags { get; }
        public GameContentPackSourceKind SourceKind { get; }
        public GameContentPackSourceState SourceState { get; }
        public string SourcePath { get; }
        public GameContentPackManifest Manifest { get; }
        public SceneAsset PlayableScene { get; }
        public UnityEngine.Object Preview { get; }
        public UnityEngine.Object Icon { get; }
        public UnityEngine.Object DefaultTheme { get; }
        public IReadOnlyList<GameContentCategoryDescriptor> Categories { get; }
        public IReadOnlyList<GameContentActionDescriptor> Actions { get; }
        public GameContentAuthoringValidationResult Validation { get; }
        public int RecordCount { get; }
        public GameContentPackAccessDescriptor Access { get; }
        public IReadOnlyList<GameContentMetadataDescriptor> Metadata { get; }
        public bool IsAvailable => SourceState == GameContentPackSourceState.Available && Validation.ErrorCount == 0;

        public static string BuildStableKey(string owningPackageId, string packId)
        {
            return Normalize(owningPackageId).ToLowerInvariant() + "::" + Normalize(packId).ToLowerInvariant();
        }

        private static string Normalize(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
        }

        private static IReadOnlyList<T> Copy<T>(IEnumerable<T> values) where T : class
        {
            return values == null ? Array.Empty<T>() : values.Where(value => value != null).ToArray();
        }
    }

    public sealed class GameContentCategoryDescriptor
    {
        public GameContentCategoryDescriptor(
            string categoryId,
            string displayName,
            string description,
            string iconOrStyleKey,
            int order,
            int recordCount)
        {
            CategoryId = Normalize(categoryId);
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? CategoryId : displayName.Trim();
            Description = Normalize(description);
            IconOrStyleKey = Normalize(iconOrStyleKey);
            Order = order;
            RecordCount = Math.Max(0, recordCount);
        }

        public string CategoryId { get; }
        public string DisplayName { get; }
        public string Description { get; }
        public string IconOrStyleKey { get; }
        public int Order { get; }
        public int RecordCount { get; }

        private static string Normalize(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
        }
    }

    public sealed class GameContentRecordDescriptor
    {
        public GameContentRecordDescriptor(
            string packScopedId,
            string sourceRecordId,
            string categoryId,
            IEnumerable<string> categoryIds,
            string displayName,
            string description,
            string summary,
            IEnumerable<GameContentMetadataDescriptor> playerFacingMetadata,
            UnityEngine.Object sourceAsset,
            string sourcePath,
            string sourceLocator,
            IEnumerable<GameContentRecordReferenceDescriptor> outboundReferences,
            IEnumerable<GameContentRecordReferenceDescriptor> inboundReferences,
            GameContentAuthoringValidationResult validation,
            int order,
            UnityEngine.Object preview,
            string iconToken,
            GameContentRecordKey canonicalKey = null,
            IEnumerable<GameContentRecordCapability> capabilities = null)
        {
            PackScopedId = Normalize(packScopedId);
            SourceRecordId = Normalize(sourceRecordId);
            CategoryId = Normalize(categoryId);
            CategoryIds = BuildCategoryIds(CategoryId, categoryIds);
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? SourceRecordId : displayName.Trim();
            Description = Normalize(description);
            Summary = Normalize(summary);
            PlayerFacingMetadata = Copy(playerFacingMetadata);
            SourceAsset = sourceAsset;
            SourcePath = Normalize(sourcePath).Replace("\\", "/");
            SourceLocator = Normalize(sourceLocator);
            OutboundReferences = Copy(outboundReferences);
            InboundReferences = Copy(inboundReferences);
            Validation = validation ?? GameContentAuthoringValidationResult.Valid;
            Order = order;
            Preview = preview;
            IconToken = Normalize(iconToken);
            CanonicalKey = canonicalKey ?? GameContentRecordKey.FromLegacy(PackScopedId, SourceRecordId);
            Capabilities = BuildCapabilities(CategoryIds, capabilities);
        }

        public string PackScopedId { get; }
        public string SourceRecordId { get; }
        public string CategoryId { get; }
        public IReadOnlyList<string> CategoryIds { get; }
        public string DisplayName { get; }
        public string Description { get; }
        public string Summary { get; }
        public IReadOnlyList<GameContentMetadataDescriptor> PlayerFacingMetadata { get; }
        public UnityEngine.Object SourceAsset { get; }
        public string SourcePath { get; }
        public string SourceLocator { get; }
        public IReadOnlyList<GameContentRecordReferenceDescriptor> OutboundReferences { get; }
        public IReadOnlyList<GameContentRecordReferenceDescriptor> InboundReferences { get; }
        public GameContentAuthoringValidationResult Validation { get; }
        public int Order { get; }
        public UnityEngine.Object Preview { get; }
        public string IconToken { get; }
        public GameContentRecordKey CanonicalKey { get; }
        public IReadOnlyList<GameContentRecordCapability> Capabilities { get; }
        public bool HasBrokenReferences => OutboundReferences.Any(reference => !reference.Valid);

        public bool IsInCategory(string categoryId)
        {
            if (string.IsNullOrWhiteSpace(categoryId)) return true;
            return CategoryIds.Any(value => string.Equals(value, categoryId, StringComparison.OrdinalIgnoreCase));
        }

        public bool HasCapability(GameContentRecordCapability capability)
        {
            return capability.IsValid && Capabilities.Contains(capability);
        }

        private static IReadOnlyList<GameContentRecordCapability> BuildCapabilities(
            IReadOnlyList<string> categoryIds,
            IEnumerable<GameContentRecordCapability> explicitCapabilities)
        {
            var values = explicitCapabilities == null
                ? new List<GameContentRecordCapability>()
                : explicitCapabilities.Where(value => value.IsValid).Distinct().ToList();
            if (values.Count > 0) return values.ToArray();

            foreach (string category in categoryIds ?? Array.Empty<string>())
            {
                switch ((category ?? string.Empty).Trim().ToLowerInvariant())
                {
                    case "attack":
                    case "attacks":
                        AddCapability(values, GameContentRecordCapabilities.Attack);
                        break;
                    case "enemy":
                    case "enemies":
                        AddCapability(values, GameContentRecordCapabilities.Enemy);
                        break;
                    case "wave":
                    case "waves":
                        AddCapability(values, GameContentRecordCapabilities.Wave);
                        AddCapability(values, GameContentRecordCapabilities.Encounter);
                        break;
                    case "run-profiles":
                    case "waves-milestones":
                        AddCapability(values, GameContentRecordCapabilities.Encounter);
                        AddCapability(values, GameContentRecordCapabilities.RunProfile);
                        break;
                    case "weapon":
                    case "weapons":
                        AddCapability(values, GameContentRecordCapabilities.Weapon);
                        break;
                    case "projectile":
                    case "projectiles":
                        AddCapability(values, GameContentRecordCapabilities.Projectile);
                        break;
                    case "upgrade":
                    case "upgrades":
                        AddCapability(values, GameContentRecordCapabilities.Upgrade);
                        break;
                    case "passives":
                        AddCapability(values, GameContentRecordCapabilities.Upgrade);
                        AddCapability(values, GameContentRecordCapabilities.Passive);
                        break;
                    case "pickup-magnet":
                        AddCapability(values, GameContentRecordCapabilities.Upgrade);
                        AddCapability(values, GameContentRecordCapabilities.PickupMagnet);
                        break;
                    case "mutations":
                        AddCapability(values, GameContentRecordCapabilities.Upgrade);
                        AddCapability(values, GameContentRecordCapabilities.Mutation);
                        break;
                    case "evolutions":
                        AddCapability(values, GameContentRecordCapabilities.Upgrade);
                        AddCapability(values, GameContentRecordCapabilities.Evolution);
                        break;
                    case "meta-upgrades":
                        AddCapability(values, GameContentRecordCapabilities.Upgrade);
                        AddCapability(values, GameContentRecordCapabilities.MetaUpgrade);
                        break;
                    case "elites":
                        AddCapability(values, GameContentRecordCapabilities.Elite);
                        AddCapability(values, GameContentRecordCapabilities.MajorThreat);
                        break;
                    case "minibosses":
                        AddCapability(values, GameContentRecordCapabilities.Miniboss);
                        AddCapability(values, GameContentRecordCapabilities.MajorThreat);
                        break;
                    case "bosses":
                        AddCapability(values, GameContentRecordCapabilities.Boss);
                        AddCapability(values, GameContentRecordCapabilities.MajorThreat);
                        break;
                    case "rewards":
                        AddCapability(values, GameContentRecordCapabilities.Reward);
                        break;
                    case "themes":
                        AddCapability(values, GameContentRecordCapabilities.Theme);
                        break;
                }
            }

            return values.ToArray();
        }

        private static void AddCapability(
            ICollection<GameContentRecordCapability> values,
            GameContentRecordCapability capability)
        {
            if (!values.Contains(capability)) values.Add(capability);
        }

        private static IReadOnlyList<string> BuildCategoryIds(string primary, IEnumerable<string> categoryIds)
        {
            var values = new List<string>();
            if (!string.IsNullOrWhiteSpace(primary)) values.Add(primary);
            if (categoryIds != null)
            {
                foreach (string value in categoryIds)
                {
                    string normalized = Normalize(value);
                    if (!string.IsNullOrWhiteSpace(normalized) && values.All(existing => !string.Equals(existing, normalized, StringComparison.OrdinalIgnoreCase)))
                        values.Add(normalized);
                }
            }

            return values.ToArray();
        }

        private static string Normalize(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
        }

        private static IReadOnlyList<T> Copy<T>(IEnumerable<T> values) where T : class
        {
            return values == null ? Array.Empty<T>() : values.Where(value => value != null).ToArray();
        }
    }

    public sealed class GameContentMetadataDescriptor
    {
        public GameContentMetadataDescriptor(string label, string value)
        {
            Label = string.IsNullOrWhiteSpace(label) ? "Value" : label.Trim();
            Value = value ?? string.Empty;
        }

        public string Label { get; }
        public string Value { get; }
    }

    public sealed class GameContentRecordReferenceDescriptor
    {
        public GameContentRecordReferenceDescriptor(
            string targetRecordId,
            string targetCategoryId,
            string targetPackId,
            string relationshipLabel,
            bool required,
            bool valid,
            string targetOwningPackageId = null,
            GameContentRecordKey targetRecordKey = null)
        {
            TargetRecordId = Normalize(targetRecordId);
            TargetCategoryId = Normalize(targetCategoryId);
            TargetPackId = Normalize(targetPackId);
            RelationshipLabel = Normalize(relationshipLabel);
            Required = required;
            Valid = valid;
            TargetOwningPackageId = Normalize(targetOwningPackageId);
            TargetRecordKey = targetRecordKey;
        }

        public string TargetRecordId { get; }
        public string TargetCategoryId { get; }
        public string TargetPackId { get; }
        public string RelationshipLabel { get; }
        public bool Required { get; }
        public bool Valid { get; }
        public string TargetOwningPackageId { get; }
        public GameContentRecordKey TargetRecordKey { get; }

        private static string Normalize(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
        }
    }

    public sealed class GameContentActionDescriptor
    {
        public GameContentActionDescriptor(
            string actionId,
            string displayName,
            string description,
            bool enabled,
            string disabledReason,
            GameContentActionKind actionKind,
            string dispatchToken = null)
        {
            ActionId = Normalize(actionId);
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? ActionId : displayName.Trim();
            Description = Normalize(description);
            Enabled = enabled;
            DisabledReason = enabled ? string.Empty : Normalize(disabledReason);
            ActionKind = actionKind;
            DispatchToken = string.IsNullOrWhiteSpace(dispatchToken) ? ActionId : dispatchToken.Trim();
        }

        public string ActionId { get; }
        public string DisplayName { get; }
        public string Description { get; }
        public bool Enabled { get; }
        public string DisabledReason { get; }
        public GameContentActionKind ActionKind { get; }
        public string DispatchToken { get; }

        private static string Normalize(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
        }
    }

    public sealed class GameContentActionResult
    {
        public GameContentActionResult(
            bool succeeded,
            string message,
            GameContentAuthoringValidationResult validation = null,
            IReadOnlyList<string> details = null)
        {
            Succeeded = succeeded;
            Message = message ?? string.Empty;
            Validation = validation;
            Details = details == null ? Array.Empty<string>() : details.Where(value => !string.IsNullOrWhiteSpace(value)).ToArray();
        }

        public bool Succeeded { get; }
        public string Message { get; }
        public GameContentAuthoringValidationResult Validation { get; }
        public IReadOnlyList<string> Details { get; }

        public static GameContentActionResult Success(string message, GameContentAuthoringValidationResult validation = null)
        {
            return new GameContentActionResult(true, message, validation);
        }

        public static GameContentActionResult Failure(string message, GameContentAuthoringValidationResult validation = null)
        {
            return new GameContentActionResult(false, message, validation);
        }
    }

}
