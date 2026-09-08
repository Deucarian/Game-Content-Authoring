using System;
using System.Collections.Generic;
using System.Linq;

namespace Deucarian.GameContentAuthoring.Editor
{
    internal sealed class GameContentExistingItemSelection
    {
        private readonly Dictionary<string, string> keys = new Dictionary<string, string>(StringComparer.Ordinal);

        internal void Select(IGameContentAuthoringProvider provider, GameContentLibraryItem item)
        {
            if (provider != null && item != null) keys[provider.ProviderId] = item.Key;
        }

        internal bool IsSelected(IGameContentAuthoringProvider provider, GameContentLibraryItem item)
        {
            return provider != null && item != null && keys.TryGetValue(provider.ProviderId, out string key)
                && string.Equals(key, item.Key, StringComparison.Ordinal);
        }

        internal bool HasSelection(IGameContentAuthoringProvider provider)
        {
            return provider != null && keys.TryGetValue(provider.ProviderId, out string key)
                && !string.IsNullOrWhiteSpace(key);
        }

        internal GameContentLibraryItem Resolve(IGameContentAuthoringProvider provider, GameContentLibraryReport report)
        {
            if (provider == null || report == null || !keys.TryGetValue(provider.ProviderId, out string key)
                || string.IsNullOrWhiteSpace(key)) return null;
            GameContentLibraryKind? kind = GameContentExistingItemPresentation.GetProviderKind(provider);
            return kind.HasValue ? report.Items.FirstOrDefault(item => item.Kind == kind.Value
                && string.Equals(item.Key, key, StringComparison.Ordinal)) : null;
        }

        internal bool Remove(IGameContentAuthoringProvider provider)
        {
            return provider != null && keys.Remove(provider.ProviderId);
        }

        internal void Clear() => keys.Clear();

        internal void Prune(GameContentLibraryReport report)
        {
            if (report == null || keys.Count == 0) return;
            var present = new HashSet<string>(report.Items.Select(item => item.Key), StringComparer.Ordinal);
            foreach (string provider in keys.Where(pair => !present.Contains(pair.Value)).Select(pair => pair.Key).ToArray())
                keys.Remove(provider);
        }
    }
}
