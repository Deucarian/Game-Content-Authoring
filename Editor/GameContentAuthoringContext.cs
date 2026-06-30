using System;
using System.Collections.Generic;
using Deucarian.GameplayFoundation;
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
            string providerId,
            Action<GameContentCreationResult> setResult,
            Func<GameContentCreationResult> getResult,
            Action<GameContentAuthoringValidationResult> setValidation)
        {
            Window = window;
            ProviderId = string.IsNullOrWhiteSpace(providerId) ? "unknown-provider" : providerId;
            _setResult = setResult;
            _getResult = getResult;
            _setValidation = setValidation;
        }

        public EditorWindow Window { get; }
        public string ProviderId { get; }
        public GUIStyle MutedStyle => DeucarianEditorStyles.MutedLabel;
        public GUIStyle SectionTitleStyle => DeucarianEditorStyles.SectionTitle;

        public void DrawSection(string title, Action draw)
        {
            DrawSection(title, null, draw, true);
        }

        public bool DrawSection(string title, string summary, Action draw, bool defaultOpen = true)
        {
            string key = DeucarianEditorAccordion.BuildStateKey("game-content-authoring", ProviderId, "section", title);
            return DeucarianEditorAccordion.DrawFoldoutCard(key, title, summary, draw, defaultOpen);
        }

        public bool DrawFoldoutCard(
            string stateKey,
            string title,
            string summary,
            Action draw,
            bool defaultOpen = true,
            bool enabled = true,
            Action drawHeaderActions = null)
        {
            return DeucarianEditorAccordion.DrawFoldoutCard(stateKey, title, summary, draw, defaultOpen, enabled, drawHeaderActions);
        }

        public void DrawInlineCard(Action draw)
        {
            DeucarianEditorCards.DrawInlineCard(draw);
        }

        public bool DrawSecondaryButton(string label, bool enabled, params GUILayoutOption[] options)
        {
            return DeucarianEditorButtons.Secondary(label, enabled, options);
        }

        public string DrawTextField(string label, string value, string hint = null)
        {
            return DeucarianEditorFieldRow.TextField(label, value, hint);
        }

        public string DrawTextArea(string label, string value, string hint = null)
        {
            return DeucarianEditorFieldRow.TextArea(label, value, hint);
        }

        public int DrawIntField(string label, int value, string hint = null)
        {
            return DeucarianEditorFieldRow.IntField(label, value, hint);
        }

        public float DrawFloatField(string label, float value, string hint = null)
        {
            return DeucarianEditorFieldRow.FloatField(label, value, hint);
        }

        public double DrawDoubleField(string label, double value, string hint = null)
        {
            return DeucarianEditorFieldRow.DoubleField(label, value, hint);
        }

        public bool DrawToggle(string label, bool value, string hint = null)
        {
            return DeucarianEditorFieldRow.Toggle(label, value, hint);
        }

        public T DrawEnumPopup<T>(string label, T value, string hint = null) where T : Enum
        {
            return DeucarianEditorFieldRow.EnumPopup(label, value, hint);
        }

        public T DrawObjectField<T>(string label, T value, bool allowSceneObjects = false, string hint = null)
            where T : UnityEngine.Object
        {
            return DeucarianEditorObjectFieldRow.Draw(label, value, allowSceneObjects, hint);
        }

        public string DrawOutputRootField(string outputRoot)
        {
            DefaultAsset asset = AssetDatabase.LoadAssetAtPath<DefaultAsset>(outputRoot);
            DefaultAsset next = DrawObjectField("Output Root", asset, false, "Choose the root folder where this authored content will be created.");
            if (next != asset && next != null)
            {
                string path = AssetDatabase.GetAssetPath(next);
                if (AssetDatabase.IsValidFolder(path)) outputRoot = path;
            }

            return DrawTextField("Output Path", outputRoot, "Final asset folders are created under this path using the stable content ID.");
        }

        public void DrawValidation(GameContentAuthoringValidationResult result, string readyMessage)
        {
            SetValidation(result);
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

        public void DrawValidation(ContentValidationReport report, string readyMessage)
        {
            DrawValidation(GameContentAuthoringValidationReports.ToAuthoringResult(report), readyMessage);
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

        public void SetValidation(GameContentAuthoringValidationResult result)
        {
            _setValidation?.Invoke(result);
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
                DeucarianEditorObjectFieldRow.Draw("Created Root", result.CreatedRoot, false, "The root asset created by the authoring flow.");
            });
        }
    }
}
