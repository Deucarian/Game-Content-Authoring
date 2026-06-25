using System.Collections.Generic;
using Deucarian.Editor;
using UnityEditor;
using UnityEngine;

namespace Deucarian.GameContentAuthoring.Editor
{
    public sealed class GameContentAuthoringWindow : EditorWindow
    {
        public const string WindowTitle = "Game Content Authoring";
        public const string MenuPath = "Tools/Deucarian/Game Content Authoring";
        private const float WidePreviewBreakpoint = 1180f;
        private const float PreviewPanelWidth = 380f;
        private Vector2 _scroll;
        private Vector2 _previewScroll;
        private int _selectedProvider;
        private GameContentCreationResult _lastResult;
        private GameContentAuthoringValidationResult _lastValidation;
        private string _previewStatus = "Preview idle";

        [MenuItem(MenuPath)]
        public static void Open()
        {
            if (Application.isBatchMode) return;

            GameContentAuthoringWindow window = GetWindow<GameContentAuthoringWindow>();
            window.titleContent = new GUIContent(WindowTitle);
            window.minSize = new Vector2(900f, 640f);
            window.Show();
        }

        private void OnGUI()
        {
            DeucarianEditorWindowChrome.DrawImGuiWindowBackground(new Rect(0f, 0f, position.width, position.height));

            IReadOnlyList<IGameContentAuthoringProvider> providers = GameContentAuthoringProviderRegistry.Providers;
            GUILayout.Space(DeucarianEditorSpacing.Small);
            using (new EditorGUILayout.VerticalScope(GUILayout.ExpandHeight(true)))
            {
                DrawHeader(providers.Count);

                using (new EditorGUILayout.HorizontalScope(GUILayout.ExpandHeight(true)))
                {
                    DrawProviderRail(providers);
                    DrawContentSurface(providers);
                }

                DrawBottomStatus(providers);
            }
        }

        private void OnDisable()
        {
            StopSelectedProvider();
        }

        private void DrawHeader(int providerCount)
        {
            DeucarianEditorCards.DrawHeaderCard(
                "Deucarian Game Content Authoring",
                "Create linked gameplay content through installed provider packages.",
                providerCount.ToString(System.Globalization.CultureInfo.InvariantCulture) + " provider(s) loaded");
        }

        private void DrawProviderRail(IReadOnlyList<IGameContentAuthoringProvider> providers)
        {
            Rect rect = EditorGUILayout.BeginVertical(
                DeucarianEditorSidebar.ContainerStyle,
                GUILayout.Width(DeucarianEditorSpacing.SidebarWidth),
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

            GUILayout.Space(DeucarianEditorSpacing.Medium);
            EditorGUILayout.LabelField("Future", DeucarianEditorSidebar.HeadingStyle);
            DeucarianEditorSidebar.DrawItem("Tower", "Future content type placeholder.", false, false, GUILayout.Height(34f));
            DeucarianEditorSidebar.DrawItem("Upgrade", "Future content type placeholder.", false, false, GUILayout.Height(34f));
            DeucarianEditorSidebar.DrawItem("Loot", "Future content type placeholder.", false, false, GUILayout.Height(34f));

            GUILayout.FlexibleSpace();
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

            EditorGUILayout.EndVertical();
        }

        private void DrawContentSurface(IReadOnlyList<IGameContentAuthoringProvider> providers)
        {
            bool wide = position.width >= WidePreviewBreakpoint;
            if (wide)
            {
                using (new EditorGUILayout.HorizontalScope(GUILayout.ExpandHeight(true)))
                {
                    DrawAuthoringSurface(providers, GUILayout.ExpandHeight(true));
                    DrawPreviewSurface(providers, GUILayout.Width(PreviewPanelWidth), GUILayout.ExpandHeight(true));
                }

                return;
            }

            using (new EditorGUILayout.VerticalScope(GUILayout.ExpandHeight(true)))
            {
                DrawAuthoringSurface(providers, GUILayout.ExpandHeight(true));
                DrawPreviewSurface(providers, GUILayout.Height(Mathf.Max(230f, position.height * 0.32f)));
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
                result => _lastResult = result,
                () => _lastResult,
                validation => _lastValidation = validation);
            provider.Draw(context);
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
    }
}
