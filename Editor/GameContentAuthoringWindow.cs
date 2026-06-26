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
            if (!layout.Narrow)
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
            DeucarianEditorCards.DrawCard("Live Preview", () =>
            {
                EditorGUILayout.LabelField(provider.DisplayName, DeucarianEditorStyles.SectionTitle);
                EditorGUILayout.LabelField("Preview editor-only assets and authored runtime data without changing the active scene.", DeucarianEditorStyles.MutedLabel);
            });

            var context = new GameContentAuthoringPreviewContext(
                this,
                provider,
                status => _previewStatus = string.IsNullOrWhiteSpace(status) ? "Preview idle" : status,
                () => _previewStatus);
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
                    DrawExistingItem(provider, context, items[i]);
            }, items.Count > 0);
        }

        private static void DrawExistingItem(IGameContentAuthoringProvider provider, GameContentAuthoringContext context, GameContentLibraryItem item)
        {
            if (item == null) return;
            string itemKey = DeucarianEditorAccordion.BuildStateKey("game-content-authoring", provider.ProviderId, "item", item.Key);
            string summary = (string.IsNullOrWhiteSpace(item.Id) ? "(missing id)" : item.Id) + " - " + item.ValidationLabel;
            context.DrawFoldoutCard(
                itemKey,
                item.DisplayName,
                summary,
                () =>
                {
                    context.DrawInlineCard(() =>
                    {
                        DeucarianEditorFieldRow.Draw("ID", () => EditorGUILayout.LabelField(string.IsNullOrWhiteSpace(item.Id) ? "(missing)" : item.Id, context.MutedStyle));
                        DeucarianEditorFieldRow.Draw("Type", () => EditorGUILayout.LabelField(item.Category, context.MutedStyle));
                        DeucarianEditorFieldRow.Draw("Path", () => EditorGUILayout.LabelField(item.Path, context.MutedStyle));
                        DeucarianEditorFieldRow.Draw("Validation", () => EditorGUILayout.LabelField(item.ValidationLabel, context.MutedStyle));
                        using (new EditorGUILayout.HorizontalScope())
                        {
                            DeucarianEditorMiniToolbar.PingButton(item.Asset);
                            DeucarianEditorMiniToolbar.SelectButton(item.Asset);
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

                    DrawItemIssues(context, item);
                    DrawItemReferences(context, "Direct References", item.DirectReferences);
                    DrawItemReferences(context, "Referenced By", item.ReverseReferences);
                },
                false,
                true,
                () =>
                {
                    DeucarianEditorMiniToolbar.PingButton(item.Asset);
                    DeucarianEditorMiniToolbar.SelectButton(item.Asset);
                });
        }

        private static void DrawItemIssues(GameContentAuthoringContext context, GameContentLibraryItem item)
        {
            if (item.Issues.Count == 0)
            {
                DeucarianEditorStatusPanel.DrawStatusCard("No validation issues found for this authored asset.", DeucarianEditorStatus.Success);
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
    }
}
