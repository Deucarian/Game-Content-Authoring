using Deucarian.Editor;
using UnityEditor;
using UnityEngine;

namespace Deucarian.GameContentAuthoring.Editor
{
    internal sealed class GameContentAuthoringStyleCache
    {
        private GUIStyle libraryHeader;
        private GUIStyle overlayLabel;
        private bool proSkin;

        internal GUIStyle LibraryHeader => Get(ref libraryHeader, true);
        internal GUIStyle OverlayLabel => Get(ref overlayLabel, false);

        internal void Clear()
        {
            libraryHeader = null;
            overlayLabel = null;
        }

        private GUIStyle Get(ref GUIStyle cached, bool header)
        {
            bool currentSkin = EditorGUIUtility.isProSkin;
            if (currentSkin != proSkin)
            {
                Clear();
                proSkin = currentSkin;
            }
            if (cached == null)
            {
                cached = new GUIStyle(header ? EditorStyles.boldLabel : EditorStyles.miniBoldLabel);
                if (header)
                {
                    cached.fontSize = 14;
                    cached.wordWrap = true;
                }
                else
                {
                    cached.alignment = TextAnchor.MiddleCenter;
                    cached.clipping = TextClipping.Clip;
                }
                cached.normal.textColor = DeucarianEditorTheme.Text;
            }
            return cached;
        }
    }
}
