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
            DrawExistingItems(provider, context, items);

            string createKey = DeucarianEditorAccordion.BuildStateKey("game-content-authoring", provider.ProviderId, "create-new");
            bool defaultOpen = items.Count == 0;
            context.DrawFoldoutCard(
                createKey,
                "Create New",
                "Create a new " + provider.DisplayName + " root asset and its linked sections.",
                () => provider.Draw(context),
                defaultOpen);
        }

        private void DrawExistingItems(IGameContentAuthoringProvider provider, GameContentAuthoringContext context, IReadOnlyList<GameContentLibraryItem> items)
        {
            string key = DeucarianEditorAccordion.BuildStateKey("game-content-authoring", provider.ProviderId, "existing-items");
            string summary = items.Count.ToString(CultureInfo.InvariantCulture) + " authored item(s) under Assets/GameContent.";
            context.DrawFoldoutCard(key, "Existing Authored Items", summary, () =>
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (context.DrawSecondaryButton("Refresh Library", true, GUILayout.Width(124f), GUILayout.Height(24f)))
                        RefreshAuthoringData();
                    GUILayout.FlexibleSpace();
                }

                if (items.Count == 0)
                {
                    DeucarianEditorStatusPanel.DrawStatusCard("No existing " + provider.DisplayName + " assets were found under Assets/GameContent.", DeucarianEditorStatus.Info);
                    return;
                }

                for (int i = 0; i < items.Count; i++)
                    DrawExistingItem(provider, context, items[i], IsSelectedExistingItem(provider, items[i]));
            }, items.Count > 0);
        }

        private void DrawExistingItem(IGameContentAuthoringProvider provider, GameContentAuthoringContext context, GameContentLibraryItem item, bool selected)
        {
            if (item == null) return;
            string itemKey = DeucarianEditorAccordion.BuildStateKey("game-content-authoring", provider.ProviderId, "item", item.Key);
            string summary = GetIdLabel(item) + " - " + item.Category + " - " + item.ValidationLabel;
            context.DrawFoldoutCard(
                itemKey,
                selected ? item.DisplayName + "  (preview)" : item.DisplayName,
                summary,
                () =>
                {
                    context.DrawInlineCard(() =>
                    {
                        using (new EditorGUILayout.HorizontalScope())
                        {
                            DeucarianEditorStatusBadge.Draw(item.ValidationLabel, GetItemStatus(item), GUILayout.Width(96f));
                            GUILayout.FlexibleSpace();
                            if (context.DrawSecondaryButton(selected ? "Previewing" : "Preview", item.Asset != null, GUILayout.Width(88f), GUILayout.Height(22f)))
                                SelectExistingItem(provider, item);
                            DeucarianEditorMiniToolbar.PingButton(item.Asset);
                            DeucarianEditorMiniToolbar.SelectButton(item.Asset);
                        }

                        DeucarianEditorFieldRow.Draw("ID", () => EditorGUILayout.LabelField(string.IsNullOrWhiteSpace(item.Id) ? "(missing)" : item.Id, context.MutedStyle));
                        DeucarianEditorFieldRow.Draw("Type", () => EditorGUILayout.LabelField(item.Category, context.MutedStyle));
                    });

                    DrawItemIssues(context, item);
                    DrawItemReferences(context, "Direct References", item.DirectReferences);
                    DrawItemReferences(context, "Referenced By", item.ReverseReferences);
                    DrawItemAdvanced(context, item);
                },
                false,
                true,
                () =>
                {
                    DeucarianEditorMiniToolbar.PingButton(item.Asset);
                    if (DeucarianEditorMiniToolbar.SelectButton(item.Asset))
                        SelectExistingItem(provider, item);
                });
        }

        private static void DrawItemIssues(GameContentAuthoringContext context, GameContentLibraryItem item)
        {
            if (item.Issues.Count == 0)
            {
                DeucarianEditorStatusBadge.Draw("Ready", DeucarianEditorStatus.Success, GUILayout.Width(72f));
                return;
            }

            List<string> messages = new List<string>();
            for (int i = 0; i < item.Issues.Count; i++)
                messages.Add(item.Issues[i].Path + ": " + item.Issues[i].Message);
            DeucarianEditorStatus status = item.ErrorCount > 0 ? DeucarianEditorStatus.Error : DeucarianEditorStatus.Warning;
            DeucarianEditorStatusPanel.DrawValidationCard(item.ValidationLabel, messages, status);
        }

        private static void DrawItemReferences(GameContentAuthoringContext context, string title, IReadOnlyList<GameContentLibraryReference> references)
        {
            context.DrawInlineCard(() =>
            {
                DeucarianEditorSectionHeader.Draw(title);
                if (references == null || references.Count == 0)
                {
                    EditorGUILayout.LabelField("None found.", context.MutedStyle);
                    return;
                }

                for (int i = 0; i < references.Count; i++)
                {
                    GameContentLibraryReference reference = references[i];
                    if (reference == null || reference.Target == null) continue;
                    EditorGUILayout.LabelField(reference.Target.DisplayName + " (" + reference.Target.Category + ")", context.MutedStyle);
                }
            });
        }

        private static void DrawItemAdvanced(GameContentAuthoringContext context, GameContentLibraryItem item)
        {
            string key = DeucarianEditorAccordion.BuildStateKey("game-content-authoring", "advanced", item.Key);
            context.DrawFoldoutCard(key, "Advanced", "Raw path and serialized reference details.", () =>
            {
                context.DrawInlineCard(() =>
                {
                    DeucarianEditorFieldRow.Draw("Path", () => EditorGUILayout.LabelField(item.Path, context.MutedStyle));
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        if (context.DrawSecondaryButton("Copy Path", !string.IsNullOrWhiteSpace(item.Path), GUILayout.Width(84f), GUILayout.Height(22f)))
                            EditorGUIUtility.systemCopyBuffer = item.Path;
                        if (context.DrawSecondaryButton("Open Folder", AssetDatabase.IsValidFolder(item.Folder), GUILayout.Width(96f), GUILayout.Height(22f)))
                        {
                            UnityEngine.Object folder = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(item.Folder);
                            if (folder != null)
                            {
                                Selection.activeObject = folder;
                                EditorGUIUtility.PingObject(folder);
                            }
                        }
                    }
                });

                DrawRawReferences(context, "Direct Property Uses", item.DirectReferences);
                DrawRawReferences(context, "Referenced By Properties", item.ReverseReferences);
            }, false);
        }

        private static void DrawRawReferences(GameContentAuthoringContext context, string title, IReadOnlyList<GameContentLibraryReference> references)
        {
            context.DrawInlineCard(() =>
            {
                DeucarianEditorSectionHeader.Draw(title);
                if (references == null || references.Count == 0)
                {
                    EditorGUILayout.LabelField("None", context.MutedStyle);
                    return;
                }

                for (int i = 0; i < references.Count; i++)
                {
                    GameContentLibraryReference reference = references[i];
                    if (reference == null || reference.Target == null) continue;
                    EditorGUILayout.LabelField(reference.Target.DisplayName + " - " + reference.PropertyPath, context.MutedStyle);
                }
            });
        }

        private List<GameContentLibraryItem> GetItemsForProvider(IGameContentAuthoringProvider provider)
        {
            if (_packContext != null && !_packContext.IsProjectContent)
                return new List<GameContentLibraryItem>();
            GameContentLibraryKind? kind = GetProviderKind(provider);
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
            _editSessions.RefreshRequested -= OnEditSessionRefreshRequested;
            _editSessions.RefreshRequested += OnEditSessionRefreshRequested;
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
                _selectedExistingItemKeys.Clear();
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
            if (provider == null) return null;
            if (_packContext != null && !_packContext.IsProjectContent) return null;
            if (!_selectedExistingItemKeys.TryGetValue(provider.ProviderId, out string key) || string.IsNullOrWhiteSpace(key))
                return null;

            GameContentLibraryKind? kind = GetProviderKind(provider);
            if (!kind.HasValue) return null;
            GameContentLibraryReport report = GetContentLibraryReport();
            return report.Items.FirstOrDefault(item => item.Kind == kind.Value && string.Equals(item.Key, key, System.StringComparison.Ordinal));
        }

        private bool IsSelectedExistingItem(IGameContentAuthoringProvider provider, GameContentLibraryItem item)
        {
            if (provider == null || item == null) return false;
            return _selectedExistingItemKeys.TryGetValue(provider.ProviderId, out string key)
                && string.Equals(key, item.Key, System.StringComparison.Ordinal);
        }

        private void SelectExistingItem(IGameContentAuthoringProvider provider, GameContentLibraryItem item)
        {
            if (provider == null || item == null) return;
            _selectedExistingItemKeys[provider.ProviderId] = item.Key;
            _previewStatus = "Previewing " + item.DisplayName;
            _previewScroll = Vector2.zero;
            GUI.FocusControl(null);
            Repaint();
        }

        private void ClearSelectedExistingItem(IGameContentAuthoringProvider provider)
        {
            if (provider == null) return;
            if (_selectedExistingItemKeys.Remove(provider.ProviderId))
            {
                _previewStatus = "Preview idle";
                _previewScroll = Vector2.zero;
                GUI.FocusControl(null);
                Repaint();
            }
        }

        private void PruneSelectedExistingItems()
        {
            if (_contentLibraryReport == null || _selectedExistingItemKeys.Count == 0) return;
            var staleProviders = new List<string>();
            foreach (KeyValuePair<string, string> selection in _selectedExistingItemKeys)
            {
                bool exists = _contentLibraryReport.Items.Any(item => string.Equals(item.Key, selection.Value, System.StringComparison.Ordinal));
                if (!exists) staleProviders.Add(selection.Key);
            }

            for (int i = 0; i < staleProviders.Count; i++)
                _selectedExistingItemKeys.Remove(staleProviders[i]);
        }

        private static GameContentAuthoringPreviewSelection CreatePreviewSelection(IGameContentAuthoringProvider provider, GameContentLibraryItem item)
        {
            if (provider == null || item == null) return null;
            return new GameContentAuthoringPreviewSelection(provider.ProviderId, item.DisplayName, item.Id, item.Category, item.Path, item.Asset);
        }

        private static string GetIdLabel(GameContentLibraryItem item)
        {
            return item == null || string.IsNullOrWhiteSpace(item.Id) ? "(missing id)" : item.Id;
        }

        private static DeucarianEditorStatus GetItemStatus(GameContentLibraryItem item)
        {
            if (item == null) return DeucarianEditorStatus.Disabled;
            if (item.ErrorCount > 0) return DeucarianEditorStatus.Error;
            if (item.WarningCount > 0) return DeucarianEditorStatus.Warning;
            return DeucarianEditorStatus.Success;
        }

        private static GameContentLibraryKind? GetProviderKind(IGameContentAuthoringProvider provider)
        {
            if (provider == null) return null;
            string id = provider.ProviderId ?? string.Empty;
            if (id.EndsWith(".attack", System.StringComparison.OrdinalIgnoreCase)) return GameContentLibraryKind.Attack;
            if (id.EndsWith(".enemy", System.StringComparison.OrdinalIgnoreCase)) return GameContentLibraryKind.Enemy;
            if (id.EndsWith(".wave", System.StringComparison.OrdinalIgnoreCase)) return GameContentLibraryKind.Wave;
            if (id.EndsWith(".weapon", System.StringComparison.OrdinalIgnoreCase)) return GameContentLibraryKind.Weapon;
            if (id.EndsWith(".upgrade", System.StringComparison.OrdinalIgnoreCase)) return GameContentLibraryKind.Upgrade;
            if (id.Contains("game-content-set")) return GameContentLibraryKind.ContentSet;
            if (id.Contains("content-pack")) return GameContentLibraryKind.ContentPack;
            return null;
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
