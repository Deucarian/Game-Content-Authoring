using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Deucarian.Editor;
using UnityEditor;
using UnityEngine;

namespace Deucarian.GameContentAuthoring.Editor
{
    public sealed class GameContentAuthoringWindow : EditorWindow
    {
        public const string WindowTitle = "Game Content Authoring";
        public const string MenuPath = "Tools/Deucarian/Game Content Authoring";
        private Vector2 _scroll;
        private Vector2 _previewScroll;
        private int _selectedProvider;
        private GameContentCreationResult _lastResult;
        private GameContentAuthoringValidationResult _lastValidation;
        private string _previewStatus = "Preview idle";
        private GameContentLibraryReport _contentLibraryReport;
        private readonly Dictionary<string, string> _selectedExistingItemKeys = new Dictionary<string, string>(System.StringComparer.Ordinal);

        [MenuItem(MenuPath)]
        public static void Open()
        {
            if (Application.isBatchMode) return;

            GameContentAuthoringWindow window = GetWindow<GameContentAuthoringWindow>();
            window.titleContent = new GUIContent(WindowTitle);
            window.minSize = new Vector2(640f, 560f);
            window.Show();
        }

        private void OnGUI()
        {
            DeucarianEditorWindowChrome.DrawImGuiWindowBackground(new Rect(0f, 0f, position.width, position.height));

            IReadOnlyList<IGameContentAuthoringProvider> providers = GameContentAuthoringProviderRegistry.Providers;
            DeucarianEditorResponsiveLayoutState layout = DeucarianEditorResponsiveLayout.Calculate(position.width, position.height);
            GUILayout.Space(DeucarianEditorSpacing.Small);
            using (new EditorGUILayout.VerticalScope(GUILayout.ExpandHeight(true)))
            {
                DrawHeader(providers.Count, layout);

                using (new EditorGUILayout.HorizontalScope(GUILayout.ExpandHeight(true)))
                {
                    DrawProviderRail(providers, layout);
                    GUILayout.Space(DeucarianEditorSpacing.Small);
                    DrawContentSurface(providers, layout);
                }

                DrawBottomStatus(providers);
            }
        }

        private void OnDisable()
        {
            StopSelectedProvider();
        }

        private void DrawHeader(int providerCount, DeucarianEditorResponsiveLayoutState layout)
        {
            if (layout.Narrow)
            {
                DeucarianEditorCards.DrawHeaderCard(
                    "Game Content Authoring",
                    "Browse, validate, and create authored content.",
                    providerCount.ToString(CultureInfo.InvariantCulture) + " provider(s)");
                return;
            }

            DeucarianEditorCards.DrawHeaderCard(
                "Deucarian Game Content Authoring",
                "Create linked gameplay content through installed provider packages.",
                providerCount.ToString(CultureInfo.InvariantCulture) + " provider(s) loaded");
        }

        private void DrawProviderRail(IReadOnlyList<IGameContentAuthoringProvider> providers, DeucarianEditorResponsiveLayoutState layout)
        {
            Rect rect = EditorGUILayout.BeginVertical(
                DeucarianEditorSidebar.ContainerStyle,
                GUILayout.Width(layout.SidebarWidth),
                GUILayout.ExpandHeight(true));
            DeucarianEditorVisualShell.DrawFrostedSurface(rect, DeucarianEditorTheme.GlassPanel, DeucarianEditorTheme.Border);
            EditorGUILayout.LabelField("Content Types", DeucarianEditorSidebar.HeadingStyle);
            if (providers.Count == 0)
            {
                DeucarianEditorStatusPanel.DrawStatusCard("No content authoring providers installed.", DeucarianEditorStatus.Info);
            }
            else
            {
                for (int i = 0; i < providers.Count; i++)
                {
                    IGameContentAuthoringProvider provider = providers[i];
                    if (DeucarianEditorSidebar.DrawItem(
                            provider.DisplayName,
                            provider.Description,
                            i == _selectedProvider,
                            provider.Enabled,
                            GUILayout.Height(40f)))
                        SelectProvider(i);
                }
            }

            GUILayout.FlexibleSpace();
            if (!layout.Narrow && !IsSelectedProviderCustomSurface(providers))
            {
                EditorGUILayout.LabelField("Installed Providers", DeucarianEditorSidebar.HeadingStyle);
                if (providers.Count == 0)
                {
                    EditorGUILayout.LabelField("None", DeucarianEditorStyles.MutedLabel);
                }
                else
                {
                    for (int i = 0; i < providers.Count; i++)
                    {
                        string state = providers[i].Enabled ? "enabled" : "disabled";
                        EditorGUILayout.LabelField(providers[i].DisplayName + " - " + state, DeucarianEditorStyles.MutedLabel);
                    }
                }
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawContentSurface(IReadOnlyList<IGameContentAuthoringProvider> providers, DeucarianEditorResponsiveLayoutState layout)
        {
            if (TryDrawCustomProviderSurface(providers, layout))
                return;

            if (layout.Wide)
            {
                using (new EditorGUILayout.HorizontalScope(GUILayout.ExpandHeight(true)))
                {
                    float authoringWidth = Mathf.Max(
                        420f,
                        position.width - layout.SidebarWidth - layout.PreviewWidth - DeucarianEditorSpacing.ExtraLarge);
                    DrawAuthoringSurface(providers, GUILayout.Width(authoringWidth), GUILayout.ExpandHeight(true));
                    GUILayout.Space(DeucarianEditorSpacing.Small);
                    DrawPreviewSurface(providers, GUILayout.Width(layout.PreviewWidth), GUILayout.ExpandHeight(true));
                }

                return;
            }

            using (new EditorGUILayout.VerticalScope(GUILayout.ExpandHeight(true)))
            {
                DrawAuthoringSurface(providers, GUILayout.ExpandHeight(true));
                GUILayout.Space(DeucarianEditorSpacing.Small);
                DrawPreviewSurface(providers, GUILayout.Height(layout.StackedPreviewHeight));
            }
        }

        private bool TryDrawCustomProviderSurface(IReadOnlyList<IGameContentAuthoringProvider> providers, DeucarianEditorResponsiveLayoutState layout)
        {
            if (providers.Count == 0)
                return false;

            _selectedProvider = Mathf.Clamp(_selectedProvider, 0, providers.Count - 1);
            IGameContentAuthoringProvider provider = providers[_selectedProvider];
            var surfaceProvider = provider as IGameContentAuthoringSurfaceProvider;
            if (surfaceProvider == null)
                return false;

            Rect rect = EditorGUILayout.BeginVertical(GUIStyle.none, GUILayout.ExpandHeight(true));
            DeucarianEditorVisualShell.DrawFrostedSurface(rect, DeucarianEditorTheme.GlassPanel, DeucarianEditorTheme.Border);
            _lastValidation = null;

            var authoringContext = new GameContentAuthoringContext(
                this,
                provider.ProviderId,
                result =>
                {
                    _lastResult = result;
                    if (result != null && result.Succeeded)
                        RefreshContentLibrary();
                },
                () => _lastResult,
                validation => _lastValidation = validation);

            GameContentLibraryItem selectedItem = GetSelectedExistingItem(provider);
            var previewContext = new GameContentAuthoringPreviewContext(
                this,
                provider,
                status => _previewStatus = string.IsNullOrWhiteSpace(status) ? "Preview idle" : status,
                () => _previewStatus,
                CreatePreviewSelection(provider, selectedItem));

            var surfaceContext = new GameContentAuthoringSurfaceContext(
                this,
                provider,
                layout,
                GetItemsForProvider(provider),
                selectedItem,
                authoringContext,
                previewContext,
                RefreshContentLibrary,
                item => SelectExistingItem(provider, item),
                () => ClearSelectedExistingItem(provider),
                Repaint);

            surfaceProvider.DrawCustomAuthoringSurface(surfaceContext);
            EditorGUILayout.EndVertical();
            return true;
        }

        private void DrawAuthoringSurface(IReadOnlyList<IGameContentAuthoringProvider> providers, params GUILayoutOption[] options)
        {
            Rect rect = EditorGUILayout.BeginVertical(GUIStyle.none, options);
            DeucarianEditorVisualShell.DrawFrostedSurface(rect, DeucarianEditorTheme.GlassPanel, DeucarianEditorTheme.Border);
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            _lastValidation = null;
            DrawSelectedProvider(providers);
            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        private void DrawPreviewSurface(IReadOnlyList<IGameContentAuthoringProvider> providers, params GUILayoutOption[] options)
        {
            Rect rect = EditorGUILayout.BeginVertical(GUIStyle.none, options);
            DeucarianEditorVisualShell.DrawFrostedSurface(rect, DeucarianEditorTheme.GlassPanel, DeucarianEditorTheme.Border);
            _previewScroll = EditorGUILayout.BeginScrollView(_previewScroll);
            DrawSelectedProviderPreview(providers);
            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        private void DrawSelectedProvider(IReadOnlyList<IGameContentAuthoringProvider> providers)
        {
            if (providers.Count == 0)
            {
                DeucarianEditorCards.DrawCard("No Providers", () =>
                {
                    EditorGUILayout.LabelField("Install a content package such as Deucarian Attacks to add authoring providers.", DeucarianEditorStyles.MutedLabel);
                });
                return;
            }

            _selectedProvider = Mathf.Clamp(_selectedProvider, 0, providers.Count - 1);
            IGameContentAuthoringProvider provider = providers[_selectedProvider];
            DeucarianEditorCards.DrawCard("Provider", () =>
            {
                EditorGUILayout.LabelField(provider.DisplayName, DeucarianEditorStyles.SectionTitle);
                EditorGUILayout.LabelField(provider.Description, DeucarianEditorStyles.MutedLabel);
            });

            var context = new GameContentAuthoringContext(
                this,
                provider.ProviderId,
                result => _lastResult = result,
                () => _lastResult,
                validation => _lastValidation = validation);
            DrawProviderBody(provider, context);
        }

        private void DrawSelectedProviderPreview(IReadOnlyList<IGameContentAuthoringProvider> providers)
        {
            if (providers.Count == 0)
            {
                DeucarianEditorCards.DrawCard("Preview", () =>
                {
                    EditorGUILayout.LabelField("Install an authoring provider to enable rich previews.", DeucarianEditorStyles.MutedLabel);
                });
                return;
            }

            _selectedProvider = Mathf.Clamp(_selectedProvider, 0, providers.Count - 1);
            IGameContentAuthoringProvider provider = providers[_selectedProvider];
            GameContentLibraryItem selectedItem = GetSelectedExistingItem(provider);
            DeucarianEditorCards.DrawCard("Live Preview", () =>
            {
                EditorGUILayout.LabelField(provider.DisplayName, DeucarianEditorStyles.SectionTitle);
                if (selectedItem == null)
                {
                    DeucarianEditorStatusBadge.Draw("Create form", DeucarianEditorStatus.Info, GUILayout.Width(94f));
                    return;
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    DeucarianEditorStatusBadge.Draw("Selected asset", GetItemStatus(selectedItem), GUILayout.Width(112f));
                    GUILayout.FlexibleSpace();
                    DeucarianEditorMiniToolbar.PingButton(selectedItem.Asset);
                    DeucarianEditorMiniToolbar.SelectButton(selectedItem.Asset);
                }

                EditorGUILayout.LabelField(selectedItem.DisplayName, DeucarianEditorStyles.SectionTitle);
                EditorGUILayout.LabelField(GetIdLabel(selectedItem) + " - " + selectedItem.Category, DeucarianEditorStyles.MutedLabel);
            });

            var context = new GameContentAuthoringPreviewContext(
                this,
                provider,
                status => _previewStatus = string.IsNullOrWhiteSpace(status) ? "Preview idle" : status,
                () => _previewStatus,
                CreatePreviewSelection(provider, selectedItem));
            provider.DrawPreview(context);
        }

        private void SelectProvider(int index)
        {
            if (_selectedProvider == index) return;
            StopSelectedProvider();
            _selectedProvider = index;
            _lastResult = null;
            _lastValidation = null;
            _previewStatus = "Preview idle";
            _contentLibraryReport = null;
            _selectedExistingItemKeys.Clear();
            _scroll = Vector2.zero;
            _previewScroll = Vector2.zero;
            GUI.FocusControl(null);
            IReadOnlyList<IGameContentAuthoringProvider> providers = GameContentAuthoringProviderRegistry.Providers;
            if (index >= 0 && index < providers.Count)
                providers[index].OnSelected();
        }

        private void StopSelectedProvider()
        {
            IReadOnlyList<IGameContentAuthoringProvider> providers = GameContentAuthoringProviderRegistry.Providers;
            if (providers.Count == 0) return;
            int index = Mathf.Clamp(_selectedProvider, 0, providers.Count - 1);
            providers[index].StopPreview();
        }

        private void DrawBottomStatus(IReadOnlyList<IGameContentAuthoringProvider> providers)
        {
            string providerName = providers.Count == 0
                ? "No provider"
                : providers[Mathf.Clamp(_selectedProvider, 0, providers.Count - 1)].DisplayName;
            string validation = GetValidationSummary();
            string operation = _lastResult == null ? _previewStatus : _lastResult.Message;
            DeucarianEditorStatusPanel.DrawStatusBar(providerName, validation, operation);
        }

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
                        RefreshContentLibrary();
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
            _contentLibraryReport = GameContentLibraryService.Scan(GameContentLibraryProvider.DefaultRoot);
            PruneSelectedExistingItems();
        }

        private GameContentLibraryItem GetSelectedExistingItem(IGameContentAuthoringProvider provider)
        {
            if (provider == null) return null;
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
