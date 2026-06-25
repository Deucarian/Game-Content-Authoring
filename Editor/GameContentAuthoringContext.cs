using System;
using System.Collections.Generic;
using Deucarian.Editor;
using UnityEditor;
using UnityEngine;

namespace Deucarian.GameContentAuthoring.Editor
{
    public sealed class GameContentAuthoringContext
    {
        private readonly Action<GameContentCreationResult> _setResult;
        private readonly Func<GameContentCreationResult> _getResult;
        private readonly Action<GameContentAuthoringValidationResult> _setValidation;

        internal GameContentAuthoringContext(
            EditorWindow window,
            Action<GameContentCreationResult> setResult,
            Func<GameContentCreationResult> getResult,
            Action<GameContentAuthoringValidationResult> setValidation)
        {
            Window = window;
            _setResult = setResult;
            _getResult = getResult;
            _setValidation = setValidation;
        }

        public EditorWindow Window { get; }
        public GUIStyle MutedStyle => DeucarianEditorStyles.MutedLabel;
        public GUIStyle SectionTitleStyle => DeucarianEditorStyles.SectionTitle;

        public void DrawSection(string title, Action draw)
        {
            DeucarianEditorCards.DrawCard(title, draw);
        }

        public void DrawInlineCard(Action draw)
        {
            DeucarianEditorCards.DrawInlineCard(draw);
        }

        public bool DrawSecondaryButton(string label, bool enabled, params GUILayoutOption[] options)
        {
            return DeucarianEditorButtons.Secondary(label, enabled, options);
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
            _setValidation?.Invoke(result);
            if (result == null || result.Issues.Count == 0)
            {
                DeucarianEditorStatusPanel.DrawStatusCard(readyMessage, DeucarianEditorStatus.Info);
                return;
            }

            string summary;
            DeucarianEditorStatus status;
            if (result.ErrorCount > 0)
            {
                summary = result.ErrorCount.ToString(System.Globalization.CultureInfo.InvariantCulture) + " blocking issue(s) and " + result.WarningCount.ToString(System.Globalization.CultureInfo.InvariantCulture) + " warning(s).";
                status = DeucarianEditorStatus.Error;
            }
            else if (result.WarningCount > 0)
            {
                summary = result.WarningCount.ToString(System.Globalization.CultureInfo.InvariantCulture) + " warning(s). You can create the asset after confirming any prompts.";
                status = DeucarianEditorStatus.Warning;
            }
            else
            {
                summary = result.Issues.Count.ToString(System.Globalization.CultureInfo.InvariantCulture) + " info item(s).";
                status = DeucarianEditorStatus.Info;
            }

            List<string> messages = new List<string>();

            for (int i = 0; i < result.Issues.Count; i++)
            {
                GameContentAuthoringValidationIssue issue = result.Issues[i];
                messages.Add(issue.Path + ": " + issue.Message);
            }

            DeucarianEditorStatusPanel.DrawValidationCard(summary, messages, status);
        }

        public bool DrawCreateButton(string label, bool enabled)
        {
            var content = new GUIContent(
                label,
                enabled ? "Create the root asset and linked section assets." : "Fix blocking validation issues before creating this asset.");
            using (new EditorGUI.DisabledScope(!enabled))
                return GUILayout.Button(content, enabled ? DeucarianEditorButtons.PrimaryStyle : DeucarianEditorButtons.DisabledStyle, GUILayout.Height(32f));
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
                DeucarianEditorStatusPanel.DrawStatusCard(result.Message, DeucarianEditorStatus.Error);
                return;
            }

            DeucarianEditorStatusPanel.DrawStatusCard(result.Message, DeucarianEditorStatus.Success);
            DeucarianEditorCards.DrawInlineCard(() =>
            {
                EditorGUILayout.ObjectField("Created Root", result.CreatedRoot, typeof(UnityEngine.Object), false);
                if (DeucarianEditorButtons.Secondary("Ping", result.CreatedRoot != null, GUILayout.Width(72f)) && result.CreatedRoot != null)
                    EditorGUIUtility.PingObject(result.CreatedRoot);
            });
        }
    }
}
