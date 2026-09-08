using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Deucarian.Editor;
using UnityEditor;
using UnityEngine;

namespace Deucarian.GameContentAuthoring.Editor
{
    public sealed partial class GameContentAuthoringWindow
    {
        private string GetValidationSummary()
        {
            if (_lastValidation == null)
            {
                return "Validation pending";
            }

            if (_lastValidation.ErrorCount > 0)
            {
                return _lastValidation.ErrorCount.ToString(System.Globalization.CultureInfo.InvariantCulture) + " blocking issue(s)";
            }

            if (_lastValidation.WarningCount > 0)
            {
                return _lastValidation.WarningCount.ToString(System.Globalization.CultureInfo.InvariantCulture) + " warning(s)";
            }

            return "Ready";
        }

        private void DrawProviderBody(IGameContentAuthoringProvider provider, GameContentAuthoringContext context)
        {
            if (provider.ProviderId == GameContentLibraryProvider.ContentLibraryProviderId)
            {
                provider.Draw(context);
                return;
            }

            if (_packContext != null && !_packContext.Access.CanCreate)
            {
                DeucarianEditorStatusPanel.DrawStatusCard(
                    _packContext.Access.PersistenceLabel + ". " + _packContext.Access.DisabledReason,
                    DeucarianEditorStatus.Info);
                return;
            }

            List<GameContentLibraryItem> items = GetItemsForProvider(provider);
            if (_existingItemsView == null)
                _existingItemsView = new GameContentExistingItemsView(
                    RefreshAuthoringData, SelectExistingItem, IsSelectedExistingItem);
            _existingItemsView.Draw(provider, context, items);

            string createKey = DeucarianEditorAccordion.BuildStateKey("game-content-authoring", provider.ProviderId, "create-new");
            bool defaultOpen = items.Count == 0;
            context.DrawFoldoutCard(
                createKey,
                "Create New",
                "Create a new " + provider.DisplayName + " root asset and its linked sections.",
                () => provider.Draw(context),
                defaultOpen);
        }

        private List<GameContentLibraryItem> GetItemsForProvider(IGameContentAuthoringProvider provider)
        {
            if (_packContext != null && !_packContext.IsProjectContent)
                return new List<GameContentLibraryItem>();
            GameContentLibraryKind? kind = GameContentExistingItemPresentation.GetProviderKind(provider);
            if (!kind.HasValue) return new List<GameContentLibraryItem>();
            GameContentLibraryReport report = GetContentLibraryReport();
            return report.Items
                .Where(item => item.Kind == kind.Value)
                .OrderBy(item => item.DisplayName)
                .ThenBy(item => item.Path)
                .ToList();
        }

        private GameContentLibraryReport GetContentLibraryReport()
        {
            if (_contentLibraryReport == null)
                RefreshContentLibrary();
            return _contentLibraryReport;
        }

        private void RefreshContentLibrary()
        {
            _contentLibraryReport = GameContentLibraryService.Scan(
                GameContentLibraryProvider.DefaultRoot,
                _packCatalog == null ? null : _packCatalog.ClaimedSourceIdentities);
            PruneSelectedExistingItems();
        }

        private void EnsurePackContext()
        {
            if (_packCatalog != null && _packContext != null) return;
            string preferred = SessionState.GetString(PackSelectionSessionStateKey, string.Empty);
            _packCatalog = GameContentPackCatalog.Build(GameContentAuthoringProviderRegistry.Providers);
            _packContext = _packSelection.Refresh(_packCatalog, preferred);
            SessionState.SetString(PackSelectionSessionStateKey, _packContext.SelectionKey);
        }

        private void EnsureEditCoordinator()
        {
            if (_editSessions != null) return;
            _editSessions = GameContentEditSessionCoordinator.Shared;
            _editSessionView = _editSessions.AttachView(OnEditSessionRefreshRequested);
        }

        private void OnEditSessionRefreshRequested()
        {
            RefreshAuthoringData();
        }

        private void RefreshAuthoringData()
        {
            string preferred = _packContext == null
                ? SessionState.GetString(PackSelectionSessionStateKey, string.Empty)
                : _packContext.SelectionKey;
            _packCatalog = GameContentPackCatalog.Build(GameContentAuthoringProviderRegistry.Providers);
            _packContext = _packSelection.Refresh(_packCatalog, preferred);
            _editSessions?.Reconcile(_packCatalog);
            RefreshContentLibrary();
            if (_recordSelection.SelectedKey != null && _recordSelection.Resolve(_packContext) == null)
                _recordSelection.Clear();
            SessionState.SetString(PackSelectionSessionStateKey, _packContext.SelectionKey);
            Repaint();
        }

        private void SelectPack(string selectionKey)
        {
            if (_packCatalog == null) return;
            string previous = _packContext == null ? string.Empty : _packContext.SelectionKey;
            _packContext = _packSelection.Select(_packCatalog, selectionKey);
            if (!string.Equals(previous, _packContext.SelectionKey, StringComparison.OrdinalIgnoreCase))
            {
                _recordSelection.Clear();
                _existingItemSelection.Clear();
                _lastResult = null;
                _lastValidation = null;
                _previewStatus = "Preview idle";
            }
            SessionState.SetString(PackSelectionSessionStateKey, _packContext.SelectionKey);
            GUI.FocusControl(null);
            Repaint();
        }

        private void SelectRecord(GameContentRecordDescriptor record)
        {
            if (record == null || _packContext == null || _packContext.ResolveRecord(record.CanonicalKey) == null) return;
            _recordSelection.Select(record);
            _previewStatus = "Previewing " + record.DisplayName;
            GUI.FocusControl(null);
            Repaint();
        }

        private void OpenLens(string lensId, GameContentRecordDescriptor record)
        {
            IReadOnlyList<IGameContentAuthoringProvider> providers = GameContentAuthoringProviderRegistry.VisibleProviders;
            int index = -1;
            for (int i = 0; i < providers.Count; i++)
            {
                if (providers[i] is IGameContentAuthoringLensProvider lensProvider &&
                    lensProvider.Lens != null &&
                    string.Equals(lensProvider.Lens.LensId, lensId, StringComparison.OrdinalIgnoreCase))
                {
                    index = i;
                    break;
                }
            }

            if (index < 0) return;
            if (record != null) SelectRecord(record);
            SelectProvider(index);
            Repaint();
        }

        private string BuildProviderLabel(
            IGameContentAuthoringProvider provider,
            GameContentLensDescriptor lens)
        {
            if (provider == null || lens == null || _packContext == null) return provider == null ? string.Empty : provider.DisplayName;
            if (!lens.MatchesAllRecords && lens.SupportedCapabilities.Count == 0) return provider.DisplayName;
            int count = _packContext.Records.Count(lens.Matches);
            return provider.DisplayName + " (" + count.ToString(CultureInfo.InvariantCulture) + ")";
        }

        private GameContentLibraryItem GetSelectedExistingItem(IGameContentAuthoringProvider provider)
        {
            if (!_existingItemSelection.HasSelection(provider)
                || (_packContext != null && !_packContext.IsProjectContent)
                || !GameContentExistingItemPresentation.GetProviderKind(provider).HasValue) return null;
            return _existingItemSelection.Resolve(provider, GetContentLibraryReport());
        }

        private bool IsSelectedExistingItem(IGameContentAuthoringProvider provider, GameContentLibraryItem item)
        {
            return _existingItemSelection.IsSelected(provider, item);
        }

        private void SelectExistingItem(IGameContentAuthoringProvider provider, GameContentLibraryItem item)
        {
            if (provider == null || item == null) return;
            _existingItemSelection.Select(provider, item);
            _previewStatus = "Previewing " + item.DisplayName;
            _previewScroll = Vector2.zero;
            GUI.FocusControl(null);
            Repaint();
        }

        private void ClearSelectedExistingItem(IGameContentAuthoringProvider provider)
        {
            if (provider == null) return;
            if (_existingItemSelection.Remove(provider))
            {
                _previewStatus = "Preview idle";
                _previewScroll = Vector2.zero;
                GUI.FocusControl(null);
                Repaint();
            }
        }

        private void PruneSelectedExistingItems()
        {
            _existingItemSelection.Prune(_contentLibraryReport);
        }

        private static GameContentAuthoringPreviewSelection CreatePreviewSelection(IGameContentAuthoringProvider provider, GameContentLibraryItem item)
        {
            if (provider == null || item == null) return null;
            return new GameContentAuthoringPreviewSelection(provider.ProviderId, item.DisplayName, item.Id, item.Category, item.Path, item.Asset);
        }

        private bool IsSelectedProviderCustomSurface(IReadOnlyList<IGameContentAuthoringProvider> providers)
        {
            if (providers == null || providers.Count == 0)
                return false;

            int index = Mathf.Clamp(_selectedProvider, 0, providers.Count - 1);
            return providers[index] is IGameContentAuthoringSurfaceProvider;
        }
    }
}
