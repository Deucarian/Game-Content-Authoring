using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Deucarian.Editor;
using UnityEditor;
using UnityEngine;

namespace Deucarian.GameContentAuthoring.Editor
{
    internal sealed class GameContentLibraryProviderV2View
    {

        public void Draw(
            GameContentAuthoringSurfaceContext context,
            GameContentLibraryReport report,
            GameContentLibraryV2State state,
            string rootPath,
            Action<string> setRootPath,
            Action refresh)
        {
            if (context == null || report == null || state == null)
                return;

            state.EnsureSelection(report);
            GameContentAuthoringWorkbench.Draw(
                context,
                () => GameContentLibraryListView.DrawLibraryList(context, report, state, refresh),
                () => GameContentLibraryDetailView.DrawSelectedDetail(context, report, state),
                () => GameContentLibraryGraphView.DrawGraphPreview(context, report, state, rootPath, setRootPath, refresh));
        }

    }
}
