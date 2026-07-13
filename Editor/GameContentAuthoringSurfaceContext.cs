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
            IReadOnlyList<GameContentLibraryItem> allAuthoredItems,
            GameContentLibraryItem selectedItem,
            GameContentAuthoringContext authoring,
            GameContentAuthoringPreviewContext preview,
            GameContentPackContext packContext,
            IReadOnlyList<GameContentLensDescriptor> lenses,
            GameContentRecordDescriptor selectedRecord,
            GameContentEditSessionCoordinator editSessions,
            Action refreshLibrary,
            Action<GameContentLibraryItem> selectItem,
            Action clearSelection,
            Action<GameContentRecordDescriptor> selectRecord,
            Action<string, GameContentRecordDescriptor> openLens,
            Action requestRepaint)
        {
            Window = window;
            Provider = provider;
            Layout = layout;
            AuthoredItems = authoredItems ?? Array.Empty<GameContentLibraryItem>();
            AllAuthoredItems = allAuthoredItems ?? AuthoredItems;
            SelectedItem = selectedItem;
            Authoring = authoring;
            Preview = preview;
            PackContext = packContext;
            Lenses = lenses ?? Array.Empty<GameContentLensDescriptor>();
            SelectedRecord = selectedRecord;
            EditSessions = editSessions;
            _refreshLibrary = refreshLibrary;
            _selectItem = selectItem;
            _clearSelection = clearSelection;
            _selectRecord = selectRecord;
            _openLens = openLens;
            _requestRepaint = requestRepaint;
        }

        private readonly Action _refreshLibrary;
        private readonly Action<GameContentLibraryItem> _selectItem;
        private readonly Action _clearSelection;
        private readonly Action<GameContentRecordDescriptor> _selectRecord;
        private readonly Action<string, GameContentRecordDescriptor> _openLens;
        private readonly Action _requestRepaint;

        public EditorWindow Window { get; }
        public IGameContentAuthoringProvider Provider { get; }
        public DeucarianEditorResponsiveLayoutState Layout { get; }
        public IReadOnlyList<GameContentLibraryItem> AuthoredItems { get; }
        public IReadOnlyList<GameContentLibraryItem> AllAuthoredItems { get; }
        public GameContentLibraryItem SelectedItem { get; }
        public GameContentAuthoringContext Authoring { get; }
        public GameContentAuthoringPreviewContext Preview { get; }
        public GameContentPackContext PackContext { get; }
        public IReadOnlyList<GameContentLensDescriptor> Lenses { get; }
        public IReadOnlyList<GameContentRecordDescriptor> PackRecords =>
            PackContext == null ? Array.Empty<GameContentRecordDescriptor>() : PackContext.Records;
        public GameContentRecordDescriptor SelectedRecord { get; }
        public GameContentEditSessionCoordinator EditSessions { get; }
        public bool HasSelectedItem => SelectedItem != null;
        public bool HasSelectedRecord => SelectedRecord != null;
        public bool CanCreate => PackContext != null && PackContext.Access.CanCreate;

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

        public void SelectRecord(GameContentRecordDescriptor record)
        {
            if (record != null) _selectRecord?.Invoke(record);
        }

        public void OpenLens(string lensId, GameContentRecordDescriptor record = null)
        {
            if (!string.IsNullOrWhiteSpace(lensId)) _openLens?.Invoke(lensId, record);
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

        public bool IsSelected(GameContentRecordDescriptor record)
        {
            return record != null && SelectedRecord != null && record.CanonicalKey.Equals(SelectedRecord.CanonicalKey);
        }

        public GameContentRecordDescriptor ResolveReference(
            GameContentRecordDescriptor source,
            GameContentRecordReferenceDescriptor reference)
        {
            return PackContext == null ? null : PackContext.ResolveReference(source, reference);
        }
    }
}
