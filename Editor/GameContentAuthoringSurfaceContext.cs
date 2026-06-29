using System;
using System.Collections.Generic;
using Deucarian.Editor;
using UnityEditor;

namespace Deucarian.GameContentAuthoring.Editor
{
    public interface IGameContentAuthoringSurfaceProvider
    {
        void DrawCustomAuthoringSurface(GameContentAuthoringSurfaceContext context);
    }

    public sealed class GameContentAuthoringSurfaceContext
    {
        internal GameContentAuthoringSurfaceContext(
            EditorWindow window,
            IGameContentAuthoringProvider provider,
            DeucarianEditorResponsiveLayoutState layout,
            IReadOnlyList<GameContentLibraryItem> authoredItems,
            GameContentLibraryItem selectedItem,
            GameContentAuthoringContext authoring,
            GameContentAuthoringPreviewContext preview,
            Action refreshLibrary,
            Action<GameContentLibraryItem> selectItem,
            Action clearSelection,
            Action requestRepaint)
        {
            Window = window;
            Provider = provider;
            Layout = layout;
            AuthoredItems = authoredItems ?? Array.Empty<GameContentLibraryItem>();
            SelectedItem = selectedItem;
            Authoring = authoring;
            Preview = preview;
            _refreshLibrary = refreshLibrary;
            _selectItem = selectItem;
            _clearSelection = clearSelection;
            _requestRepaint = requestRepaint;
        }

        private readonly Action _refreshLibrary;
        private readonly Action<GameContentLibraryItem> _selectItem;
        private readonly Action _clearSelection;
        private readonly Action _requestRepaint;

        public EditorWindow Window { get; }
        public IGameContentAuthoringProvider Provider { get; }
        public DeucarianEditorResponsiveLayoutState Layout { get; }
        public IReadOnlyList<GameContentLibraryItem> AuthoredItems { get; }
        public GameContentLibraryItem SelectedItem { get; }
        public GameContentAuthoringContext Authoring { get; }
        public GameContentAuthoringPreviewContext Preview { get; }
        public bool HasSelectedItem => SelectedItem != null;

        public void RefreshLibrary()
        {
            _refreshLibrary?.Invoke();
        }

        public void SelectItem(GameContentLibraryItem item)
        {
            if (item != null)
                _selectItem?.Invoke(item);
        }

        public void ClearSelection()
        {
            _clearSelection?.Invoke();
        }

        public void RequestRepaint()
        {
            _requestRepaint?.Invoke();
        }

        public bool IsSelected(GameContentLibraryItem item)
        {
            if (item == null || SelectedItem == null)
                return false;
            return string.Equals(item.Key, SelectedItem.Key, StringComparison.Ordinal);
        }
    }
}
