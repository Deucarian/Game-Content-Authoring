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
        private GameContentPackCatalog(IReadOnlyList<GameContentPackCatalogEntry> entries)
        {
            Entries = entries ?? Array.Empty<GameContentPackCatalogEntry>();
            AllRecords = Entries.SelectMany(entry => entry.Records).ToArray();
        }

        public IReadOnlyList<GameContentPackCatalogEntry> Entries { get; }
        public IReadOnlyList<GameContentRecordDescriptor> AllRecords { get; }

        public static GameContentPackCatalog Build(IEnumerable<IGameContentAuthoringProvider> providers)
        {
            var discovered = new List<Tuple<GameContentPackDescriptor, IGameContentPackProvider, IReadOnlyList<GameContentRecordDescriptor>>>();
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
                        discovered.Add(Tuple.Create(
                            pack,
                            packProvider,
                            (IReadOnlyList<GameContentRecordDescriptor>)records.Where(value => value != null).ToArray()));
                    }
                }
            }

            HashSet<string> duplicates = new HashSet<string>(
                discovered.GroupBy(value => value.Item1.StableKey, StringComparer.OrdinalIgnoreCase)
                    .Where(group => group.Count() > 1)
                    .Select(group => group.Key),
                StringComparer.OrdinalIgnoreCase);

            GameContentPackCatalogEntry[] entries = discovered
                .Select(value => new GameContentPackCatalogEntry(
                    value.Item1,
                    value.Item2,
                    value.Item3,
                    duplicates.Contains(value.Item1.StableKey)))
                .OrderBy(value => value.Pack.SourceKind == GameContentPackSourceKind.Project ? 1 : 0)
                .ThenBy(value => value.Pack.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(value => value.StableKey, StringComparer.OrdinalIgnoreCase)
                .ThenBy(value => value.Pack.SourcePath, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            return new GameContentPackCatalog(entries);
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
        public bool IsProjectContent => Pack != null && Pack.SourceKind == GameContentPackSourceKind.Project;
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
