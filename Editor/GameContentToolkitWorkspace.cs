using System;
using System.Collections.Generic;
using System.Linq;
using Deucarian.Editor;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Ui = Deucarian.Editor.DeucarianEditorWorkspaceControls;

namespace Deucarian.GameContentAuthoring.Editor
{
    public interface IGameContentToolkitAuthoringProvider
    {
        VisualElement CreateEditor(GameContentAuthoringSurfaceContext context);
    }

    public interface IGameContentToolkitRecordProvider
    {
        string RecordIconId { get; }
        VisualElement CreateRecordDetails(GameContentRecordDescriptor record);
    }

    internal sealed class GameContentToolkitWorkspace : IDisposable
    {
        private readonly EditorWindow window;
        private readonly DeucarianEditorCollectionWorkspace collection;
        private readonly GameContentEditSessionCoordinator sessions = GameContentEditSessionCoordinator.Shared;
        private readonly IDisposable sessionView;
        private readonly GameContentPackSelectionState selection = new GameContentPackSelectionState();
        private readonly GameContentEditWorkbenchState workbench = new GameContentEditWorkbenchState();
        private readonly string providerPrefix;
        private readonly bool localFilters;
        private readonly VisualElement additionalFilters;
        private TextField contentSearch;
        private readonly Dictionary<string, bool> disclosures = new Dictionary<string, bool>();
        private GameContentPackContext pack;
        private IGameContentAuthoringProvider provider;
        private GameContentRecordDescriptor record;
        private GameContentLibraryReport library;
        private GameContentCreationResult lastResult;
        private GameContentAuthoringValidationResult validation;
        private string search = string.Empty;
        private string category = string.Empty;
        private bool creating, queued, refreshQueued, disposed;
        private string renderedKey;

        internal GameContentToolkitWorkspace(EditorWindow window, VisualElement root, string toolId,
            string title, string subtitle, string providerPrefix)
        {
            this.window = window; this.providerPrefix = providerPrefix ?? string.Empty;
            localFilters = toolId == DeucarianToolIds.GameContentAuthoring;
            collection = new DeucarianEditorCollectionWorkspace(root, Application.productName, title,
                subtitle, toolId, "Find content…");
            collection.Workspace.Root.AddToClassList("dw-authoring-page");
            collection.UsePanels(localFilters);
            additionalFilters = Ui.Region("authoring-additional-filters", "dw-collection-footer");
            collection.Collection.Q<ScrollView>("workspace-collection").Add(additionalFilters);
            if (localFilters) collection.Workspace.SetSearchPrompt("Find a tool…");
            else
            {
                contentSearch = collection.Workspace.SearchField;
                contentSearch.RegisterValueChangedCallback(evt => Search(evt.newValue));
            }
            collection.Workspace.PageActions.Add(Ui.Button("Refresh", Refresh));
            sessionView = sessions.AttachView(QueueRefresh);
            Refresh();
        }

        private void Refresh()
        {
            if (disposed) return;
            var catalog = GameContentPackCatalog.Build(GameContentAuthoringProviderRegistry.Providers);
            string key = pack?.SelectionKey ?? SessionState.GetString(GameContentAuthoringWindow.PackSelectionSessionStateKey, string.Empty);
            if (pack == null && providerPrefix.Length > 0)
            {
                var owned = catalog.Entries.FirstOrDefault(entry => entry.Provider is IGameContentAuthoringProvider authoring &&
                    authoring.ProviderId.StartsWith(providerPrefix, StringComparison.OrdinalIgnoreCase));
                if (owned != null) key = owned.StableKey;
            }
            pack = selection.Refresh(catalog, key);
            sessions.Reconcile(catalog);
            var projectProvider = GameContentAuthoringProviderRegistry.Providers.OfType<GameContentLibraryProvider>().FirstOrDefault();
            library = GameContentLibraryService.Scan(projectProvider?.ContentRoot ?? GameContentLibraryProvider.DefaultRoot, catalog.ClaimedSourceIdentities);
            if (record != null) record = pack.ResolveRecord(record.CanonicalKey);
            RenderScope(); RenderRecords(); RenderDetails();
        }

        private void RenderScope()
        {
            var scope = collection.Workspace.Scope; scope.Clear();
            var entries = pack.Catalog.Entries.ToArray();
            string[] names = entries.Select(entry => entry.Pack.DisplayName).Concat(new[] { "All packs" }).ToArray();
            string[] keys = entries.Select(entry => entry.StableKey).Concat(new[] { GameContentPackContext.AllPacksSelectionKey }).ToArray();
            var filters = Ui.Region("authoring-filters", "dw-content-filters"); scope.Add(filters);
            var fields = new DeucarianEditorWorkspaceForm(filters);
            var packField = fields.Choice("authoring-pack", "Pack", names, () => Math.Max(0, Array.IndexOf(keys, pack.SelectionKey)), index =>
            {
                StopPreview(); pack = selection.Select(pack.Catalog, keys[index]); record = null; category = string.Empty; creating = false;
                SessionState.SetString(GameContentAuthoringWindow.PackSelectionSessionStateKey, pack.SelectionKey);
                RenderScope(); RenderRecords(); RenderDetails();
            });
            var providers = GameContentAuthoringProviderRegistry.VisibleProviders
                .Where(value => providerPrefix.Length == 0 || value.ProviderId == GameContentLibraryProvider.ContentLibraryProviderId ||
                    value.ProviderId.StartsWith(providerPrefix, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (providerPrefix.Length > 0 && !providers.Any(value => value.ProviderId != GameContentLibraryProvider.ContentLibraryProviderId &&
                value is IGameContentAuthoringLensProvider))
                providers = GameContentAuthoringProviderRegistry.VisibleProviders.Where(value =>
                    value.ProviderId == GameContentLibraryProvider.ContentLibraryProviderId ||
                    value is IGameContentAuthoringLensProvider lens && lens.Lens != null && pack.Records.Any(lens.Lens.Matches)).ToArray();
            if (providers.Length == 0) providers = GameContentAuthoringProviderRegistry.VisibleProviders.ToArray();
            if (!providers.Contains(provider)) provider = providers.FirstOrDefault(value => providerPrefix.Length > 0 &&
                value.ProviderId.StartsWith(providerPrefix, StringComparison.OrdinalIgnoreCase)) ??
                providers.FirstOrDefault(value => value.ProviderId == GameContentLibraryProvider.ContentLibraryProviderId) ?? providers.FirstOrDefault();
            Ui.Show(packField.parent.Q(className: "dw-field-label"), !localFilters);
            if (providers.Length > 0)
            {
                var typeField = fields.Choice("authoring-view", "Type", providers.Select(value => value.DisplayName).ToArray(),
                    () => Math.Max(0, Array.IndexOf(providers, provider)), index =>
                    {
                        StopPreview(); provider = providers[index]; creating = false; record = null;
                        provider.OnSelected(); RenderRecords(); RenderDetails();
                    });
                Ui.Show(typeField.parent.Q(className: "dw-field-label"), !localFilters);
            }
            if (localFilters)
            {
                var searchBox = Ui.Search("authoring-search", "Find content…", out contentSearch);
                searchBox.AddToClassList("dw-list-search"); scope.Add(searchBox);
                contentSearch.value = search;
                contentSearch.RegisterValueChangedCallback(evt => Search(evt.newValue));
            }
            var tabs = collection.Workspace.Tabs; tabs.Clear(); additionalFilters.Clear();
            var categories = pack.Pack?.Categories.Where(value => value.RecordCount > 0).ToArray();
            if (categories != null && categories.Length > 1)
            {
                var labels = new[] { "All content" }.Concat(categories.Select(value => value.DisplayName)).ToArray();
                var ids = new[] { string.Empty }.Concat(categories.Select(value => value.CategoryId)).ToArray();
                var more = new DeucarianEditorWorkspaceForm(additionalFilters).Section("More filters", true);
                more.Choice("authoring-category", "Category", labels, () => Math.Max(0, Array.IndexOf(ids, category)),
                    index => { category = ids[index]; record = null; RenderRecords(); RenderDetails(); });
            }
            Ui.Show(tabs, tabs.childCount > 0);
            Ui.Show(additionalFilters, additionalFilters.childCount > 0);
        }

        private void RenderRecords()
        {
            var lens = (provider as IGameContentAuthoringLensProvider)?.Lens;
            var filtered = pack.Records.Where(value => (lens == null || lens.Matches(value)) && value.IsInCategory(category) &&
                (search.Length == 0 || (value.DisplayName + " " + value.SourceRecordId + " " + value.CategoryId).IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0)).ToArray();
            if (!creating && (record == null || !filtered.Contains(record))) record = filtered.FirstOrDefault();
            collection.SetItems(filtered.Select(value => new DeucarianEditorCollectionItem(value.CanonicalKey.StableKey,
                value.DisplayName, localFilters ? ObjectNames.NicifyVariableName(value.CategoryId) : string.Empty,
                value.Validation.ErrorCount > 0 ? "Needs attention" : string.Empty,
                () => { StopPreview(); creating = false; record = value; RenderRecords(); RenderDetails(); },
                iconId: localFilters ? RecordProvider(value)?.RecordIconId ?? DeucarianEditorIconIds.Document : null)).ToArray(), record?.CanonicalKey.StableKey,
                "No matching content. Choose a pack or create project content.");
        }

        private void RenderDetails()
        {
            var details = collection.Details;
            string key = pack.SelectionKey + "|" + provider?.ProviderId + "|" + (creating ? "new" : record?.CanonicalKey.StableKey);
            var offset = key == renderedKey ? (details as ScrollView)?.scrollOffset ?? Vector2.zero : Vector2.zero;
            CaptureDisclosures(details, renderedKey);
            renderedKey = key;
            details.Clear();
            var context = CreateContext();
            IGameContentAuthoringProvider editorProvider = provider;
            if (pack.IsProjectContent && record != null && !(provider is IGameContentToolkitAuthoringProvider))
                editorProvider = GameContentAuthoringProviderRegistry.VisibleProviders.FirstOrDefault(value =>
                    value is IGameContentToolkitAuthoringProvider && (value as IGameContentAuthoringLensProvider)?.Lens?.Matches(record) == true);
            if (provider is GameContentPackDashboardProvider)
                GameContentToolkitLibraryDetails.AddDashboard(details, context);
            else if (pack.IsProjectContent && editorProvider is IGameContentToolkitAuthoringProvider native)
                details.Add(native.CreateEditor(CreateContext(editorProvider)));
            else if (record != null)
            {
                var domain = editorProvider as IGameContentToolkitRecordProvider ?? RecordProvider(record);
                details.Add(GameContentToolkitRecordEditor.Create(context, record,
                    (provider as IGameContentAuthoringLensProvider)?.Lens?.LensId ?? "all-content", domain?.CreateRecordDetails(record)));
            }
            else
            {
                details.Add(Ui.Label(pack.DisplayName, "dw-section-title"));
                details.Add(Ui.Label(pack.Pack?.Description ?? "Browse content from installed providers.", "dw-note"));
                GameContentToolkitRecordEditor.AddValidation(details, pack.Pack?.Validation);
            }
            if (localFilters && record != null)
            {
                var editor = details.Q("content-draft-editor") ?? details.Q("content-record-editor");
                if (editor != null)
                {
                    editor.Q<Label>(className: "dw-section-title")?.RemoveFromHierarchy();
                    var heading = new DeucarianEditorFeatureSection("authoring-record-heading", record.DisplayName,
                        ObjectNames.NicifyVariableName(record.CategoryId), RecordProvider(record)?.RecordIconId ?? DeucarianEditorIconIds.Document);
                    heading.Root.AddToClassList("dw-record-heading");
                    editor.Insert(0, heading.Root);
                }
            }
            if (pack.IsProjectContent && provider is IGameContentToolkitAuthoringProvider)
            {
                var create = Ui.Button(creating ? "Browse existing content" : "Create new", () =>
                {
                    creating = !creating; if (creating) record = null; RenderRecords(); RenderDetails();
                });
                create.name = "authoring-create-new"; create.SetEnabled(pack.Access.CanCreate); details.Add(create);
            }
            AddPackActions(details, context);
            if (context.SelectedItem != null) GameContentToolkitLibraryDetails.Add(details, context, library);
            if (provider is GameContentLibraryProvider projectLibrary)
            {
                var tools = new DeucarianEditorWorkspaceForm(details).Section("Library tools", true);
                string scanRoot = projectLibrary.ContentRoot;
                tools.Text("content-scan-root", "Content root", () => scanRoot, value => scanRoot = value);
                tools.Action("content-scan", "Scan library", () => { projectLibrary.ContentRoot = scanRoot; QueueRefresh(); });
                tools.Action("content-copy-library", "Copy library report", () => EditorGUIUtility.systemCopyBuffer = GameContentLibraryReportWriter.ToMarkdown(library));
            }
            if (lastResult != null) details.Add(Ui.Label(lastResult.Message, "dw-note"));
            if (validation != null) GameContentToolkitRecordEditor.AddValidation(details, validation);
            if (details is ScrollView scroll) scroll.scrollOffset = offset;
            RestoreDisclosures(details, key);
            collection.Workspace.FooterLeading.text = pack.AccessStatusLabel;
            collection.Workspace.FooterTrailing.text = string.Empty;
        }

        private GameContentAuthoringSurfaceContext CreateContext(IGameContentAuthoringProvider selectedProvider = null)
        {
            selectedProvider = selectedProvider ?? provider;
            var all = pack.IsProjectContent ? library.Items : Array.Empty<GameContentLibraryItem>();
            var selected = creating || record == null ? null : all.FirstOrDefault(item => item.Asset == record.SourceAsset);
            var kind = GameContentExistingItemPresentation.GetProviderKind(selectedProvider);
            var authored = kind.HasValue ? all.Where(item => item.Kind == kind.Value).ToArray() : all;
            var authoring = new GameContentAuthoringContext(window, selectedProvider?.ProviderId,
                result => { lastResult = result; if (result?.Succeeded == true) { creating = false; QueueRefresh(); } },
                () => lastResult, result => validation = result, pack);
            return new GameContentAuthoringSurfaceContext(window, selectedProvider, default, authored, all, selected,
                authoring, null, pack, GameContentAuthoringProviderRegistry.Lenses, record, sessions, QueueRefresh,
                item => SelectReferencedRecord(pack.Records.FirstOrDefault(value => value.SourceAsset == item.Asset)),
                () => { record = null; creating = true; QueueRender(); },
                SelectReferencedRecord,
                (id, value) => { provider = GameContentAuthoringProviderRegistry.VisibleProviders.FirstOrDefault(candidate => (candidate as IGameContentAuthoringLensProvider)?.Lens?.LensId == id) ?? provider; record = value ?? record; RenderScope(); QueueRender(); },
                QueueRender, workbench, id => DeucarianEditorNavigation.Open(collection.Workspace.Root, id));
        }

        private void SelectReferencedRecord(GameContentRecordDescriptor value)
        {
            if (value == null) return;
            if (pack.ResolveRecord(value.CanonicalKey) == null)
                pack = selection.Select(pack.Catalog, GameContentPackDescriptor.BuildStableKey(value.CanonicalKey.OwningPackageId, value.CanonicalKey.PackId));
            StopPreview(); creating = false; record = value; search = category = string.Empty;
            contentSearch.value = string.Empty;
            var lens = (provider as IGameContentAuthoringLensProvider)?.Lens;
            if (lens != null && !lens.Matches(value))
                provider = GameContentAuthoringProviderRegistry.VisibleProviders.FirstOrDefault(candidate => candidate.ProviderId == GameContentLibraryProvider.ContentLibraryProviderId);
            RenderScope(); QueueRender();
        }

        private void AddPackActions(VisualElement root, GameContentAuthoringSurfaceContext context)
        {
            if (pack.Pack == null) return;
            var advanced = new DeucarianEditorWorkspaceForm(root).Section("Pack actions", true);
            var currentPack = pack;
            foreach (var action in currentPack.Pack.Actions)
            {
                var button = advanced.Action("pack-action-" + action.ActionId, action.DisplayName, () =>
                {
                    if (!CanExecute(currentPack, action) || disposed) return;
                    var result = GameContentPackActionDispatcher.Execute(currentPack.Provider, currentPack.Pack, action);
                    context.ApplyNavigation(result); validation = result.Validation;
                    lastResult = new GameContentCreationResult(result.Succeeded, result.Message, null); QueueRefresh();
                }, () => CanExecute(currentPack, action));
                button.tooltip = action.Enabled ? action.Description : action.DisabledReason;
            }
        }

        private static bool CanExecute(GameContentPackContext context, GameContentActionDescriptor action) => action.Enabled &&
            (action.ActionKind == GameContentActionKind.Validate ? context.Access.CanValidate :
                action.ActionKind == GameContentActionKind.RevealSource ? context.Access.CanRevealSource : context.Access.CanRead);

        private void QueueRender()
        {
            if (disposed || queued) return; queued = true;
            collection.Workspace.Root.schedule.Execute(() => { queued = false; if (!disposed) { RenderRecords(); RenderDetails(); } });
        }
        private void Search(string value)
        {
            search = value ?? string.Empty; RenderRecords(); RenderDetails();
        }
        private static IGameContentToolkitRecordProvider RecordProvider(GameContentRecordDescriptor value) =>
            GameContentAuthoringProviderRegistry.VisibleProviders.FirstOrDefault(candidate =>
                candidate is IGameContentToolkitRecordProvider &&
                (candidate as IGameContentAuthoringLensProvider)?.Lens?.Matches(value) == true) as IGameContentToolkitRecordProvider;
        private void QueueRefresh()
        {
            if (disposed || refreshQueued) return;
            refreshQueued = true;
            collection.Workspace.Root.schedule.Execute(() => { refreshQueued = false; Refresh(); });
        }

        private void CaptureDisclosures(VisualElement root, string key)
        {
            if (key == null) return;
            int index = 0;
            root.Query<Foldout>().ForEach(foldout => disclosures[key + "|" + index++ + "|" + foldout.text] = foldout.value);
        }
        private void RestoreDisclosures(VisualElement root, string key)
        {
            int index = 0;
            root.Query<Foldout>().ForEach(foldout =>
            {
                if (disclosures.TryGetValue(key + "|" + index++ + "|" + foldout.text, out bool open)) foldout.SetValueWithoutNotify(open);
            });
        }
        internal void StopPreview() => provider?.StopPreview();
        public void Dispose() { disposed = true; StopPreview(); sessionView.Dispose(); workbench.Clear(); collection.Dispose(); }
    }

}
