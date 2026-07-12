using System;
using System.Collections.Generic;
using System.Linq;

namespace Deucarian.GameContentAuthoring.Editor
{
    [Flags]
    public enum GameContentPackBackendCapability
    {
        None = 0,
        Read = 1 << 0,
        Validate = 1 << 1,
        RevealSource = 1 << 2,
        EditExisting = 1 << 3,
        Create = 1 << 4,
        Duplicate = 1 << 5,
        Delete = 1 << 6,
        ClonePack = 1 << 7
    }

    public sealed class GameContentPackAccessDescriptor
    {
        public GameContentPackAccessDescriptor(
            GameContentPackBackendCapability capabilities,
            string persistenceLabel,
            string disabledReason = null)
        {
            Capabilities = capabilities;
            PersistenceLabel = string.IsNullOrWhiteSpace(persistenceLabel) ? "Unknown source" : persistenceLabel.Trim();
            DisabledReason = disabledReason ?? string.Empty;
        }

        public GameContentPackBackendCapability Capabilities { get; }
        public string PersistenceLabel { get; }
        public string DisabledReason { get; }
        public bool CanRead => Has(GameContentPackBackendCapability.Read);
        public bool CanValidate => Has(GameContentPackBackendCapability.Validate);
        public bool CanRevealSource => Has(GameContentPackBackendCapability.RevealSource);
        public bool CanEditExisting => Has(GameContentPackBackendCapability.EditExisting);
        public bool CanCreate => Has(GameContentPackBackendCapability.Create);
        public bool CanDuplicate => Has(GameContentPackBackendCapability.Duplicate);
        public bool CanDelete => Has(GameContentPackBackendCapability.Delete);
        public bool CanClonePack => Has(GameContentPackBackendCapability.ClonePack);
        public bool IsWritable => CanEditExisting || CanCreate || CanDuplicate || CanDelete || CanClonePack;

        public bool Has(GameContentPackBackendCapability capability)
        {
            return (Capabilities & capability) == capability;
        }

        public GameContentPackAccessDescriptor AsReadOnly(string reason)
        {
            GameContentPackBackendCapability readOnly = Capabilities &
                (GameContentPackBackendCapability.Read |
                 GameContentPackBackendCapability.Validate |
                 GameContentPackBackendCapability.RevealSource);
            return new GameContentPackAccessDescriptor(readOnly, PersistenceLabel, reason);
        }

        public static GameContentPackAccessDescriptor ReadOnlyJson { get; } =
            new GameContentPackAccessDescriptor(
                GameContentPackBackendCapability.Read |
                GameContentPackBackendCapability.Validate |
                GameContentPackBackendCapability.RevealSource,
                "Read-only JSON source",
                "JSON editing is deferred to the safe-editing milestone.");

        public static GameContentPackAccessDescriptor WritableProjectContent { get; } =
            new GameContentPackAccessDescriptor(
                GameContentPackBackendCapability.Read |
                GameContentPackBackendCapability.Validate |
                GameContentPackBackendCapability.RevealSource |
                GameContentPackBackendCapability.EditExisting |
                GameContentPackBackendCapability.Create,
                "Writable ScriptableObject project content");

        public static GameContentPackAccessDescriptor ReadOnlyAggregate { get; } =
            new GameContentPackAccessDescriptor(
                GameContentPackBackendCapability.Read |
                GameContentPackBackendCapability.RevealSource,
                "Read-only cross-pack view",
                "All Packs is a read-only browsing context.");
    }

    public sealed class GameContentPackCatalogEntry
    {
        internal GameContentPackCatalogEntry(
            GameContentPackDescriptor pack,
            IGameContentPackProvider provider,
            IReadOnlyList<GameContentRecordDescriptor> records,
            bool duplicateStableKey)
        {
            Pack = pack;
            Provider = provider;
            Records = records ?? Array.Empty<GameContentRecordDescriptor>();
            DuplicateStableKey = duplicateStableKey;
        }

        public GameContentPackDescriptor Pack { get; }
        public IGameContentPackProvider Provider { get; }
        public IReadOnlyList<GameContentRecordDescriptor> Records { get; }
        public bool DuplicateStableKey { get; }
        public string StableKey => Pack == null ? string.Empty : Pack.StableKey;
        public bool IsConflict => DuplicateStableKey || Pack == null || Pack.SourceState == GameContentPackSourceState.DuplicateConflict;

        public GameContentPackAccessDescriptor EffectiveAccess
        {
            get
            {
                GameContentPackAccessDescriptor access = Pack == null
                    ? GameContentPackAccessDescriptor.ReadOnlyAggregate
                    : Pack.Access;
                if (!IsConflict && Pack != null && Pack.SourceState == GameContentPackSourceState.Available)
                    return access;

                return access.AsReadOnly(IsConflict
                    ? "Duplicate or conflicted packs cannot be edited."
                    : "This pack is unavailable until its source and validation issues are resolved.");
            }
        }
    }

    public sealed class GameContentPackCatalog
    {
        private sealed class DiscoveredPack
        {
            public DiscoveredPack(
                GameContentPackDescriptor pack,
                IGameContentPackProvider provider,
                IReadOnlyList<GameContentRecordDescriptor> records)
            {
                Pack = pack;
                Provider = provider;
                Records = records ?? Array.Empty<GameContentRecordDescriptor>();
            }

            public GameContentPackDescriptor Pack { get; set; }
            public IGameContentPackProvider Provider { get; }
            public IReadOnlyList<GameContentRecordDescriptor> Records { get; set; }
        }

        private sealed class SourceClaimAssignment
        {
            public SourceClaimAssignment(GameContentPackDescriptor pack, GameContentSourceClaim claim)
            {
                Pack = pack;
                Claim = claim;
            }

            public GameContentPackDescriptor Pack { get; }
            public GameContentSourceClaim Claim { get; }
        }

        private GameContentPackCatalog(
            IReadOnlyList<GameContentPackCatalogEntry> entries,
            IReadOnlyList<GameContentSourceClaimConflict> sourceClaimConflicts,
            IReadOnlyList<GameContentSourceIdentity> claimedSourceIdentities)
        {
            Entries = entries ?? Array.Empty<GameContentPackCatalogEntry>();
            AllRecords = Entries.SelectMany(entry => entry.Records).ToArray();
            SourceClaimConflicts = sourceClaimConflicts ?? Array.Empty<GameContentSourceClaimConflict>();
            ClaimedSourceIdentities = claimedSourceIdentities ?? Array.Empty<GameContentSourceIdentity>();
        }

        public IReadOnlyList<GameContentPackCatalogEntry> Entries { get; }
        public IReadOnlyList<GameContentRecordDescriptor> AllRecords { get; }
        public IReadOnlyList<GameContentSourceClaimConflict> SourceClaimConflicts { get; }
        public IReadOnlyList<GameContentSourceIdentity> ClaimedSourceIdentities { get; }

        public static GameContentPackCatalog Build(IEnumerable<IGameContentAuthoringProvider> providers)
        {
            var discovered = new List<DiscoveredPack>();
            if (providers != null)
            {
                foreach (IGameContentAuthoringProvider authoringProvider in providers.Where(value => value != null))
                {
                    if (!(authoringProvider is IGameContentPackProvider packProvider)) continue;
                    IReadOnlyList<GameContentPackDescriptor> packs = packProvider.GetContentPacks() ?? Array.Empty<GameContentPackDescriptor>();
                    for (int i = 0; i < packs.Count; i++)
                    {
                        GameContentPackDescriptor pack = packs[i];
                        if (pack == null || string.IsNullOrWhiteSpace(pack.StableKey)) continue;
                        IReadOnlyList<GameContentRecordDescriptor> records = packProvider.GetRecords(pack.PackId)
                            ?? Array.Empty<GameContentRecordDescriptor>();
                        discovered.Add(new DiscoveredPack(
                            pack,
                            packProvider,
                            records.Where(value => value != null).ToArray()));
                    }
                }
            }

            IReadOnlyList<GameContentSourceClaimConflict> claimConflicts = ApplySourceClaims(
                discovered,
                out IReadOnlyList<GameContentSourceIdentity> claimedSourceIdentities);

            HashSet<string> duplicates = new HashSet<string>(
                discovered.GroupBy(value => value.Pack.StableKey, StringComparer.OrdinalIgnoreCase)
                    .Where(group => group.Count() > 1)
                    .Select(group => group.Key),
                StringComparer.OrdinalIgnoreCase);

            GameContentPackCatalogEntry[] entries = discovered
                .Select(value => new GameContentPackCatalogEntry(
                    value.Pack,
                    value.Provider,
                    value.Records,
                    duplicates.Contains(value.Pack.StableKey)))
                .OrderBy(value => value.Pack.SourceKind == GameContentPackSourceKind.Project ? 1 : 0)
                .ThenBy(value => value.Pack.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(value => value.StableKey, StringComparer.OrdinalIgnoreCase)
                .ThenBy(value => value.Pack.SourcePath, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            return new GameContentPackCatalog(entries, claimConflicts, claimedSourceIdentities);
        }

        private static IReadOnlyList<GameContentSourceClaimConflict> ApplySourceClaims(
            IReadOnlyList<DiscoveredPack> discovered,
            out IReadOnlyList<GameContentSourceIdentity> claimedSourceIdentities)
        {
            var assignments = new List<SourceClaimAssignment>();
            var providerIssues = new Dictionary<string, List<GameContentAuthoringValidationIssue>>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < discovered.Count; i++)
            {
                DiscoveredPack entry = discovered[i];
                if (IsSyntheticProjectContent(entry.Pack) || !CanContributeClaims(entry.Pack)) continue;
                if (!(entry.Provider is IGameContentSourceClaimProvider claimProvider)) continue;

                try
                {
                    IReadOnlyList<GameContentSourceClaim> claims = claimProvider.GetSourceClaims(entry.Pack.PackId)
                        ?? Array.Empty<GameContentSourceClaim>();
                    foreach (GameContentSourceClaim claim in claims.Where(value => value != null && value.IsValid)
                                 .GroupBy(value => value.SourceIdentity.StableKey, StringComparer.OrdinalIgnoreCase)
                                 .Select(group => group.First()))
                        assignments.Add(new SourceClaimAssignment(entry.Pack, claim));
                }
                catch (Exception ex)
                {
                    AddIssue(providerIssues, entry.Pack.StableKey, GameContentAuthoringValidationIssue.Error(
                        "Source Claims",
                        "Content source claims could not be read from provider '" + entry.Pack.ProviderId + "': " + ex.GetBaseException().Message));
                }
            }

            GameContentSourceClaimConflict[] conflicts = assignments
                .GroupBy(value => value.Claim.SourceIdentity.StableKey, StringComparer.OrdinalIgnoreCase)
                .Where(group => group.Select(value => value.Pack.StableKey).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1)
                .Select(group => new GameContentSourceClaimConflict(
                    group.First().Claim.SourceIdentity,
                    group.Select(value => value.Claim.SourcePath).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)),
                    group.Select(value => value.Pack.StableKey)))
                .OrderBy(value => value.SourceIdentity.StableKey, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var claimedIdentities = new HashSet<string>(
                assignments.Select(value => value.Claim.SourceIdentity.StableKey),
                StringComparer.OrdinalIgnoreCase);
            claimedSourceIdentities = assignments
                .Select(value => value.Claim.SourceIdentity)
                .Where(value => value != null && value.IsValid)
                .Distinct()
                .OrderBy(value => value.StableKey, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var conflictsByPack = conflicts
                .SelectMany(conflict => conflict.ClaimantPackKeys.Select(packKey => new { packKey, conflict }))
                .GroupBy(value => value.packKey, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.Select(value => value.conflict).ToArray(), StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < discovered.Count; i++)
            {
                DiscoveredPack entry = discovered[i];
                var extraIssues = providerIssues.TryGetValue(entry.Pack.StableKey, out List<GameContentAuthoringValidationIssue> issues)
                    ? new List<GameContentAuthoringValidationIssue>(issues)
                    : new List<GameContentAuthoringValidationIssue>();
                bool hasClaimConflict = conflictsByPack.TryGetValue(entry.Pack.StableKey, out GameContentSourceClaimConflict[] packConflicts);
                if (hasClaimConflict)
                {
                    for (int conflictIndex = 0; conflictIndex < packConflicts.Length; conflictIndex++)
                        extraIssues.Add(BuildConflictIssue(packConflicts[conflictIndex]));
                }

                if (IsSyntheticProjectContent(entry.Pack))
                {
                    entry.Records = entry.Records.Where(record => !IsClaimed(record, claimedIdentities)).ToArray();
                    for (int conflictIndex = 0; conflictIndex < conflicts.Length; conflictIndex++)
                        extraIssues.Add(BuildConflictIssue(conflicts[conflictIndex]));
                }

                bool syntheticProjectContent = IsSyntheticProjectContent(entry.Pack);
                if (extraIssues.Count > 0 || syntheticProjectContent || hasClaimConflict)
                    entry.Pack = RebuildPack(
                        entry.Pack,
                        entry.Records,
                        extraIssues,
                        hasClaimConflict,
                        syntheticProjectContent);
            }

            return conflicts;
        }

        private static GameContentAuthoringValidationIssue BuildConflictIssue(GameContentSourceClaimConflict conflict)
        {
            string location = string.IsNullOrWhiteSpace(conflict.SourcePath)
                ? conflict.SourceIdentity.StableKey
                : conflict.SourcePath;
            return GameContentAuthoringValidationIssue.Error(
                "Source Claims/" + location,
                "Source is claimed by multiple named packs: " + string.Join(", ", conflict.ClaimantPackKeys) + ". No claimant was selected.");
        }

        private static bool IsClaimed(
            GameContentRecordDescriptor record,
            ISet<string> claimedIdentities)
        {
            return record != null &&
                   GameContentSourceIdentity.TryCreate(record.SourceAsset, record.SourcePath, out GameContentSourceIdentity identity) &&
                   claimedIdentities.Contains(identity.StableKey);
        }

        private static bool CanContributeClaims(GameContentPackDescriptor pack)
        {
            if (pack == null) return false;
            return pack.SourceState != GameContentPackSourceState.MissingSource &&
                   pack.SourceState != GameContentPackSourceState.ProviderUnavailable &&
                   pack.SourceState != GameContentPackSourceState.SampleNotImported &&
                   pack.SourceState != GameContentPackSourceState.InvalidManifest;
        }

        private static bool IsSyntheticProjectContent(GameContentPackDescriptor pack)
        {
            return pack != null &&
                   string.Equals(pack.OwningPackageId, GameContentProjectPackProjection.OwningPackageId, StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(pack.PackId, GameContentProjectPackProjection.PackId, StringComparison.OrdinalIgnoreCase);
        }

        private static void AddIssue(
            IDictionary<string, List<GameContentAuthoringValidationIssue>> issuesByPack,
            string packKey,
            GameContentAuthoringValidationIssue issue)
        {
            if (!issuesByPack.TryGetValue(packKey, out List<GameContentAuthoringValidationIssue> issues))
            {
                issues = new List<GameContentAuthoringValidationIssue>();
                issuesByPack.Add(packKey, issues);
            }

            issues.Add(issue);
        }

        private static GameContentPackDescriptor RebuildPack(
            GameContentPackDescriptor pack,
            IReadOnlyList<GameContentRecordDescriptor> records,
            IEnumerable<GameContentAuthoringValidationIssue> extraIssues,
            bool sourceClaimConflict,
            bool validationFollowsProjectedRecords)
        {
            GameContentRecordDescriptor[] safeRecords = records == null
                ? Array.Empty<GameContentRecordDescriptor>()
                : records.Where(value => value != null).ToArray();
            GameContentCategoryDescriptor[] categories = pack.Categories.Select(category =>
                new GameContentCategoryDescriptor(
                    category.CategoryId,
                    category.DisplayName,
                    category.Description,
                    category.IconOrStyleKey,
                    category.Order,
                    safeRecords.Count(record => record.IsInCategory(category.CategoryId)))).ToArray();
            IEnumerable<GameContentAuthoringValidationIssue> baseIssues = validationFollowsProjectedRecords
                ? safeRecords.SelectMany(record => record.Validation.Issues)
                : pack.Validation.Issues;
            GameContentAuthoringValidationResult validation = new GameContentAuthoringValidationResult(
                baseIssues.Concat(extraIssues ?? Array.Empty<GameContentAuthoringValidationIssue>()).ToArray());
            return new GameContentPackDescriptor(
                pack.PackId,
                pack.OwningPackageId,
                pack.ProviderId,
                pack.DisplayName,
                pack.Description,
                pack.SchemaVersion,
                pack.Tags,
                pack.SourceKind,
                sourceClaimConflict ? GameContentPackSourceState.DuplicateConflict : pack.SourceState,
                pack.SourcePath,
                pack.Manifest,
                pack.PlayableScene,
                pack.Preview,
                pack.Icon,
                pack.DefaultTheme,
                categories,
                pack.Actions,
                validation,
                safeRecords.Length,
                pack.Access,
                pack.Metadata);
        }

        public GameContentPackCatalogEntry Find(string stableKey)
        {
            if (string.IsNullOrWhiteSpace(stableKey)) return null;
            return Entries.FirstOrDefault(entry => string.Equals(entry.StableKey, stableKey, StringComparison.OrdinalIgnoreCase));
        }

        public GameContentPackCatalogEntry Find(GameContentRecordKey recordKey)
        {
            if (recordKey == null) return null;
            return Entries.FirstOrDefault(entry =>
                string.Equals(entry.Pack.OwningPackageId, recordKey.OwningPackageId, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(entry.Pack.PackId, recordKey.PackId, StringComparison.OrdinalIgnoreCase));
        }
    }

    public sealed class GameContentPackContext
    {
        public const string AllPacksSelectionKey = "__all-packs__";

        internal GameContentPackContext(GameContentPackCatalog catalog, GameContentPackCatalogEntry selectedEntry)
        {
            Catalog = catalog ?? GameContentPackCatalog.Build(Array.Empty<IGameContentAuthoringProvider>());
            SelectedEntry = selectedEntry;
            IsAllPacks = selectedEntry == null;
            Records = IsAllPacks ? Catalog.AllRecords : selectedEntry.Records;
            Access = IsAllPacks ? GameContentPackAccessDescriptor.ReadOnlyAggregate : selectedEntry.EffectiveAccess;
        }

        public GameContentPackCatalog Catalog { get; }
        public GameContentPackCatalogEntry SelectedEntry { get; }
        public GameContentPackDescriptor Pack => SelectedEntry == null ? null : SelectedEntry.Pack;
        public IGameContentPackProvider Provider => SelectedEntry == null ? null : SelectedEntry.Provider;
        public IReadOnlyList<GameContentRecordDescriptor> Records { get; }
        public GameContentPackAccessDescriptor Access { get; }
        public bool IsAllPacks { get; }
        public bool IsProjectContent => Pack != null &&
                                        string.Equals(Pack.OwningPackageId, GameContentProjectPackProjection.OwningPackageId, StringComparison.OrdinalIgnoreCase) &&
                                        string.Equals(Pack.PackId, GameContentProjectPackProjection.PackId, StringComparison.OrdinalIgnoreCase);
        public string SelectionKey => IsAllPacks ? AllPacksSelectionKey : SelectedEntry.StableKey;
        public string DisplayName => IsAllPacks ? "All Packs" : Pack.DisplayName;
        public string SourceStatusLabel
        {
            get
            {
                if (IsAllPacks) return "Available";
                if (SelectedEntry.IsConflict) return "Conflict";
                if (Pack.Validation.ErrorCount > 0 ||
                    Pack.SourceState == GameContentPackSourceState.ValidationFailed ||
                    Pack.SourceState == GameContentPackSourceState.InvalidManifest)
                    return "Validation failed";
                switch (Pack.SourceState)
                {
                    case GameContentPackSourceState.MissingSource:
                    case GameContentPackSourceState.ProviderUnavailable:
                    case GameContentPackSourceState.SampleNotImported:
                        return "Missing source";
                    default:
                        return "Available";
                }
            }
        }
        public string AccessStatusLabel => Access.IsWritable ? "Writable" : "Read-only";

        public GameContentRecordDescriptor ResolveRecord(GameContentRecordKey key)
        {
            if (key == null) return null;
            return Records.FirstOrDefault(record => record.CanonicalKey.Equals(key));
        }

        public GameContentRecordDescriptor ResolveReference(
            GameContentRecordDescriptor source,
            GameContentRecordReferenceDescriptor reference)
        {
            if (reference == null) return null;
            if (reference.TargetRecordKey != null)
                return Records.FirstOrDefault(record => record.CanonicalKey.Equals(reference.TargetRecordKey));

            string owner = !string.IsNullOrWhiteSpace(reference.TargetOwningPackageId)
                ? reference.TargetOwningPackageId
                : source == null ? string.Empty : source.CanonicalKey.OwningPackageId;
            string pack = !string.IsNullOrWhiteSpace(reference.TargetPackId)
                ? reference.TargetPackId
                : source == null ? string.Empty : source.CanonicalKey.PackId;
            return Records.FirstOrDefault(record =>
                string.Equals(record.CanonicalKey.OwningPackageId, owner, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(record.CanonicalKey.PackId, pack, StringComparison.OrdinalIgnoreCase) &&
                (string.Equals(record.CanonicalKey.SourceRecordId, reference.TargetRecordId, StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(record.PackScopedId, reference.TargetRecordId, StringComparison.OrdinalIgnoreCase)));
        }
    }

    public sealed class GameContentPackSelectionState
    {
        public string SelectedKey { get; private set; } = string.Empty;

        public GameContentPackContext Refresh(GameContentPackCatalog catalog, string preferredKey = null)
        {
            catalog = catalog ?? GameContentPackCatalog.Build(Array.Empty<IGameContentAuthoringProvider>());
            string candidate = string.IsNullOrWhiteSpace(preferredKey) ? SelectedKey : preferredKey;
            if (string.Equals(candidate, GameContentPackContext.AllPacksSelectionKey, StringComparison.Ordinal))
            {
                SelectedKey = GameContentPackContext.AllPacksSelectionKey;
                return new GameContentPackContext(catalog, null);
            }

            GameContentPackCatalogEntry entry = catalog.Find(candidate)
                ?? catalog.Entries.FirstOrDefault(value => value.Pack.SourceKind != GameContentPackSourceKind.Project && value.Pack.SourceState == GameContentPackSourceState.Available)
                ?? catalog.Entries.FirstOrDefault(value => value.Pack.SourceState == GameContentPackSourceState.Available)
                ?? catalog.Entries.FirstOrDefault();
            SelectedKey = entry == null ? GameContentPackContext.AllPacksSelectionKey : entry.StableKey;
            return new GameContentPackContext(catalog, entry);
        }

        public GameContentPackContext Select(GameContentPackCatalog catalog, string selectionKey)
        {
            SelectedKey = selectionKey ?? string.Empty;
            return Refresh(catalog);
        }
    }

    public sealed class GameContentRecordSelectionState
    {
        public GameContentRecordKey SelectedKey { get; private set; }

        public void Select(GameContentRecordDescriptor record)
        {
            SelectedKey = record == null ? null : record.CanonicalKey;
        }

        public void Clear()
        {
            SelectedKey = null;
        }

        public GameContentRecordDescriptor Resolve(GameContentPackContext context)
        {
            return context == null ? null : context.ResolveRecord(SelectedKey);
        }
    }
}
