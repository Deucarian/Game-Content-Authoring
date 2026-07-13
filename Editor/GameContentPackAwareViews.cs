using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Deucarian.Editor;
using UnityEditor;
using UnityEngine;

namespace Deucarian.GameContentAuthoring.Editor
{
    public class GameContentRecordLensBrowserState
    {
        public string SearchText = string.Empty;
        public Vector2 ListScroll;
        public Vector2 DetailScroll;
        public Vector2 PreviewScroll;
    }

    public sealed class GameContentAllContentBrowserState : GameContentRecordLensBrowserState
    {
        public string CapabilityId = string.Empty;
        public string SourceId = string.Empty;
        public GameContentRecordValidationFilter ValidationFilter;
        public GameContentRecordSortMode SortMode;
    }

    public static class GameContentRecordLensBrowser
    {
        public static void Draw(
            GameContentAuthoringSurfaceContext context,
            GameContentLensDescriptor lens,
            GameContentRecordLensBrowserState state,
            Action<GameContentRecordDescriptor> drawDomainDetails,
            Action<GameContentRecordDescriptor> drawDomainPreview,
            Func<GameContentRecordDescriptor, bool> additionalFilter = null,
            Action drawAdditionalListControls = null,
            Func<IEnumerable<GameContentRecordDescriptor>, IEnumerable<GameContentRecordDescriptor>> orderRecords = null)
        {
            if (context == null || lens == null || state == null) return;
            IReadOnlyList<GameContentRecordDescriptor> records = GetRecords(context, lens, state.SearchText, additionalFilter, orderRecords);
            GameContentRecordDescriptor selected = ResolveSelected(context, lens, records);
            context.Authoring.SetValidation(context.PackContext == null || context.PackContext.Pack == null
                ? GameContentAuthoringValidationResult.Valid
                : context.PackContext.Pack.Validation);

            GameContentAuthoringWorkbench.Draw(
                context,
                () => DrawList(context, lens, state, records, drawAdditionalListControls),
                () => DrawDetail(context, lens, state, selected, drawDomainDetails),
                () => DrawPreview(context, lens, state, selected, drawDomainPreview));
        }

        private static IReadOnlyList<GameContentRecordDescriptor> GetRecords(
            GameContentAuthoringSurfaceContext context,
            GameContentLensDescriptor lens,
            string searchText,
            Func<GameContentRecordDescriptor, bool> additionalFilter,
            Func<IEnumerable<GameContentRecordDescriptor>, IEnumerable<GameContentRecordDescriptor>> orderRecords)
        {
            IEnumerable<GameContentRecordDescriptor> query = context.PackRecords.Where(lens.Matches);
            if (additionalFilter != null) query = query.Where(additionalFilter);
            string search = string.IsNullOrWhiteSpace(searchText) ? string.Empty : searchText.Trim();
            if (!string.IsNullOrWhiteSpace(search))
            {
                query = query.Where(record => MatchesSearch(record, GetPackName(context, record), search));
            }

            query = orderRecords == null
                ? query.OrderBy(record => record.DisplayName, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(record => record.CanonicalKey.StableKey, StringComparer.OrdinalIgnoreCase)
                : orderRecords(query);
            return query.ToArray();
        }

        internal static bool MatchesSearch(
            GameContentRecordDescriptor record,
            string packDisplayName,
            string search)
        {
            if (record == null) return false;
            if (string.IsNullOrWhiteSpace(search)) return true;
            return Contains(record.DisplayName, search) ||
                   Contains(record.SourceRecordId, search) ||
                   Contains(record.Description, search) ||
                   Contains(record.Summary, search) ||
                   Contains(record.SourcePath, search) ||
                   Contains(packDisplayName, search);
        }

        private static GameContentRecordDescriptor ResolveSelected(
            GameContentAuthoringSurfaceContext context,
            GameContentLensDescriptor lens,
            IReadOnlyList<GameContentRecordDescriptor> records)
        {
            GameContentRecordDescriptor selected = context.SelectedRecord;
            if (selected != null && lens.Matches(selected) && records.Any(record => record.CanonicalKey.Equals(selected.CanonicalKey)))
                return selected;
            if (selected != null) return null;
            if (records.Count == 0) return null;
            context.SelectRecord(records[0]);
            return records[0];
        }

        private static void DrawList(
            GameContentAuthoringSurfaceContext context,
            GameContentLensDescriptor lens,
            GameContentRecordLensBrowserState state,
            IReadOnlyList<GameContentRecordDescriptor> records,
            Action drawAdditionalListControls)
        {
            EditorGUILayout.LabelField(lens.DisplayName, DeucarianEditorStyles.SectionTitle);
            EditorGUILayout.LabelField(
                context.PackContext.DisplayName + " - " + records.Count.ToString(CultureInfo.InvariantCulture) + " matching record(s)",
                DeucarianEditorStyles.MutedLabel);
            DrawAccessStatus(context.PackContext);
            state.SearchText = DeucarianEditorSearchField.Draw(
                state.SearchText,
                "Search " + lens.DisplayName.ToLowerInvariant(),
                GUILayout.ExpandWidth(true));
            drawAdditionalListControls?.Invoke();

            state.ListScroll = EditorGUILayout.BeginScrollView(state.ListScroll);
            if (records.Count == 0)
            {
                string[] exposed = context.PackRecords.SelectMany(record => record.Capabilities)
                    .Select(value => value.Id)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                string capabilities = exposed.Length > 0 ? string.Join(", ", exposed) : "none";
                EditorGUILayout.HelpBox(
                    "Not used by this content pack. Exposed capabilities: " + capabilities + ".",
                    MessageType.Info);
                EditorGUILayout.EndScrollView();
                return;
            }

            for (int i = 0; i < records.Count; i++)
            {
                GameContentRecordDescriptor record = records[i];
                bool selected = context.IsSelected(record);
                string pack = context.PackContext.IsAllPacks ? "\n" + GetPackName(context, record) : string.Empty;
                string label = record.DisplayName + pack + "\n" + record.SourceRecordId + " - " + RecordStatus(record);
                if (GUILayout.Toggle(selected, new GUIContent(label, record.Summary), "Button", GUILayout.MinHeight(48f)) && !selected)
                {
                    context.SelectRecord(record);
                    context.RequestRepaint();
                }
            }
            EditorGUILayout.EndScrollView();
        }

        private static void DrawDetail(
            GameContentAuthoringSurfaceContext context,
            GameContentLensDescriptor lens,
            GameContentRecordLensBrowserState state,
            GameContentRecordDescriptor selected,
            Action<GameContentRecordDescriptor> drawDomainDetails)
        {
            state.DetailScroll = EditorGUILayout.BeginScrollView(state.DetailScroll);
            if (selected == null)
            {
                if (context.SelectedRecord != null)
                {
                    EditorGUILayout.HelpBox(
                        context.SelectedRecord.DisplayName + " is not compatible with the " + lens.DisplayName + " lens. Its selection is preserved for a compatible view.",
                        MessageType.Info);
                    DrawCompatibleLenses(context, context.SelectedRecord);
                }
                else
                {
                    EditorGUILayout.HelpBox("Select a compatible record.", MessageType.Info);
                }
                EditorGUILayout.EndScrollView();
                return;
            }

            DrawBreadcrumb(context, lens, selected);
            EditorGUILayout.LabelField(selected.DisplayName, HeaderStyle);
            if (!string.IsNullOrWhiteSpace(selected.Description))
                EditorGUILayout.LabelField(selected.Description, EditorStyles.wordWrappedLabel);
            DrawRow("Record ID", selected.SourceRecordId);
            DrawRow("Canonical Key", selected.CanonicalKey.StableKey);
            DrawRow("Owning Pack", GetPackName(context, selected));
            DrawRow("Source", selected.SourcePath);
            DrawRow("Capabilities", selected.Capabilities.Count == 0
                ? "None"
                : string.Join(", ", selected.Capabilities.Select(value => value.Id)));
            DrawRow("Locator", selected.SourceLocator);

            using (new EditorGUILayout.HorizontalScope())
            {
                bool canReveal = selected.SourceAsset != null && context.PackContext.Access.CanRevealSource;
                using (new EditorGUI.DisabledScope(!canReveal))
                {
                    if (GUILayout.Button(new GUIContent("Reveal Source", canReveal ? "Reveal the owning source asset." : context.PackContext.Access.DisabledReason), GUILayout.Height(24f)))
                    {
                        Selection.activeObject = selected.SourceAsset;
                        EditorGUIUtility.PingObject(selected.SourceAsset);
                    }
                }
                bool canValidate = !context.PackContext.IsAllPacks &&
                                   context.PackContext.Provider != null &&
                                   context.PackContext.Access.CanValidate;
                using (new EditorGUI.DisabledScope(!canValidate))
                {
                    if (GUILayout.Button(
                            new GUIContent(
                                "Validate Pack",
                                canValidate ? "Run validation against the selected content pack." : "Validation is unavailable in this context."),
                            GUILayout.Height(24f)))
                    {
                        GameContentAuthoringValidationResult validation = GameContentPackActionDispatcher.Validate(
                            context.PackContext.Provider,
                            context.PackContext.Pack);
                        context.Authoring.SetValidation(validation);
                        context.RefreshLibrary();
                    }
                }
            }

            GameContentEditWorkbench.Draw(context, selected, lens.LensId);
            drawDomainDetails?.Invoke(selected);
            DrawMetadata(selected);
            DrawReferences(context, selected, "References", selected.OutboundReferences);
            DrawReferences(context, selected, "Referenced By", selected.InboundReferences);
            DrawCompatibleLenses(context, selected);
            DrawValidation(selected.Validation);
            EditorGUILayout.EndScrollView();
        }

        private static void DrawPreview(
            GameContentAuthoringSurfaceContext context,
            GameContentLensDescriptor lens,
            GameContentRecordLensBrowserState state,
            GameContentRecordDescriptor selected,
            Action<GameContentRecordDescriptor> drawDomainPreview)
        {
            state.PreviewScroll = EditorGUILayout.BeginScrollView(state.PreviewScroll);
            EditorGUILayout.LabelField("Preview Lab", DeucarianEditorStyles.SectionTitle);
            if (selected == null)
            {
                EditorGUILayout.HelpBox("Select a " + lens.DisplayName + " record to preview authored values.", MessageType.Info);
                EditorGUILayout.EndScrollView();
                return;
            }

            DrawAccessStatus(context.PackContext);
            DrawRow("Owning Pack", GetPackName(context, selected));
            DrawRow("Source", selected.SourcePath);
            drawDomainPreview?.Invoke(selected);
            if (drawDomainPreview == null)
            {
                EditorGUILayout.LabelField(selected.DisplayName, HeaderStyle);
                EditorGUILayout.LabelField(selected.Summary, EditorStyles.wordWrappedLabel);
                DrawMetadata(selected);
            }
            EditorGUILayout.EndScrollView();
        }

        private static void DrawBreadcrumb(
            GameContentAuthoringSurfaceContext context,
            GameContentLensDescriptor lens,
            GameContentRecordDescriptor record)
        {
            EditorGUILayout.LabelField(
                GetPackName(context, record) + " / " + lens.DisplayName + " / " + record.DisplayName,
                DeucarianEditorStyles.MutedLabel);
        }

        private static void DrawMetadata(GameContentRecordDescriptor record)
        {
            if (record == null || record.PlayerFacingMetadata.Count == 0) return;
            GUILayout.Space(DeucarianEditorSpacing.Small);
            EditorGUILayout.LabelField("Authored Metadata", DeucarianEditorStyles.SectionTitle);
            for (int i = 0; i < record.PlayerFacingMetadata.Count; i++)
                DrawRow(record.PlayerFacingMetadata[i].Label, record.PlayerFacingMetadata[i].Value);
        }

        private static void DrawReferences(
            GameContentAuthoringSurfaceContext context,
            GameContentRecordDescriptor source,
            string title,
            IReadOnlyList<GameContentRecordReferenceDescriptor> references)
        {
            EditorGUILayout.LabelField(title, DeucarianEditorStyles.SectionTitle);
            if (references == null || references.Count == 0)
            {
                EditorGUILayout.LabelField("None", DeucarianEditorStyles.MutedLabel);
                return;
            }

            for (int i = 0; i < references.Count; i++)
            {
                GameContentRecordReferenceDescriptor reference = references[i];
                GameContentRecordDescriptor target = context.ResolveReference(source, reference);
                string label = (reference.Valid ? string.Empty : "Broken: ") + reference.RelationshipLabel + " -> " + reference.TargetRecordId;
                using (new EditorGUI.DisabledScope(target == null))
                {
                    if (GUILayout.Button(label, GUILayout.Height(22f)) && target != null)
                    {
                        context.SelectRecord(target);
                        context.RequestRepaint();
                    }
                }
            }
        }

        private static void DrawCompatibleLenses(
            GameContentAuthoringSurfaceContext context,
            GameContentRecordDescriptor record)
        {
            GameContentLensDescriptor[] compatible = context.Lenses.Where(lens => lens.Matches(record)).ToArray();
            if (compatible.Length == 0) return;
            EditorGUILayout.LabelField("Open In", DeucarianEditorStyles.SectionTitle);
            for (int i = 0; i < compatible.Length; i++)
            {
                GameContentLensDescriptor lens = compatible[i];
                if (GUILayout.Button("Open " + lens.DisplayName, GUILayout.Height(22f)))
                    context.OpenLens(lens.LensId, record);
            }
        }

        private static void DrawValidation(GameContentAuthoringValidationResult validation)
        {
            validation = validation ?? GameContentAuthoringValidationResult.Valid;
            EditorGUILayout.LabelField("Validation", DeucarianEditorStyles.SectionTitle);
            if (validation.Issues.Count == 0)
            {
                EditorGUILayout.HelpBox("Ready", MessageType.Info);
                return;
            }

            for (int i = 0; i < validation.Issues.Count; i++)
            {
                GameContentAuthoringValidationIssue issue = validation.Issues[i];
                MessageType type = issue.Severity == GameContentAuthoringValidationSeverity.Error
                    ? MessageType.Error
                    : issue.Severity == GameContentAuthoringValidationSeverity.Warning
                        ? MessageType.Warning
                        : MessageType.Info;
                EditorGUILayout.HelpBox(issue.Path + ": " + issue.Message, type);
            }
        }

        internal static void DrawAccessStatus(GameContentPackContext context, bool compact = false)
        {
            if (context == null) return;
            DeucarianEditorStatus sourceStatus = string.Equals(context.SourceStatusLabel, "Available", StringComparison.Ordinal)
                ? DeucarianEditorStatus.Success
                : string.Equals(context.SourceStatusLabel, "Validation failed", StringComparison.Ordinal) ||
                  string.Equals(context.SourceStatusLabel, "Conflict", StringComparison.Ordinal)
                    ? DeucarianEditorStatus.Error
                    : DeucarianEditorStatus.Warning;
            using (new EditorGUILayout.HorizontalScope())
            {
                DeucarianEditorStatusBadge.Draw(
                    new GUIContent(context.SourceStatusLabel, "Content-pack source status."),
                    sourceStatus,
                    GUILayout.MinWidth(82f));
                DeucarianEditorStatusBadge.Draw(
                    new GUIContent(context.AccessStatusLabel, context.Access.PersistenceLabel),
                    context.Access.IsWritable ? DeucarianEditorStatus.Success : DeucarianEditorStatus.Info,
                    GUILayout.MinWidth(74f));
            }
            if (compact) return;
            EditorGUILayout.LabelField(context.Access.PersistenceLabel, DeucarianEditorStyles.MutedLabel);
            if (!context.Access.IsWritable && !string.IsNullOrWhiteSpace(context.Access.DisabledReason))
                EditorGUILayout.LabelField(context.Access.DisabledReason, DeucarianEditorStyles.MutedLabel);
        }

        internal static string GetPackName(GameContentAuthoringSurfaceContext context, GameContentRecordDescriptor record)
        {
            if (context == null || context.PackContext == null || record == null) return string.Empty;
            GameContentPackCatalogEntry entry = context.PackContext.Catalog.Find(record.CanonicalKey);
            return entry == null ? record.CanonicalKey.PackId : entry.Pack.DisplayName;
        }

        public static void DrawRow(string label, string value)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(label, DeucarianEditorStyles.MutedLabel, GUILayout.Width(108f));
                EditorGUILayout.SelectableLabel(value ?? string.Empty, EditorStyles.wordWrappedLabel, GUILayout.MinHeight(18f));
            }
        }

        private static string RecordStatus(GameContentRecordDescriptor record)
        {
            if (record.Validation.ErrorCount > 0) return record.Validation.ErrorCount.ToString(CultureInfo.InvariantCulture) + " error(s)";
            if (record.HasBrokenReferences) return "Broken reference";
            if (record.Validation.WarningCount > 0) return record.Validation.WarningCount.ToString(CultureInfo.InvariantCulture) + " warning(s)";
            return "Ready";
        }

        private static bool Contains(string value, string search)
        {
            return !string.IsNullOrWhiteSpace(value) && value.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static GUIStyle headerStyle;
        private static GUIStyle HeaderStyle => headerStyle ?? (headerStyle = new GUIStyle(EditorStyles.boldLabel)
        {
            fontSize = 14,
            wordWrap = true
        });
    }

    public static class GameContentAllContentBrowser
    {
        private static readonly GameContentLensDescriptor AllContentLens = new GameContentLensDescriptor(
            "all-content",
            "All Content",
            "Content Pack",
            "content-library",
            10,
            Array.Empty<GameContentRecordCapability>(),
            true);
        private static readonly string[] ValidationLabels = { "All", "Ready", "Warnings", "Errors", "Broken Refs" };
        private static readonly string[] SortLabels = { "Source Order", "Name", "Category", "Status" };

        public static void Draw(GameContentAuthoringSurfaceContext context, GameContentAllContentBrowserState state)
        {
            GameContentRecordCapability[] capabilities = context.PackRecords.SelectMany(record => record.Capabilities)
                .Distinct()
                .OrderBy(value => value.Id, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            string[] sources = context.PackRecords.Select(record => record.CanonicalKey.SourceId)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            GameContentRecordLensBrowser.Draw(
                context,
                AllContentLens,
                state,
                null,
                null,
                record => Matches(state, record),
                () => DrawFilters(state, capabilities, sources),
                records => ApplySort(records, state.SortMode));
        }

        private static void DrawFilters(
            GameContentAllContentBrowserState state,
            IReadOnlyList<GameContentRecordCapability> capabilities,
            IReadOnlyList<string> sources)
        {
            string[] capabilityLabels = new[] { "All Capabilities" }.Concat(capabilities.Select(value => value.Id)).ToArray();
            int capabilityIndex = string.IsNullOrWhiteSpace(state.CapabilityId)
                ? 0
                : Math.Max(0, Array.FindIndex(capabilityLabels, value => string.Equals(value, state.CapabilityId, StringComparison.OrdinalIgnoreCase)));
            int nextCapability = EditorGUILayout.Popup(capabilityIndex, capabilityLabels);
            state.CapabilityId = nextCapability <= 0 ? string.Empty : capabilityLabels[nextCapability];

            string[] sourceLabels = new[] { "All Sources" }.Concat(sources).ToArray();
            int sourceIndex = string.IsNullOrWhiteSpace(state.SourceId)
                ? 0
                : Math.Max(0, Array.FindIndex(sourceLabels, value => string.Equals(value, state.SourceId, StringComparison.OrdinalIgnoreCase)));
            int nextSource = EditorGUILayout.Popup(sourceIndex, sourceLabels);
            state.SourceId = nextSource <= 0 ? string.Empty : sourceLabels[nextSource];

            using (new EditorGUILayout.HorizontalScope())
            {
                state.ValidationFilter = (GameContentRecordValidationFilter)EditorGUILayout.Popup(
                    (int)state.ValidationFilter,
                    ValidationLabels,
                    GUILayout.MinWidth(96f));
                state.SortMode = (GameContentRecordSortMode)EditorGUILayout.Popup(
                    (int)state.SortMode,
                    SortLabels,
                    GUILayout.MinWidth(96f));
            }
        }

        internal static bool Matches(GameContentAllContentBrowserState state, GameContentRecordDescriptor record)
        {
            if (!string.IsNullOrWhiteSpace(state.CapabilityId) &&
                !record.HasCapability(new GameContentRecordCapability(state.CapabilityId))) return false;
            if (!string.IsNullOrWhiteSpace(state.SourceId) &&
                !string.Equals(record.CanonicalKey.SourceId, state.SourceId, StringComparison.OrdinalIgnoreCase)) return false;
            switch (state.ValidationFilter)
            {
                case GameContentRecordValidationFilter.Ready:
                    return record.Validation.ErrorCount == 0 && record.Validation.WarningCount == 0 && !record.HasBrokenReferences;
                case GameContentRecordValidationFilter.Warnings:
                    return record.Validation.WarningCount > 0 && record.Validation.ErrorCount == 0;
                case GameContentRecordValidationFilter.Errors:
                    return record.Validation.ErrorCount > 0;
                case GameContentRecordValidationFilter.BrokenReferences:
                    return record.HasBrokenReferences;
                default:
                    return true;
            }
        }

        internal static IEnumerable<GameContentRecordDescriptor> ApplySort(
            IEnumerable<GameContentRecordDescriptor> records,
            GameContentRecordSortMode sortMode)
        {
            switch (sortMode)
            {
                case GameContentRecordSortMode.DisplayName:
                    return records.OrderBy(record => record.DisplayName, StringComparer.OrdinalIgnoreCase)
                        .ThenBy(record => record.CanonicalKey.StableKey, StringComparer.OrdinalIgnoreCase);
                case GameContentRecordSortMode.Category:
                    return records.OrderBy(record => record.CategoryId, StringComparer.OrdinalIgnoreCase)
                        .ThenBy(record => record.DisplayName, StringComparer.OrdinalIgnoreCase);
                case GameContentRecordSortMode.Status:
                    return records.OrderBy(record => record.Validation.ErrorCount > 0 ? 0 : record.HasBrokenReferences ? 1 : record.Validation.WarningCount > 0 ? 2 : 3)
                        .ThenBy(record => record.DisplayName, StringComparer.OrdinalIgnoreCase);
                default:
                    return records.OrderBy(record => record.Order)
                        .ThenBy(record => record.DisplayName, StringComparer.OrdinalIgnoreCase);
            }
        }
    }

    [InitializeOnLoad]
    internal static class GameContentPackDashboardProviderRegistration
    {
        static GameContentPackDashboardProviderRegistration()
        {
            GameContentAuthoringProviderRegistry.Register(new GameContentPackDashboardProvider());
        }
    }

    public sealed class GameContentPackDashboardProvider :
        IGameContentAuthoringProvider,
        IGameContentAuthoringSurfaceProvider,
        IGameContentAuthoringLensProvider
    {
        public const string StableProviderId = "com.deucarian.game-content-authoring.pack-dashboard";

        public string ProviderId => StableProviderId;
        public string DisplayName => "Pack Dashboard";
        public string Description => "Inspect the selected content pack, validation, categories, and provider actions.";
        public int SortOrder => 0;
        public bool Enabled => true;
        public GameContentLensDescriptor Lens { get; } = new GameContentLensDescriptor(
            "pack-dashboard",
            "Pack Dashboard",
            "Content Pack",
            "content-pack",
            0,
            Array.Empty<GameContentRecordCapability>());

        public void OnSelected()
        {
        }

        public void Draw(GameContentAuthoringContext context)
        {
        }

        public void DrawPreview(GameContentAuthoringPreviewContext context)
        {
        }

        public void StopPreview()
        {
        }

        public void DrawCustomAuthoringSurface(GameContentAuthoringSurfaceContext context)
        {
            GameContentPackDashboard.Draw(context);
        }
    }

    public static class GameContentPackDashboard
    {
        public static void Draw(GameContentAuthoringSurfaceContext context)
        {
            if (context == null || context.PackContext == null) return;
            GameContentPackContext packContext = context.PackContext;
            if (packContext.IsAllPacks)
            {
                DrawAllPacks(context, packContext.Catalog.Entries);
                return;
            }

            GameContentPackDescriptor pack = packContext.Pack;
            context.Authoring.SetValidation(pack.Validation);
            using (new EditorGUILayout.VerticalScope(GUILayout.ExpandHeight(true)))
            {
                EditorGUILayout.LabelField(pack.DisplayName, HeaderStyle);
                EditorGUILayout.LabelField(pack.Description, EditorStyles.wordWrappedLabel);
                GameContentRecordLensBrowser.DrawAccessStatus(packContext);
                GameContentRecordLensBrowser.DrawRow("Owner", pack.OwningPackageId);
                GameContentRecordLensBrowser.DrawRow("Pack ID", pack.PackId);
                GameContentRecordLensBrowser.DrawRow("Schema", pack.SchemaVersion);
                GameContentRecordLensBrowser.DrawRow("Source Type", pack.Access.PersistenceLabel);
                GameContentRecordLensBrowser.DrawRow("Source", pack.SourcePath);
                GameContentRecordLensBrowser.DrawRow("State", pack.SourceState.ToString());
                GameContentRecordLensBrowser.DrawRow("Records", packContext.Records.Count.ToString(CultureInfo.InvariantCulture));
                for (int metadataIndex = 0; metadataIndex < pack.Metadata.Count; metadataIndex++)
                    GameContentRecordLensBrowser.DrawRow(pack.Metadata[metadataIndex].Label, pack.Metadata[metadataIndex].Value);

                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Open All Content", GUILayout.Height(26f)))
                        context.OpenLens("all-content");
                    if (GUILayout.Button("Refresh", GUILayout.Height(26f)))
                        context.RefreshLibrary();
                }

                DrawCategories(pack);
                DrawActions(context, packContext);
                DrawValidation(pack.Validation);
            }
        }

        private static void DrawAllPacks(
            GameContentAuthoringSurfaceContext context,
            IReadOnlyList<GameContentPackCatalogEntry> entries)
        {
            EditorGUILayout.LabelField("All Packs", HeaderStyle);
            EditorGUILayout.LabelField("Cross-pack comparison-friendly browsing is read-only.", EditorStyles.wordWrappedLabel);
            GameContentRecordLensBrowser.DrawAccessStatus(context.PackContext);
            if (GUILayout.Button("Open All Content", GUILayout.Height(26f))) context.OpenLens("all-content");
            for (int i = 0; i < entries.Count; i++)
            {
                GameContentPackCatalogEntry entry = entries[i];
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.LabelField(entry.Pack.DisplayName, DeucarianEditorStyles.SectionTitle);
                GameContentRecordLensBrowser.DrawRow("Owner", entry.Pack.OwningPackageId);
                GameContentRecordLensBrowser.DrawRow("State", entry.IsConflict ? "Conflict" : entry.Pack.SourceState.ToString());
                GameContentRecordLensBrowser.DrawRow("Access", entry.EffectiveAccess.PersistenceLabel);
                GameContentRecordLensBrowser.DrawRow("Records", entry.Records.Count.ToString(CultureInfo.InvariantCulture));
                EditorGUILayout.EndVertical();
            }
        }

        private static void DrawCategories(GameContentPackDescriptor pack)
        {
            EditorGUILayout.LabelField("Pack Views", DeucarianEditorStyles.SectionTitle);
            if (pack.Categories.Count == 0)
            {
                EditorGUILayout.LabelField("No pack-specific categories.", DeucarianEditorStyles.MutedLabel);
                return;
            }

            foreach (GameContentCategoryDescriptor category in pack.Categories.OrderBy(value => value.Order))
                GameContentRecordLensBrowser.DrawRow(category.DisplayName, category.RecordCount.ToString(CultureInfo.InvariantCulture));
        }

        private static void DrawActions(GameContentAuthoringSurfaceContext context, GameContentPackContext packContext)
        {
            if (packContext.Pack.Actions.Count == 0) return;
            EditorGUILayout.LabelField("Actions", DeucarianEditorStyles.SectionTitle);
            for (int i = 0; i < packContext.Pack.Actions.Count; i++)
            {
                GameContentActionDescriptor action = packContext.Pack.Actions[i];
                bool enabled = action.Enabled && IsAllowed(packContext.Access, action.ActionKind);
                string reason = enabled
                    ? action.Description
                    : !action.Enabled ? action.DisabledReason : packContext.Access.DisabledReason;
                using (new EditorGUI.DisabledScope(!enabled))
                {
                    if (GUILayout.Button(new GUIContent(action.DisplayName, reason), GUILayout.Height(25f)))
                    {
                        GameContentActionResult result = GameContentPackActionDispatcher.Execute(
                            packContext.Provider,
                            packContext.Pack,
                            action);
                        EditorUtility.DisplayDialog(
                            result.Succeeded ? "Game Content Authoring" : "Action Failed",
                            result.Message,
                            "OK");
                        if (action.ActionKind == GameContentActionKind.Validate) context.RefreshLibrary();
                    }
                }
            }
        }

        private static bool IsAllowed(GameContentPackAccessDescriptor access, GameContentActionKind actionKind)
        {
            switch (actionKind)
            {
                case GameContentActionKind.Validate: return access.CanValidate;
                case GameContentActionKind.RevealSource: return access.CanRevealSource;
                default: return access.CanRead;
            }
        }

        private static void DrawValidation(GameContentAuthoringValidationResult validation)
        {
            EditorGUILayout.LabelField("Validation", DeucarianEditorStyles.SectionTitle);
            if (validation == null || validation.Issues.Count == 0)
            {
                EditorGUILayout.HelpBox("No validation issues.", MessageType.Info);
                return;
            }
            for (int i = 0; i < validation.Issues.Count; i++)
            {
                GameContentAuthoringValidationIssue issue = validation.Issues[i];
                MessageType type = issue.Severity == GameContentAuthoringValidationSeverity.Error
                    ? MessageType.Error
                    : issue.Severity == GameContentAuthoringValidationSeverity.Warning
                        ? MessageType.Warning
                        : MessageType.Info;
                EditorGUILayout.HelpBox(issue.Path + ": " + issue.Message, type);
            }
        }

        private static GUIStyle headerStyle;
        private static GUIStyle HeaderStyle => headerStyle ?? (headerStyle = new GUIStyle(EditorStyles.boldLabel)
        {
            fontSize = 16,
            wordWrap = true
        });
    }
}
