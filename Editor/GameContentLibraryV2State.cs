using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Deucarian.Editor;
using UnityEditor;
using UnityEngine;

namespace Deucarian.GameContentAuthoring.Editor
{
    public sealed class GameContentLibraryV2State
    {
        private readonly HashSet<GameContentLibraryKind> _collapsedGroups = new HashSet<GameContentLibraryKind>();

        public string SearchText = string.Empty;
        public int TypeFilterIndex;
        public GameContentLibraryV2SeverityFilter SeverityFilter;
        public GameContentLibraryV2ReadinessFilter ReadinessFilter;
        public int DetailPage;
        public bool DebugGraph;
        public Vector2 ListScroll;
        public Vector2 DetailScroll;
        public Vector2 GraphScroll;
        public string SelectedKey = string.Empty;
        public string StatusMessage = "Library ready";

        public void ResetSession()
        {
            SearchText = string.Empty;
            TypeFilterIndex = 0;
            SeverityFilter = GameContentLibraryV2SeverityFilter.All;
            ReadinessFilter = GameContentLibraryV2ReadinessFilter.All;
            DetailPage = 0;
            DebugGraph = false;
            ListScroll = Vector2.zero;
            DetailScroll = Vector2.zero;
            GraphScroll = Vector2.zero;
            SelectedKey = string.Empty;
            StatusMessage = "Library ready";
            _collapsedGroups.Clear();
        }

        public void StopPreview()
        {
            DebugGraph = false;
            StatusMessage = "Library preview stopped";
        }

        public void EnsureSelection(GameContentLibraryReport report)
        {
            if (report == null || report.Items.Count == 0)
            {
                SelectedKey = string.Empty;
                return;
            }

            if (report.Items.Any(item => string.Equals(item.Key, SelectedKey, StringComparison.Ordinal)))
                return;

            SelectedKey = report.Items[0].Key;
        }

        public GameContentLibraryItem GetSelected(GameContentLibraryReport report)
        {
            if (report == null || string.IsNullOrWhiteSpace(SelectedKey))
                return null;
            return report.Items.FirstOrDefault(item => string.Equals(item.Key, SelectedKey, StringComparison.Ordinal));
        }

        public void Select(GameContentLibraryItem item)
        {
            if (item == null) return;
            SelectedKey = item.Key;
            DetailScroll = Vector2.zero;
            GraphScroll = Vector2.zero;
            StatusMessage = "Selected " + item.DisplayName;
            GUI.FocusControl(null);
        }

        public bool IsGroupExpanded(GameContentLibraryKind kind)
        {
            return !_collapsedGroups.Contains(kind);
        }

        public void ToggleGroup(GameContentLibraryKind kind)
        {
            if (!_collapsedGroups.Add(kind))
                _collapsedGroups.Remove(kind);
        }
    }
}
