using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Deucarian.Editor;
using UnityEditor;
using UnityEngine;

namespace Deucarian.GameContentAuthoring.Editor
{
    internal static class GameContentLibraryViewControls
    {
        internal static void DrawSummaryRow(string label, string value)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(label, DeucarianEditorStyles.MutedLabel, GUILayout.Width(128f));
                EditorGUILayout.LabelField(value ?? string.Empty, DeucarianEditorStyles.MutedLabel);
            }
        }

        internal static void OpenAsset(UnityEngine.Object asset)
        {
            if (asset == null)
                return;
            Selection.activeObject = asset;
            EditorGUIUtility.PingObject(asset);
        }

        private static readonly GameContentAuthoringStyleCache styles = new GameContentAuthoringStyleCache();
        internal static GUIStyle HeaderStyle => styles.LibraryHeader;
    }
}
