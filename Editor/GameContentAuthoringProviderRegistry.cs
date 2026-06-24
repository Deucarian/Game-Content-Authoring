using System;
using System.Collections.Generic;
using System.Linq;

namespace Deucarian.GameContentAuthoring.Editor
{
    public static class GameContentAuthoringProviderRegistry
    {
        private static readonly List<IGameContentAuthoringProvider> ProvidersInternal = new List<IGameContentAuthoringProvider>();

        public static IReadOnlyList<IGameContentAuthoringProvider> Providers => ProvidersInternal;

        public static void Register(IGameContentAuthoringProvider provider)
        {
            if (provider == null) return;
            string providerId = provider.ProviderId ?? string.Empty;
            if (string.IsNullOrWhiteSpace(providerId)) return;
            for (int i = 0; i < ProvidersInternal.Count; i++)
                if (string.Equals(ProvidersInternal[i].ProviderId, providerId, StringComparison.OrdinalIgnoreCase))
                    return;
            ProvidersInternal.Add(provider);
            ProvidersInternal.Sort((left, right) =>
            {
                int sort = left.SortOrder.CompareTo(right.SortOrder);
                return sort != 0 ? sort : string.Compare(left.DisplayName, right.DisplayName, StringComparison.OrdinalIgnoreCase);
            });
        }

        public static bool IsProviderRegistered(string providerId)
        {
            if (string.IsNullOrWhiteSpace(providerId)) return false;
            return ProvidersInternal.Any(provider => string.Equals(provider.ProviderId, providerId, StringComparison.OrdinalIgnoreCase));
        }
    }
}
