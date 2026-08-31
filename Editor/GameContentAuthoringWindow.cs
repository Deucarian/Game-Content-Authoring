using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Deucarian.Editor;
using UnityEditor;
using UnityEngine;

namespace Deucarian.GameContentAuthoring.Editor
{
    public sealed partial class GameContentAuthoringWindow : EditorWindow
    {
        public const string WindowTitle = "Game Content Authoring";
        public const string MenuPath = "Tools/Deucarian/Authoring/Game Content...";
        internal const string PackSelectionSessionStateKey = "Deucarian.GameContentAuthoring.SelectedPack";
        private Vector2 _scroll;
        private Vector2 _previewScroll;
        private int _selectedProvider;
        private GameContentCreationResult _lastResult;
        private GameContentAuthoringValidationResult _lastValidation;
        private string _previewStatus = "Preview idle";
        private GameContentLibraryReport _contentLibraryReport;
        private readonly Dictionary<string, string> _selectedExistingItemKeys = new Dictionary<string, string>(System.StringComparer.Ordinal);
        private readonly GameContentPackSelectionState _packSelection = new GameContentPackSelectionState();
        private readonly GameContentRecordSelectionState _recordSelection = new GameContentRecordSelectionState();
        private GameContentPackCatalog _packCatalog;
        private GameContentPackContext _packContext;
        private GameContentEditSessionCoordinator _editSessions;

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

            EnsureEditCoordinator();
            EnsurePackContext();
            IReadOnlyList<IGameContentAuthoringProvider> providers = GameContentAuthoringProviderRegistry.VisibleProviders;
            DeucarianEditorResponsiveLayoutState layout = DeucarianEditorResponsiveLayout.Calculate(position.width, position.height);
            GUILayout.Space(DeucarianEditorSpacing.Small);
            using (new EditorGUILayout.VerticalScope(GUILayout.ExpandHeight(true)))
            {
                DrawHeader(providers.Count, layout);
                DrawPackSelector();

                using (new EditorGUILayout.HorizontalScope(GUILayout.ExpandHeight(true)))
                {
                    DrawProviderRail(providers, layout);
                    GUILayout.Space(DeucarianEditorSpacing.Small);
                    DrawContentSurface(providers, layout);
                }

                DrawBottomStatus(providers);
            }
        }

        private void OnEnable()
        {
            EnsureEditCoordinator();
        }

        private void OnDisable()
        {
            StopSelectedProvider();
            if (_editSessions != null)
            {
                _editSessions.RefreshRequested -= OnEditSessionRefreshRequested;
                _editSessions.Reset();
                _editSessions = null;
            }
        }

        private void DrawHeader(int providerCount, DeucarianEditorResponsiveLayoutState layout)
        {
            if (layout.Narrow)
            {
                DeucarianEditorCards.DrawHeaderCard(
                    "Game Content Authoring",
                    "Content packs own records; authoring views inspect the selected pack.",
                    providerCount.ToString(CultureInfo.InvariantCulture) + " view(s)");
                return;
            }

            DeucarianEditorCards.DrawHeaderCard(
                "Deucarian Game Content Authoring",
                "Content packs own records; reusable views provide domain inspection and previews.",
                providerCount.ToString(CultureInfo.InvariantCulture) + " view(s) loaded");
        }

        private void DrawPackSelector()
        {
            if (_packCatalog == null || _packContext == null) return;
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                EditorGUILayout.LabelField("Content Pack", GUILayout.Width(88f));
                string[] labels = _packCatalog.Entries.Select(entry => entry.Pack.DisplayName)
                    .Concat(new[] { "All Packs" })
                    .ToArray();
                string[] keys = _packCatalog.Entries.Select(entry => entry.StableKey)
                    .Concat(new[] { GameContentPackContext.AllPacksSelectionKey })
                    .ToArray();
                int current = Array.FindIndex(keys, key => string.Equals(key, _packContext.SelectionKey, StringComparison.OrdinalIgnoreCase));
                if (current < 0) current = 0;
                int next = EditorGUILayout.Popup(current, labels, GUILayout.MinWidth(180f));
                if (next != current && next >= 0 && next < keys.Length) SelectPack(keys[next]);
                GUILayout.Space(6f);
                GameContentRecordLensBrowser.DrawAccessStatus(_packContext, true);
                GUILayout.FlexibleSpace();
                if (GUILayout.Button(new GUIContent("Refresh", "Refresh content packs and Project Content."), EditorStyles.toolbarButton, GUILayout.Width(62f)))
                    RefreshAuthoringData();
            }
        }

        private void DrawProviderRail(IReadOnlyList<IGameContentAuthoringProvider> providers, DeucarianEditorResponsiveLayoutState layout)
        {
            Rect rect = EditorGUILayout.BeginVertical(
                DeucarianEditorSidebar.ContainerStyle,
                GUILayout.Width(layout.SidebarWidth),
                GUILayout.ExpandHeight(true));
            DeucarianEditorVisualShell.DrawFrostedSurface(rect, DeucarianEditorTheme.GlassPanel, DeucarianEditorTheme.Border);
            EditorGUILayout.LabelField("Authoring Views", DeucarianEditorSidebar.HeadingStyle);
            if (providers.Count == 0)
            {
                DeucarianEditorStatusPanel.DrawStatusCard("No content authoring providers installed.", DeucarianEditorStatus.Info);
            }
            else
            {
                string currentGroup = string.Empty;
                for (int i = 0; i < providers.Count; i++)
                {
                    IGameContentAuthoringProvider provider = providers[i];
                    GameContentLensDescriptor lens = (provider as IGameContentAuthoringLensProvider)?.Lens;
                    string group = lens == null ? "Other" : lens.GroupName;
                    if (!string.Equals(group, currentGroup, StringComparison.Ordinal))
                    {
                        if (!string.IsNullOrWhiteSpace(currentGroup)) GUILayout.Space(DeucarianEditorSpacing.Small);
                        EditorGUILayout.LabelField(group, DeucarianEditorStyles.MutedLabel);
                        currentGroup = group;
                    }
                    string label = BuildProviderLabel(provider, lens);
                    if (DeucarianEditorSidebar.DrawItem(
                            label,
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
                        RefreshAuthoringData();
                },
                () => _lastResult,
                validation => _lastValidation = validation,
                _packContext);

            GameContentLibraryItem selectedItem = GetSelectedExistingItem(provider);
            GameContentRecordDescriptor selectedRecord = _recordSelection.Resolve(_packContext);
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
                _packContext != null && _packContext.IsProjectContent
                    ? GetContentLibraryReport().Items
                    : System.Array.Empty<GameContentLibraryItem>(),
                selectedItem,
                authoringContext,
                previewContext,
                _packContext,
                GameContentAuthoringProviderRegistry.Lenses,
                selectedRecord,
                _editSessions,
                RefreshAuthoringData,
                item => SelectExistingItem(provider, item),
                () => ClearSelectedExistingItem(provider),
                SelectRecord,
                OpenLens,
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
                validation => _lastValidation = validation,
                _packContext);
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
            IReadOnlyList<IGameContentAuthoringProvider> providers = GameContentAuthoringProviderRegistry.VisibleProviders;
            if (index >= 0 && index < providers.Count)
                providers[index].OnSelected();
        }

        private void StopSelectedProvider()
        {
            IReadOnlyList<IGameContentAuthoringProvider> providers = GameContentAuthoringProviderRegistry.VisibleProviders;
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

    }
}
