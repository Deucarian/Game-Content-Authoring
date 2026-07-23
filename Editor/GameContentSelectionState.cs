using System;
using System.Linq;

namespace Deucarian.GameContentAuthoring.Editor
{
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
