using System;
using Deucarian.Editor;
using UnityEditor;
using UnityEngine;

namespace Deucarian.GameContentAuthoring.Editor
{
    public sealed class GameContentAuthoringContext
    {
        private readonly Action<GameContentCreationResult> _setResult;
        private readonly Func<GameContentCreationResult> _getResult;

        internal GameContentAuthoringContext(EditorWindow window, Action<GameContentCreationResult> setResult, Func<GameContentCreationResult> getResult)
        {
            Window = window;
            _setResult = setResult;
            _getResult = getResult;
        }

        public EditorWindow Window { get; }
        public GUIStyle MutedStyle => DeucarianEditorStyles.MutedLabel;
        public GUIStyle SectionTitleStyle => DeucarianEditorStyles.SectionTitle;

        public void DrawSection(string title, Action draw)
        {
            DeucarianEditorChrome.DrawSectionHeader(title);
            DeucarianEditorChrome.BeginSection();
            draw?.Invoke();
            DeucarianEditorChrome.EndSection();
        }

        public string DrawOutputRootField(string outputRoot)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                DefaultAsset asset = AssetDatabase.LoadAssetAtPath<DefaultAsset>(outputRoot);
                DefaultAsset next = (DefaultAsset)EditorGUILayout.ObjectField("Output Root", asset, typeof(DefaultAsset), false);
                if (next != asset && next != null)
                {
                    string path = AssetDatabase.GetAssetPath(next);
                    if (AssetDatabase.IsValidFolder(path)) outputRoot = path;
                }

                if (GUILayout.Button(new GUIContent("Ping", "Ping output root"), GUILayout.Width(48f)) && asset != null)
                    EditorGUIUtility.PingObject(asset);
            }

            return EditorGUILayout.TextField("Output Path", outputRoot);
        }

        public void DrawValidation(GameContentAuthoringValidationResult result, string readyMessage)
        {
            if (result == null || result.Issues.Count == 0)
            {
                EditorGUILayout.HelpBox(readyMessage, MessageType.Info);
                return;
            }

            string summary = result.ErrorCount == 0
                ? result.WarningCount.ToString(System.Globalization.CultureInfo.InvariantCulture) + " warning(s). You can create the asset after confirming any prompts."
                : result.ErrorCount.ToString(System.Globalization.CultureInfo.InvariantCulture) + " blocking issue(s) and " + result.WarningCount.ToString(System.Globalization.CultureInfo.InvariantCulture) + " warning(s).";
            EditorGUILayout.HelpBox(summary, result.ErrorCount == 0 ? MessageType.Warning : MessageType.Error);

            for (int i = 0; i < result.Issues.Count; i++)
            {
                GameContentAuthoringValidationIssue issue = result.Issues[i];
                MessageType type = issue.Severity == GameContentAuthoringValidationSeverity.Error
                    ? MessageType.Error
                    : issue.Severity == GameContentAuthoringValidationSeverity.Warning
                        ? MessageType.Warning
                        : MessageType.Info;
                EditorGUILayout.HelpBox(issue.Path + ": " + issue.Message, type);
            }
        }

        public bool DrawCreateButton(string label, bool enabled)
        {
            var content = new GUIContent(
                label,
                enabled ? "Create the root asset and linked section assets." : "Fix blocking validation issues before creating this asset.");
            using (new EditorGUI.DisabledScope(!enabled))
                return GUILayout.Button(content, DeucarianEditorStyles.ToolbarButton, GUILayout.Height(30f));
        }

        public void SetCreationResult(GameContentCreationResult result)
        {
            _setResult?.Invoke(result);
            if (result != null && result.CreatedRoot != null)
            {
                Selection.activeObject = result.CreatedRoot;
                EditorGUIUtility.PingObject(result.CreatedRoot);
            }
        }

        public void DrawCreationResult()
        {
            GameContentCreationResult result = _getResult == null ? null : _getResult();
            if (result == null) return;
            GUILayout.Space(8f);
            if (!result.Succeeded)
            {
                EditorGUILayout.HelpBox(result.Message, MessageType.Error);
                return;
            }

            EditorGUILayout.HelpBox(result.Message, MessageType.Info);
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.ObjectField("Created Root", result.CreatedRoot, typeof(UnityEngine.Object), false);
                if (GUILayout.Button(new GUIContent("Ping", "Ping created root asset"), GUILayout.Width(48f)) && result.CreatedRoot != null)
                    EditorGUIUtility.PingObject(result.CreatedRoot);
            }
        }
    }
}
