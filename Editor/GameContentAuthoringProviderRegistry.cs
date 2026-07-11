using System;
using System.Collections.Generic;
using System.Linq;

namespace Deucarian.GameContentAuthoring.Editor
{
    public static class GameContentAuthoringProviderRegistry
    {
        private static readonly List<IGameContentAuthoringProvider> ProvidersInternal = new List<IGameContentAuthoringProvider>();

        public static IReadOnlyList<IGameContentAuthoringProvider> Providers => ProvidersInternal;
        public static IReadOnlyList<IGameContentAuthoringProvider> VisibleProviders => ProvidersInternal
            .Where(provider => !(provider is IGameContentAuthoringProviderVisibility visibility) || visibility.VisibleInNavigation)
            .OrderBy(provider => provider is IGameContentAuthoringLensProvider lensProvider && lensProvider.Lens != null
                ? lensProvider.Lens.SortOrder
                : provider.SortOrder)
            .ThenBy(provider => provider.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        public static IReadOnlyList<GameContentLensDescriptor> Lenses => ProvidersInternal
            .OfType<IGameContentAuthoringLensProvider>()
            .Select(provider => provider.Lens)
            .Where(lens => lens != null && !string.IsNullOrWhiteSpace(lens.LensId))
            .OrderBy(lens => lens.SortOrder)
            .ThenBy(lens => lens.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        public static void Register(IGameContentAuthoringProvider provider)
        {
            if (provider == null) return;
            string providerId = provider.ProviderId ?? string.Empty;
            if (string.IsNullOrWhiteSpace(providerId)) return;
            for (int i = 0; i < ProvidersInternal.Count; i++)
                if (string.Equals(ProvidersInternal[i].ProviderId, providerId, StringComparison.OrdinalIgnoreCase))
                    return;
            if (provider is IGameContentAuthoringLensProvider lensProvider && lensProvider.Lens != null &&
                ProvidersInternal.OfType<IGameContentAuthoringLensProvider>().Any(existing =>
                    existing.Lens != null && string.Equals(
                        existing.Lens.LensId,
                        lensProvider.Lens.LensId,
                        StringComparison.OrdinalIgnoreCase)))
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

        public static IGameContentAuthoringProvider FindProvider(string providerId)
        {
            if (string.IsNullOrWhiteSpace(providerId)) return null;
            return ProvidersInternal.FirstOrDefault(provider => string.Equals(
                provider.ProviderId,
                providerId,
                StringComparison.OrdinalIgnoreCase));
        }

        public static IGameContentAuthoringProvider FindLensProvider(string lensId)
        {
            if (string.IsNullOrWhiteSpace(lensId)) return null;
            return ProvidersInternal.FirstOrDefault(provider =>
                provider is IGameContentAuthoringLensProvider lensProvider &&
                lensProvider.Lens != null &&
                string.Equals(lensProvider.Lens.LensId, lensId, StringComparison.OrdinalIgnoreCase));
        }
    }
}
