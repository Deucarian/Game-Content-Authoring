using System.Collections.Generic;
using Deucarian.Editor;
using UnityEditor;
using UnityEngine;

namespace Deucarian.GameContentAuthoring.Editor
{
    public sealed class GameContentAuthoringWindow : EditorWindow
    {
        public const string WindowTitle = "Game Content Authoring";
        public const string MenuPath = "Deucarian/Game Content Authoring";
        private Vector2 _scroll;
        private int _selectedProvider;
        private GameContentCreationResult _lastResult;
        private GUIStyle _providerButton;
        private GUIStyle _selectedProviderButton;
        private bool _stylesReady;

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
            EnsureStyles();
            DeucarianEditorChrome.DrawPackageHeader(
                "editor",
                "Deucarian Game Content Authoring",
                "Create linked gameplay content through installed provider packages.");
            using (new EditorGUILayout.HorizontalScope())
            {
                DrawProviderRail();
                using (new EditorGUILayout.VerticalScope())
                {
                    _scroll = EditorGUILayout.BeginScrollView(_scroll);
                    DrawSelectedProvider();
                    EditorGUILayout.EndScrollView();
                }
            }
        }

        private void DrawProviderRail()
        {
            IReadOnlyList<IGameContentAuthoringProvider> providers = GameContentAuthoringProviderRegistry.Providers;
            using (new EditorGUILayout.VerticalScope(GUILayout.Width(250f), GUILayout.ExpandHeight(true)))
            {
                EditorGUILayout.LabelField("Content Types", DeucarianEditorStyles.SectionTitle);
                if (providers.Count == 0)
                {
                    EditorGUILayout.HelpBox("No content authoring providers installed.", MessageType.Info);
                }
                else
                {
                    for (int i = 0; i < providers.Count; i++)
                    {
                        IGameContentAuthoringProvider provider = providers[i];
                        GUI.enabled = provider.Enabled;
                        GUIStyle style = i == _selectedProvider ? _selectedProviderButton : _providerButton;
                        if (GUILayout.Button(new GUIContent(provider.DisplayName, provider.Description), style, GUILayout.Height(44f)))
                            SelectProvider(i);
                        GUI.enabled = true;
                    }
                }

                GUILayout.FlexibleSpace();
                EditorGUILayout.LabelField("Installed Providers", DeucarianEditorStyles.SectionTitle);
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
        }

        private void DrawSelectedProvider()
        {
            IReadOnlyList<IGameContentAuthoringProvider> providers = GameContentAuthoringProviderRegistry.Providers;
            if (providers.Count == 0)
            {
                DeucarianEditorChrome.DrawSectionHeader("No Providers");
                DeucarianEditorChrome.BeginSection();
                EditorGUILayout.LabelField("Install a content package such as Deucarian Attacks to add authoring providers.", DeucarianEditorStyles.MutedLabel);
                DeucarianEditorChrome.EndSection();
                return;
            }

            _selectedProvider = Mathf.Clamp(_selectedProvider, 0, providers.Count - 1);
            IGameContentAuthoringProvider provider = providers[_selectedProvider];
            var context = new GameContentAuthoringContext(this, result => _lastResult = result, () => _lastResult);
            provider.Draw(context);
        }

        private void SelectProvider(int index)
        {
            if (_selectedProvider == index) return;
            _selectedProvider = index;
            _lastResult = null;
            _scroll = Vector2.zero;
            GUI.FocusControl(null);
            IReadOnlyList<IGameContentAuthoringProvider> providers = GameContentAuthoringProviderRegistry.Providers;
            if (index >= 0 && index < providers.Count)
                providers[index].OnSelected();
        }

        private void EnsureStyles()
        {
            if (_stylesReady) return;
            _providerButton = new GUIStyle(EditorStyles.miniButton) { alignment = TextAnchor.MiddleLeft, fontStyle = FontStyle.Bold, padding = new RectOffset(10, 10, 6, 6) };
            _selectedProviderButton = new GUIStyle(_providerButton);
            _selectedProviderButton.normal.textColor = Color.white;
            _selectedProviderButton.normal.background = Texture2D.grayTexture;
            _stylesReady = true;
        }
    }
}
