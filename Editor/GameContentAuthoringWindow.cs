using Deucarian.Editor;
using UnityEditor;
using UnityEngine.UIElements;

namespace Deucarian.GameContentAuthoring.Editor
{
    public sealed class GameContentAuthoringWindow : EditorWindow
    {
        public const string WindowTitle = "Game Content Authoring";
        public const string MenuPath = "Tools/Deucarian/Authoring/Game Content...";
        internal const string PackSelectionSessionStateKey = "Deucarian.GameContentAuthoring.SelectedPack";
        private DeucarianEditorPageSession navigation;
        private GameContentToolkitWorkspace workspace;

        [MenuItem(MenuPath)]
        public static void Open()
        {
            if (UnityEngine.Application.isBatchMode) return;
            var window = DeucarianEditorWindowPages.GetStandalone<GameContentAuthoringWindow>(WindowTitle);
            DeucarianEditorWorkspace.ConfigureWindow(window); window.Show(); window.Focus();
        }
        public void CreateGUI()
        {
            navigation?.Dispose();
            navigation = new DeucarianEditorPageSession(this, DeucarianToolIds.GameContentAuthoring,
                root => Build(root, DeucarianToolIds.GameContentAuthoring, "Game content", "Find, edit and validate your content.", null),
                deactivateHome: () => workspace?.StopPreview());
        }
        public static IDeucarianEditorPage CreatePage() => CreatePage(DeucarianToolIds.GameContentAuthoring,
            "Game content", "Find, edit and validate your content.");
        public static IDeucarianEditorPage CreatePage(string toolId, string title, string subtitle, string providerPrefix = null) =>
            DeucarianEditorWindowPages.Create<GameContentAuthoringWindow>((window, root) => window.Build(root, toolId, title, subtitle, providerPrefix),
                deactivate: window => window.workspace?.StopPreview());
        private void Build(VisualElement root, string toolId, string title, string subtitle, string prefix)
        {
            workspace?.Dispose(); root.Clear();
            workspace = new GameContentToolkitWorkspace(this, root, toolId, title, subtitle, prefix);
        }
        private void OnDisable()
        {
            navigation?.Dispose(); navigation = null;
            workspace?.Dispose(); workspace = null;
        }
    }
}
